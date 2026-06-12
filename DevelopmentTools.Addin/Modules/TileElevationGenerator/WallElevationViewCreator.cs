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

            // 判斷剖面觀看方向是否需要反向
            XYZ roomSideDir = data.RoomSideDirection;
            if (settings.FlipDirection)
            {
                roomSideDir = -roomSideDir;
            }

            // 讀取 UI 設定參數並轉為英呎 (Revit 內部單位)
            double userWallOffsetFeet = settings.WallOffset / 304.8;
            double userViewDepthFeet = settings.ViewDepth / 304.8;
            
            double offsetFeet;
            double depthFeet;
            if (data.WallElement == null)
            {
                // 樓板外廓剖切 (無牆體)
                // 剖刀往房間內退 (即法線的反方向)
                offsetFeet = -userWallOffsetFeet;  
                depthFeet = userViewDepthFeet;    // 剖切深度
            }
            else
            {
                // 牆面剖切
                // 剖刀往房間內退：從中心退牆體半寬，再退 WallOffset
                offsetFeet = -(wallThickness / 2.0) - userWallOffsetFeet; 
                // 深度為 WallOffset 加上牆厚度 + userViewDepthFeet，以確保切到牆體並看透
                depthFeet = userWallOffsetFeet + wallThickness + userViewDepthFeet;          
            }
            double bottomOffsetFeet = settings.BottomOffset / 304.8;            // 底部延伸量
            double topOffsetFeet = settings.TopOffset / 304.8;                        // 頂部延伸量
            double leftRightExtensionFeet = settings.SideExtension / 304.8;             // 左右延伸量

            // 使用樓層高程(LevelElevation)做為 Z 軸基準，確保底部切齊樓層線
            XYZ midPointWithZ = new XYZ(data.MidPoint.X, data.MidPoint.Y, data.LevelElevation + (heightFeet / 2.0));
            // 剖刀原點定位在往房間內偏移 offsetFeet 處
            t.Origin = midPointWithZ + roomSideDir * offsetFeet;

            // X 軸平行於牆面，Y 軸為 Z 正向 (朝上)
            t.BasisX = data.WallDirection;
            t.BasisY = XYZ.BasisZ;
            // 觀看方向朝向牆面（與 roomSideDir 相反，亦即朝向牆體方向看）
            t.BasisZ = -roomSideDir; 

            // 確保 BasisX、BasisY、BasisZ 為右手坐標系 (X x Y = Z)
            if (!t.BasisX.CrossProduct(t.BasisY).IsAlmostEqualTo(t.BasisZ))
            {
                t.BasisX = -t.BasisX;
            }

            // 3. 建立 BoundingBoxXYZ (CropBox)
            var bbox = new BoundingBoxXYZ();
            bbox.Transform = t;

            // 設定裁剪邊界，將 Max.Z 設為 150mm (原本是 0.1mm) 以避免切掉牆面往外貼磚的厚度
            bbox.Min = new XYZ(-(lengthFeet / 2.0) - leftRightExtensionFeet, -(heightFeet / 2.0) - bottomOffsetFeet, -depthFeet);
            bbox.Max = new XYZ((lengthFeet / 2.0) + leftRightExtensionFeet, (heightFeet / 2.0) + topOffsetFeet, 150.0 / 304.8);

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
