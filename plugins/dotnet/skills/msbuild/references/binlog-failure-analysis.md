# Analyzing MSBuild failures with binary logs

Based on the original `binlog-failure-analysis` skill. Diagnose an existing `.binlog` with available
structured tools or MSBuild's built-in replay, including when the original checkout is unavailable.

## Before analysis

- Reuse a log that records the relevant invocation. If none exists and the build can be reproduced,
  follow [binlog-generation](binlog-generation.md) with the original arguments.
- A `.binlog` is binary. Do not use a text reader, `strings`, or text search directly on it.
- Recorded paths identify the original build; they are not permission to search unrelated
  checkouts or proof that source/project files exist locally.
- Synthesize findings as evidence emerges. Stop once an actionable cause and its initiating rule
  are evidenced, or the remaining causal gap is explicitly bounded. Do not collect unrelated events.

## Choose the evidence path

Use available structured binlog tools, including an enabled MCP server, for scoped errors, project
instances, task inputs, and captured source. Discover their capabilities rather than guessing tool
names. If they are unavailable or cannot answer the question, use replay below. Neither route
requires installing a new server or constructing a custom binary-log reader.

Scope negative findings to captured evaluations, project instances, event/task types, filters, and
truncation. A missing property is not necessarily unset; a Copy-only view excludes other writers.
When coverage does not include the failing task's inputs or producer, change the evidence query
rather than concluding that a file, shared output, or dependency does not exist.

## Replay a binary log

Choose unused text-log filenames, substitute the supplied binary-log path, and replay it:

```powershell
dotnet msbuild ".\build.binlog" -noconlog -fl "-flp:logfile=full-01.log;verbosity=diagnostic;PerformanceSummary" -fl1 "-flp1:logfile=errors-01.log;errorsonly" -fl2 "-flp2:logfile=warnings-01.log;warningsonly"
```

Quote each complete semicolon-delimited logger argument, especially in PowerShell. The command
reads the recorded build; it does not restore or rebuild its projects. A compatible `MSBuild.exe`
can replace `dotnet msbuild`; no analysis server or extra package is required.

Confirm the command completed and the diagnostic log is nonempty. Errors-only or warnings-only
logs can legitimately be empty. If the installed MSBuild cannot read the binary-log format,
report that blocker and the need for compatible tooling instead of pretending analysis succeeded.

Replay success is **not** proof that the recorded build succeeded. Replay duration is **not**
the recorded build's elapsed time. The same replay mechanics serve
[performance diagnostics](build-perf-diagnostics.md): use recorded events and the Project,
Target, and Task Performance Summaries, not the time spent converting the log.

## Inspect the replayed text

Start with errors, then search the diagnostic text around the actual codes, targets, and projects:

```powershell
Get-Content -LiteralPath .\errors-01.log
Select-String -LiteralPath .\full-01.log -Pattern 'CS0246', 'CoreCompile', 'Build FAILED' -Context 2
```

The patterns above are examples, not a filter that should discard other error codes. Inspect the
recorded evidence needed to explain the failure:

- Build errors and warnings, including custom task errors.
- Project-instance identity, configuration, target framework, and global properties.
- Property assignments and item/reference metadata visible in the log.
- Evaluation, import, target, and task execution or skip messages.

Text replay cannot manufacture data that was not recorded and may omit embedded project/import
source. If a needed definition is unavailable locally, inspect source captured in the supplied
binlog using an available compatible reader. Check inventory coverage before treating a filename
lookup as proof of absence. Distinguish a source declaration from the value a task actually consumed.
If accessible source and recorded events do not resolve the question, state the specific evidence
gap; do not invent file contents or an exact edit location.

## Find independent causes

1. Associate each error with its project **instance**, target, and task. The same project path
   under different global properties can represent distinct builds.
2. Follow producer/consumer dependencies. Parallel log order is not causal order: a missing
   referenced assembly may be downstream of a failed producer, while another project has an
   independent failure. Identify the invocation that produced the consumed artifact, not a later
   metadata-only invocation of the same project.
3. Inspect the actual values seen by the failing task rather than assuming the project body's
   value won. Trace decisive values to their originating input or transformation: conditions,
   imports, item metadata, command-line global properties, or solution configuration mappings.
   Real intermediate metadata does not by itself establish the initiating cause.
4. Connect each root cause to a minimal repair. A namespace error alone does not prove which
   package should be added; confirm the relevant references and target framework.

Apply these checks only when the corresponding failure needs them:

| Failure | Check before concluding |
| --- | --- |
| Assembly resolution or version conflict | Keep package version, selected asset framework, assembly identity, original requested identity, and post-unification identity distinct. A resolver's reported dependency may already be transformed. An `AppConfigFile` path does not reveal its XML; an enabled property does not prove a target ran or produced output. |
| Compiler errors across frameworks | Summarize error codes by project instance/framework. Separate undefined-symbol cascades from independent errors on supported targets, and distinguish distinct diagnostics from repeated occurrences. |
| Shared outputs, signing, or access denial | Trace the failing task's actual input collection, artifact producer, and ownership. Access denial alone does not identify a lock, ACL, read-only attribute, hard link, or cache policy; state what recorded evidence distinguishes the mechanisms. |

Use [msbuild-antipatterns](msbuild-antipatterns.md) for a demonstrated authoring defect,
[extension-points](extension-points.md) for imports/hooks or packed-layout issues, and
[incremental-build](incremental-build.md) for stale outputs or incorrect skip decisions.
For confirmed shared-output ownership problems, reuse the
[ownership and isolation guidance](antipatterns/additional-antipatterns.md#ap-22-project-instances-with-path-neutral-global-properties).
Do not turn failure diagnosis into an unrequested modernization pass.

## Verify and report

If source can be changed, rerun the original failing scenario with a new binary log, preserving
its configuration/framework/properties. Confirm the independent root errors and their cascades
are resolved, and exercise affected dependents. Check a no-change build when targets/items/outputs
were changed.

Before finalizing a repair, explain how it changes the relevant inputs or output ownership while
preserving downstream requirements. Removing a framework or disabling copying/signing needs an
explicit coverage consequence and a remaining owner for required work, not merely a plausible
green build.

For artifact-only analysis, provide proposed changes and explicitly say they were not applied
or rebuilt. Connect the observed failure, initiating rule or bounded uncertainty, and predicted
repair effect; do not present that prediction as verification. Keep this proportional to the task,
not an exhaustive evidence ledger. Treat replayed text as sensitive diagnostic data like its binlog.
