# MSBuild authoring checks

Use this shared catalog for a specific failure or a requested review. A matching text pattern is
not itself a bug: verify the evaluated behavior and relevant exceptions before changing it.
The AP identifiers retain the source catalog's mapping for maintainers.

## Decision catalog

| Source | Evidence to check | Safe correction or exception |
| --- | --- | --- |
| AP-01, AP-16 | `Exec` used for file operations or simple string/path transformations | Prefer built-in tasks/property functions when they preserve behavior. Keep real external tools as tools, with correct quoting and error propagation. |
| AP-02 | Unquoted operands in string comparisons | Quote both operands, for example `Condition="'$(Configuration)' == 'Release'"`. Do not rewrite valid boolean/function conditions just because they have a different form. |
| AP-03 | Machine-specific paths | Derive paths from the project or the defining import (`MSBuildThisFileDirectory`), not the caller's incidental working directory. |
| AP-04 | Explicit values matching SDK defaults | Remove only after checking the selected SDK, imports, configurations, and intentional pinning. An apparent default may preserve a contract. |
| AP-05 | Explicit file includes in SDK-style projects | Remove true duplicate implicit items; retain metadata, exclusions, linked/generated files, and ordered F# inputs. See the language exceptions below. |
| AP-06 | Package DLLs referenced through a legacy `packages` HintPath | Consider `PackageReference` during an authorized migration, preserving assets and versions. GAC, local, and vendor assembly references are not automatically NuGet packages. |
| AP-07 | Build-only/analyzer packages escaping to consumers | Use `PrivateAssets="all"` when dependencies should remain private. Do not hide intentionally transitive build assets or required runtime dependencies. |
| AP-08 | Shared settings copied into many projects | Consolidate truly shared defaults with project override behavior intact. Check nearest-file discovery before moving them. |
| AP-09 | Unintended package-version drift | Central Package Management is an explicit package-management change, not an automatic build fix. Preserve intended per-framework differences. |
| AP-10 | One target performs independent generation/copy/signing work | Split by responsibility only with execution order and individual incremental contracts preserved. |
| AP-11 | Artifact-producing targets run on every no-change build | Model complete inputs and real outputs using [performance guidance](troubleshoot-performance.md). Validation/phony targets without persistent outputs must not be given misleading skip conditions. |
| AP-12 | A default is defined after its consumers | Move it before the first consumer, normally into `.props`, with an empty-value condition. Values depending on later project properties must stay late enough to see them. |
| AP-13 | Optional import can be absent | Guard optional files. Required imports and package-contract files must fail when missing; first resolve the [packed layout](extension-points.md#source-tree-versus-packed-layout). |
| AP-14 | Backslashes in cross-platform paths | Distinguish evaluator/built-in-task paths, which MSBuild normalizes, from raw shell strings or custom-task inputs. Backslashes in a normal `Import` are not by themselves a Linux failure. |
| AP-15 | A value is unexpectedly overwritten | Trace its assignments and scope. Preserve intentional project overrides and command-line global properties rather than mechanically adding conditions everywhere. |
| AP-17 | `Update` does not affect the intended item | Check whether the item exists when `Update` runs. `Include` followed by `Update` in the same `ItemGroup` is valid; separating groups alone does not fix wrong ordering. |
| AP-18 | Apparently redundant project references | Keep direct dependencies actually used by source or build ordering. Do not remove a reference merely because a transitive route also exists. |
| AP-19 | Evaluation performs writes, network calls, or other side effects | Move side effects to appropriately scheduled targets. Evaluation may repeat during IDE/design-time and graph discovery. |
| AP-20 | A raw shell command assumes one operating system | Prefer a portable built-in task; otherwise explicitly gate the command and preserve the required behavior on supported systems. |
| AP-21 | A property condition reads `TargetFramework` before the project sets it | Move that property assignment after the value exists. Item and target conditions have different timing; see below. |
| AP-22 | An `MSBuild` task creates another instance writing the same outputs | Compare global-property sets and output paths. Remove an unnecessary fork, sequence the producer correctly, or isolate genuinely different outputs. |
| AP-23 | `SetTargetFramework` redundantly injects a single-targeted project's own TFM | Confirm the duplicate instance/path collision before removing metadata. Multi-target selection and intentional different-TFM builds are different cases. |

## Property and item evaluation

Properties/imports are evaluated before item definitions and items. A single-target project often
sets `TargetFramework` in its body, **after** `Directory.Build.props`. A property condition using
that value in an early props file can therefore silently miss:

```xml
<!-- Put this after the project has set TargetFramework, for example in Directory.Build.targets. -->
<PropertyGroup Condition="'$(TargetFramework)' == 'net10.0'">
  <DefineConstants>$(DefineConstants);NET10_FEATURE</DefineConstants>
</PropertyGroup>
```

The same warning does **not** automatically apply to `ItemGroup` or item conditions. These are
evaluated after properties and may correctly use `TargetFramework` in an early props file:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
  <PackageReference Include="Example.Library" Version="1.0.0" />
</ItemGroup>
```

Likewise, `PackageVersion` conditions in `Directory.Packages.props` can be valid. An outer
multi-targeting build may still have no single `TargetFramework`; scope per-framework work to the
appropriate inner builds. Inspect actual evaluated values instead of assuming all `.props` or
`.targets` have identical timing.

## Item and language exceptions

- An SDK-style C#/VB project may already include a file. Use `Update` to change metadata on that
  existing item, `Include` for a genuinely new item, and `Remove` for an exclusion. Preserve
  `Link`, `CopyToOutputDirectory`, resource metadata, and files outside default globs.
- **F# source order is semantic.** Keep explicit `.fsproj` compile items in dependency order,
  with each `.fsi` immediately before its matching `.fs` and the entry point last. Do not apply
  C# implicit-glob cleanup to F#.
- Generated files under the intermediate directory are normally excluded from SDK globs.
  Ensure they are included at the right phase on both the first build and an up-to-date build.
- `Update` only affects existing items. A preceding `Include` in the same group is sufficient;
  a later include, even in another group, is not repaired by an earlier update.

## Project instances and output collisions

An MSBuild instance is identified by the project path **and global properties**. Two instances can
be different to MSBuild yet write the same assembly, PDB, assets file, or generated source.

For a suspected collision:

1. Compare the involved instances in the binlog, including configuration, framework, runtime,
   and extra properties passed through an `MSBuild` task or project-reference metadata.
2. Confirm their actual output/intermediate paths overlap. A property difference alone is not a
   collision, and an ordinary parallel build is not inherently unsafe.
3. Remove an unnecessary path-neutral discriminator or duplicate invocation. A producer should
   own its outputs; another project should sequence and consume them instead of re-publishing it.
   Same-instance target scheduling must be checked for dependency cycles.
4. If separate instances are intentional, isolate **all** relevant outputs and intermediate paths,
   setting path-defining properties early enough for SDK/NuGet consumers. Retest in the original
   parallel/graph scenario rather than accepting serialization as the final repair.

`SetTargetFramework` selecting one TFM of a multi-targeted project, or deliberately overriding a
single-targeted project to a **different** TFM, is not the redundant same-TFM case. Verify both the
intent and output isolation.

For build-only references to an incompatible framework, `ReferenceOutputAssembly="false"` and
`SkipGetTargetFrameworkProperties="true"` may be needed. Bypassing framework negotiation can also
allow the caller's global `TargetFramework` to leak into the callee. For a single-target producer
that should build as declared, consider `UndefineProperties="TargetFramework"`; when an explicit
TFM must be selected, use `SetTargetFramework` instead. Do not set and remove the same property or
apply these metadata changes to ordinary compatible assembly references.

## Validate the affected contract

For every proposed fix, identify the symptom it addresses and what behavior must not change.
Build the relevant configurations, preserve package/assembly contracts, and test incremental
behavior when target/item/output definitions change. A review that finds only valid exceptions
should say so and leave the files unchanged.
