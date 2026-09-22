# Consolidated MSBuild troubleshooting skill

[SKILL.md](SKILL.md) is the AI entry point for build fixes, build performance, and
project/build-file modernization. It selects a task before loading the relevant bundled references.
This directory is an ordinary, self-contained skill, not a replacement for an existing plugin.

## Isolated scope

The addition consists only of this new skill directory and its
[dedicated tests](../../../../tests/dotnet/msbuild/). Existing skills, agents, plugin manifests,
marketplace entries, ownership metadata, and shared documentation remain unchanged.
The existing plugin manifest already discovers skill directories through `./skills/`; no
registration or integration edits are required.

The source links below document provenance for maintainers. They are not runtime dependencies:
the entry skill reads its own bundled references and does not activate the original specialist
skills or require their plugin to be installed.

## Structure

```text
msbuild\
  README.md                        # Scope and structural overview
  SKILL.md                         # Entry: classify, route, verify
  references\
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

## Structural overview and source mapping

The source is the unchanged [`dotnet-msbuild`](../../../dotnet-msbuild/) plugin. The consolidation
reorganizes its decision-making into task workflows rather than concatenating skill bodies.

| Source skill | Consolidated destination | Structural decision |
| --- | --- | --- |
| [binlog-failure-analysis](../../../dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md) | [Troubleshoot issues](references/troubleshoot-issues.md) | Artifact-first diagnosis through MSBuild replay, causal versus cascading failures, and explicit limits on source/evaluation data. |
| [binlog-generation](../../../dotnet-msbuild/skills/binlog-generation/SKILL.md) | [Shared capture and replay](references/binlog-generation.md) | Preserve the original command, capture only when needed, replay existing logs without rebuilding, and protect diagnostic artifacts. |
| [build-perf-baseline](../../../dotnet-msbuild/skills/build-perf-baseline/SKILL.md) | [Baseline detail](references/build-perf-baseline.md) | Preserve measurement rigor and optimization domains behind the performance route; further simplification is deferred. |
| [build-perf-diagnostics](../../../dotnet-msbuild/skills/build-perf-diagnostics/SKILL.md) | [Performance diagnosis](references/troubleshoot-performance.md#2-classify-the-bottleneck) | Choose a bottleneck from evidence instead of applying a universal tuning checklist. |
| [copy-to-output-directory](../../../dotnet-msbuild/skills/copy-to-output-directory/SKILL.md) | [Copy and output I/O](references/troubleshoot-performance.md#copy-and-output-io) | Keep version gates and destination-mutation semantics; copying less must not leave stale output. |
| [eval-performance](../../../dotnet-msbuild/skills/eval-performance/SKILL.md) | [Evaluation](references/troubleshoot-performance.md#evaluation-before-target-execution) | Separate evaluation from target execution, and avoid speculative glob/import rewrites. |
| [incremental-build](../../../dotnet-msbuild/skills/incremental-build/SKILL.md) | [Incremental diagnosis](references/troubleshoot-performance.md#unexpected-work-on-warm-or-no-change-builds) | Preserve first/no-change/changed-input/missing-output correctness, generated items, clean tracking, and IDE distinctions. |
| [extension-points](../../../dotnet-msbuild/skills/extension-points/SKILL.md) | [Shared extension detail](references/extension-points.md), reached from issues or modernization | Preserve existing hooks, supported import-list composition, and package contracts. |
| [msbuild-antipatterns](../../../dotnet-msbuild/skills/msbuild-antipatterns/SKILL.md) | [Shared authoring decisions](references/antipatterns.md) | Retain AP-01 through AP-23 and their exceptions without turning every review into an automatic rewrite. |
| [msbuild-modernization](../../../dotnet-msbuild/skills/msbuild-modernization/SKILL.md) | [Modernize](references/modernize.md) | Supply the conversion path while preserving TFMs, assembly attributes, package versions, resources, and configuration. |

The shared anti-pattern reference incorporates the source catalog's nested references rather than
requiring cross-plugin runtime reads. Related parallelism and project-reference guidance supports
the performance baseline; unrelated specialist authoring skills are not pulled into the entry
workflow.

The consolidation also avoids unsafe generalizations: extending a hook must not discard prior
imports; `Include` followed by `Update` in one item group is valid; F# file order is not redundant
boilerplate; and SDK-style conversion does not justify deleting custom assembly attributes or
runtime configuration.

## Binary-log analysis

Analysis uses `dotnet msbuild <log.binlog>` with diagnostic file loggers, or a compatible
`MSBuild.exe` installation. The shared [replay instructions](references/binlog-generation.md#replay-an-existing-log)
serve both failure and performance investigations; no analysis server or extra package is
required. Replay does not rebuild the original project, and its duration is not the recorded
build's elapsed time. Missing source, evaluation, or scheduling details must be reported rather
than inferred from incomplete text logs.

## Evaluation coverage

The dedicated [eval](../../../../tests/dotnet/msbuild/eval.yaml) covers artifact-only failures,
capture boundaries, project-instance collisions, incremental correctness, copy semantics,
evaluation versus execution, comparable performance measurements, contract-preserving
modernization, import/hook correctness, valid no-op patterns, and off-target requests.
Existing specialist evals remain unchanged and are not relabeled as proof of the new entry
skill's quality.
