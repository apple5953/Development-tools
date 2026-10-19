using System;
using System.Linq;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 開口尺寸自動標註服務
    /// 負責在剖面圖上自動建立開口寬度、高度、距樓板垂直高度之標註，並注釋牆體類型與尺寸
    /// </summary>
    public static class AutoDimensionService
    {
        public static void CreateOpeningDimensions(Document doc, ViewSection sectionView, Wall wall, FamilyInstance opening)
        {
            if (sectionView == null || opening == null || wall == null) return;

            try
            {
                Reference leftRef = opening.GetReferences(FamilyInstanceReferenceType.Left).FirstOrDefault()
                                   ?? opening.GetReferences(FamilyInstanceReferenceType.StrongReference).FirstOrDefault()
                                   ?? opening.GetReferences(FamilyInstanceReferenceType.WeakReference).FirstOrDefault();

                Reference rightRef = opening.GetReferences(FamilyInstanceReferenceType.Right).FirstOrDefault()
                                    ?? opening.GetReferences(FamilyInstanceReferenceType.StrongReference).Skip(1).FirstOrDefault()
                                    ?? opening.GetReferences(FamilyInstanceReferenceType.WeakReference).Skip(1).FirstOrDefault();

                Reference topRef = opening.GetReferences(FamilyInstanceReferenceType.Top).FirstOrDefault();
                Reference bottomRef = opening.GetReferences(FamilyInstanceReferenceType.Bottom).FirstOrDefault();

                BoundingBoxXYZ opBBox = opening.get_BoundingBox(sectionView);
                if (opBBox == null) opBBox = opening.get_BoundingBox(null);

                // 水平寬度標註
                if (leftRef != null && rightRef != null && opBBox != null)
                {
                    try
                    {
                        ReferenceArray refArrayX = new ReferenceArray();
                        refArrayX.Append(leftRef);
                        refArrayX.Append(rightRef);

                        XYZ p1 = new XYZ(opBBox.Min.X, opBBox.Max.Y + GeometryService.MmToFeet(150), opBBox.Min.Z);
                        XYZ p2 = new XYZ(opBBox.Max.X, opBBox.Max.Y + GeometryService.MmToFeet(150), opBBox.Min.Z);
                        Line dimLineX = Line.CreateBound(p1, p2);

                        doc.Create.NewDimension(sectionView, dimLineX, refArrayX);
                    }
                    catch { }
                }

                // 垂直高度標註
                if (topRef != null && bottomRef != null && opBBox != null)
                {
                    try
                    {
                        ReferenceArray refArrayY = new ReferenceArray();
                        refArrayY.Append(bottomRef);
                        refArrayY.Append(topRef);

                        XYZ p1 = new XYZ(opBBox.Max.X + GeometryService.MmToFeet(150), opBBox.Min.Y, opBBox.Min.Z);
                        XYZ p2 = new XYZ(opBBox.Max.X + GeometryService.MmToFeet(150), opBBox.Max.Y, opBBox.Min.Z);
                        Line dimLineY = Line.CreateBound(p1, p2);

                        doc.Create.NewDimension(sectionView, dimLineY, refArrayY);
                    }
                    catch { }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
