# Build performance baseline and controlled experiments

Use this reference to establish a trustworthy before/after baseline or to run one
advanced experiment selected by the
[performance task guide](troubleshoot-performance.md). It preserves the full
baseline workflow; output-layout, reproducibility, dependency, graph, parallelism,
and inner-loop controls are optional branches, not a blanket optimization recipe.

For file-reading tools, resolve sibling references under the skill root's
`references` directory. Use [binary-log capture and replay](binlog-generation.md)
for capture details, MSBuild replay of existing artifacts, and privacy handling.

## Measurement protocol

### Freeze the experiment

Record the repository revision and relevant local changes; exact project,
solution, or solution filter; working directory; invocation/targets; configuration;
TFMs/RIDs; SDK and MSBuild versions; host OS; CPU/memory/resource limits; node
count; environment/global properties; and diagnostic instrumentation.

Also record restore inclusion, NuGet cache/feed state, prior output state, compiler
and build-server warm-up, and significant concurrent activity. Do not change SDK,
node count, restore policy, input scope, and analyzer settings together and then
attribute the difference to one optimization.

If the user supplies logs matching the scenario, analyze them first. If a command
cannot succeed in this environment, report the command, exit/failure evidence,
and missing requirement; a failed or partial build is not a timing baseline.

### Measure three distinct scenarios

| Scenario | Preparation | Measurement and expected evidence |
| --- | --- | --- |
| Cold-output build | Use an authorized disposable workspace or an explicitly scoped clean output set. No prior selected build outputs; record whether packages are already cached. | Ordinary build including restore if end-to-end cost is the question. Compilation and required generation occur; identify restore, evaluation, execution, and I/O contributions. |
| Warm changed-input build | First produce valid outputs. Make one recorded, reproducible change to a selected source/input. | Ordinary build with that change; record projects/targets affected. Dependent work may be necessary, but unrelated recompilation needs explanation. |
| Warm no-change / no-op build | Produce a successful build, then invoke the exact same command with no edits, cleanup, or property changes. | Expensive compilation/generation should skip when up to date. Evaluation, reference resolution, restore checks, and orchestration can still legitimately occur. |

"Warm" alone is ambiguous: label whether an input changed. A timestamp-only
touch measures timestamp invalidation; a real implementation edit or public-API
edit can have different downstream effects. Record which one was used and use
the same edit on both sides of the experiment.

Do not use `Rebuild`, `-t:Rebuild`, or `--no-incremental` as the second no-change
build. Those deliberately force full work. A separately labeled forced-rebuild
benchmark can measure full-work throughput but is neither an ordinary warm build
nor proof of a cold cache.

### Prepare cold state safely

Discover the actual output and intermediate paths, including imported overrides,
custom generators, and artifacts layout. Do not recursively search a repository
for directories named `bin`/`obj` and delete them. Those names do not prove
ownership, and custom Clean targets can have additional side effects.

Prefer a disposable, agreed workspace. Otherwise enumerate the precise generated
paths, confirm that nothing user-authored or shared will be removed, obtain
permission for that scope, and use the repository's supported scoped cleanup.
If safe cleanup is not possible, leave outputs alone and mark the cold-output
scenario unmeasured.

Never clear shared NuGet caches, stop other users' compiler/build servers, or try
to evict OS caches as an implicit part of "cold". Output-cold, package-cold, and
process/cache-cold are different experiments. Record them instead of pretending
one cleanup command establishes all three.

### Repeat and retain the evidence

1. Establish the three scenarios before editing configuration. For a narrowly
   scoped complaint, reuse matching existing measurements and state any scenario
   not measured rather than fabricating a complete table.
1. Preserve a binlog and wall-clock duration for each measured run. Match logging
   overhead on both sides; retain detailed profiler runs separately if they add
   instrumentation. Resolve log paths through the capture guide.
1. Prefer at least three measured runs per scenario and report median plus range
   or another stated spread. Re-establish that scenario before every repeat:
   output-cold each time, the same warm edit from the same primed state, or an
   unchanged successful state for no-op. Do not average cold and warmed runs.
1. Note outliers and environmental interference instead of silently discarding
   unfavorable results. Define the relevant improvement goal and noise tolerance
   before selecting a winner.
1. Make one evidence-driven change. Repeat the same scenario, input change,
   success checks, and instrumentation. Keep a rollback scoped to your own change.

