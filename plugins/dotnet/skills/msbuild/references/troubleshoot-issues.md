# Troubleshoot build issues

Use this path for build failures and incorrect build behavior. A style review without a failing
scenario belongs in [modernization](modernize.md); a successful but slow build belongs in
[performance](troubleshoot-performance.md).

## 1. Choose the available evidence

| Evidence | Action |
| --- | --- |
| Existing binary log, with or without a checkout | Inspect that log. Original files need not exist locally. |
| Reproducible failure, but no useful log | Follow [binlog generation](binlog-generation.md) once using the original invocation. |
| Clear source-level authoring problem or read-only review | Inspect the relevant project/imports directly; do not require a build just to answer. |
| Only a console error and no reproduction | Explain what the error proves, identify the next missing evidence, and label the cause as provisional. |

Never read a binary log with text-file tools, `strings`, or a text search. Discover the available
`binlog` MCP tools and use their structured queries. Start with the build result and errors; query
only the projects, targets, properties, items, and embedded files that explain those errors.

## 2. Establish causes, not just the first printed error

1. Record the invocation, failed project **instance**, error code/message, target, and task.
   The same project path may have several configurations, target frameworks, or global-property
   sets. Do not merge their evidence.
2. Trace failures through project/target dependencies. Parallel console ordering is not a causal
   ordering. An absent referenced assembly can be downstream of an earlier failed producer;
   another project may have a separate independent root cause.
3. Inspect the evaluated values and imported definitions that caused the failure. A property
   written in the project body may not be the value seen by a task. Check item metadata,
   conditions, import order, and command-line global properties.
4. Read local source only when it exists in the current checkout. Otherwise retrieve embedded
   project/import content from the log if available. If source was not embedded, report that
   limitation instead of inventing a file or exact edit location.

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

## 4. Fallback when the MCP server is unavailable

Replay the existing log through MSBuild into **new** text-log paths. This does not rebuild the
original checkout and can work when that checkout is unavailable:

```powershell
dotnet msbuild failure.binlog -noconlog -fl "-flp:logfile=failure-01.log;verbosity=diagnostic;PerformanceSummary" -fl1 "-flp1:logfile=errors-01.log;errorsonly" -fl2 "-flp2:logfile=warnings-01.log;warningsonly"
```

Choose unused filenames before running the command. Quoting the complete semicolon-delimited
logger arguments is necessary in PowerShell and also works in common command shells. Search the
resulting **text** logs around error codes, failed targets, and producer/consumer projects.

Replay's exit code is not evidence that the original build succeeded. Text logs also do not
replace every structured query or embedded-file retrieval capability. If replay cannot read a
newer log, or necessary source/evaluation data is unavailable, state the reduced coverage and
request compatible tooling or the missing artifact; never pretend the log was analyzed.

## 5. Verify and report

When changes are possible, rerun the original failing scenario with a new log and the same
configuration/framework/properties. Confirm the independent root errors are gone, not merely that
the console is quieter. Exercise affected dependent projects and relevant tests. If targets,
items, or output paths changed, also check a no-change build and the affected incremental case.

For artifact-only analysis, provide evidence-backed suggested changes and explicitly state that
they were not applied or rebuilt. Stop investigating once the actionable causes and remaining
uncertainties are clear; do not spend the entire task collecting unrelated log data.
