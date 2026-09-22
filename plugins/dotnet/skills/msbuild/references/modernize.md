# Modernize build files without changing their contract

Use this path for an explicit project/build-file cleanup, SDK-style conversion, shared-settings
change, or extension-point modernization. An SDK-style project can still need cleanup; an already
correct file can also need **no change**. A long project file is not proof that it is legacy.

## 1. Choose a bounded modernization

| Requested outcome | Read/do | Do not combine implicitly |
| --- | --- | --- |
| Review or clean up existing MSBuild files | Apply the relevant [anti-pattern decisions](antipatterns.md), including their exceptions. | Runtime/framework/package upgrades or formatting unrelated files. |
| Repair or modernize shared imports and hooks | Follow [extension points](extension-points.md). | A new plugin/agent extension or SDK-style conversion that the task did not request. |
| Convert a supported legacy project to SDK-style | Follow the conversion checklist below. | Changing the target framework, compiler/language features, package versions, or public assembly identity. |
| Consolidate shared defaults | Compare actual project differences, then preserve override and discovery behavior. | Replacing intentional per-project/per-framework settings with a universal value. |

Record the current SDK/MSBuild, project type, target frameworks, configurations, references,
assembly attributes, resources/content, generated files, and custom imports/targets. Reuse an
existing successful build or establish one when practical. If the starting build fails, distinguish
the existing failure from the migration's effects.

For properties/items whose values matter, use an existing binary log or MSBuild's evaluated
property/item queries when supported (MSBuild 17.8+). Use preprocessing to understand imports,
not as proof of final evaluated values. Keep diagnostic artifacts local.

## 2. SDK-style conversion checklist

Do not assume every old project type supports an equivalent SDK-style build. Custom project
systems, deployment projects, and legacy web project types may require another migration path.
Some .NET Framework/desktop scenarios still require Windows, reference assemblies, or full
MSBuild. State those constraints instead of promising that a short XML rewrite is sufficient.

For a supported project:

1. **Keep the framework contract.** Replace legacy framework notation with the equivalent TFM,
   for example `v4.7.2` with `net472` or `v4.8` with `net48`. Converting project format is not
   permission to retarget to modern .NET. Preserve separate conditions/configurations.
2. **Replace only superseded SDK boilerplate.** Choose the SDK appropriate to the project, remove
   legacy standard imports it replaces, and retain required custom imports and ordering. Preserve
   WPF/WinForms settings, platform/architecture, signing, constants, output identity, and relevant
   project-system metadata. Do not delete a property solely because it appears in a generic list.
3. **Reconcile items rather than deleting all lists.** SDK C#/VB defaults may replace explicit
   source entries. Preserve files outside default globs, removals, resources, content, copy/publish
   metadata, links, custom generators, and nondefault item types. Use `Update` for metadata on an
   already-included item. Keep F# compile order and `.fsi`/`.fs` adjacency.
4. **Preserve assembly attributes.** The SDK can generate common attributes from properties, but
   it does not replace every custom attribute in `AssemblyInfo.cs`. Retain `ComVisible`, `Guid`,
   `InternalsVisibleTo`, custom metadata, and any other non-generated contract. Either keep the
   file and use `GenerateAssemblyInfo=false`, or move only generated attributes to equivalent
   properties and keep the rest. Preserve distinct `AssemblyVersion` and `FileVersion` values;
   do not collapse them into a different `Version`.
5. **Migrate package references deliberately.** When `packages.config` conversion is in scope,
   preserve package IDs, versions, framework conditions, and required assets. Account for legacy
   install scripts/content transforms and package-supplied build logic that `PackageReference`
   does not reproduce automatically. Keep unrelated GAC/vendor/local assembly references.
   Remove old package declarations only after the replacement restores and behaves correctly.
6. **Retain application configuration.** Do not delete the `app.config` runtime section or binding
   redirects wholesale. Verify the application's actual generated configuration and runtime
   behavior before removing anything superseded.
7. **Remove proven redundancy last.** Compare against the selected SDK/imported defaults and the
   original output. Preserve intentional pins and overrides. Enabling nullable analysis, implicit
   usings, a new language version, or a new framework is a separate change requiring agreement.

A minimal converted library might be:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
</Project>
```

That example intentionally keeps an existing `AssemblyInfo.cs`; it is not a template for deleting
custom metadata from a real project. Preserve an executable's `OutputType` and all other
nondefault contracts as appropriate.

Use an existing repository migration tool if suitable and inspect its diff. Do not invent a
built-in `dotnet migrate-packages-config` command or install a global converter by default.
Supported IDE migration or a reviewed manual transformation are alternatives, not guarantees
that every legacy asset is compatible.

## 3. Shared settings and build logic

- Move only genuinely common defaults into a `Directory.Build.props` at the intended scope.
  Preserve deliberate project overrides and conditions; do not rely on every descendant importing
  all ancestor files automatically.
- Place work that needs project-defined properties late enough to observe them. In particular,
  early **property** conditions on `TargetFramework` differ from late-evaluated **item**
  conditions. Use the [evaluation checks](antipatterns.md#property-and-item-evaluation).
- Replace shell file operations with built-in MSBuild tasks only where behavior is equivalent.
  Split oversized targets by responsibility without losing ordering, error handling, or outputs.
- For artifact-producing targets, use complete inputs and real outputs, and preserve generated
  item inclusion, clean tracking, and no-change behavior. Route that part of the change through
  [incremental performance guidance](troubleshoot-performance.md).
- Extend hooks according to their actual scalar/list contract. Preserve existing extensions,
  nearest-file discovery, and packaged import layouts using
  [extension-point verification](extension-points.md#verify-the-extension).

Do not turn a targeted cleanup into Central Package Management, SDK upgrades, or framework
retargeting without authorization. These can be useful separate migrations, but obscure whether
the requested change preserved behavior.

## 4. Validate and stop

Compare the same framework/configuration builds before and after. Verify relevant tests, output
and assembly identities, public/custom assembly attributes, resources, copied/published content,
and package assets. Exercise affected package consumers and shared-file scopes. A successful
compilation alone does not prove packaging or runtime configuration stayed equivalent.

Run a second no-change build and affected changed-input/missing-output cases when build logic
changed. If a required platform or dependency prevents verification, state exactly which contract
remains unverified. Report preserved behavior and intentional changes separately. Leave healthy
patterns unchanged, explaining the relevant exception rather than manufacturing a cleanup.