An illustrative invocation after discovering the real solution and safe node
budget is:

```powershell
dotnet build .\Repo.sln -c Debug -m:4 "-bl:before-noop-01.binlog"
```

The label is truthful only if the preparation matches it. `--no-restore` may be
added for a build-only experiment after a successful matching restore, but it
must not quietly turn an end-to-end baseline into a different benchmark.

### Baseline ledger and interpretation

| Scenario / fixed input change | Before median [range] | After median [range] | Delta | Artifacts / correctness |
| --- | --- | --- | --- | --- |
| Cold-output, restore included/excluded | Measured value or not measured | Measured value or not measured | Seconds and percent | Logs, result, output completeness |
| Warm, named edit | Measured value or not measured | Measured value or not measured | Seconds and percent | Logs, invalidated projects/targets |
| No-change, same invocation | Measured value or not measured | Measured value or not measured | Seconds and percent | Logs, executed/skipped work |

Compute percentage improvement as `(before - after) / before * 100` using the
same statistic; a negative value is a regression. Keep restore time, evaluation
time, critical-path work, and task totals distinguishable. Parallel/nested task
totals are not additive wall time.

Historical triage hints include no-op builds under about 5 seconds for small
repositories or 30 seconds for large ones, and full builds under roughly
10 seconds / 60 seconds / 5 minutes for small / medium / large workloads.
Neither project-size labels nor these times are universal budgets. A no-op over
30 seconds, or roughly 10 seconds per project, warrants investigation, not an
automatic conclusion that incrementality is broken.

A warm build recompiling broadly can reflect dependency/API changes or broken
tracking; a slow cold restore can reflect network/feed work as well as cache
state. Measure the cause. If all scenarios meet the actual goal with correct
behavior, stop: no optimization is required.

