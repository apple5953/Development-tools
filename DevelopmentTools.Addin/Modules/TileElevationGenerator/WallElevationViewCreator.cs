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

            // 3. 建立 BoundingBoxXYZ (CropBox) - 這裡做為建立 Section 的預估定位值
            double halfHeightFeet = (heightFeet + topOffsetFeet + bottomOffsetFeet) / 2.0;
            double centerElevationZ = data.LevelElevation + (heightFeet + topOffsetFeet - bottomOffsetFeet) / 2.0;

            XYZ midPointWithZ = new XYZ(data.MidPoint.X, data.MidPoint.Y, centerElevationZ);
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

            var bbox = new BoundingBoxXYZ();
            bbox.Transform = t;

            bbox.Min = new XYZ(-(lengthFeet / 2.0) - leftRightExtensionFeet, -halfHeightFeet, -depthFeet);
            bbox.Max = new XYZ((lengthFeet / 2.0) + leftRightExtensionFeet, halfHeightFeet, 150.0 / 304.8);

            // 4. 建立 Section View
            ViewSection section = ViewSection.CreateSection(doc, sectionTypeId, bbox);
            if (section == null)
            {
                throw new InvalidOperationException("無法建立 Section 視圖。");
            }

            // 強制啟用裁剪框
            section.CropBoxActive = true;

            // 讀取 Revit 自動對齊並鎖定後的實體 CropBox
            BoundingBoxXYZ actualBox = section.CropBox;
            XYZ actualOrigin = actualBox.Transform.Origin;

            // 重新計算基於真實 Origin.Z 的垂直邊界，確保世界座標下的底頂高程為精確的 [zMin, zMax]
            double zMin = data.LevelElevation - bottomOffsetFeet;
            double zMax = data.LevelElevation + heightFeet + topOffsetFeet;

            double localMinY = zMin - actualOrigin.Z;
            double localMaxY = zMax - actualOrigin.Z;

            // 僅覆寫局部 Y 座標 (高度)，其餘 X (寬度) 和 Z (深度) 完全繼承自 Revit 建立時的正確邊界，防堵寬度被改壞
            actualBox.Min = new XYZ(actualBox.Min.X, localMinY, actualBox.Min.Z);
            actualBox.Max = new XYZ(actualBox.Max.X, localMaxY, actualBox.Max.Z);

            // 重新套用
            section.CropBox = actualBox;

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
