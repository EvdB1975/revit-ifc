// Copyright (C) 2026 Autodesk, Inc.
// Licensed under the GNU Lesser General Public License, version 2.1 or later.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Enums;
using Revit.IFC.Common.Utility;
using Revit.IFC.Export.Utility;

namespace Revit.IFC.Export.Exporter
{
   /// <summary>
   /// Restores whole/part relationships for independently exported shared family components.
   /// </summary>
   internal static class SharedFamilyInstanceExporter
   {
      /// <summary>
      /// Runs after assembly decomposition and before spatial containment.  Both export order and
      /// element filtering are respected: only existing handles and immediate parents are used.
      /// </summary>
      internal static void RelateSharedComponents(ExporterIFC exporterIFC, Document document)
      {
         // ElementToHandleCache keeps only the first handle.  Use the reverse cache so that a
         // split parent is not silently represented by its first storey occurrence.
         Dictionary<ElementId, HashSet<IFCAnyHandle>> familyHandles = new();
         foreach (IFCAnyHandle handle in ExporterCacheManager.HandleToElementCache.GetHandles())
         {
            if (IFCAnyHandleUtil.IsNullOrHasNoValue(handle) ||
               ExporterCacheManager.HandleToDeleteCache.Contains(handle) ||
               !IFCAnyHandleUtil.IsSubTypeOf(handle, IFCEntityType.IfcElement))
               continue;

            ElementId id = ExporterCacheManager.HandleToElementCache.Find(handle);
            if (!(document.GetElement(id) is FamilyInstance))
               continue;

            if (!familyHandles.TryGetValue(id, out HashSet<IFCAnyHandle> handles))
            {
               handles = new HashSet<IFCAnyHandle>();
               familyHandles[id] = handles;
            }
            handles.Add(handle);
         }

         HashSet<IFCAnyHandle> pendingParts = new();
         foreach (HashSet<IFCAnyHandle> parts in ExporterCacheManager.ContainmentCache.Cache.Values)
            pendingParts.UnionWith(parts);

         Dictionary<IFCAnyHandle, HashSet<IFCAnyHandle>> relations = new();
         foreach (KeyValuePair<ElementId, HashSet<IFCAnyHandle>> entry in familyHandles)
         {
            FamilyInstance child = document.GetElement(entry.Key) as FamilyInstance;
            if (child.Symbol?.Family?.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() != 1)
               continue;

            Element parent = child.SuperComponent;
            if (parent == null || parent.Id == child.Id ||
               !familyHandles.TryGetValue(parent.Id, out HashSet<IFCAnyHandle> parents) || parents.Count != 1)
               continue;

            IFCAnyHandle parentHandle = null;
            foreach (IFCAnyHandle handle in parents)
               parentHandle = handle;

            // Keep specialized decompositions intact, including ones awaiting finalization.
            if (IFCAnyHandleUtil.GetRelDecomposes(parentHandle).Count != 0 ||
               ExporterCacheManager.ContainmentCache.Cache.ContainsKey(parentHandle))
               continue;

            foreach (IFCAnyHandle childHandle in entry.Value)
            {
               if (childHandle == parentHandle || pendingParts.Contains(childHandle) ||
                  ExporterCacheManager.ElementsInAssembliesCache.Contains(childHandle) ||
                  IFCAnyHandleUtil.HasRelDecomposes(childHandle) ||
                  IFCAnyHandleUtil.GetAggregateInstanceAttribute<HashSet<IFCAnyHandle>>(
                     childHandle, "ContainedInStructure")?.Count > 0)
                  continue;

               if (!relations.TryGetValue(parentHandle, out HashSet<IFCAnyHandle> children))
               {
                  children = new HashSet<IFCAnyHandle>();
                  relations[parentHandle] = children;
               }
               children.Add(childHandle);
            }
         }

         foreach (KeyValuePair<IFCAnyHandle, HashSet<IFCAnyHandle>> relation in relations)
         {
            ExporterUtil.RelateObjects(exporterIFC, null, relation.Key, relation.Value);
            // Level/orphan containment already filters HasRelDecomposes.  Room containment
            // is emitted separately and must also defer to the new whole/part relationship.
            foreach (SpaceInfo space in ExporterCacheManager.SpaceInfoCache.SpaceInfos.Values)
               space.RelatedElements.ExceptWith(relation.Value);
         }
      }
   }
}