Return to [bottleneck classification](troubleshoot-performance.md#2-classify-the-bottleneck)
before selecting one of the experiments below.

## Artifacts output layout

Use this branch for measured output collisions, copy/layout overhead, or a
deliberate CI output-management need. **.NET SDK 8+** supports centralized
artifacts layout; changing the target framework alone does not enable it.

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <UseArtifactsOutput>true</UseArtifactsOutput>
    <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

`ArtifactsPath` customizes the root; it is optional when the SDK-selected root is
appropriate. The CLI's `--artifacts-path` is another SDK 8+ option. Keep restore,
build, test, and publish invocations consistent rather than changing only one.

Traditional outputs live near each project in `bin` and `obj`. Artifacts layout
groups them below the common root, for example `artifacts\bin\<project>\<pivot>`
and `artifacts\obj\<project>\<pivot>`. Pivots distinguish configuration and, where
needed, TFM/RID; inspect the evaluated paths rather than assuming one fixed naming
pattern. Check projects with duplicate names and custom path/pivot overrides for
collisions.

This can simplify output discovery, CI cache handling, and ignore rules. It does
not by itself make compilation faster or make a cache valid. Preserve scripts,
test discovery, packaging/publish locations, generated-file ownership, and Clean
behavior. Update directly affected path consumers and ignore rules; compare
complete outputs across the supported build matrix. Do not combine a layout
migration with unrelated tuning. Cross-cutting migrations use
[modernize](modernize.md) with these compatibility requirements.

## Determinism and cache validity

Use this branch when output reuse/reproducibility is the actual measured need,
not as a substitute for incremental input/output tracking.

```xml
<PropertyGroup>
  <Deterministic>true</Deterministic>
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
</PropertyGroup>
```

Deterministic compilation is already the default in modern SDK projects. It
produces consistent compiler outputs for identical compiler inputs; it is not a
promise that every build target, generator, PDB path, signing step, or package is
reproducible. `ContinuousIntegrationBuild` supports SDK CI normalization, but the
repository must supply a real CI condition and compatible source/path information.

Check stable generated content and paths, compiler/SDK/package versions, build
options, and all relevant inputs before sharing cached results across machines.
Compare the required artifacts for reproducibility; do not assume setting two
properties creates a cache. NuGet lock files stabilize dependency resolution,
not compilation results. Determinism and a centralized `artifacts` directory do
not justify reusing stale outputs after an untracked input changes.

## Dependency graph and reference semantics

Use the actual project graph, per-instance timings, and critical path. Identify
the exact candidate edge and why it is unnecessary. A direct reference can be
transitively reachable yet intentionally describe direct API use or carry
important metadata; do not delete it based only on reachability.

| Candidate | What it changes | Required proof |
| --- | --- | --- |
| Remove a redundant direct `ProjectReference` | May reduce redundant resolution/graph work | Transitive availability and all reference metadata remain equivalent for supported SDK/TFM/RID/configurations, build, runtime, and pack/publish. |
| `ReferenceOutputAssembly="false"` | Keeps a build-order dependency without adding that output assembly as a compiler reference | The consumer does not need that assembly API. The project still builds first: this does **not** remove the scheduling edge. |
| `PrivateAssets="all"` | Controls dependency asset exposure, notably to package consumers | Inspect restore/pack and consuming-project behavior. It is not a generic "do not build this project" or "remove its compiler reference" switch. |
| `DisableTransitiveProjectReferences=true` | Opts out of implicit transitive project references in supporting SDK builds | Last resort for measured closure overhead. Explicitly declare every consumed dependency and verify all consumers; it does not flatten required build ordering. |
| Prebuilt package instead of project dependency | Changes the source/build/deployment boundary | An agreed versioning and rebuild policy; never silently use stale binaries to shorten a benchmark. |

For a genuinely build-order-only code generator, the scoped intent can be:

```xml
<ProjectReference Include="..\CodeGen\CodeGen.csproj"
                  ReferenceOutputAssembly="false" />
```

For `App -> Core -> Utils`, removing an additional `App -> Utils` reference does
not necessarily shorten that serial chain. It can be correct only if `App` keeps
all intended compile/runtime/pack behavior and metadata. If `App` directly uses
`Utils`, a deliberate direct reference may be clearer or required by repository
policy even when the current SDK supplies transitive compile references.

Identify deep chains versus many small independent projects. Splitting a measured
large bottleneck can expose parallel work; merging tiny leaves can reduce
per-project evaluation overhead. Those are opposing remedies for different
evidence, not simultaneous recommendations. Moving common code to a base library
still creates a dependency and can lengthen the critical path. Seek an agreed
architecture change and measure the resulting graph before claiming a gain.

## Graph and parallelism

Name the critical chain, for example `Core -> Api -> Web -> Tests`, with durations
and dependency/wait evidence. More nodes cannot parallelize a required serial
chain. A summed Target Performance Summary alone cannot reveal node utilization;
use recorded project-instance and node-scheduling evidence exposed by replay.
If that detail is absent, state the gap instead of estimating utilization or a
critical path from aggregate timings.

### Controlled node-count experiment

Raw MSBuild defaults to one node without `-m`; `-m` without a number permits up to
the logical processor count, and `-m:N` gives an explicit limit. The `dotnet build`
driver can supply MSBuild switches itself, so inspect the invocation rather than
assuming every host has the same default. Keep a fixed, recorded node count in
baseline comparisons.

Compare an appropriate explicit node budget, respecting CI quotas, memory,
other users, and I/O contention. `-m:4` is an example, not a universal optimum.
Do not change node count silently when collecting the "after" result.

For custom orchestration, the `MSBuild` task must allow parallel project requests,
and the process must have more than one available node:

```xml
<MSBuild Projects="@(ProjectsToBuild)" Targets="Build" BuildInParallel="true" />
```

Confirm those requests are independent and their outputs isolated. A
`BuildInParallel` setting cannot repair a false or required dependency edge, and
more nodes may make a memory- or disk-bound workload worse. A hot task inside one
project needs task-specific evidence, not another project scheduling flag.

### Static graph experiment

Graph build (`-graphBuild`, also `/graph`) constructs the project graph before
execution and schedules from declared dependencies. It is most worth measuring
for large multi-project/CI builds; small graphs can pay extra upfront overhead.
Project count alone, including rules of thumb such as 20 projects, is not proof
of a benefit.

Check installed-tool support and static discoverability of the complete
`ProjectReference` graph. Execution-time reference discovery and hidden
programmatic `MSBuild` calls can violate the graph model. Do not merely move them
outside a target if their semantics depend on execution-time state.

After a matching restore, a build-only candidate might be:

```powershell
dotnet build .\Repo.sln -c Debug --no-restore -m:4 -graphBuild "-bl:graph-candidate.binlog"
```

Compare against the same invocation without `-graphBuild`. Verify the same
project instances, outputs, diagnostics, and incremental propagation. Graph mode
can improve scheduling; it neither guarantees fewer evaluations nor automatically
enables project isolation/results caching. `-isolateProjects` and results-cache
options are separate contracts, not free benefits of `/graph`.

If graph construction fails or dependencies cannot be represented statically,
stop this experiment and retain the normal build. Report the actual failure
rather than assuming a particular error code. Do not mask it by dropping
references or disabling dependency builds.

## Restore isolation

Use this branch when restore contributes measurable cost. Preserve a
restore-inclusive baseline even if CI or the development loop benefits from a
separate build-only stage.

```powershell
dotnet restore .\Repo.sln
if ($LASTEXITCODE -ne 0) { throw "Restore failed; do not measure a build with stale assets." }
dotnet build .\Repo.sln -c Debug --no-restore -m:4
```

These are separate steps: run the second only after the first succeeds. Supply
the actual matching configuration, TFM/RID, package, and other restore-affecting
properties to both stages. `--no-restore` is unsafe with missing/stale assets or
after dependency/restore-input changes.

For a measured large restore graph,
`<RestoreUseStaticGraphEvaluation>true</RestoreUseStaticGraphEvaluation>` is a
separate experiment from graph **build**. Verify toolset support, resolved assets,
feed/cache behavior, and restore diagnostics before comparing speed. Investigate
expensive feed/network or cache misses rather than purging shared caches.

`dotnet test --no-build` is appropriate only for tests whose matching outputs and
dependencies were successfully built. Keep configuration/TFM/RID consistent;
otherwise it can exercise stale or absent binaries. It is not a validation
shortcut after a failed build.

## Scoped inner-loop work

Use these controls only for measured work the development scenario does not
require, with an agreed policy. Preserve the CI, packaging, runtime, and diagnostic
contracts separately.

| Control | Decision and verification |
| --- | --- |
| `RunAnalyzers=false` for a controlled dev scenario | First measure analyzer cost, including global/package-provided analyzers. Preserve required CI enforcement; do not remove generator-produced code or treat disabled diagnostics as unchanged behavior. |
| `EnforceCodeStyleInBuild` conditional on the real CI signal | Check effective values in both environments and verify the CI pipeline actually sets that signal. Do not assume every runner sets `ContinuousIntegrationBuild` automatically. |
| `GenerateDocumentationFile` limited to required scenarios | XML documentation may be a package/output contract. Disable only where intentionally optional, preserve shipping artifacts and warnings policy, and measure the real cost. |
| `ProduceReferenceAssembly` | Inspect the SDK's existing effective value first. Reference assemblies can avoid downstream recompilation for implementation-only changes; verify public-API changes still invalidate consumers. |
| Build a project or solution filter instead of the entire solution | Scope the inner loop deliberately, with its project dependencies and required tests. Compare like-sized workloads; selecting less work is a new scenario, not a faster full-solution build. |

For an agreed CI-only code-style policy, an explicit conditional example is:

```xml
<PropertyGroup>
  <EnforceCodeStyleInBuild Condition="'$(ContinuousIntegrationBuild)' == 'true'">true</EnforceCodeStyleInBuild>
  <EnforceCodeStyleInBuild Condition="'$(ContinuousIntegrationBuild)' != 'true'">false</EnforceCodeStyleInBuild>
</PropertyGroup>
```

Place settings where they take effect under the repository's import order and
confirm evaluated values. Configuration-only conditions such as `Debug` are not
a safe substitute for a CI policy: CI may also build Debug.

For excessive I/O, use the performance guide's
[copy-mode and filesystem branch](troubleshoot-performance.md#copy-and-output-io)
before enabling hardlinks, shared outputs, or broad skip-copy settings.

## Acceptance and stopping conditions

Accept a change only after the
[same-scenario and behavior checks](troubleshoot-performance.md#4-compare-the-same-scenario-and-preserve-behavior)
show a repeatable benefit for the intended workload and no unintended regression.
Retain the before/after ledger and supporting artifacts. Explicitly label any
intentional scope, output, or diagnostics-policy change.

Stop if measurements are missing, the result is within run-to-run noise, the
candidate cannot build correctly, or it trades correctness for speed. Report
unmeasured scenarios and blocked checks; never claim success from a smaller,
failed, or validation-disabled build. Finish using the performance guide's
[output contract](troubleshoot-performance.md#output-contract).
