# Capture a binary log

Capture is a shared prerequisite **when needed**, not a separate mandatory workflow for every
build command. If the supplied log already records the relevant failure or performance scenario,
analyze it first. Do not rerun a build just to rename its log.

## Preserve the operation

Append the binary logger switch to the existing MSBuild invocation. Keep its entry point, working
directory, target, configuration, framework, runtime, restore behavior, and global properties.
Do not replace a repository wrapper with a bare `dotnet build` unless the wrapper's equivalent
invocation is known.

For MSBuild 17.8+ (.NET 8 SDK+), quote a filename containing `{}` to get a distinct artifact:

```powershell
dotnet build App.sln -c Release --no-restore "-bl:build-{}.binlog"
dotnet msbuild App.csproj -t:Pack "-bl:pack-{}.binlog"
```

The quotes pass the braces literally in PowerShell, without treating them as a script block.
The same quoted argument works in common command shells. These are examples: retain the user's
actual arguments rather than substituting these project names or adding `--no-restore`.

For an older MSBuild, or a CI uploader that requires a known name, first check existing artifacts
and choose an unused explicit name such as `failure-02.binlog`. Every invocation in a comparison
or retry sequence needs a different name. Do not use bare `-bl`, which reuses `msbuild.binlog`.

`dotnet build`, `restore`, `pack`, and `publish` accept MSBuild logger switches. Test runners and
wrapper scripts differ: only forward `-bl` through a supported MSBuild argument surface. A
Microsoft.Testing.Platform-native invocation or `dotnet test --no-build` is not automatically a
request to capture a new build.

## Verify the artifact, including on failure

Record the command's exit code and locate the newly created, nonempty `.binlog` before starting
analysis:

```powershell
Get-ChildItem -File *.binlog | Select-Object Name, Length, LastWriteTime
```

An intentional failing build can still produce a useful log. Failure before MSBuild starts
(missing SDK, invalid command-line arguments, wrapper rejection) may produce none. Report that
condition; do not invent a path or repeatedly rebuild without addressing it.

Keep the path, original exit code, and scenario together. For a capture-only request, this is the
deliverable. For an investigation, return to the selected task reference.

## Privacy and retention

Binary logs can contain command lines, environment/property values, credentials, paths, and
embedded project/import contents. Keep them local by default and out of commits. Preserve existing
logs during any approved cleanup; do not use a repository-wide clean to prepare capture.

If embedding imported files is inappropriate, use a quoted logger argument such as:

```powershell
dotnet build App.csproj "-bl:build-{}.binlog;ProjectImports=None"
```

`ProjectImports=None` reduces embedded source coverage; it does **not** redact secrets from
properties, environment values, task arguments, or messages. Explain the diagnostic tradeoff,
and review/redact artifacts before any authorized sharing.
