# Imports, hooks, and extension points

Use this detail for a proven import/hook problem or requested extensibility modernization. Read the
actual importing SDK/targets and package layout before choosing a pattern. Extension properties
do not all have the same contract.

## Choose the right hook

| Need | Preferred decision | Verify |
| --- | --- | --- |
| Run custom logic before/after an existing target | A uniquely named target with `BeforeTargets` or `AfterTargets` | The named target exists and runs in the intended build/inner build. Do not replace SDK target definitions. |
| Extend a documented target dependency list | Preserve the existing `$(...DependsOn)` value when appending/prepending | The assignment occurs after the original list is defined and does not introduce a cycle. |
| Import a file through `CustomBefore...` / `CustomAfter...` | Inspect the consumer's `Import` and preserve its existing hook value when extending the supported import list | Both prior and new hooks actually run, in the intended order. |
| Compose files for a custom consumer that accepts only one path | Point it at an aggregation `.props`/`.targets` file that imports the existing and new hooks explicitly | Verify the actual consumer contract; do not infer a single-path restriction merely from an `Exists()` guard on an MSBuild import. |
| Create an extension contract for your own SDK | Define early defaults, explicit before/after imports, and stable target hooks | Do not advertise a list unless both import and gating logic support it. |

Do not overwrite prior hooks when adding an extension. MSBuild imports support semicolon-separated
files; the presence of an `Exists()` guard does not by itself make a valid import chain incorrect.
For a supported list, preserve the previous value:

```xml
<PropertyGroup>
  <CustomBeforeMicrosoftCommonTargets>$(CustomBeforeMicrosoftCommonTargets);$(MSBuildThisFileDirectory)MyExtension.targets</CustomBeforeMicrosoftCommonTargets>
</PropertyGroup>
```

Check the actual importing target and version, including how optional missing files are handled.
For a genuinely single-path custom consumer, use an aggregation file instead. In either case,
verify that existing and new hooks run rather than assuming the assignment alone proves it.

Use an existence guard for an **optional** import:

```xml
<Import Project="$(MSBuildThisFileDirectory)local.props"
        Condition="Exists('$(MSBuildThisFileDirectory)local.props')" />
```

Keep required imports unguarded so a missing dependency fails explicitly. Do not set an SDK's
"has been imported" flag to suppress a legitimate import. An import-once flag belongs to the file
whose initialization it records and must reflect successful initialization.

## Wildcard imports and control properties

Wildcard imports are ordered by filename. If ordering is part of your extension contract, use
explicitly ordered names such as `01-defaults.props` and `02-overrides.props`, and verify the
expanded imports rather than relying on filesystem enumeration order.

Distinguish machine-wide (`MSBuildExtensionsPath`), per-user (`MSBuildUserExtensionsPath`), and
per-project generated (`MSBuildProjectExtensionsPath`, normally `obj`) extension locations.
Changing a machine/user extension is broader than fixing a repository-local file.

`ImportDirectoryBuildProps`, `ImportDirectoryBuildTargets`, `ImportProjectExtensionProps`,
`ImportProjectExtensionTargets`, and the `ImportByWildcardBefore*` / `ImportByWildcardAfter*`
properties can gate discovery. Set a control property **before** its consumer evaluates. Do not
disable NuGet-generated imports or all shared build logic to hide one faulty extension.

## Directory.Build discovery and evaluation order

Only the nearest `Directory.Build.props` and nearest `Directory.Build.targets` are automatically
discovered. A nested file does not automatically merge its parent. If the repository intends
inheritance, explicitly find/import the parent, starting above the current file to avoid recursion:

```xml
<Project>
  <PropertyGroup>
    <_ParentProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..\'))</_ParentProps>
  </PropertyGroup>
  <Import Project="$(_ParentProps)" Condition="'$(_ParentProps)' != ''" />
</Project>
```

Whether a missing parent is optional is a repository contract; fail explicitly if it is required.
Put defaults before their consumers, usually in `.props`, and logic that needs project-defined
values late enough, usually in `.targets`. See
[property and item evaluation](antipatterns.md#property-and-item-evaluation) before moving a
`TargetFramework` condition: property conditions and item conditions are not interchangeable.

## Source tree versus packed layout

A source folder is not necessarily the layout of its `.nupkg`. Before reporting a missing
`build` or `buildTransitive` import:

1. Inspect every `.nuspec` in the project directory and its **immediate parent**; do not search
   unrelated ancestors. Resolve the relevant `<file src="..." target="...">` mappings.
2. Inspect the project's `Pack`/`PackagePath` item metadata and SDK packaging properties such as
   `BuildOutputTargetFolder` and `IncludeBuildOutput`.
3. Project the destination paths, including renamed files and per-TFM copies. Prefer inspecting
   the actual package when one is available.
4. Flag the import only if its target is absent from both the applicable source layout and the
   packed contract. A required file guaranteed by packaging must not gain an `Exists()` guard
   that would silently hide a broken package.

For example, one `buildTransitive\common\Example.props` source can be packed into several
`buildTransitive\<tfm>` folders. The absence of those TFM directories in source is not a defect.

## NuGet build assets and forwarding

`build` assets affect direct consumers; `buildTransitive` assets can flow to transitive consumers;
`buildMultiTargeting` serves the outer cross-targeting build. Props run early, targets late. Use
package-ID-based `.props`/`.targets` filenames and inspect generated NuGet imports to confirm that
the intended files are actually imported.

Where a package shares implementation through forwarding, retain a clear
`buildTransitive -> build -> shared` ownership chain instead of duplicating hooks or bypassing
the direct-consumer implementation. Do not blindly redirect all assets to `buildMultiTargeting`,
whose outer-build semantics differ from per-framework execution.

If both `buildTransitive` and `build` are per-TFM, preserve the matched folder segment. Derive it
from the importing file's directory, **not** the consuming project's `TargetFramework`: NuGet
can select a `net8.0` asset for a `net10.0` consumer.

```xml
<!-- Inside packed buildTransitive/<tfm>/Example.props. -->
<Import Project="$(MSBuildThisFileDirectory)..\..\build\$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName('$(MSBuildThisFileDirectory)'))))\Example.props" />
```

Only use this shape when the actual package has that layout. A non-TFM-specific package needs its
own correct relative path, not an invented framework directory.

## Verify the extension

Check the import graph (a preprocessed project can show import ordering), evaluated values, and
target execution with the original scenario. Preprocessing is not a substitute for evaluated
property/item values. Exercise both single-targeted and multi-targeted consumers when affected,
and direct/transitive consumers of package assets. Confirm pre-existing hooks still run, new hooks
run exactly where intended, and optional versus required missing files behave differently.
