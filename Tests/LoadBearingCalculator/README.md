# Wall LoadBearing regression and Agent 3 handoff

Target: Autodesk/revit-ifc tag `IFC_v25.4.4`, commit `cae082eca1e6702f7b7deb7039fee849292ae5bd`.

## Investigation and change

`LoadBearingCalculator.Calculate` previously returned true only for wall
`StructuralUsage == StructuralWallUsage.Bearing`. It now also recognizes a nonzero,
valued integer `BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT` (Revit's Structural
instance setting). The original Bearing result remains the fallback for missing,
unset, zero, or incompatible parameters. Nonstructural non-bearing walls remain false.

Other existing paths are unchanged: structural column/framing/framing-system/truss
categories return true, floors use `FLOOR_PARAM_IS_STRUCTURAL`, and other elements
fall back to named integer parameters through `ParameterUtil`.

`ExporterInitializer.InitPset_WallCommon` in `ExporterInitializer_PsetDef.cs` already
registers Boolean `Pset_WallCommon.LoadBearing` with this calculator in the IFC2x2,
IFC2x3, IFC4 and IFC4x3 branches. Existing property-set enablement and certification
filters still apply. No generated registration changes are needed.

`PropertySetEntryMap.ProcessEntry` attempts `CreatePropertyFromElementOrSymbol`
before the calculator, and invokes the calculator only if no property was created.
Qualified (`Pset_WallCommon.LoadBearing`), compatible (`LoadBearing`), localized,
and applicable type parameter paths are unchanged, including explicit false values.
Type lookup remains governed by the existing export context and helper methods;
this change does not introduce a new type override policy.

## Automated validation

From the repository root, in a fresh PowerShell process:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/LoadBearingCalculator/Run.ps1
```

Result: **29 cases passed**. The harness compiles the production calculator against
minimal Revit API doubles. It covers all four structural usages with flags on/off,
missing/unset/wrong-storage parameters, singleton state reset, the four existing
structural categories, floor flags, and generic named parameter fallback.
It does not exercise actual Revit APIs, IFC serialization, or override lookup.

## Revit integration checks for Agent 3

Agent 3 independently reviewed the implementation and reran all 29 cases with no
actionable findings. Override resolution and IFC serialization remain integration
checks, not automated claims from this harness.

Build with the Revit 2025 API assemblies, then export a small Revit 2025 model with
common IFC property sets enabled for each supported schema/profile that permits
`Pset_WallCommon`:

| Wall/setup | Expected result |
| --- | --- |
| Structural checked; Bearing, Shear, or Structural combined | LoadBearing TRUE |
| Structural unchecked; Non-bearing | LoadBearing FALSE |
| Structural wall; recognized explicit instance override FALSE | FALSE, override wins |
| Nonstructural wall; recognized explicit instance override TRUE | TRUE, override wins |
| Recognized type override, in a context where current exporter reads it | Existing precedence preserved, including FALSE |
| Conflicting instance/type overrides | Existing instance/type precedence preserved |
| Export walls in alternating structural/nonstructural order | No stale calculator value |

Inspect actual `IfcPropertySingleValue` Boolean values and their association with
the exported wall. Test both qualified and compatible override parameter names;
also check localized/type-suffixed names where supported by the existing path.

The Release/x64 exporter build subsequently passed against the installed
`C:/Program Files/Autodesk/Revit 2025/` assemblies (RevitAPI, RevitAPIIFC,
RevitAPIUI), with zero warnings and zero errors. A temporary build target in
`Intermediate/RevitApi.targets` redirects reference HintPaths without changing
project files. Build command after package restore:

```powershell
$env:DOTNET_CLI_HOME = 'G:/dev/revit-ifc/Intermediate/dotnet-home'
dotnet build Source/Revit.IFC.Export/Revit.IFC.Export.csproj --no-restore -p:SolutionDir=G:/dev/revit-ifc/ -p:Configuration=Release -p:Platform=x64 -p:CustomAfterMicrosoftCommonTargets=G:/dev/revit-ifc/Intermediate/RevitApi.targets -v:minimal
```

Build log: `Intermediate/LoadBearing-build.log`. Actual Revit model export remains
unverified. The checkout also arrived with
tracked files staged as deleted and working copies untracked; that index state
was left untouched. Review filesystem contents against the tag, not the index diff.
