# Troubleshoot build performance

Measure the reported scenario, identify the work on its critical path, change one
cause, and compare equivalent builds without losing required behavior. Do not
apply a repository-wide tuning checklist.

For file-reading tools, resolve sibling references under the skill root's
`references` directory. Read the [baseline protocol](build-perf-baseline.md#measurement-protocol)
when comparable measurements are missing; its advanced controls are for measured
bottlenecks, not mandatory setup.

## 1. Establish a comparable scenario

Discover the project or solution and its build configuration from the repository
and supplied artifacts. Record the exact invocation, working directory, SDK and
MSBuild versions, configuration, target frameworks, runtime identifiers, node
count, restore/cache state, and whether the problem occurs in CLI builds, Visual
Studio, or CI. Identify the actual input change for an incremental-build complaint.

Use an existing matching binlog first. If evidence is absent or insufficient, use
[binlog capture and artifact handling](binlog-generation.md); keep capture,
privacy, and tool setup there. Capture the user's ordinary build, not an invented
`Rebuild`. For a complete baseline distinguish:

| Scenario | Required state | What it answers |
| --- | --- | --- |
| Cold-output build | No prior outputs for the selected build; restore inclusion and package-cache state recorded | Cost of producing the complete output set |
| Warm changed-input build | Valid outputs, then one recorded source/input change | How far that change propagates |
| Warm no-change / no-op build | Successful prior build, identical command, no intervening changes | Work still performed when outputs should be reusable |
| Forced `Rebuild` or `--no-incremental` | Intentionally forced work, separately labeled | Full-work throughput, not ordinary incremental correctness |

A cold-output build is not necessarily a cold package cache, compiler server, or
filesystem cache. Never substitute a forced rebuild for the no-change scenario.
Do not delete output trees, clear shared caches, or stop shared build servers to
manufacture a baseline without specific scope and permission.

If the build fails, use [troubleshoot issues](troubleshoot-issues.md) before timing
optimizations. If execution is unavailable, report an artifact-based diagnosis or
an unmeasured hypothesis; do not invent timings or claim an improvement.

## 2. Classify the bottleneck

Replay the existing artifact with MSBuild using the shared
[binary-log replay instructions](binlog-generation.md#replay-an-existing-log).
Read the emitted diagnostic text for recorded build time, evaluation durations,
project-instance/global-property identity, expensive projects/targets/tasks, and
execution or skip reasons. Use dependency and node-scheduling events where the log
exposes them. Do not time replay and report it as the original build's duration.

If the text output lacks required evaluation, analyzer, or scheduling detail,
report that gap rather than inferring it from aggregate timings. When the original
build is available, collect the specific missing MSBuild diagnostics in a separately
labeled run; otherwise keep the conclusion bounded by the existing artifact.

Use the Project, Target, and Task Performance Summaries to find candidates, then
inspect their individual events. Summed durations can exceed elapsed time because
projects overlap and parent targets/tasks include child work or waits. They are
not CPU utilization or additive shares of wall time.

**Do not optimize `ResolveProjectReferences` just because it tops the target
summary.** Its duration includes waiting for dependent builds while a yielded node
can do other work. Follow the dependency chain and actual `Csc`,
`ResolveAssemblyReference`, `Copy`, or custom-task work. Prefer exclusive/self
timing where available; even an orchestration task's duration can include children.

| Measured evidence | Select this path |
| --- | --- |
| Time before target execution, costly evaluations/imports/globs | [Evaluation before target execution](#evaluation-before-target-execution) |
| Unexpected compilation or generation in a second ordinary build | [Unexpected work on warm or no-change builds](#unexpected-work-on-warm-or-no-change-builds) |
| High Copy cost, repeated content copies, or mutated/stale output files | [Copy and output I/O](#copy-and-output-io) |
| Actual compiler work dominates, especially analyzer/generator time | [Compiler, analyzers, and source generators](#compiler-analyzers-and-source-generators) |
| RAR work or restore dominates | [Reference resolution and restore](#reference-resolution-and-restore) |
| Idle nodes, a long dependency chain, or many small project invocations | [Graph and parallelism](build-perf-baseline.md#graph-and-parallelism) |

Use thresholds as triage hints, never diagnoses: RAR over 5 seconds per project
(especially over 15), analyzer time around 30% or more of `Csc`, one target above
50% of elapsed time, or node activity below roughly 80% deserve inspection.
Confirm overlap, waits, legitimate serialized work, and instrumentation before
assigning impact. A slow no-op can be dominated by evaluation or restore even
when compilation is correctly skipped.

## 3. Follow one targeted path

Select the path supported by the evidence. Make the smallest causal change, then
return to [same-scenario verification](#4-compare-the-same-scenario-and-preserve-behavior).
If that path is already fast or the evidence contradicts it, stop pursuing it.

### Evaluation before target execution

Evaluation establishes initial/global properties, processes imports and
properties, item definitions, items/globs, and task registrations before targets
execute. Compiler and generator execution time is not evaluation time.

1. Identify the expensive evaluation instance and phase. Inspect evaluated
   properties/items and its import chain, not just project text. Multiple
   evaluations can be legitimate: outer/inner multi-targeting builds, restore,
   design-time builds, configurations, RIDs, and differing global properties.
   Compare the full project-instance identity before declaring duplication.
1. Use preprocessing to locate import ownership and definitions, with the same
   configuration/TFM/global properties as the slow instance. For example:

   ```powershell
   dotnet msbuild .\src\App\App.csproj -p:Configuration=Debug -p:TargetFramework=net8.0 "-pp:expanded.xml"
   ```

   `/pp` inlines imports; it is not a dump of final evaluated property/item values
   and does not measure their cost. If available, `-profileEvaluation:eval.tsv`
   provides focused evaluation profiling; label that instrumented run separately.
1. Follow the measured cause below. Without timings, describe patterns as
   observations only; do not rewrite working configuration.

| Measured cause | Targeted change and guardrail |
| --- | --- |
| Globs walking large trees such as `node_modules`, `.git`, or generated outputs | Narrow to intended roots/file types; append appropriate `DefaultItemExcludes` without replacing existing exclusions. Custom item globs must actually use `Exclude` or the relevant exclusion property; changing SDK defaults alone need not affect them. |
| Costly imports, including package or `Directory.Build` chains | Locate the expensive import and its consumers before removing/consolidating it. Depth over 20 or preprocessed output over 10,000 lines is a clue, not proof; SDK imports can be large and fast. |
| File I/O or heavy property functions, such as `ReadAllText` on every evaluation | Reduce repeated reads or move work to a correctly incremental target only when evaluation-time consumers do not require the value. Keep property functions fast and side-effect-free. |
| Repeated equivalent evaluations | Find which caller/global-property differences created them. Normalize only accidental differences; never collapse distinct TFM/RID/configuration or design-time instances. Graph mode is a measured experiment, not guaranteed deduplication. |
| SDK resolver or task-registration/import overhead | Measure the resolver/import cost separately. NuGet-based SDK resolution can add startup cost, but changing SDKs or removing `UsingTask` declarations requires preserving their actual consumers. |

For a demonstrated custom-glob problem, adapt this pattern to the intended files:

```xml
<PropertyGroup>
  <DefaultItemExcludes>$(DefaultItemExcludes);**\node_modules\**;**\.git\**</DefaultItemExcludes>
</PropertyGroup>
<ItemGroup>
  <AdditionalFiles Include="inputs\**\*.json" Exclude="$(DefaultItemExcludes)" />
</ItemGroup>
```

Do not start by setting `EnableDefaultItems=false`. It is a last resort requiring
an intentional replacement for SDK compile/resource/content discovery, including
future additions and removals. Likewise, `TreatAsLocalProperty` changes how global
properties can be overridden; it is not a general evaluation-speed switch or
blanket property-flow filter.

Verify both elapsed evaluation time and the intended evaluated item sets. Exercise
file addition, modification, and removal so a faster glob has not silently dropped
inputs. If evaluation was already cheap, leave it alone.

### Unexpected work on warm or no-change builds

Analyze the second successful, identical ordinary `Build`, not a `Rebuild`.
Search for `Building target completely`, `Building target incrementally`,
`Skipping target`, missing-output messages, and `is newer than output`.
Record the project instance, target, declared inputs/outputs, and exact file or
condition behind the decision. "Building completely" is a symptom, not proof that
all outputs were missing; read its accompanying reason.

| Evidence | Correct response |
| --- | --- |
| Costly file-producing custom target lacks `Inputs`/`Outputs` | Declare every real input and output with stable paths. Include templates, tool/configuration changes, and relevant project/import changes, not just source files. Do not add fictitious outputs to suppress necessary work. |
| Timestamp, GUID, build counter, or changing property in an output path | Use stable, configuration/TFM/RID-isolated paths; decide explicitly whether volatile content is required. |
| Generated output rewritten on every build | Generate deterministically and avoid rewriting unchanged content. If preserving output timestamps leaves them older than inputs, model completion/fingerprints explicitly so the generator does not run forever; still detect missing real outputs. |
| Undeclared or stale generated files | Track actual outputs for incrementality and register owned files in `FileWrites` for Clean. Merely placing a file under `obj` does not register it. |
| Input membership changes through globs | Test additions and removals. General target timestamp checks do not remember a removed input; use an appropriately updated input-set manifest/fingerprint and remove obsolete owned outputs when required. |
| Configuration, TFM/RID, package/assets, or relevant property changed | Expected invalidation can be correct. Compare like with like; returning to an already-built configuration may reuse its separate outputs. |
| Compiler server was recycled | This can slow a compilation that actually executes; it does not by itself explain why an up-to-date `CoreCompile` was scheduled. |

`Inputs`/`Outputs` use file timestamps, not content hashes. Equal or newer output
timestamps can be up to date; one-to-one item transforms can support partial
incremental execution rather than rebuilding every output. There is no
`Incremental="false"` attribute on an MSBuild `Target`. Targets without file-based
checks may correctly run for orchestration, item discovery, or cheap validation.
Use `Returns` to return items without inventing a file-based `Outputs` contract.

Keep generated compile items and clean registration available even when generation
is skipped. For an existing `GenerateConfig` target that produces the named file,
a separate registration target can express this:

```xml
<Target Name="RegisterGeneratedConfig"
        BeforeTargets="CoreCompile"
        DependsOnTargets="GenerateConfig">
  <ItemGroup>
    <Compile Include="$(IntermediateOutputPath)config.generated.cs" />
    <FileWrites Include="$(IntermediateOutputPath)config.generated.cs" />
  </ItemGroup>
</Target>
```

Use a single registration, preserve any existing conditions, and remove duplicate
inclusion. Do not depend on an evaluation-time glob to discover a file that does
not exist yet. `FileWrites` supports cleaning, not target freshness or compiler
inclusion. For shared files, inspect ownership and `FileWritesShareable`/Clean
behavior; do not assume reference-counted protection from deletion.

For clock, Git revision, or other external-state generators, decide the contract
before adding skip checks. A project-file timestamp alone cannot detect a new
commit; `.git\HEAD` alone also misses updates to the referenced branch. Use the
repository's reliable state/fingerprint mechanism or retain a cheap probe that
only rewrites changed content. Do not silently freeze per-build metadata or hide
probe failures to make the second build look fast.

**Visual Studio-only discrepancy:** Fast Up-to-Date Check (FUTDC) may decide not
to invoke MSBuild at all. Enable verbose up-to-date-check logging in Visual
Studio's SDK-style project options and inspect its file/item reason separately
from MSBuild skip reasons. Custom generated inputs/outputs or dynamically added
items must be visible to the appropriate check. `DisableFastUpToDateCheck=true`
is a temporary diagnostic comparison, not the default performance fix.

### Copy and output I/O

Inspect evaluated item metadata, source/destination paths, copy counts, and the
actual `Copy` invocations. Aggregate time may be many unnecessary files, a network
destination, or accidental per-item task batching rather than one slow copy.
Determine whether consumers intentionally mutate the destination before changing
metadata.

| Effective mode | Copies when | Choose when |
| --- | --- | --- |
| `Never` | Never through this copy pipeline | The file is not required there; inspect SDK/imported defaults before assuming omitted metadata means `Never`. |
| `PreserveNewest` | Destination is missing or the source timestamp is newer | Normal source-edited content; a newer mutated destination is intentionally not reset. |
| `IfDifferent` | Destination is missing, or timestamp or size differs in either direction | The destination can drift and should be restored to the source, subject to the metadata heuristic. |
| `Always` | Every eligible build unless an explicit skip-unchanged override is enabled | A real unconditional-copy requirement, including strict reset behavior that metadata comparisons cannot satisfy. |

`IfDifferent` is **not a content-hash comparison**. An edit retaining both size and
last-write timestamp is invisible to it. `PreserveNewest` can leave a mutated,
newer output untouched, so replacing every `Always` with `PreserveNewest` is unsafe.

`IfDifferent` and `SkipUnchangedFilesOnCopyAlways` require **MSBuild 17.13+**
(**.NET SDK 9.0.2xx+ / Visual Studio 2022 17.13+**). Check the actual executing host
and the repository's minimum supported toolset, not just the target framework.
An older common-targets implementation can silently omit unrecognized
`IfDifferent` items. Retain compatible behavior, such as `Always` for required
resets, or gate the choice; `global.json` constrains SDK selection but is not a
substitute for checking every Visual Studio/MSBuild host.

For SDK-default `None` items, use `Update` rather than duplicating them with
`Include`; match the actual item type (`Content`, `None`, `Compile`, or
`EmbeddedResource`) and use `Include` only when the item is not already present:

```xml
<ItemGroup>
  <None Update="config.json" CopyToOutputDirectory="PreserveNewest" />
  <None Update="fixtures\seed.db" CopyToOutputDirectory="IfDifferent" />
</ItemGroup>
```

For a verified fleet of legacy `Always` items,
`<SkipUnchangedFilesOnCopyAlways>true</SkipUnchangedFilesOnCopyAlways>` opts into
the same timestamp-and-size heuristic without editing each item. Its default is
`false` for compatibility. Prefer explicit per-item intent; a repo-wide setting
changes every affected item's reset semantics and needs equivalent verification.

`GetCopyToOutputDirectoryItems` buckets these items.
`_CopyOutOfDateSourceItemsToOutputDirectory` handles `PreserveNewest`,
`_CopyOutOfDateSourceItemsToOutputDirectoryAlways` handles `Always`, and
`_CopyDifferingSourceItemsToOutputDirectory` handles `IfDifferent` with
`SkipUnchangedFiles=true`. Standard build copy targets register `FileWrites`;
custom `Copy` tasks need their own ownership/clean tracking. Inspect
`SkipCopyUnchangedFiles` only where the selected targets actually consume it;
the custom-task control is the `Copy` task's `SkipUnchangedFiles` parameter.

Copyable content can flow transitively through `ProjectReference`; `Never` does
not participate in that pipeline. Verify referencing projects, not just the
defining project. Review `CopyToPublishDirectory` independently and verify the
selected publishing SDK/targets support the chosen mode. Check normal publish
and ClickOnce collection when used; do not infer publish behavior from a successful
build or assume the global `Always` switch controls all publish copy tasks.

Only after eliminating unnecessary copies, consider filesystem/layout controls:
`CreateHardLinksForCopyFilesToOutputDirectoryIfPossible` requires suitable
same-volume, immutable-file behavior; a mutated hardlink can modify its source.
Do not use it for resettable test databases or writable content.
`UseCommonOutputDirectory` is safe only when consumers genuinely share a verified,
collision-free output layout. Prefer the separately measured
[artifacts layout](build-perf-baseline.md#artifacts-output-layout) over guessing
that shared directories remove work safely. Windows Dev Drive is an optional
environment experiment, not a portable fix or a reason to disable security tools.

Verify no-change skipping, source edits, a deleted destination, destination
mutation in both timestamp directions, source removal/stale-output policy, and
same-size/same-timestamp mutation if strict reset is required. Copy modes alone
are not a guarantee that obsolete destination files will be deleted.

### Compiler, analyzers, and source generators

Find actual `Csc` work on the critical path and its analyzer/generator timing.
A compiler task taking over twice a comparable compiler-only measurement is a
clue, not evidence that analyzers specifically caused the difference. Analyzer
timings may overlap; their sums need not equal elapsed compiler time.

Inspect the evaluated analyzer set and its provenance in the project,
`Directory.Build.props`, `Directory.Packages.props`, package imports, and
`GlobalPackageReference`. A declaration alone does not prove the package was
restored or passed to the compiler. Global references can affect many projects;
check production and test projects rather than counting only local references.

A controlled `/p:RunAnalyzers=false` comparison can isolate some diagnostic
analyzer cost, but must compile the same inputs in both runs. It is not an
acceptable final result if required diagnostics disappear. Do not assume it
disables or measures every source generator; preserve generated code and inspect
generator-specific evidence.

If the evidence supports an intentional lighter development policy, use the
[baseline's inner-loop controls](build-perf-baseline.md#scoped-inner-loop-work)
to conditionalize `RunAnalyzers`, `EnforceCodeStyleInBuild`, or documentation
generation. Confirm the repository's CI flag really enables full enforcement.
Scope redundant/expensive analyzer rules or packages carefully, including
`.editorconfig` policy; never remove required analyzers or generators just for a
faster number. Check `ProduceReferenceAssembly` where downstream recompilation
is the measured problem; it is already enabled by many SDK configurations.

### Reference resolution and restore

For **`ResolveAssemblyReference` (RAR)**, inspect reference count, assembly search
paths, network locations, and transitive inputs for the affected project.
Remove only demonstrated unused references/search paths and verify all consumers.
RAR can legitimately run on a no-change build to account for external resolution
state such as installed targeting packs or GAC assemblies; forcing it to skip
can be incorrect. Use the [dependency audit](build-perf-baseline.md#dependency-graph-and-reference-semantics)
for reference changes.

Do not treat `DesignTimeBuild=false`, silencing RAR, or changing architecture
mismatch warnings as performance fixes. They alter build/diagnostic behavior and
do not establish that reference resolution improved.

For **restore**, distinguish invocation/up-to-date checking from actual repeated
network or dependency-resolution work. Trace changing restore inputs, package
cache/feed access, and SDK/configuration differences. A build-only experiment can
use `dotnet restore` followed by `dotnet build --no-restore` only after a successful
matching restore. Compare restore-inclusive and build-only scenarios separately.
`RestoreUseStaticGraphEvaluation=true` is a candidate for a measured large-graph
restore; it is distinct from graph build and requires equivalent resolved assets.
Do not clear shared NuGet caches or add lock files and then claim they are a
compiled-output cache.

## 4. Compare the same scenario and preserve behavior

Rerun the baseline's equivalent scenario with identical instrumentation and one
intentional change. Use repeated measurements, their median and spread, and the
same input edit. Restore/cache changes, SDK upgrades, smaller solution selections,
different node counts, and skipped validation must be disclosed as experimental
variables, not hidden improvements.

For affected outputs and consumers, perform the applicable checks:

| Check | Required result |
| --- | --- |
| Immediate second ordinary build | Expensive work that should be reusable skips; any remaining work has an explained reason. |
| One input modified or added | Correct affected outputs regenerate; downstream invalidation matches the dependency/API change. |
| One input removed | Required outputs reflect its absence; obsolete owned files cannot silently remain inputs or ship. |
| A generated/copied output deleted | The next build reconstructs it; a stamp cannot mask the missing file. |
| Template, generator, relevant property, package, or imported build file changed | The declared dependency/fingerprint model notices it. |
| Destination modified between builds | The selected copy/reset contract holds, including the metadata heuristic's limits. |
| Supported configuration/TFM/RID and consuming project | Outputs stay isolated, complete, and correctly referenced; expected diagnostics remain enabled. |
| Relevant tests, runtime use, pack/publish, and approved scoped Clean | Behavior and required artifacts remain correct; cleanup removes only owned outputs. |

Run destructive or mutation checks only in an agreed disposable workspace or on
explicitly authorized files/directories. Do not overwrite user edits or delete
shared generated outputs. If the change deliberately alters an output or
diagnostic policy, get that policy agreed and report it rather than calling the
change behavior-preserving.

Stop when the bottleneck is not measured, the improvement falls within noise,
the build cannot be compared successfully, or correctness regresses. Revert only
the experimental change you own, or report the blocked verification. If all
three scenarios are healthy, recommend no tuning. Broad SDK/layout/project
restructuring belongs in [modernize](modernize.md) once justified, not as an
automatic escalation.

## Output contract

Return a compact evidence-backed result, not a generic optimization list:

- **Scenario and status:** exact command/environment, cold/warm/no-change or
  forced-work label, success/failure, and local artifact paths.
- **Finding:** affected project/TFM, target/task or evaluation phase, timing and
  precise skip/input/copy/dependency evidence. Separate measured causes from
  unmeasured observations.
- **One action:** smallest proposed/applied change, ownership/location, supported
  toolset, and any deliberate behavior tradeoff. State "no change" when appropriate.
- **Comparison:** same-scenario before/after median, spread, absolute and percentage
  change; say "not measured" when either side is unavailable.
- **Correctness and residuals:** checks actually performed, failures or gaps,
  remaining expensive work, and the next evidence needed only if blocked.

Prioritize verified critical-path costs: over roughly 10% is high impact, 2-10%
medium, and low-effort modest changes are quick wins only after confirmation.
Do not turn inclusive/overlapping timings into a fictitious percentage saved.
