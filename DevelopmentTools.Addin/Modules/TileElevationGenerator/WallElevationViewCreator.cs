using System;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public static class WallElevationViewCreator
    {
        public static ViewSection CreateElevationView(Document doc, WallElevationData data, GeneratorSettings settings, string viewName)
        {
            // 1. 取得 Section ViewFamilyType
            ElementId sectionTypeId = GetSectionViewFamilyTypeId(doc);
            if (sectionTypeId == ElementId.InvalidElementId)
            {
                throw new InvalidOperationException("Revit 專案中找不到 Section 視圖類型！");
            }

            // 2. 準備 Transform 局部坐標系
            var t = Transform.Identity;
            
            // 將原點 Origin 設定在牆體高度的中心點，並朝向房間內部（RoomSideDirection）微調 WallOffset 距離
            double heightFeet = data.WallHeight;
            double lengthFeet = data.WallLength;
            double wallThickness = data.WallThickness;

            // 讀取 UI 設定參數並轉為英呎 (Revit 內部單位)
            double userWallOffsetFeet = settings.WallOffset / 304.8;
            double userViewDepthFeet = settings.ViewDepth / 304.8;
            
            double offsetFeet;
            double depthFeet;
            if (data.WallElement == null)
            {
                // 樓板外廓剖切 (無牆體)
                offsetFeet = userWallOffsetFeet;  // 前移偏移量
                depthFeet = userViewDepthFeet;    // 剖切深度
            }
            else
            {
                // 牆面剖切
                offsetFeet = (wallThickness / 2.0) + userWallOffsetFeet; // 牆體半寬 + 前移偏移量
                depthFeet = wallThickness + userViewDepthFeet;          // 牆厚度 + 剖切深度，確保切透牆面
            }
            double bottomOffsetFeet = settings.BottomOffset / 304.8;            // 底部延伸量
            double topOffsetFeet = 50.0 / 304.8;                        // 頂部延伸預設維持 50mm
            double leftRightExtensionFeet = 0.0;                        // 左右延伸改為 0.0，使多面牆能完美連續接合不重疊

            XYZ midPointWithZ = new XYZ(data.MidPoint.X, data.MidPoint.Y, data.MidPoint.Z + heightFeet / 2.0);
            t.Origin = midPointWithZ + data.RoomSideDirection * offsetFeet;

            // X 軸平行於牆面，Y 軸為 Z 正向 (朝上)
            t.BasisX = data.WallDirection;
            t.BasisY = XYZ.BasisZ;
            t.BasisZ = data.RoomSideDirection; // 觀看方向為 -BasisZ (朝向牆面)

            // 確保 BasisX、BasisY、BasisZ 為右手坐標系 (X x Y = Z)
            if (!t.BasisX.CrossProduct(t.BasisY).IsAlmostEqualTo(t.BasisZ))
            {
                t.BasisX = -t.BasisX;
            }

            // 3. 建立 BoundingBoxXYZ (CropBox)
            var bbox = new BoundingBoxXYZ();
            bbox.Transform = t;

            // 設定裁剪邊界
            bbox.Min = new XYZ(-(lengthFeet / 2.0) - leftRightExtensionFeet, -(heightFeet / 2.0) - bottomOffsetFeet, -depthFeet);
            bbox.Max = new XYZ((lengthFeet / 2.0) + leftRightExtensionFeet, (heightFeet / 2.0) + topOffsetFeet, 0.1 / 304.8);

            // 4. 建立 Section View
            ViewSection section = ViewSection.CreateSection(doc, sectionTypeId, bbox);
            if (section == null)
            {
                throw new InvalidOperationException("無法建立 Section 視圖。");
            }

            // 5. 設定視圖名稱
            section.Name = viewName;

            // 6. 套用 View Template (若使用者有選取)
            if (settings.SelectedViewTemplateId != ElementId.InvalidElementId)
            {
                section.ViewTemplateId = settings.SelectedViewTemplateId;
            }

            return section;
        }

        private static ElementId GetSectionViewFamilyTypeId(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType));
            foreach (var elem in collector)
            {
                if (elem is ViewFamilyType type && type.ViewFamily == ViewFamily.Section)
                {
                    return type.Id;
                }
            }
            return ElementId.InvalidElementId;
        }
    }
}
