# dotnet

Core .NET and C# skills for coding agents.

## Includes

- Common .NET development skills
- A task-routed MSBuild troubleshooting skill using MSBuild binary-log replay
- A C# language server integration for `.cs` files

## LSP

This plugin declares a C# LSP server that is launched through the .NET CLI.
The LSP declaration is available to hosts that support the plugin `lspServers` extension. Codex
plugin installs expose this plugin's skills but do not load that host-specific LSP declaration.

Prerequisites:
- .NET 10 SDK installed
- `dotnet` available on PATH

## Skills

- [csharp-refactoring](skills/csharp-refactoring/SKILL.md)
- [msbuild: MSBuild troubleshooting](skills/msbuild/SKILL.md)
- [setup-local-sdk](skills/setup-local-sdk/SKILL.md)

## MSBuild starting point

`msbuild` is the general-purpose AI entry point for build fixes, build performance, and
project/build-file modernization. Its description is the only additional MSBuild skill-menu
entry in this plugin. The body selects a task before loading detailed references:

```text
skills/msbuild/
  SKILL.md                         # Entry: classify, route, verify
  references/
    troubleshoot-issues.md         # Failure diagnosis and incorrect build behavior
    troubleshoot-performance.md    # Measure, classify, change, compare
    modernize.md                   # Preserve contracts while updating build files
    binlog-generation.md           # Shared capture and MSBuild replay
    antipatterns.md                # Shared correctness and cleanup decisions
    extension-points.md            # Imports, hooks, and package layout
    build-perf-baseline.md         # Detailed baseline/optimization protocol
```

These are plain references, not independently activated skills, custom agents, or plugin
extensions. Start with one task reference; load common detail only when that investigation needs
it. Correctness comes before timing, and modernization is not an implicit part of every build fix.

### Structural overview and source mapping

The source is the existing [`dotnet-msbuild`](../dotnet-msbuild/) plugin. The consolidation
reorganizes its decision-making into task workflows rather than concatenating skill bodies.

| Source skill | Consolidated destination | Structural decision |
| --- | --- | --- |
| [binlog-failure-analysis](../dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md) | [Troubleshoot issues](skills/msbuild/references/troubleshoot-issues.md) | Artifact-first diagnosis through MSBuild replay, causal versus cascading failures, and explicit limits on source/evaluation data. |
| [binlog-generation](../dotnet-msbuild/skills/binlog-generation/SKILL.md) | [Shared capture and replay](skills/msbuild/references/binlog-generation.md) | Preserve the original command, capture only when needed, replay existing logs without rebuilding, and protect diagnostic artifacts. |
| [build-perf-baseline](../dotnet-msbuild/skills/build-perf-baseline/SKILL.md) | [Baseline detail](skills/msbuild/references/build-perf-baseline.md) | Preserve measurement rigor and optimization domains behind the performance route; further simplification is deferred. |
| [build-perf-diagnostics](../dotnet-msbuild/skills/build-perf-diagnostics/SKILL.md) | [Performance diagnosis](skills/msbuild/references/troubleshoot-performance.md#2-classify-the-bottleneck) | Choose a bottleneck from evidence instead of applying a universal tuning checklist. |
| [copy-to-output-directory](../dotnet-msbuild/skills/copy-to-output-directory/SKILL.md) | [Copy and output I/O](skills/msbuild/references/troubleshoot-performance.md#copy-and-output-io) | Keep version gates and destination-mutation semantics; copying less must not leave stale output. |
| [eval-performance](../dotnet-msbuild/skills/eval-performance/SKILL.md) | [Evaluation](skills/msbuild/references/troubleshoot-performance.md#evaluation-before-target-execution) | Separate evaluation from target execution, and avoid speculative glob/import rewrites. |
| [incremental-build](../dotnet-msbuild/skills/incremental-build/SKILL.md) | [Incremental diagnosis](skills/msbuild/references/troubleshoot-performance.md#unexpected-work-on-warm-or-no-change-builds) | Preserve first/no-change/changed-input/missing-output correctness, generated items, clean tracking, and IDE distinctions. |
| [extension-points](../dotnet-msbuild/skills/extension-points/SKILL.md) | [Shared extension detail](skills/msbuild/references/extension-points.md), reached from issues or modernization | Preserve existing hooks, supported import-list composition, and package contracts. |
| [msbuild-antipatterns](../dotnet-msbuild/skills/msbuild-antipatterns/SKILL.md) | [Shared authoring decisions](skills/msbuild/references/antipatterns.md) | Retain AP-01 through AP-23 and their exceptions without turning every review into an automatic rewrite. |
| [msbuild-modernization](../dotnet-msbuild/skills/msbuild-modernization/SKILL.md) | [Modernize](skills/msbuild/references/modernize.md) | Supply the conversion path while preserving TFMs, assembly attributes, package versions, resources, and configuration. |

The shared anti-pattern reference incorporates the source catalog's nested references rather than
requiring cross-plugin runtime reads. Related parallelism and project-reference guidance supports
the performance baseline; unrelated specialist authoring skills are not pulled into the entry
workflow.

The consolidation also avoids unsafe generalizations: extending a hook must not discard prior
imports; `Include` followed by `Update` in one item group is valid; F# file order is not redundant
boilerplate; and SDK-style conversion does not justify deleting custom assembly attributes or
runtime configuration.

### Compatibility and tools

The original `dotnet-msbuild` skills, agents, tests, and installation remain unchanged. Users
who need those individual specialist entry points can continue using that plugin. The new
`dotnet/msbuild` entry is self-contained and does not require it to be installed. This is an
additive consolidation, not a removal or rename of existing skill commands.

Binary-log analysis uses `dotnet msbuild <log.binlog>` with diagnostic file loggers, or a compatible
`MSBuild.exe` installation. The shared [replay instructions](skills/msbuild/references/binlog-generation.md#replay-an-existing-log)
serve both failure and performance investigations; no analysis server or extra package is
required. Replay does not rebuild the original project, and its duration is not the recorded
build's elapsed time. Missing source, evaluation, or scheduling details must be reported rather
than inferred from incomplete text logs.

### Evaluation coverage

The consolidated eval lives at [`tests/dotnet/msbuild`](../../tests/dotnet/msbuild/).
It covers artifact-only failures, capture boundaries, project-instance collisions, incremental
correctness, copy semantics, evaluation versus execution, comparable performance measurements,
contract-preserving modernization, import/hook correctness, valid no-op patterns, and off-target
requests. Existing specialist evals are retained as source coverage, not relabeled as proof of
the new entry skill's quality.
