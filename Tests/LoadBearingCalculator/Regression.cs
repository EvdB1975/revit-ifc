// Standalone regression harness: compile the production calculator against minimal API doubles.
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Revit.IFC.Export.Exporter.PropertySet.Calculators;

namespace Autodesk.Revit.DB
{
   public enum BuiltInParameter { WALL_STRUCTURAL_SIGNIFICANT, FLOOR_PARAM_IS_STRUCTURAL }
   public enum BuiltInCategory { OST_StructuralColumns, OST_StructuralFraming, OST_StructuralFramingSystem, OST_StructuralTruss }
   public enum StorageType { Integer, String }
   public class Parameter
   {
      public bool HasValue = true;
      public StorageType StorageType = StorageType.Integer;
      public int Value;
      public int AsInteger() { return Value; }
   }
   public class ElementId
   {
      private int value;
      public ElementId(BuiltInCategory category) { value = (int)category; }
      public override bool Equals(object other) { return other is ElementId && ((ElementId)other).value == value; }
      public override int GetHashCode() { return value; }
      public static bool operator ==(ElementId a, ElementId b) { return Object.Equals(a, b); }
      public static bool operator !=(ElementId a, ElementId b) { return !Object.Equals(a, b); }
   }
   public class Element
   {
      public Parameter Structural;
      public Parameter Floor;
      public ElementId Category;
      public int? Override;
      public Parameter get_Parameter(BuiltInParameter parameter)
      { return parameter == BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT ? Structural : Floor; }
   }
   public class ElementType : Element { }
   public class Wall : Element { public StructuralWallUsage StructuralUsage; }
}
namespace Autodesk.Revit.DB.Structure
{ public enum StructuralWallUsage { NonBearing, Bearing, Shear, Combined } }
namespace Autodesk.Revit.DB.IFC { public class ExporterIFC { } }
namespace Revit.IFC.Export.Utility
{
   public class IFCExportBodyParams { }
   public class EntryMap { public string RevitParameterName; public string CompatibleRevitParameterName; }
   public static class CategoryUtil
   { public static ElementId GetSafeCategoryId(Element element) { return element.Category; } }
   public static class ParameterUtil
   { public static int? GetIntValueFromElementOrSymbol(Element element, string name, string compatible) { return element.Override; } }
}
namespace Revit.IFC.Export.Exporter.PropertySet.Calculators
{
   public abstract class PropertyCalculator
   {
      public abstract bool Calculate(Autodesk.Revit.DB.IFC.ExporterIFC exporter,
         Revit.IFC.Export.Utility.IFCExportBodyParams body, Element element, ElementType type,
         Revit.IFC.Export.Utility.EntryMap map);
      public abstract bool GetBooleanValue();
   }
}
public static class LoadBearingRegression
{
   private static int checks;
   private static void Check(Element element, bool success, bool value)
   {
      var calculator = LoadBearingCalculator.Instance;
      bool actual = calculator.Calculate(null, null, element, null, new Revit.IFC.Export.Utility.EntryMap());
      if (actual != success || (actual && calculator.GetBooleanValue() != value))
         throw new Exception("Regression failed at case " + (checks + 1));
      checks++;
   }
   public static void Run()
   {
      foreach (StructuralWallUsage usage in Enum.GetValues(typeof(StructuralWallUsage)))
      {
         Check(new Wall { StructuralUsage = usage, Structural = new Parameter { Value = 1 } }, true, true);
         Check(new Wall { StructuralUsage = usage, Structural = new Parameter { Value = 0 } }, true, usage == StructuralWallUsage.Bearing);
         Check(new Wall { StructuralUsage = usage }, true, usage == StructuralWallUsage.Bearing);
         Check(new Wall { StructuralUsage = usage, Structural = new Parameter { Value = 1, HasValue = false } }, true, usage == StructuralWallUsage.Bearing);
         Check(new Wall { StructuralUsage = usage, Structural = new Parameter { Value = 1, StorageType = StorageType.String } }, true, usage == StructuralWallUsage.Bearing);
      }
      foreach (BuiltInCategory category in Enum.GetValues(typeof(BuiltInCategory)))
         Check(new Element { Category = new ElementId(category) }, true, true);
      Check(new Element { Floor = new Parameter { Value = 1 } }, true, true);
      Check(new Element { Floor = new Parameter { Value = 0 } }, true, false);
      Check(new Element { Override = 1 }, true, true);
      Check(new Element { Override = 0 }, true, false);
      Check(new Element(), false, false);
      Console.WriteLine("PASS: " + checks + " production calculator regression cases.");
   }
}
