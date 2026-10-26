# Shared nested family investigation and Agent 3 handoff

Upstream repository: `Autodesk/revit-ifc`. Work branch: `shared-family-fix`.
Baseline: `IFC_v25.4.4`, commit `cae082eca1e6702f7b7deb7039fee849292ae5bd`.
The branch started exactly at this baseline. No other branch was checked out or changed.

# CURRENT BEHAVIOR

Shared nested family instances are independently exportable Revit project elements.
Subject to normal visibility, category, selection, and geometry filters, each gets
its own IFC occurrence, properties, geometry, type association, and spatial
containment. The parent also gets an occurrence if its export path succeeds.
Nothing in the baseline managed exporter records a decomposition relationship
between those occurrences. They generally appear as peers in the spatial tree.
Room-relative containment can replace level containment for some mappings.

There is no universal IFC entity for a family. Category/export overrides determine
the occurrence: e.g. IfcBuildingElementProxy, IfcFurniture, IfcDoor, IfcWindow,
IfcColumn, or an MEP element. Types and representation maps are reused where the
selected path supports them. A parent is not automatically an IfcElementAssembly.

Non-shared nested components ordinarily contribute geometry to the parent family
representation. They do **not** receive a general-purpose IfcRelNests or
IfcRelAggregates relationship in this managed implementation. Geometric nesting
(GeometryInstance transforms, symbol geometry, and mapped representations) is not
an IFC object decomposition relationship. Specialized curtain wall, stair, roof,
part, railing, or assembly decomposition should not be confused with generic
family nesting.

These findings come from source inspection and Autodesk API/model documentation;
no baseline or patched RVT export was executed in this task. Geometry extraction
and IFC serialization inside Revit's native API cannot be fully established from
the managed repository alone.

# RELEVANT SOURCE FILES

All paths below are repository-relative.

| File | Relevant classes/methods and role |
| --- | --- |
| `Source/Revit.IFC.Export/Utility/ElementFilteringUtil.cs` | `GetNonSpatialElements`: selection or filtered project collector |
| `Source/Revit.IFC.Export/Exporter/Exporter.cs` | `ExportNonSpatialElements`, `ExportElement`, `ExportElementImpl`: independent element dispatch; `EndDocumentExportCommon`: deferred assembly and spatial relationships; `ValidateContainedHandle`, `RemoveContainedHandlesFromSet`, `RelateLevels`: exclude decomposed children from spatial containment |
| `Source/Revit.IFC.Export/Exporter/FamilyInstanceExporter.cs` | `ExportFamilyInstanceElement`, `ExportGenericToSpecificElement`, `ExportFamilyInstanceAsMappedItem`: category-specific or mapped family occurrence creation |
| `Source/Revit.IFC.Common/Utility/SolidMeshGeometryInfo.cs` | `CollectSolidMeshGeometry`: recursive GeometryInstance symbol geometry with accumulated transforms |
| `Source/Revit.IFC.Export/Utility/GeometryUtil.cs` | `GetSplitSolidMeshGeometry`: geometry collection used by family export |
| `Source/Revit.IFC.Export/Utility/RepresentationUtil.cs` | `CreateAppropriateProductDefinitionShape`: allows an empty-body family with `GetSubComponentIds().Any()` to proceed so parameter data can survive |
| `Source/Revit.IFC.Export/Utility/LevelUtil.cs` | `GetBaseLevelIdForElement`: consults `SuperComponent` for family level information |
| `Source/Revit.IFC.Export/Utility/ProductWrapper.cs` | `RegisterHandleWithElement`, `AddElement`: export handle registration and level/orphan association; `ParentWrapper` is wrapper lifetime/property bookkeeping, not family ownership |
| `Source/Revit.IFC.Export/Utility/ElementToHandleCache.cs` | `Register`: keeps only the first handle for an ElementId |
| `Source/Revit.IFC.Export/Utility/HandleToElementCache.cs` | Reverse mapping retains additional registered handles; new `GetHandles` returns a snapshot |
| `Source/Revit.IFC.Export/Utility/AssemblyInstanceCache.cs` | `RegisterElements`: explicit Revit assembly membership bookkeeping, unrelated to SuperComponent |
| `Source/Revit.IFC.Export/Exporter/AssemblyInstanceExporter.cs` | Assembly creation and placement adjustment; precedent for unordered physical decomposition |
| `Source/Revit.IFC.Export/Utility/ExporterUtil.cs` | `RelateObjects`: immediately creates IfcRelAggregates with a deterministic parent-derived GUID when no GUID is supplied |
| `Source/Revit.IFC.Export/Utility/ContainmentCache.cs` | Pending aggregations, despite the broader cache name |
| `Source/Revit.IFC.Export/Utility/SpaceInfo.cs` | `SpaceInfo.RelatedElements`, `SpaceInfoCache.RelateToSpace`: separate pending room containment |
| `Source/Revit.IFC.Common/Utility/IFCAnyHandleUtil.cs` | `HasRelDecomposes` tests a child's parent; confusingly, `GetRelDecomposes` reads the parent's `IsDecomposedBy` inverse |
| `Source/Revit.IFC.Export/Exporter/SharedFamilyInstanceExporter.cs` | New deferred relationship pass |

