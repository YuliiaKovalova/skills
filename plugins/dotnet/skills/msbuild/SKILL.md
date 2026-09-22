---
name: msbuild
description: "Diagnose MSBuild failures, optimize .NET builds, and modernize project/build files. USE FOR: failing dotnet build, restore, pack, publish, or MSBuild-based test builds; .binlog analysis or capture; slow builds, evaluation, repeated compilation, copying, or broken incremental builds; reviewing or modernizing .csproj, .vbproj, .fsproj, .props, .targets, and Directory.Build files; SDK-style conversion and build extension hooks. Start here when the right build workflow is unclear. DO NOT USE FOR: application runtime CPU/memory performance (dotnet-diag), failing test assertions (dotnet-test), C# source refactoring (csharp-refactoring), SDK installation alone (setup-local-sdk), framework/API upgrades (dotnet-upgrade), or non-MSBuild build systems."
license: MIT
---

# MSBuild troubleshooting

Use this entry point to choose **one task**, gather the evidence that task needs, and make the
smallest justified change. Detailed guidance is bundled as references, not additional skills to
activate. Do not load the entire reference collection or run every diagnostic on every request.

## 1. Establish the task and available evidence

Discover the relevant solution/project, shared build files, SDK selection (`global.json`), and
repository build instructions in the current workspace. Preserve the user's actual command,
working directory, configuration, target framework, runtime identifier, properties, and targets.
Ask only for consequential information that is not available locally or in the supplied evidence.

An existing `.binlog` may be the **only** artifact available. Treat paths recorded in it as build
identities, not proof that those files exist on this machine. Do not search other checkouts,
recreate missing projects, or rerun an unavailable build just to start analysis.

| Task or symptom | Read next | First decision |
| --- | --- | --- |
| Failed build/restore, unexplained errors, wrong items/properties, missing imports, intermittent output conflicts | [Troubleshoot issues](references/troubleshoot-issues.md) | Separate independent causes from downstream symptoms before editing. |
| Slow build/evaluation, repeated work, unnecessary copies, broken no-change or incremental builds | [Troubleshoot performance](references/troubleshoot-performance.md) | Identify the measured scenario and bottleneck before choosing a fix. |
| SDK-style conversion, project-file cleanup, shared settings, extensibility or import/hook modernization | [Modernize](references/modernize.md) | Establish which existing behavior must remain unchanged. |
| Capture a binary log, with no diagnosis requested yet | [Binlog generation](references/binlog-generation.md) | Preserve the command, produce a new artifact, report its path, and stop. |

For mixed requests, resolve correctness failures before timing a successful build. Keep requested
modernization separate from the failure fix or performance experiment so its effects are attributable.
A failing test assertion after a successful build is a test problem, not an MSBuild failure.

## 2. Load only the guidance needed for that task

The task references route to shared detail when evidence calls for it:

- [Binary-log capture and replay](references/binlog-generation.md): replay existing logs with
  MSBuild; capture only when evidence is missing or a new comparison is necessary. Neither is a
  prerequisite for a static review or an advisory answer.
- [Anti-patterns](references/antipatterns.md): correctness and cleanup decisions, including the
  exceptions that prevent destructive "fixes."
- [Extension points](references/extension-points.md): import order, hooks, shared files, and NuGet
  packed-layout checks. These are MSBuild extension points, not agent/plugin extensions.

Resolve bundled references relative to this skill's directory. Do not search for another plugin's
installation or require the standalone `dotnet-msbuild` skills. If a bundled file is missing, allow
one listing of `references`, report the missing guidance and reduced coverage, and do not claim to
have followed it.

## 3. Investigate, change, and verify within scope

1. Reuse the available source and artifacts. Follow the shared
   [MSBuild replay workflow](references/binlog-generation.md#replay-an-existing-log) and inspect
   the emitted text logs. Never treat a `.binlog` as a text file or rerun an unavailable build
   just to analyze its recorded events.
2. Form a specific explanation supported by the failing project/target/task, evaluated properties
   or items, timestamps, or comparable timings. Distinguish measured facts from hypotheses.
3. Apply a targeted change only when the user requested changes and the evidence supports it.
   Preserve working behavior, intentional exceptions, framework/package versions, and unrelated
   edits. For an already-correct input, explain why and leave it unchanged.
4. Verify the original scenario and the affected behavior. A failed restore/build is not a
   successful fix; a replayed log is not a new build; one faster run is not a demonstrated
   performance improvement. Follow the selected task's validation requirements.

Do not clean the repository, delete caches or outputs, disable analyzers, change SDKs, or turn off
parallelism as a generic first step. Scope any necessary destructive reset to known generated
outputs with approval and preserve diagnostic artifacts. Never upload or commit binary or
replayed text logs without checking their sensitive contents and the user's permission.

If a missing SDK, unavailable tool, unsupported project type, or absent artifact blocks the next
step, state the blocker and what can still be concluded. Do not silently replace measured evidence
with assumptions or install unrelated tooling.

## Completion

Give a compact result appropriate to the task:

- The selected task and the root cause, bottleneck, or modernization decision.
- Supporting evidence: source location or recorded project/target/error; for performance, the
  scenario, comparable measurements, and remaining uncertainty.
- The minimal change made or proposed, or why no change is appropriate.
- The exact verification performed and its result, clearly distinguishing successful, failed, and
  not-run checks. If blocked, name the missing evidence or prerequisite.

For multiple independent failures, use a short findings table. For a simple capture or no-op
review, a short answer is sufficient.
