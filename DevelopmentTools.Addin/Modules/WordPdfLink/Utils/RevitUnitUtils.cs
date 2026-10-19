using Autodesk.Revit.DB;

namespace DT_WordPdfLink.Utils
{
    public static class RevitUnitUtils
    {
        public static double MmToInternal(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }
    }
}