# CALL PATH

1. Exporter initialization registers `ExportNonSpatialElements` among export passes.
2. `ElementFilteringUtil.GetNonSpatialElements` obtains project/selected elements.
   Shared children are individually addressable project instances. There is no
   shared-family-specific recursive export traversal here.
3. `ExportNonSpatialElements` calls `ExportElement` for each selected element.
4. `ExportElement` applies export checks and creates a ProductWrapper.
   `ExportElementImpl` obtains `element.get_Geometry(options)` and dispatches a
   FamilyInstance to `FamilyInstanceExporter.ExportFamilyInstanceElement`.
5. `ExportFamilyInstanceElement` checks visibility/geometry, resolves export type,
   tries specialized export, and otherwise calls `ExportFamilyInstanceAsMappedItem`.
   The latter reads instance or symbol geometry, collects solids/meshes, builds
   representations/types, and creates the mapped occurrence. Columns can split
   across levels. There is no shared/non-shared ownership branch in this method.
6. `ProductWrapper.AddElement`/`RegisterHandleWithElement` register handles in both
   directions. Assembly/group membership is registered explicitly in
   `ExportElementImpl`; family ownership is not.
7. `EndDocumentExportCommon` creates deferred assembly relationships, calls
   `RelateLevels`, handles orphan containment, emits pending aggregations, and
   later emits room containment. Baseline has no family relationship pass.
8. Patched step: immediately after assembly processing and before `RelateLevels`,
   call `SharedFamilyInstanceExporter.RelateSharedComponents`.

# REASON FOR CURRENT BEHAVIOR

The structural divergence starts at Revit's project element/geometry boundary:
non-shared contents are consumed as parent geometry, whereas shared contents can
be exported independently. The independent-element architecture faithfully
creates separate occurrences but never translates their Revit family ownership
into IFC object decomposition. Reusing symbol geometry or a type is insufficient:
neither identifies the parent occurrence of a particular shared child.

The managed source has only two direct family hierarchy queries in this baseline:
`SuperComponent` for level inference and `GetSubComponentIds` for the empty-body
exception. Neither records a relationship. The `parentLocalPlacement` argument
of mapped family export is placement support; the ordinary top-level call passes
null. Explicit AssemblyInstanceId and GroupId handling cannot substitute for
SuperComponent, since a nested family is not thereby a Revit AssemblyInstance or
Group member.

Thus the relationship is omitted by absence of a translation/finalization step,
not by a found `if (shared) skip relationship` rule. Source inspection establishes
the mechanism, but does not establish an original author's intent. This checkout
is shallow at the baseline; its single baseline commit cannot date the omission
or prove a historical decision. No unsupported claim of deliberate suppression
or an Autodesk policy is made.

# ROOT CAUSE

| Candidate | Finding |
| --- | --- |
| Revit API limitation | Not for exported, project-addressable shared children: SuperComponent survives and identifies their parent. API/project visibility still limits non-shared, filtered, or unexported ancestors. |
| Exporter design choice | Independent element export and specialized deferred relationship caches are architectural choices. No generic family ownership pass exists in the baseline. |
| IFC schema limitation | No: unordered physical decomposition is representable by IfcRelAggregates. |
| Implementation bug / missing bookkeeping | A missing family-to-IFC relationship translation is the immediate omission. The existing document and handle caches suffice for the supported cases, so new persistent export state is unnecessary. |
| Historical decision | Unknown; no relevant rationale is evidenced by the inspected comments or available shallow history. |

# PROPOSED IFC BEHAVIOR

Represent an independently exported shared family occurrence as a part of its
immediate exported parent occurrence using **IfcRelAggregates**. Siblings share
one relationship; multiple nesting levels retain each immediate edge. Revit's
Shared flag exposes a component independently; it does not dissolve its physical
dependence on its host occurrence. This also matches Autodesk's existing
`ExporterUtil.RelateObjects` usage for physical assemblies/stair components.

