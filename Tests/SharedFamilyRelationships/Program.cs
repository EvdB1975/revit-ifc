using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Export.Exporter;
using Revit.IFC.Export.Utility;

int passed = 0;
void Check(bool condition, string message)
{
   if (!condition) throw new Exception(message);
}
void Test(string name, Action<Fixture> test)
{
   ExporterCacheManager.Reset();
   test(new Fixture());
   Console.WriteLine("PASS " + name);
   passed++;
}

Test("child exported before parent; siblings grouped; room containment removed; repeat safe", f =>
{
   var sibling = f.Add(3, f.Parent);
   var room = new SpaceInfo();
   room.RelatedElements.UnionWith(new[] { f.ChildHandle, sibling, f.ParentHandle });
   ExporterCacheManager.SpaceInfoCache.SpaceInfos[new(10)] = room;
   f.Run();
   Check(ExporterUtil.Relations.Count == 1, "one relationship per parent");
   Check(ExporterUtil.Relations[0].Children.SetEquals(new[] { f.ChildHandle, sibling }), "siblings missing");
   Check(room.RelatedElements.SetEquals(new[] { f.ParentHandle }), "redundant room containment");
   f.Run();
   Check(ExporterUtil.Relations.Count == 1, "duplicate relationship");
});
Test("parent exported before child", f =>
{
   ExporterCacheManager.HandleToElementCache.Clear();
   ExporterCacheManager.HandleToElementCache.Register(f.ParentHandle, f.Parent.Id);
   ExporterCacheManager.HandleToElementCache.Register(f.ChildHandle, f.Child.Id);
   f.Run();
   Check(ExporterUtil.Relations.Single().Parent == f.ParentHandle, "order dependence");
});
Test("three levels retain immediate parents", f =>
{
   var leaf = f.Add(3, f.Child);
   f.Run();
   Check(ExporterUtil.Relations.Count == 2, "flattened chain");
   Check(ExporterUtil.Relations.Any(r => r.Parent == f.ChildHandle && r.Children.Contains(leaf)), "wrong immediate parent");
});
Test("filtered parent does not create or reparent elements", f =>
{
   ExporterCacheManager.HandleToElementCache.Clear();
   ExporterCacheManager.HandleToElementCache.Register(f.ChildHandle, f.Child.Id);
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "created missing parent");
});
Test("non-shared and standalone shared families unaffected", f =>
{
   f.Child.Symbol.Family.Shared.Value = 0;
   f.Add(3, null);
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "unrelated family changed");
});
Test("split parent is ambiguous", f =>
{
   ExporterCacheManager.HandleToElementCache.Register(new(), f.Parent.Id);
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "arbitrarily selected parent segment");
});
Test("all split child occurrences retained", f =>
{
   var other = new IFCAnyHandle();
   ExporterCacheManager.HandleToElementCache.Register(other, f.Child.Id);
   f.Run();
   Check(ExporterUtil.Relations.Single().Children.SetEquals(new[] { f.ChildHandle, other }), "lost split child");
});
Test("existing decomposition wins", f =>
{
   f.ChildHandle.HasParent = true;
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "second decomposition parent");
});
Test("assembly membership wins", f =>
{
   ExporterCacheManager.ElementsInAssembliesCache.Add(f.ChildHandle);
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "assembly membership changed");
});
Test("pending decomposition wins", f =>
{
   ExporterCacheManager.ContainmentCache.Cache[new()] = new() { f.ChildHandle };
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "pending relationship conflict");
});
Test("specialized parent decomposition preserved", f =>
{
   f.ParentHandle.Decomposition.Add(new());
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "duplicate parent relationship");
});
Test("pending parent decomposition preserved", f =>
{
   ExporterCacheManager.ContainmentCache.Cache[f.ParentHandle] = new() { new() };
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "pending parent changed");
});
Test("already emitted spatial containment preserved", f =>
{
   f.ChildHandle.SpatialContainers = new() { new() };
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "conflicting direct containment");
});
Test("deleted and non-element handles ignored", f =>
{
   f.ChildHandle.Valid = false;
   f.Add(3, f.Parent).IsElement = false;
   ExporterCacheManager.HandleToDeleteCache.Add(f.Add(4, f.Parent));
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "invalid handle used");
});
Test("self reference rejected", f =>
{
   f.Child.SuperComponent = f.Child;
   f.Run();
   Check(ExporterUtil.Relations.Count == 0, "self reference created");
});
Test("handle enumeration is a snapshot", f =>
{
   var snapshot = ExporterCacheManager.HandleToElementCache.GetHandles();
   ExporterCacheManager.HandleToElementCache.Clear();
   Check(snapshot.Count == 2, "live cache exposed");
});
Console.WriteLine($"{passed} tests passed.");

class Fixture
{
   public Document Document = new();
   public FamilyInstance Parent;
   public FamilyInstance Child;
   public IFCAnyHandle ParentHandle;
   public IFCAnyHandle ChildHandle;
   public Fixture()
   {
      Parent = new() { Id = new(1) };
      Document.Elements[Parent.Id] = Parent;
      ChildHandle = Add(2, Parent);
      Child = (FamilyInstance)Document.GetElement(new(2));
      ParentHandle = new();
      ExporterCacheManager.HandleToElementCache.Register(ParentHandle, Parent.Id);
   }
   public IFCAnyHandle Add(int id, FamilyInstance parent)
   {
      FamilyInstance element = new() { Id = new(id), SuperComponent = parent };
      Document.Elements[element.Id] = element;
      IFCAnyHandle handle = new();
      ExporterCacheManager.HandleToElementCache.Register(handle, element.Id);
      return handle;
   }
   public void Run() => SharedFamilyInstanceExporter.RelateSharedComponents(new(), Document);
}
