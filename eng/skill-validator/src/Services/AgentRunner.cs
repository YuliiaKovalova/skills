using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkillValidator.Models;
using SkillValidator.Utilities;
using GitHub.Copilot.SDK;

namespace SkillValidator.Services;

public sealed record RunOptions(
    EvalScenario Scenario,
    SkillInfo? Skill,
    string? EvalPath,
    string Model,
    bool Verbose,
    string? PluginRoot = null,
    Action<string>? Log = null,
    IReadOnlyList<SkillInfo>? AdditionalSkills = null);

public static class AgentRunner
{
    private static readonly ConcurrentDictionary<string, CopilotClient> _pluginClients = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _clientLock = new(1, 1);
    private static readonly ConcurrentBag<string> _workDirs = [];
    private static string? _capturedGitHubToken;
    private static bool _tokenCaptured;

    /// <summary>
    /// Capture GITHUB_TOKEN once at startup so multiple clients can share it
    /// and the env var is cleared from child processes.
    /// </summary>
    public static void CaptureGitHubToken()
    {
        if (_tokenCaptured) return;
        _capturedGitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(_capturedGitHubToken))
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        _tokenCaptured = true;
    }

    /// <summary>
    /// Returns a CopilotClient configured for the given plugin.
    /// The client is created once per plugin root and reused.
    /// Note: --plugin-dir is NOT honored by the SDK, so all clients share
    /// the same configuration. Plugin skills are loaded manually via
    /// SkillDirectories in BuildSessionConfig instead.
    /// </summary>
    public static async Task<CopilotClient> GetPluginClient(
        string? pluginRoot, bool verbose)
    {
        var key = pluginRoot ?? "";

        if (_pluginClients.TryGetValue(key, out var existing))
            return existing;

        await _clientLock.WaitAsync();
        try
        {
            if (_pluginClients.TryGetValue(key, out existing))
                return existing;

            CaptureGitHubToken();

            var options = new CopilotClientOptions
            {
                LogLevel = verbose ? "info" : "none",
            };

            if (!string.IsNullOrEmpty(_capturedGitHubToken))
                options.GitHubToken = _capturedGitHubToken;

            var client = new CopilotClient(options);
            await client.StartAsync();
            _pluginClients[key] = client;
            return client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// Backward-compatible alias — returns the no-plugin client.
    /// Used by judge sessions that don't need plugin loading.
    /// </summary>
    public static Task<CopilotClient> GetSharedClient(bool verbose)
        => GetPluginClient(null, verbose);

    /// <summary>Stop all plugin clients (including the no-plugin client).</summary>
    public static async Task StopAllClients()
    {
        foreach (var (_, client) in _pluginClients)
        {
            try { await client.StopAsync(); }
            catch { /* best effort */ }
        }
        _pluginClients.Clear();
    }

    /// <summary>Backward-compatible alias.</summary>
    public static Task StopSharedClient() => StopAllClients();

    /// <summary>Remove all temporary working directories created during runs.</summary>
    public static Task CleanupWorkDirs()
    {
        var dirs = _workDirs.ToArray();
        _workDirs.Clear();
        return Task.WhenAll(dirs.Select(dir =>
        {
            try { Directory.Delete(dir, true); } catch { }
            return Task.CompletedTask;
        }));
    }

    public static bool CheckPermission(PermissionRequest request, string workDir, string? skillPath, string? pluginRoot = null)
    {
        string? reqPath = null;
        if (request.ExtensionData is { } data)
        {
            if (data.TryGetValue("path", out var pathVal) && pathVal is JsonElement pathEl && pathEl.ValueKind == JsonValueKind.String)
                reqPath = pathEl.GetString() ?? "";
            else if (data.TryGetValue("command", out var cmdVal) && cmdVal is JsonElement cmdEl && cmdEl.ValueKind == JsonValueKind.String)
                reqPath = cmdEl.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(reqPath)) return true;

        var resolved = Path.GetFullPath(reqPath);
        var allowedDirs = new List<string> { Path.GetFullPath(workDir) };
        if (skillPath is not null) allowedDirs.Add(Path.GetFullPath(skillPath));
        if (pluginRoot is not null) allowedDirs.Add(Path.GetFullPath(pluginRoot));

        return allowedDirs.Any(dir =>
            resolved.Equals(dir, StringComparison.OrdinalIgnoreCase) ||
            resolved.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    internal static SessionConfig BuildSessionConfig(
        SkillInfo? skill, string? pluginRoot, string model, string workDir,
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers = null,
        IReadOnlyList<SkillInfo>? additionalSkills = null)
    {
        // The SDK expects SkillDirectories entries to be parent directories that
        // it scans for child folders containing SKILL.md.
        var skillPath = skill is not null ? Path.GetDirectoryName(skill.Path) : null;

        // Create a unique temporary config directory for this session to not share any data
        var configDir = Path.Combine(Path.GetTempPath(), $"sv-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);
        _workDirs.Add(configDir);

        // Build additional noise skill directories when noise testing is active.
        // For additional skills we stage a temp directory with copies of each
        // skill's SKILL.md so the SDK discovers exactly those skills — not
        // every sibling that happens to share the same parent directory.
        var noiseDirs = new List<string>();
        if (additionalSkills is { Count: > 0 })
        {
            var stageDir = Path.Combine(Path.GetTempPath(), $"sv-noise-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stageDir);
            _workDirs.Add(stageDir);

            foreach (var s in additionalSkills)
            {
                var skillMdPath = Path.Combine(s.Path, "SKILL.md");
                if (!File.Exists(skillMdPath))
                    continue;

                var stagedSkillDir = Path.Combine(stageDir, Path.GetFileName(s.Path));
                Directory.CreateDirectory(stagedSkillDir);
                File.Copy(skillMdPath, Path.Combine(stagedSkillDir, "SKILL.md"));
            }

            noiseDirs.Add(stageDir);
        }

        // Convert MCPServerDef records to the SDK's Dictionary<string, object> shape
        Dictionary<string, object>? sdkMcp = ConvertMcpServers(mcpServers);

        // Three run types:
        // 1. Baseline (skill == null, pluginRoot == null): no skills, no MCP.
        // 2. Skilled-isolated (skill != null, pluginRoot == null): single skill via SkillDirectories.
        // 3. Skilled-plugin (skill != null, pluginRoot != null): entire plugin loaded manually
        //    via SkillDirectories (--plugin-dir is NOT honored by SDK).
        //
        // For skilled-plugin, we enumerate all skill directories from plugin.json
        // so that all sibling skills are loaded, matching production behavior.
        string[] skillDirs;
        if (pluginRoot is not null)
        {
            skillDirs = ResolvePluginSkillDirectories(pluginRoot);
        }
        else if (skill is not null)
        {
            skillDirs = [skillPath!];
        }
        else
        {
            skillDirs = [];
        }

        return new SessionConfig
        {
            Model = model,
            Streaming = true,
            WorkingDirectory = workDir,
            SkillDirectories = [..skillDirs, ..noiseDirs],
            ConfigDir = configDir,
            McpServers = sdkMcp,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            OnPermissionRequest = (request, _) =>
            {
                var result = CheckPermission(request, workDir, skillPath, pluginRoot);
                return Task.FromResult(new PermissionRequestResult
                {
                    Kind = result ? PermissionRequestResultKind.Approved : PermissionRequestResultKind.DeniedByRules,
                });
            },
        };
    }

    /// <summary>
    /// Resolves the skill directories for a plugin by reading its plugin.json
    /// and returning the resolved skills path. The SDK scans this directory
    /// for subdirectories containing SKILL.md files.
    /// </summary>
    internal static string[] ResolvePluginSkillDirectories(string pluginRoot)
    {
        var pluginJsonPath = Path.Combine(pluginRoot, "plugin.json");
        PluginInfo? pluginInfo;
        try
        {
            pluginInfo = PluginValidator.ParsePluginJson(pluginJsonPath);
        }
        catch (JsonException)
        {
            // Malformed plugin.json — return empty so the session is created
            // without extra skill directories; validation surfaces the real error.
            return [];
        }
        if (pluginInfo?.SkillsPath is null) return [];

        if (!PluginValidator.TryGetSafeSubdirectory(
                pluginRoot, pluginInfo.SkillsPath, out var skillsDir, out _))
            return [];

        if (!Directory.Exists(skillsDir!)) return [];

        return [skillsDir!];
    }

    private static Dictionary<string, object>? ConvertMcpServers(
        IReadOnlyDictionary<string, MCPServerDef>? mcpServers)
    {
        if (mcpServers is not { Count: > 0 })
            return null;

        var sdkMcp = new Dictionary<string, object>();
        foreach (var (name, def) in mcpServers)
        {
            var entry = new Dictionary<string, object>
            {
                ["type"] = def.Type ?? "stdio",
                ["command"] = def.Command,
                ["args"] = def.Args,
                ["tools"] = def.Tools ?? ["*"],
            };
            if (def.Env is not null) entry["env"] = def.Env;
            if (def.Cwd is not null) entry["cwd"] = def.Cwd;
            sdkMcp[name] = entry;
        }
        return sdkMcp;
    }

    public static async Task<RunMetrics> RunAgent(RunOptions options)
    {
        var runType = options.Skill is null ? "baseline"
            : options.PluginRoot is not null ? "skilled-plugin"
            : "skilled-isolated";
        return await RetryHelper.ExecuteWithRetry(
            async ct => await RunAgentCore(options, ct),
            label: $"RunAgent({options.Scenario.Name}, {runType})",
            maxRetries: 2,
            baseDelayMs: 5_000,
            totalTimeoutMs: (options.Scenario.Timeout + 60) * 1000);
    }

    private static async Task<RunMetrics> RunAgentCore(RunOptions options, CancellationToken cancellationToken)
    {
        var workDir = await SetupWorkDir(options.Scenario, options.Skill?.Path, options.EvalPath);
        if (options.Verbose)
        {
            var write = options.Log ?? (msg => Console.Error.WriteLine(msg));
            write($"      📂 Work dir: {workDir} ({(options.Skill is not null ? "skilled" : "baseline")})");
        }

        var events = new List<AgentEvent>();
        string agentOutput = "";
        var startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool timedOut = false;

        try
        {
            // All runs use the shared client — plugin skills are loaded manually
            // via SkillDirectories (--plugin-dir is not honored by SDK).
            var client = await GetSharedClient(options.Verbose);

            await using var session = await client.CreateSessionAsync(
                BuildSessionConfig(options.Skill, options.PluginRoot, options.Model, workDir, options.Skill?.McpServers, options.AdditionalSkills));

            var done = new TaskCompletionSource();
            var effectiveTimeout = options.Scenario.Timeout;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout * 1000);
            cts.Token.Register(() =>
                done.TrySetException(new TimeoutException($"Scenario timed out after {effectiveTimeout}s")));

            session.On(evt =>
            {
                var agentEvent = new AgentEvent(
                    evt.Type,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    []);

                // Copy known event data
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta:
                        agentEvent.Data["deltaContent"] = JsonValue.Create(delta.Data.DeltaContent);
                        agentOutput += delta.Data.DeltaContent ?? "";
                        break;
                    case AssistantMessageEvent msg:
                        agentEvent.Data["content"] = JsonValue.Create(msg.Data.Content);
                        if (!string.IsNullOrEmpty(msg.Data.Content))
                            agentOutput = msg.Data.Content;
                        break;
                    case ToolExecutionStartEvent toolStart:
                        agentEvent.Data["toolName"] = JsonValue.Create(toolStart.Data.ToolName);
                        agentEvent.Data["arguments"] = JsonValue.Create(toolStart.Data.Arguments?.ToString());
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      🔧 {toolStart.Data.ToolName}");
                        }
                        break;
                    case ToolExecutionCompleteEvent toolComplete:
                        agentEvent.Data["success"] = JsonValue.Create(toolComplete.Data.Success.ToString());
                        agentEvent.Data["result"] = JsonValue.Create(toolComplete.Data.Result?.Content ?? toolComplete.Data.Error?.Message ?? "");
                        break;
                    case SkillInvokedEvent skillInvoked:
                        agentEvent.Data["name"] = JsonValue.Create(skillInvoked.Data.Name);
                        agentEvent.Data["path"] = JsonValue.Create(skillInvoked.Data.Path);
                        if (skillInvoked.Data.AllowedTools is { } allowedTools)
                        {
                            var arr = new JsonArray();
                            foreach (var tool in allowedTools)
                                arr.Add((JsonNode?)JsonValue.Create(tool));
                            agentEvent.Data["allowedTools"] = arr;
                        }
                        if (options.Verbose)
                        {
                            var write = options.Log ?? (m => Console.Error.WriteLine(m));
                            write($"      📘 Skill invoked: {skillInvoked.Data.Name}");
                        }
                        break;
                    case AssistantUsageEvent usage:
                        agentEvent.Data["inputTokens"] = JsonValue.Create(usage.Data.InputTokens);
                        agentEvent.Data["outputTokens"] = JsonValue.Create(usage.Data.OutputTokens);
                        agentEvent.Data["model"] = JsonValue.Create(usage.Data.Model);
                        break;
                    case UserMessageEvent userMsg:
                        agentEvent.Data["content"] = JsonValue.Create(userMsg.Data.Content);
                        break;
                    case SessionIdleEvent:
                        done.TrySetResult();
                        break;
                    case SessionErrorEvent err:
                        agentEvent.Data["message"] = JsonValue.Create(err.Data.Message);
                        done.TrySetException(new InvalidOperationException(err.Data.Message ?? "Session error"));
                        break;
                }

                events.Add(agentEvent);
            });

            await session.SendAsync(new MessageOptions { Prompt = options.Scenario.Prompt });
            await done.Task;
        }
        catch (TimeoutException te)
        {
            timedOut = true;
            events.Add(new AgentEvent(
                "runner.error",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(te.ToString()) }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Budget exhausted — let RetryHelper handle it.
        }
        catch (Exception error)
        {
            var msg = error.ToString();

            // Re-throw rate-limit (429) errors so RetryHelper can retry them.
            if (msg.Contains("429", StringComparison.Ordinal)
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }

            if (error is TimeoutException || error.InnerException is TimeoutException
                || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                // Timeout: record a dedicated event (the timer fired, no session.error exists)
                events.Add(new AgentEvent(
                    "runner.timeout",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
            else if (!events.Any(e => e.Type == "session.error"))
            {
                // Only add runner.error when there isn't already a session.error event
                events.Add(new AgentEvent(
                    "runner.error",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    new Dictionary<string, JsonNode?> { ["message"] = JsonValue.Create(msg) }));
            }
        }

        var wallTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
        var metrics = MetricsCollector.CollectMetrics(events, agentOutput, wallTimeMs, workDir);
        metrics.TimedOut = timedOut;
        return metrics;
    }

    private static async Task<string> SetupWorkDir(EvalScenario scenario, string? skillPath, string? evalPath)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"sv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        _workDirs.Add(workDir);

        // Copy all sibling files from the eval directory when opted in
        if (evalPath is not null && scenario.Setup?.CopyTestFiles == true)
        {
            var evalDir = Path.GetDirectoryName(evalPath)!;
            foreach (var entry in new DirectoryInfo(evalDir).EnumerateFileSystemInfos())
            {
                if (entry.Name == "eval.yaml") continue;
                var dest = Path.Combine(workDir, entry.Name);
                if (entry is DirectoryInfo dir)
                    CopyDirectory(dir.FullName, dest);
                else if (entry is FileInfo file)
                    file.CopyTo(dest, true);
            }
        }

        // Explicit setup files override/supplement auto-copied files
        if (scenario.Setup?.Files is { } files)
        {
            foreach (var file in files)
            {
                var targetPath = Path.Combine(workDir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                if (file.Content is not null)
                {
                    await File.WriteAllTextAsync(targetPath, file.Content);
                }
                else if (file.Source is not null && skillPath is not null)
                {
                    var sourcePath = Path.Combine(skillPath, file.Source);
                    File.Copy(sourcePath, targetPath, true);
                }
            }
        }

        // Run setup commands (e.g. build to produce a binlog, then strip sources)
        if (scenario.Setup?.Commands is { } commands)
        {
            foreach (var cmd in commands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                        Arguments = OperatingSystem.IsWindows() ? $"/c {cmd}" : $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                        WorkingDirectory = workDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    using var proc = Process.Start(psi);
                    if (proc is not null)
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                        await proc.WaitForExitAsync(cts.Token);
                    }
                }
                catch
                {
                    // Setup commands may return non-zero exit codes
                    // (e.g. building a broken project to produce a binlog)
                }
            }
        }

        return workDir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