IfcRelNests is a candidate by name, but IFC4 repurposes it for ordered composition,
including ports. Revit family child enumeration supplies no semantic part order.
IfcRelContainedInSpatialStructure requires a spatial parent and describes
location, not physical family composition. IfcRelAssignsToGroup describes group
membership without the relevant whole/part dependence. IfcElementAssembly is an
entity rather than a relationship; changing a parent's mapped class would be a
larger, unrelated behavior change. Type relations and geometric placements also
do not encode this occurrence-to-occurrence ownership.

The fix keeps the existing occurrence classes, geometry, type mappings, and world
placements. IFC does not require aggregate children to have parent-relative local
placements. Existing placement chains remain valid, avoiding geometry movement
and placement cycles. Decomposed children inherit spatial containment through the
parent: existing level/orphan filtering handles this, and the new pass removes
the successfully related children from pending room containment.

References supporting the API/schema interpretation:

- [Autodesk API: Subcomponent and Supercomponent](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Revit_Geometric_Elements/Family_Instances/Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Family_Instances_FamilyInstances_html.html).
  The installed Revit 2025 RevitAPI.xml also documents SuperComponent and GetSubComponentIds.
- [Autodesk: Create a Family with Nested and Shared Components](https://help.autodesk.com/cloudhelp/2022/ENU/Revit-Customize/files/GUID-C869D633-4714-4A6E-8B0B-F55A1C13C056.htm).
- [Autodesk: Shared Components in a Project](https://help.autodesk.com/cloudhelp/2021/ENU/Revit-Model/files/GUID-FD2E1B04-7C1D-4B1D-8242-EA5EDFC6D1B4.htm).
- [buildingSMART: IfcRelAggregates](https://standards.buildingsmart.org/IFC/RELEASE/IFC4/FINAL/HTML/schema/ifckernel/lexical/ifcrelaggregates.htm).
- [buildingSMART: IfcRelNests](https://standards.buildingsmart.org/IFC/RELEASE/IFC4/FINAL/HTML/schema/ifckernel/lexical/ifcrelnests.htm).

# FEASIBILITY

The Revit document is still available at finalization, so family ownership need
not be captured before per-element parameter caches are cleared. SuperComponent
is an API relationship, not an exporter parameter-cache entry. Both endpoints'
IFC handles already exist after the element passes, independent of their export
order. The reverse handle cache supplies all registered occurrences; relying on
ElementToHandleCache alone would silently select the first segment of a split
parent. No additional persistent cache or cache-reset work is needed.

Conservative boundaries of this implementation:

- Only valid, registered IfcElement occurrences belonging to FamilyInstances are
  candidates; handles scheduled for deletion are excluded.
- The child's family must have FAMILY_SHARED set to 1. Standalone shared families
  and non-shared contents are unchanged.
- The immediate parent must have exactly one eligible exported handle. Missing
  or filtered parents are not synthesized, and a missing intermediate ancestor
  is not bypassed. Multiple parent handles are ambiguous and are skipped. All
  eligible split child handles can belong to an unambiguous parent.
- Existing child decomposition, assembly membership, pending aggregation, and
  already-emitted direct spatial containment take precedence.
- Parents with existing or pending decomposition retain specialized behavior.
  This avoids overlapping decomposition or duplicate parent-derived GUIDs.
- Non-IfcElement mappings are unchanged. Pure containers still depend on the
  baseline geometry/export path actually creating their parent occurrence; the
  patch does not broaden geometry acceptance or force their export.

Supporting ambiguous split parents would require retaining or creating a logical
whole occurrence rather than guessing one storey segment. Supporting missing
parent occurrences would require changing their earlier creation/filtering path.
Those are separate changes and are intentionally outside this minimal repair.

# IMPLEMENTATION PLAN

Implemented production changes:

1. `HandleToElementCache.GetHandles()` in
   `Source/Revit.IFC.Export/Utility/HandleToElementCache.cs`: return a snapshot of
   reverse cache keys without exposing its mutable dictionary.
2. New `SharedFamilyInstanceExporter.RelateSharedComponents(ExporterIFC, Document)`
   in `Source/Revit.IFC.Export/Exporter/SharedFamilyInstanceExporter.cs`: collect
   eligible occurrences, resolve shared children and immediate parents, avoid
   conflicting/specialized decomposition, group siblings, call the existing
   aggregation helper, and remove redundant pending room containment.
3. `Exporter.EndDocumentExportCommon` in
   `Source/Revit.IFC.Export/Exporter/Exporter.cs`: invoke the pass after assemblies
   and before spatial containment. No geometry/export-class/placement methods changed.

# VALIDATION

The Release exporter and its Common project built successfully against installed
Revit 2025 assemblies in `C:\Program Files\Autodesk\Revit 2025`: **0 warnings,
0 errors**. NuGet restore required network access. The repository's default API
HintPaths point outside this checkout, so a local, uncommitted target overrides
only RevitAPI/RevitAPIIFC/RevitAPIUI HintPaths at build time. No project files were
changed to accommodate this machine.

Local build override (`artifacts/RevitApi.targets`):

```xml
<Project>
  <Target Name="UseInstalledRevit2025" BeforeTargets="ResolveAssemblyReferences">
    <ItemGroup>
      <Reference Update="RevitAPI;RevitAPIIFC;RevitAPIUI">
        <HintPath>C:\Program Files\Autodesk\Revit 2025\%(Identity).dll</HintPath>
      </Reference>
    </ItemGroup>
  </Target>
</Project>
```

Build from the repository root in PowerShell using the shared API reference fallback:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location).Path 'artifacts/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build Revit.IFC.Export.slnf --configuration Release -p:Platform=x64
dotnet run --project Tests/SharedFamilyRelationships/SharedFamilyRelationships.csproj --configuration Release
```

The dependency-free console regression suite links the **production** new pass
and reverse handle cache into a test assembly with minimal Revit/IFC doubles.
**16 scenarios passed**: both export orders; sibling grouping; room containment
cleanup; repeated execution; three-level chains; filtered parents; non-shared
and standalone families; ambiguous split parents; all split children; existing
decomposition; assembly membership; pending child/parent decomposition;
specialized parent decomposition; emitted spatial containment; invalid/deleted/
non-element handles; self-reference rejection; and snapshot isolation. Several
checks are grouped into one scenario.

These are managed algorithm regression tests. They do not validate native IFC
inverse updates, schema serialization, actual Revit family parameter access,
geometry fidelity, or an RVT export. No end-to-end Revit execution is claimed.

# AGENT 3 HANDOFF

Review the pushed `origin/shared-family-fix` commit identified in the accompanying
Agent 2 report. Compare it directly with IFC_v25.4.4; no other feature is included.
Production scope is the three files in the implementation plan. Test scope is
`Tests/SharedFamilyRelationships`; this document records the investigation and
remaining integration checks. Existing untracked files named `git` and
`shared-family-fix` were present before work and were left untouched.

Run the console suite and rebuild as above. Before approving for deployment,
perform actual Revit 2025 exports with baseline and patched binaries:

1. Parent with body geometry and two shared children: preserve occurrence counts,
   names/types, properties, GUIDs, and world geometry; add one IfcRelAggregates
   with the parent as RelatingObject and both children as RelatedObjects.
2. Repeat with three nesting levels, repeated/mirrored/rotated parent instances,
   and different instance/type parameters. Each child must reference its own
   immediate occurrence parent, without cross-instance links or movement.
3. Put furniture/MEP children in a room with room-relative export enabled. The
   parent alone should supply the hierarchy's spatial containment; related
   children must not remain in direct room or storey containment.
4. Verify standalone shared families, non-shared nested geometry, unrelated
   families, assemblies, groups exported as physical aggregates, parts, and
   specialized decompositions retain baseline behavior.
5. Filter out a parent; filter out one child; test a geometry-free parent which
   succeeds and one which fails its baseline export path. No endpoint should be
   forced into the export, no child lost, and no edge should bypass a missing parent.
6. Enable column splitting. An unambiguous parent may aggregate all child
   segments; a parent with multiple occurrence handles must be skipped.
7. Export IFC2x3 and IFC4, then IFC4x3 if supported by the test environment. Check
   schema validity, no duplicate relationship GUIDs, no multiple aggregate parents,
   no self-reference/cycles, and no direct spatial containment for related children.
8. Export linked documents and repeated exports in one Revit session to verify
   document cache isolation and no stale handle reuse. The new pass adds no
   persistent state and runs in common host/link document finalization.

Review specifically the conservative treatment of already-decomposed parents and
multiple parent handles. Expanding those cases requires an explicit semantic
decision about combining decompositions or creating logical container occurrences;
it should not be done by selecting the first cached handle.
