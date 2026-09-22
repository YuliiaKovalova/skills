# Troubleshoot build issues

Use this path for build failures and incorrect build behavior. A style review without a failing
scenario belongs in [modernization](modernize.md); a successful but slow build belongs in
[performance](troubleshoot-performance.md).

## 1. Choose the available evidence

| Evidence | Action |
| --- | --- |
| Existing binary log, with or without a checkout | Replay that log with MSBuild. Original files need not exist locally. |
| Reproducible failure, but no useful log | Follow [binlog generation](binlog-generation.md) once using the original invocation. |
| Clear source-level authoring problem or read-only review | Inspect the relevant project/imports directly; do not require a build just to answer. |
| Only a console error and no reproduction | Explain what the error proves, identify the next missing evidence, and label the cause as provisional. |

Follow the shared [MSBuild replay workflow](binlog-generation.md#replay-an-existing-log).
Never read a binary log with text-file tools, `strings`, or a text search. Start with the recorded
build result and errors in the replayed text, then inspect only the project, target, task, property,
and item evidence that explains those errors.

## 2. Establish causes, not just the first printed error

1. Record the invocation, failed project **instance**, error code/message, target, and task.
   The same project path may have several configurations, target frameworks, or global-property
   sets. Do not merge their evidence.
2. Trace failures through project/target dependencies. Parallel console ordering is not a causal
   ordering. An absent referenced assembly can be downstream of an earlier failed producer;
   another project may have a separate independent root cause.
3. Inspect the evaluated values and imported definitions recorded in the diagnostic text. A
   property written in the project body may not be the value seen by a task. Check available
   item metadata, conditions, import order, and command-line global properties; identify any
   missing data explicitly.
4. Read local source only when it exists in the current checkout. Otherwise use the recorded
   definitions and messages that replay exposes. Embedded source is not necessarily emitted as
   text; if the responsible definition is unavailable, report the limitation instead of inventing
   a file, value, or exact edit location.

For each independent cause, connect the error to the responsible definition and the smallest
repair. A compiler error mentioning a namespace alone does not prove which package to add;
confirm the relevant references, target framework, and source context.

## 3. Select a focused correction

| Observed evidence | Targeted follow-up | Avoid |
| --- | --- | --- |
| Missing import or hook | Check [extension points](extension-points.md), including optional versus required imports and the packed NuGet layout. | Adding `Exists()` to hide a required missing dependency. |
| Property unexpectedly empty/overwritten | Trace evaluation order and global properties; use [anti-patterns](antipatterns.md#property-and-item-evaluation). | Moving every item/property to `.targets` indiscriminately. |
| Duplicate items or missing generated inputs | Check implicit SDK items, `Include` versus `Update`, generation timing, and F# ordering. | Deleting all explicit includes. |
| File locks, duplicate builds, shared `bin`/`obj` | Compare project instances and their output paths using [anti-patterns](antipatterns.md#project-instances-and-output-collisions). | Treating `-m:1` or repeated clean builds as the permanent fix. |
| Stale output or a target incorrectly skipped | Follow [incremental diagnosis](troubleshoot-performance.md), preserving correctness before speed. | Forcing every build to rebuild, or adding a stamp that ignores real inputs. |
| Restore failure or incompatible assets | Inspect the actual restore error, selected framework and package assets; use NuGet-specific guidance if the problem is package management. | Suppressing restore or blindly upgrading packages. |

Consult only the relevant [anti-pattern](antipatterns.md) checks. A targeted build fix is not
permission to modernize the entire solution.

## 4. Verify and report

When changes are possible, rerun the original failing scenario with a new log and the same
configuration/framework/properties. Confirm the independent root errors are gone, not merely that
the console is quieter. Exercise affected dependent projects and relevant tests. If targets,
items, or output paths changed, also check a no-change build and the affected incremental case.

For artifact-only analysis, provide evidence-backed suggested changes and explicitly state that
they were not applied or rebuilt. Stop investigating once the actionable causes and remaining
uncertainties are clear; do not spend the entire task collecting unrelated log data.
