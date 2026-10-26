// Minimal managed doubles: these tests exercise the production relationship pass,
// not Revit's native geometry, IFC inverse attributes, or file serialization.
namespace Autodesk.Revit.DB
{
   public record ElementId(int Value)
   {
      public static ElementId InvalidElementId = new(-1);
   }
   public enum BuiltInParameter { FAMILY_SHARED }
   public class Parameter { public int Value = 1; public int AsInteger() => Value; }
   public class Element { public ElementId Id; }
   public class Family : Element
   {
      public Parameter Shared = new();
      public Parameter get_Parameter(BuiltInParameter parameter) => Shared;
   }
   public class FamilySymbol { public Family Family = new(); }
   public class FamilyInstance : Element
   {
      public FamilySymbol Symbol = new();
      public Element SuperComponent;
   }
   public class Document
   {
      public Dictionary<ElementId, Element> Elements = new();
      public Element GetElement(ElementId id) => Elements.GetValueOrDefault(id);
   }
}
namespace Autodesk.Revit.DB.IFC
{
   public class ExporterIFC { }
   public class IFCAnyHandle
   {
      public bool Valid = true;
      public bool IsElement = true;
      public bool HasParent;
      public HashSet<IFCAnyHandle> Decomposition = new();
      public HashSet<IFCAnyHandle> SpatialContainers;
   }
}
namespace Revit.IFC.Common.Enums { public enum IFCEntityType { IfcElement } }
namespace Revit.IFC.Common.Utility
{
   using Autodesk.Revit.DB.IFC;
   using Revit.IFC.Common.Enums;
   public static class IFCAnyHandleUtil
   {
      public static bool IsNullOrHasNoValue(IFCAnyHandle h) => h == null || !h.Valid;
      public static bool IsSubTypeOf(IFCAnyHandle h, IFCEntityType type) => h.IsElement;
      public static bool HasRelDecomposes(IFCAnyHandle h) => h.HasParent;
      public static HashSet<IFCAnyHandle> GetRelDecomposes(IFCAnyHandle h) => h.Decomposition;
      public static T GetAggregateInstanceAttribute<T>(IFCAnyHandle h, string name)
         where T : ICollection<IFCAnyHandle>, new()
      {
         if (h.SpatialContainers == null) return default;
         T result = new();
         foreach (var item in h.SpatialContainers) result.Add(item);
         return result;
      }
   }
}
namespace Revit.IFC.Export.Toolkit { }
namespace Revit.IFC.Export.Utility
{
   using Autodesk.Revit.DB;
   using Autodesk.Revit.DB.IFC;
   public class ElementToHandleCache { public void Delete(ElementId id) { } }
   public class ContainmentCache { public Dictionary<IFCAnyHandle, HashSet<IFCAnyHandle>> Cache = new(); }
   public class SpaceInfo { public HashSet<IFCAnyHandle> RelatedElements = new(); }
   public class SpaceInfoCache { public Dictionary<ElementId, SpaceInfo> SpaceInfos = new(); }
   public static class ExporterCacheManager
   {
      public static HandleToElementCache HandleToElementCache = new();
      public static ElementToHandleCache ElementToHandleCache = new();
      public static HashSet<IFCAnyHandle> HandleToDeleteCache = new();
      public static HashSet<IFCAnyHandle> ElementsInAssembliesCache = new();
      public static ContainmentCache ContainmentCache = new();
      public static SpaceInfoCache SpaceInfoCache = new();
      public static void Reset()
      {
         HandleToElementCache.Clear();
         HandleToDeleteCache.Clear();
         ElementsInAssembliesCache.Clear();
         ContainmentCache.Cache.Clear();
         SpaceInfoCache.SpaceInfos.Clear();
         ExporterUtil.Relations.Clear();
      }
   }
   public static class ExporterUtil
   {
      public static List<(IFCAnyHandle Parent, HashSet<IFCAnyHandle> Children)> Relations = new();
      public static void RelateObjects(ExporterIFC exporter, string guid, IFCAnyHandle parent,
         ICollection<IFCAnyHandle> children)
      {
         Relations.Add((parent, new(children)));
         foreach (var child in children) child.HasParent = true;
         parent.Decomposition.Add(new IFCAnyHandle());
      }
   }
}
