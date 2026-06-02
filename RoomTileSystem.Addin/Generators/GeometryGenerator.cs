using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using RoomTileSystem.Core;

namespace RoomTileSystem.Generators
{
    public class GeometryGenerator
    {
        private Document _doc;

        public GeometryGenerator(Document doc)
        {
            _doc = doc;
        }

        // 獲取或建立預設材質
        public ElementId GetOrCreateTileMaterial(string materialName, Color color)
        {
            // 搜尋既有材質
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(Material));
            foreach (Material mat in collector)
            {
                if (mat.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase))
                {
                    if (mat.Color.Red != color.Red || mat.Color.Green != color.Green || mat.Color.Blue != color.Blue)
                    {
                        try
                        {
                            mat.Color = color;
                        }
                        catch { }
                    }
                    return mat.Id;
                }
            }


            // 若找不到，則在 Transaction 中建立 (呼叫端需已啟動 Transaction)
            try
            {
                ElementId matId = Material.Create(_doc, materialName);
                Material newMat = _doc.GetElement(matId) as Material;
                if (newMat != null)
                {
                    newMat.Color = color;
                    newMat.Transparency = 0;
                }
                return matId;
            }
            catch
            {
                return ElementId.InvalidElementId;
            }
        }
        // 獲取宿主裝修層材質
        public ElementId GetHostFinishMaterial(Element host)
        {
            GetHostFinishMaterialAndThickness(host, ElementId.InvalidElementId, out ElementId tileMatId, out _, 10.0);
            return tileMatId;
        }

        // 精確偵測宿主的磁磚材質與厚度 (新增 targetMaterialId 以優先精準匹配點選面材質球)
        public void GetHostFinishMaterialAndThickness(Element host, ElementId targetMaterialId, out ElementId tileMatId, out double thicknessMm, double defaultMm)
        {
            tileMatId = ElementId.InvalidElementId;
            
            // 優先從傳入的磁磚面材質球名稱中解析厚度，100% 防止被結構層物理厚度覆寫 (解決厚度為 150mm 或 15mm 的 Bug)
            double tFromTarget = 0;
            if (targetMaterialId != ElementId.InvalidElementId)
            {
                Material targetMat = _doc.GetElement(targetMaterialId) as Material;
                if (targetMat != null && RoomTileSystem.Algorithms.TileDimensionDetector.TryParseThicknessFromNameFlexible(targetMat.Name, out double tVal))
                {
                    tFromTarget = tVal;
                }
            }

            if (tFromTarget > 0.01)
            {
                thicknessMm = tFromTarget;
            }
            else
            {
                thicknessMm = RoomTileSystem.Algorithms.TileDimensionDetector.DetectThickness(host, _doc, defaultMm);
            }

            if (host == null) return;

            CompoundStructure cs = null;
            if (host is Wall wall)
            {
                cs = wall.WallType.GetCompoundStructure();
            }
            else if (host is Floor floor)
            {
                cs = floor.FloorType.GetCompoundStructure();
            }

            if (cs != null && cs.LayerCount > 0)
            {
                int count = cs.LayerCount;

                // 1. 優先精準匹配與點選面材質相同的結構層，直接讀取其結構厚度
                if (targetMaterialId != ElementId.InvalidElementId)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (cs.GetMaterialId(i) == targetMaterialId)
                        {
                            tileMatId = targetMaterialId;
                            return;
                        }
                    }
                }

                // 2. 其次匹配材質名稱中包含關鍵字的層 (從頂層到底層，即 0 到 count-1 遍歷，優先磁磚系列關鍵字)
                for (int i = 0; i < count; i++)
                {
                    ElementId matId = cs.GetMaterialId(i);
                    if (matId != ElementId.InvalidElementId)
                    {
                        Material mat = _doc.GetElement(matId) as Material;
                        if (mat != null)
                        {
                            string name = mat.Name;
                            if (name.Contains("磁磚") || name.Contains("瓷磚") || 
                                name.Contains("Tile") || name.Contains("地磚") || 
                                name.Contains("壁磚"))
                            {
                                tileMatId = matId;
                                return;
                            }
                        }
                    }
                }

                // 3. 再其次匹配較寬泛的裝修關鍵字 (finish/地坪/裝修)
                for (int i = 0; i < count; i++)
                {
                    ElementId matId = cs.GetMaterialId(i);
                    if (matId != ElementId.InvalidElementId)
                    {
                        Material mat = _doc.GetElement(matId) as Material;
                        if (mat != null)
                        {
                            string name = mat.Name;
                            if (name.Contains("裝修") || name.Contains("地坪") || name.Contains("Finish") || name.Contains("PAT"))
                            {
                                tileMatId = matId;
                                return;
                            }
                        }
                    }
                }

                // 4. 再其次匹配標記為 Finish1 或 Finish2 並且有有效材質的層
                for (int i = 0; i < count; i++)
                {
                    MaterialFunctionAssignment func = cs.GetLayerFunction(i);
                    if (func == MaterialFunctionAssignment.Finish1 || func == MaterialFunctionAssignment.Finish2)
                    {
                        ElementId matId = cs.GetMaterialId(i);
                        if (matId != ElementId.InvalidElementId)
                        {
                            tileMatId = matId;
                            return;
                        }
                    }
                }

                // 5. 回退到第 0 層 (但排除混凝土等結構材料)
                int fallbackIdx = 0;
                if (count > fallbackIdx)
                {
                    ElementId matId = cs.GetMaterialId(fallbackIdx);
                    if (matId != ElementId.InvalidElementId)
                    {
                        Material mat = _doc.GetElement(matId) as Material;
                        if (mat != null)
                        {
                            string name = mat.Name;
                            bool isConcreteOrStructure = name.Contains("混凝土") || name.Contains("Concrete") || 
                                                         name.Contains("RC") || name.Contains("結構") || 
                                                         name.Contains("Structure") || name.Contains("泥") || 
                                                         name.Contains("Slab") || name.Contains("樓板");
                            if (!isConcreteOrStructure)
                            {
                                tileMatId = matId;
                                return;
                            }
                        }
                    }
                }
            }
        }

        // 複製原材質並產生帶有厚度尾綴的獨立材質球
        public ElementId GetOrCreateTileMaterialWithThickness(ElementId originalMaterialId, double thicknessMm)
        {
            if (originalMaterialId == ElementId.InvalidElementId)
            {
                return GetOrCreateTileMaterial("Tile_Default_Material", new Color(245, 245, 245));
            }

            Material origMat = _doc.GetElement(originalMaterialId) as Material;
            if (origMat == null)
            {
                return GetOrCreateTileMaterial("Tile_Default_Material", new Color(245, 245, 245));
            }

            // 格式化厚度，整數則不帶小數點，例如 8mm；小數則帶一位小數，例如 8.5mm
            string thickSuffix = (thicknessMm % 1 == 0) ? $"{thicknessMm:F0}mm" : $"{thicknessMm:F1}mm";
            string newMatName = $"{origMat.Name}_{thickSuffix}";

            // 檢查是否已有同名材質球，有則直接複用並同步屬性
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            collector.OfClass(typeof(Material));
            foreach (Material mat in collector)
            {
                if (mat.Name.Equals(newMatName, StringComparison.OrdinalIgnoreCase))
                {
                    if (origMat != null)
                    {
                        try
                        {
                            if (mat.Color.Red != origMat.Color.Red || mat.Color.Green != origMat.Color.Green || mat.Color.Blue != origMat.Color.Blue)
                            {
                                mat.Color = origMat.Color;
                            }
                            if (mat.Transparency != origMat.Transparency)
                            {
                                mat.Transparency = origMat.Transparency;
                            }
                        }
                        catch { }
                    }
                    return mat.Id;
                }
            }

            // 若找不到，則在 Transaction 中複製
            try
            {
                Material newMat = origMat.Duplicate(newMatName) as Material;
                if (newMat != null)
                {
                    return newMat.Id;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"複製材質 [{newMatName}] 失敗: {ex.Message}");
            }

            return originalMaterialId;
        }

        // 生成單片磁磚的 Element 實體 (優先使用原生 Wall 或 Floor，特殊情況 fallback 為 DirectShape)
        // 生成單片磁磚的 Element 實體 (100% 建立為原生 Wall 或 Floor，並修正對齊平面與端點合併)
        public Element GenerateTileElement(TileData tile, ElementId materialId, ElementId levelId)
        {
            XYZ normal = new XYZ(tile.Normal.X, tile.Normal.Y, tile.Normal.Z).Normalize();

            // 嘗試自動從宿主元件的類型構造中，讀取裝修層 (Finish Layer) 的真實厚度與材質
            double thicknessMm = tile.Thickness;
            ElementId tileMaterialId = materialId;
            
            // 物理法向判定品類：Z 軸朝上/朝下 (地坪) 歸類為樓板，其餘歸類為牆面 (解決族群歸類為牆的 Bug)
            BuiltInCategory category = Math.Abs(normal.Z) > 0.85 ? BuiltInCategory.OST_Floors : BuiltInCategory.OST_Walls;

            if (!string.IsNullOrEmpty(tile.Host_ID))
            {
                Element host = _doc.GetElement(tile.Host_ID);
                if (host != null)
                {
                    // 傳入 materialId 以供優先精準匹配該磁磚材質
                    GetHostFinishMaterialAndThickness(host, materialId, out ElementId detectedMatId, out double detectedThick, tile.Thickness);
                    if (detectedMatId != ElementId.InvalidElementId)
                    {
                        tileMaterialId = detectedMatId;
                    }
                    thicknessMm = detectedThick;
                }
            }

            // 排除傳入的基礎材質是混凝土等結構材料，防止複製出 混凝土_8mm 材質球 (解決材質為混凝土的 Bug)
            Material baseMat = _doc.GetElement(tileMaterialId) as Material;
            if (baseMat != null)
            {
                string baseMatName = baseMat.Name;
                bool isConcreteOrStructure = baseMatName.Contains("混凝土") || baseMatName.Contains("Concrete") || 
                                             baseMatName.Contains("RC") || baseMatName.Contains("結構") || 
                                             baseMatName.Contains("Structure") || baseMatName.Contains("泥") || 
                                             baseMatName.Contains("Slab") || baseMatName.Contains("樓板") ||
                                             baseMatName.Contains("地坪");
                
                if (isConcreteOrStructure && 
                    !baseMatName.Contains("磁磚") && !baseMatName.Contains("瓷磚") && 
                    !baseMatName.Contains("Tile") && !baseMatName.Contains("地磚"))
                {
                    tileMaterialId = GetOrCreateTileMaterial("Tile_Default_Material", new Color(245, 245, 245));
                }
            }

            // 複製原材質並產生帶有厚度尾綴的獨立材質球
            tileMaterialId = GetOrCreateTileMaterialWithThickness(tileMaterialId, thicknessMm);

            double tFeet = thicknessMm / 304.8; // mm 轉 feet

            int n = tile.XYZBoundary.Count;
            if (n < 3) return null;

            string matName = "DefaultTileMat";
            Material mat = _doc.GetElement(tileMaterialId) as Material;
            if (mat != null) matName = mat.Name;

            double tileZ = tile.XYZBoundary[0].Z;

            // 優先使用裝修面宿主原本設定的樓層 levelId
            // 只有當傳入的 levelId 為無效的 InvalidElementId 時，才進行最鄰近標高偵測防呆
            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                FilteredElementCollector lvlCollector = new FilteredElementCollector(_doc);
                lvlCollector.OfClass(typeof(Level));
                double minDiff = double.MaxValue;
                ElementId closestLevelId = levelId;
                foreach (Level lvl in lvlCollector)
                {
                    double diff = Math.Abs(tileZ - lvl.Elevation);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closestLevelId = lvl.Id;
                    }
                }
                levelId = closestLevelId;
            }

            if (category == BuiltInCategory.OST_Walls)
            {
                try
                {
                    ElementId wallTypeId = GetOrCreateTileWallType(_doc, tFeet, tileMaterialId, matName);
                    if (wallTypeId != ElementId.InvalidElementId)
                    {
                        Level lvl = _doc.GetElement(levelId) as Level;
                        double lvlElev = lvl != null ? lvl.Elevation : 0;

                        // 水平方向（沿牆面）= worldUp × normal
                        XYZ worldUp = XYZ.BasisZ;
                        XYZ basisH = worldUp.CrossProduct(normal).Normalize();
                        if (basisH.GetLength() < 0.01) basisH = XYZ.BasisX;

                        // 從 XYZBoundary 計算磁磚的水平範圍（U）和垂直範圍（Z），將所有座標（已為 feet）投影計算
                        XYZ refPt = new XYZ(tile.XYZBoundary[0].X, tile.XYZBoundary[0].Y, tile.XYZBoundary[0].Z);
                        double minH = double.MaxValue, maxH = double.MinValue;
                        double minZ = double.MaxValue, maxZ = double.MinValue;
                        foreach (var pt in tile.XYZBoundary)
                        {
                            XYZ p = new XYZ(pt.X, pt.Y, pt.Z);
                            double h = (p - refPt).DotProduct(basisH);
                            if (h < minH) minH = h;
                            if (h > maxH) maxH = h;
                            if (p.Z < minZ) minZ = p.Z;
                            if (p.Z > maxZ) maxZ = p.Z;
                        }

                        // 最小尺寸防呆
                        if (maxH - minH < 0.01) maxH = minH + 0.01;
                        if (maxZ - minZ < 0.005) maxZ = minZ + 0.005;

                        double tileWallHeight = maxZ - minZ;      // 磁磚高度（feet）
                        double baseOffset = minZ - lvlElev;        // 基底相對 Level 偏移（feet）

                        // 水平基底線：裝修面上的 XY + minZ（Revit Wall 的 Location Curve）
                        XYZ p0 = new XYZ((refPt + minH * basisH).X, (refPt + minH * basisH).Y, minZ);
                        XYZ p1 = new XYZ((refPt + maxH * basisH).X, (refPt + maxH * basisH).Y, minZ);

                        if (p0.DistanceTo(p1) >= 0.01)
                        {
                            Curve baseLine = Line.CreateBound(p0, p1);
                            // 用 height + offset overload：真正控制磁磚高度，不受 Profile 約束
                            Wall wall = Wall.Create(_doc, baseLine, wallTypeId, levelId,
                                tileWallHeight, baseOffset, false, false);

                            if (wall != null)
                            {
                                // 確保 Top Constraint = Unconnected（防止 Level 約束把牆拉回樓板到樓板）
                                Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                                if (topConstraint != null && !topConstraint.IsReadOnly)
                                    topConstraint.Set(ElementId.InvalidElementId);

                                // 設定定位線：讓牆的裝修面（外面）對齊 baseLine
                                // 只影響平面圖 XY 偏移，不影響 Z 高度
                                Parameter locParam = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                                if (locParam != null && !locParam.IsReadOnly)
                                {
                                    if (wall.Orientation.DotProduct(normal) >= 0)
                                        locParam.Set((int)WallLocationLine.FinishFaceExterior);
                                    else
                                        locParam.Set((int)WallLocationLine.FinishFaceInterior);
                                }

                                SafeDisallowWallJoin(wall);

                                // 裁切磁磚加上標記，供人工 Edit Profile 修正邊腳
                                if (tile.Tile_Type == "Cut" || tile.Tile_Type == "Border")
                                {
                                    Parameter commentParam = wall.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (commentParam != null && !commentParam.IsReadOnly)
                                        commentParam.Set($"[需人工修正邊腳] {tile.Tile_ID}|Type:{tile.Tile_Type}");
                                }

                                WriteMetaDataAndParams(wall, tile, matName, thicknessMm);
                                return wall;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Wall.Create 失敗，fallback DirectShape: {ex.Message}");
                }

                // Fallback: DirectShape（幾何正確，但不可編輯）
                DirectShape ds = GenerateTileDirectShape(tile, tileMaterialId, tFeet, normal, thicknessMm, matName, category);
                if (ds != null)
                {
                    WriteMetaDataAndParams(ds, tile, matName, thicknessMm);
                    return ds;
                }
            }

            else if (category == BuiltInCategory.OST_Floors)
            {
                try
                {
                    ElementId floorTypeId = GetOrCreateTileFloorType(_doc, tFeet, tileMaterialId, matName);
                    if (floorTypeId != ElementId.InvalidElementId)
                    {
                        Level lvl = _doc.GetElement(levelId) as Level;
                        double lvlElev = lvl != null ? lvl.Elevation : 0;

                        // 建立局部水平與垂直方向的軸 (BasisU, BasisV) 用於計算矩形包圍盒，對齊排版面
                        XYZ basisU = XYZ.BasisX;
                        XYZ basisV = XYZ.BasisY;
                        if (Math.Abs(normal.Z) > 0.5)
                        {
                            XYZ worldForward = XYZ.BasisY;
                            basisU = worldForward.CrossProduct(normal).Normalize();
                            if (basisU.GetLength() < 0.01) basisU = XYZ.BasisX;
                            basisV = normal.CrossProduct(basisU).Normalize();
                        }

                        // 計算頂點在局部軸上的投影範圍，建構矩形 Profile，避免微小不共面或裁剪自相交導致建立失敗
                        XYZ refPt = new XYZ(tile.XYZBoundary[0].X, tile.XYZBoundary[0].Y, tile.XYZBoundary[0].Z);
                        double minU = double.MaxValue, maxU = double.MinValue;
                        double minV = double.MaxValue, maxV = double.MinValue;
                        foreach (var pt in tile.XYZBoundary)
                        {
                            XYZ p = new XYZ(pt.X, pt.Y, pt.Z);
                            double u = (p - refPt).DotProduct(basisU);
                            double v = (p - refPt).DotProduct(basisV);
                            if (u < minU) minU = u;
                            if (u > maxU) maxU = u;
                            if (v < minV) minV = v;
                            if (v > maxV) maxV = v;
                        }

                        // 防呆最小尺寸限制 (Revit 最小線段)
                        if (maxU - minU < 0.01) maxU = minU + 0.01;
                        if (maxV - minV < 0.01) maxV = minV + 0.01;

                        // 計算四個角點在標高高程 (lvlElev) 的位置，建立基準輪廓，隨後透過偏移量調整高度，此為 Revit API 最穩定且不報錯之標準作法
                        XYZ p0 = refPt + minU * basisU + minV * basisV;
                        XYZ p1 = refPt + maxU * basisU + minV * basisV;
                        XYZ p2 = refPt + maxU * basisU + maxV * basisV;
                        XYZ p3 = refPt + minU * basisU + maxV * basisV;

                        XYZ pt0 = new XYZ(p0.X, p0.Y, lvlElev);
                        XYZ pt1 = new XYZ(p1.X, p1.Y, lvlElev);
                        XYZ pt2 = new XYZ(p2.X, p2.Y, lvlElev);
                        XYZ pt3 = new XYZ(p3.X, p3.Y, lvlElev);

                        if (pt0.DistanceTo(pt1) >= 0.01 && pt1.DistanceTo(pt2) >= 0.01)
                        {
                            CurveLoop loop = new CurveLoop();
                            loop.Append(Line.CreateBound(pt0, pt1));
                            loop.Append(Line.CreateBound(pt1, pt2));
                            loop.Append(Line.CreateBound(pt2, pt3));
                            loop.Append(Line.CreateBound(pt3, pt0));

                            List<CurveLoop> loops = new List<CurveLoop> { loop };
                            Floor floor = Floor.Create(_doc, loops, floorTypeId, levelId);
                            if (floor != null)
                            {
                                // 優先直接繼承宿主地坪的高度偏移量，完美繞過專案基準點/測量點/群組局部座標等所有高程天坑
                                double heightOffset = tileZ - lvlElev;
                                if (!string.IsNullOrEmpty(tile.Host_ID))
                                {
                                    try
                                    {
                                        Element host = _doc.GetElement(tile.Host_ID);
                                        if (host is Floor hostFloor)
                                        {
                                            Parameter pOffset = hostFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                            if (pOffset == null || !pOffset.HasValue)
                                            {
                                                pOffset = hostFloor.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                                            }
                                            if (pOffset == null || !pOffset.HasValue)
                                            {
                                                pOffset = hostFloor.LookupParameter("自標高高度偏差");
                                            }
                                            if (pOffset == null || !pOffset.HasValue)
                                            {
                                                pOffset = hostFloor.LookupParameter("Height Offset From Level");
                                            }
                                            
                                            if (pOffset != null && pOffset.HasValue)
                                            {
                                                heightOffset = pOffset.AsDouble();
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                
                                bool offsetSet = false;
                                Parameter offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                                }
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.LookupParameter("自標高高度偏差");
                                }
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.LookupParameter("Height Offset From Level");
                                }
                                
                                if (offsetParam != null && !offsetParam.IsReadOnly)
                                {
                                    offsetSet = offsetParam.Set(heightOffset);
                                }

                                if (!offsetSet)
                                {
                                    // Fallback: 當參數唯讀或不存在時，透過移動元素調整高度
                                    double deltaZ = heightOffset;
                                    if (Math.Abs(deltaZ) > 0.0001)
                                    {
                                        try
                                        {
                                            ElementTransformUtils.MoveElement(_doc, floor.Id, new XYZ(0, 0, deltaZ));
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"透過 MoveElement 移動 Floor 失敗: {ex.Message}");
                                        }
                                    }
                                }

                                // 裁切磁磚加上標記，供人工編輯輪廓修正邊腳
                                if (tile.Tile_Type == "Cut" || tile.Tile_Type == "Border")
                                {
                                    Parameter commentParam = floor.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                    if (commentParam != null && !commentParam.IsReadOnly)
                                        commentParam.Set($"[需人工修正邊腳] {tile.Tile_ID}|Type:{tile.Tile_Type}");
                                }

                                WriteMetaDataAndParams(floor, tile, matName, thicknessMm);
                                return floor;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"建立原生 Floor 失敗: {ex.Message}");
                }

                // Fallback: DirectShape（幾何正確，但不可編輯）
                DirectShape ds = GenerateTileDirectShape(tile, tileMaterialId, tFeet, normal, thicknessMm, matName, category);
                if (ds != null)
                {
                    WriteMetaDataAndParams(ds, tile, matName, thicknessMm);
                    return ds;
                }
            }

            return null;
        }

        // 裁切磁磚用：取 XYZBoundary 在牆面局部座標的包圍盒，建矩形 Profile
        // 確保非矩形磁磚也能用 Wall.Create 成功，使用者可再 Edit Profile 手動修正形狀
        private List<Curve> CreateRectProfileCurves(List<XYZPoint> boundary, XYZ offset, XYZ wallNormal)
        {
            if (boundary == null || boundary.Count < 3) return null;

            // 牆面的水平軸 = worldUp × wallNormal，垂直軸 = worldUp
            XYZ worldUp = XYZ.BasisZ;
            XYZ basisH = worldUp.CrossProduct(wallNormal).Normalize(); // 水平方向
            if (basisH.GetLength() < 0.01) basisH = XYZ.BasisX;
            XYZ basisV = worldUp; // 垂直方向（Z）

            // 把 boundary 投影到牆面局部座標，取包圍盒
            double minH = double.MaxValue, maxH = double.MinValue;
            double minV = double.MaxValue, maxV = double.MinValue;
            XYZ origin = new XYZ(boundary[0].X, boundary[0].Y, boundary[0].Z);
            foreach (var pt in boundary)
            {
                XYZ p = new XYZ(pt.X, pt.Y, pt.Z);
                XYZ rel = p - origin;
                double h = rel.DotProduct(basisH);
                double v = rel.DotProduct(basisV);
                if (h < minH) minH = h; if (h > maxH) maxH = h;
                if (v < minV) minV = v; if (v > maxV) maxV = v;
            }

            // 確保最小尺寸，避免 Revit 因浮點精度拒絕建立
            if (maxH - minH < 0.005) maxH = minH + 0.005;
            if (maxV - minV < 0.005) maxV = minV + 0.005;

            // 建立矩形的 4 個角點（套用 offset）
            XYZ bl = origin + minH * basisH + minV * basisV + offset; // 左下
            XYZ br = origin + maxH * basisH + minV * basisV + offset; // 右下
            XYZ tr = origin + maxH * basisH + maxV * basisV + offset; // 右上
            XYZ tl = origin + minH * basisH + maxV * basisV + offset; // 左上

            return new List<Curve>
            {
                Line.CreateBound(bl, br),
                Line.CreateBound(br, tr),
                Line.CreateBound(tr, tl),
                Line.CreateBound(tl, bl)
            };
        }

        private List<Curve> CreateProfileCurves(List<XYZPoint> boundary, XYZ offset)

        {
            List<XYZ> pts = new List<XYZ>();
            foreach (var pt in boundary)
            {
                pts.Add(new XYZ(pt.X, pt.Y, pt.Z) + offset);
            }

            // 過濾距離過近的點 (防呆，避免微小線段小於 1/128 呎)
            List<XYZ> cleanPts = new List<XYZ>();
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ p = pts[i];
                if (cleanPts.Count == 0)
                {
                    cleanPts.Add(p);
                }
                else
                {
                    double dist = p.DistanceTo(cleanPts[cleanPts.Count - 1]);
                    if (dist > 0.007) // 約 2.1 mm
                    {
                        cleanPts.Add(p);
                    }
                }
            }

            // 檢查首尾連線
            if (cleanPts.Count > 2)
            {
                double dist = cleanPts[cleanPts.Count - 1].DistanceTo(cleanPts[0]);
                if (dist <= 0.007)
                {
                    cleanPts.RemoveAt(cleanPts.Count - 1);
                }
            }

            if (cleanPts.Count < 3) return null;

            List<Curve> curves = new List<Curve>();
            int n = cleanPts.Count;
            for (int i = 0; i < n; i++)
            {
                curves.Add(Line.CreateBound(cleanPts[i], cleanPts[(i + 1) % n]));
            }
            return curves;
        }

        private void SafeDisallowWallJoin(Wall wall)
        {
            try
            {
                if (wall.Location is LocationCurve locCurve)
                {
                    WallUtils.DisallowWallJoinAtEnd(wall, 0);
                    WallUtils.DisallowWallJoinAtEnd(wall, 1);
                }
            }
            catch { }
        }

        // 為了相容於其他背景元件 (如 TileRvtExporter、TileUpdater) 的 DirectShape 呼叫
        public DirectShape GenerateTileSolid(TileData tile, ElementId materialId, BuiltInCategory category = BuiltInCategory.OST_Walls)
        {
            XYZ normal = new XYZ(tile.Normal.X, tile.Normal.Y, tile.Normal.Z).Normalize();
            double thicknessMm = tile.Thickness;
            double tFeet = thicknessMm / 304.8;
            string matName = "DefaultTileMat";
            Material mat = _doc.GetElement(materialId) as Material;
            if (mat != null) matName = mat.Name;
            
            return GenerateTileDirectShape(tile, materialId, tFeet, normal, thicknessMm, matName, category);
        }

        public ElementId GetOrCreateTileWallType(Document doc, double thicknessFeet, ElementId materialId, string matName)
        {
            string safeMaterialToken = SanitizeTypeToken(matName);
            string typeName = $"TileWall_{safeMaterialToken}_{(thicknessFeet * 304.8):F1}mm";
            foreach (WallType wt in new FilteredElementCollector(doc).OfClass(typeof(WallType)))
            {
                if (wt.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        CompoundStructure cs = wt.GetCompoundStructure();
                        if (cs != null && cs.LayerCount > 0)
                        {
                            bool needUpdate = false;
                            if (Math.Abs(cs.GetLayerWidth(0) - thicknessFeet) > 0.0001)
                            {
                                cs.SetLayerWidth(0, thicknessFeet);
                                needUpdate = true;
                            }
                            if (cs.GetMaterialId(0) != materialId)
                            {
                                cs.SetMaterialId(0, materialId);
                                needUpdate = true;
                            }
                            if (needUpdate)
                            {
                                wt.SetCompoundStructure(cs);
                            }
                        }
                    }
                    catch { }
                    return wt.Id;
                }
            }

            WallType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .FirstElement() as WallType;
            if (baseType == null) return ElementId.InvalidElementId;

            try
            {
                WallType newType = baseType.Duplicate(typeName) as WallType;
                if (newType != null)
                {
                    CompoundStructureLayer layer = new CompoundStructureLayer(thicknessFeet, MaterialFunctionAssignment.Finish1, materialId);
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
                    newType.SetCompoundStructure(cs);
                    return newType.Id;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"無法複製 WallType: {ex.Message}");
            }
            return baseType.Id;
        }

        public ElementId GetOrCreateTileFloorType(Document doc, double thicknessFeet, ElementId materialId, string matName)
        {
            string safeMaterialToken = SanitizeTypeToken(matName);
            string typeName = $"TileFloor_{safeMaterialToken}_{(thicknessFeet * 304.8):F1}mm";
            foreach (FloorType ft in new FilteredElementCollector(doc).OfClass(typeof(FloorType)))
            {
                if (ft.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        CompoundStructure cs = ft.GetCompoundStructure();
                        if (cs != null && cs.LayerCount > 0)
                        {
                            bool needUpdate = false;
                            if (Math.Abs(cs.GetLayerWidth(0) - thicknessFeet) > 0.0001)
                            {
                                cs.SetLayerWidth(0, thicknessFeet);
                                needUpdate = true;
                            }
                            if (cs.GetMaterialId(0) != materialId)
                            {
                                cs.SetMaterialId(0, materialId);
                                needUpdate = true;
                            }
                            if (needUpdate)
                            {
                                ft.SetCompoundStructure(cs);
                            }
                        }
                    }
                    catch { }
                    return ft.Id;
                }
            }

            FloorType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .FirstElement() as FloorType;
            if (baseType == null) return ElementId.InvalidElementId;

            try
            {
                FloorType newType = baseType.Duplicate(typeName) as FloorType;
                if (newType != null)
                {
                    CompoundStructureLayer layer = new CompoundStructureLayer(thicknessFeet, MaterialFunctionAssignment.Finish1, materialId);
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
                    newType.SetCompoundStructure(cs);
                    return newType.Id;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"無法複製 FloorType: {ex.Message}");
            }
            return baseType.Id;
        }

        private string SanitizeTypeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Material";
            }

            string sanitized = Regex.Replace(value, @"[^\p{L}\p{Nd}_\-.]+", "_");
            sanitized = Regex.Replace(sanitized, @"_+", "_").Trim('_');

            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "Material";
            }

            if (sanitized.Length > 64)
            {
                sanitized = sanitized.Substring(0, 64).Trim('_');
            }

            return sanitized;
        }

        private DirectShape GenerateTileDirectShape(TileData tile, ElementId tileMaterialId, double tFeet, XYZ normal, double thicknessMm, string matName, BuiltInCategory category)
        {
            int n = tile.XYZBoundary.Count;
            bool isFloorTile = category == BuiltInCategory.OST_Floors && Math.Abs(normal.Z) > 0.9;
            if (n < 3) return null;

            TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
            builder.OpenConnectedFaceSet(true);

            // 1. 建立 CurveLoop 邊界
            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < n; i++)
            {
                XYZ p0 = new XYZ(tile.XYZBoundary[i].X, tile.XYZBoundary[i].Y, tile.XYZBoundary[i].Z);
                XYZ p1 = new XYZ(tile.XYZBoundary[(i + 1) % n].X, tile.XYZBoundary[(i + 1) % n].Y, tile.XYZBoundary[(i + 1) % n].Z);
                loop.Append(Line.CreateBound(p0, p1));
            }

            // 2. 利用 Revit 內建拉伸引擎建立 Solid，以獲得高精度且能相容凹凸多邊形的三角化面網
            Solid solid = null;
            try
            {
                // 朝法向的反向拉伸（向宿主內部/地坪下方生長）
                XYZ primaryExtrusionDirection = -normal;
                solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, primaryExtrusionDirection, tFeet);
            }
            catch
            {
                try
                {
                    // 若失敗，朝法向正向拉伸
                    XYZ fallbackExtrusionDirection = normal;
                    solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, fallbackExtrusionDirection, tFeet);
                }
                catch
                {
                    // 兩者皆失敗則在下方 fallback 回原手動三角化
                }
            }

            if (solid != null)
            {
                foreach (Face face in solid.Faces)
                {
                    Mesh mesh = face.Triangulate();
                    if (mesh != null)
                    {
                        for (int i = 0; i < mesh.NumTriangles; i++)
                        {
                            MeshTriangle tri = mesh.get_Triangle(i);
                            var triPts = new List<XYZ>
                            {
                                tri.get_Vertex(0),
                                tri.get_Vertex(1),
                                tri.get_Vertex(2)
                            };
                            builder.AddFace(new TessellatedFace(triPts, tileMaterialId));
                        }
                    }
                }
            }
            else
            {
                // Fallback：手動三角化 (僅適用於簡單凸多邊形，凹多邊形或裁剪磚可能產生幾何扭曲)
                List<XYZ> bottomPts = new List<XYZ>();
                List<XYZ> topPts = new List<XYZ>();

                foreach (var pt in tile.XYZBoundary)
                {
                    XYZ surfacePt = new XYZ(pt.X, pt.Y, pt.Z);
                    // 不論地坪或牆面，頂面點皆在完成面上，底面點向宿主結構內部退去厚度 tFeet
                    bottomPts.Add(surfacePt - normal * tFeet);
                    topPts.Add(surfacePt);
                }

                for (int i = 1; i < n - 1; i++)
                {
                    var triPts = new List<XYZ> { topPts[0], topPts[i], topPts[i + 1] };
                    builder.AddFace(new TessellatedFace(triPts, tileMaterialId));
                }

                List<XYZ> revBottomPts = new List<XYZ>(bottomPts);
                revBottomPts.Reverse();
                for (int i = 1; i < n - 1; i++)
                {
                    var triPts = new List<XYZ> { revBottomPts[0], revBottomPts[i], revBottomPts[i + 1] };
                    builder.AddFace(new TessellatedFace(triPts, tileMaterialId));
                }

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    var tri1 = new List<XYZ> { bottomPts[i], bottomPts[next], topPts[next] };
                    var tri2 = new List<XYZ> { bottomPts[i], topPts[next], topPts[i] };
                    builder.AddFace(new TessellatedFace(tri1, tileMaterialId));
                    builder.AddFace(new TessellatedFace(tri2, tileMaterialId));
                }
            }

            builder.CloseConnectedFaceSet();
            builder.Build();
            TessellatedShapeBuilderResult result = builder.GetBuildResult();
            var geomObjects = result.GetGeometricalObjects();
            if (geomObjects == null || geomObjects.Count == 0) return null;

            // 3. 確保 Category 在當前 Revit 中是 DirectShape 支援的合法品類，否則退回到 OST_GenericModel (一般模型)
            ElementId catId = new ElementId(category);
            if (!DirectShape.IsValidCategoryId(catId, _doc))
            {
                catId = new ElementId(BuiltInCategory.OST_GenericModel);
            }

            DirectShape ds = DirectShape.CreateElement(_doc, catId);
            ds.SetShape(geomObjects);
            ds.Name = $"Tile_{tile.Tile_Type}_{tile.Tile_ID.Substring(tile.Tile_ID.LastIndexOf('_') + 1)}";

            WriteMetaDataAndParams(ds, tile, matName, thicknessMm);
            return ds;
        }

        private void WriteMetaDataAndParams(Element elem, TileData tile, string matName, double thicknessMm)
        {
            Parameter commentParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (commentParam != null)
            {
                List<string> ptStrs = new List<string>();
                if (tile.XYZBoundary != null)
                {
                    foreach (var pt in tile.XYZBoundary)
                    {
                        ptStrs.Add($"{pt.X:F6},{pt.Y:F6},{pt.Z:F6}");
                    }
                }
                string ptsData = string.Join(";", ptStrs);

                string metaData = $"Anchor_ID:{tile.Anchor_ID}|Room_ID:{tile.Room_ID}|Surface_ID:{tile.Surface_ID}|Tile_ID:{tile.Tile_ID}|Type:{tile.Tile_Type}|Host_ID:{tile.Host_ID}|Area:{tile.Area:F6}|BoundaryPoints:{ptsData}";
                commentParam.Set(metaData);
            }

            SetSharedParameter(elem, "Anchor_ID", tile.Anchor_ID);
            SetSharedParameter(elem, "Room_ID", tile.Room_ID);
            SetSharedParameter(elem, "Tile_ID", tile.Tile_ID);
            SetSharedParameter(elem, "Tile_Type", tile.Tile_Type);
            SetSharedParameter(elem, "Tile_Width", tile.Width / 304.8);
            SetSharedParameter(elem, "Tile_Height", tile.Height / 304.8);
            SetSharedParameter(elem, "Tile_Thickness", thicknessMm / 304.8);
            SetSharedParameter(elem, "Tile_Material", matName);
        }

        private void SetSharedParameter(Element elem, string paramName, object value)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) return;

            if (value is string strVal)
            {
                param.Set(strVal);
            }
            else if (value is double dbVal)
            {
                param.Set(dbVal);
            }
            else if (value is int intVal)
            {
                param.Set(intVal);
            }
        }

        private bool ParseThicknessFromName(string name, out double thicknessMm)
        {
            thicknessMm = 0;
            if (string.IsNullOrEmpty(name)) return false;

            MatchCollection matches = Regex.Matches(
                name,
                @"(?<![xX*×])(\d+(?:\.\d+)?)\s*(mm|cm)\b",
                RegexOptions.IgnoreCase
            );
            if (matches.Count > 0)
            {
                try
                {
                    Match match = matches[matches.Count - 1];
                    double val = double.Parse(match.Groups[1].Value);
                    string unit = match.Groups[2].Value.ToLower();
                    if (unit == "cm")
                    {
                        thicknessMm = val * 10.0;
                    }
                    else
                    {
                        thicknessMm = val;
                    }
                    return true;
                }
                catch { }
            }
            return false;
        }

        private double GetWallFinishThickness(Wall wall, ElementId targetMatId, double defaultMm)
        {
            try
            {
                // 1. 優先從 WallType 名稱解析厚度 (例如 _8mm 或 _0.8cm)
                if (ParseThicknessFromName(wall.WallType.Name, out double tFromName))
                {
                    return tFromName;
                }

                WallType type = wall.WallType;
                CompoundStructure structure = type.GetCompoundStructure();
                if (structure != null)
                {
                    int count = structure.LayerCount;

                    // 2. 優先匹配與目標材質相同的層，並讀取其結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        ElementId matId = structure.GetMaterialId(i);
                        if (matId == targetMatId && matId != ElementId.InvalidElementId)
                        {
                            double wFeet = structure.GetLayerWidth(i);
                            if (wFeet > 0.001) return wFeet * 304.8;
                        }
                    }

                    // 3. 其次匹配材質名稱包含關鍵字的層，並讀取其結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        ElementId matId = structure.GetMaterialId(i);
                        if (matId != ElementId.InvalidElementId)
                        {
                            Material mat = _doc.GetElement(matId) as Material;
                            if (mat != null)
                            {
                                string name = mat.Name;
                                if (name.Contains("磁磚") || name.Contains("瓷磚") || 
                                    name.Contains("Tile") || name.Contains("PAT") || 
                                    name.Contains("MAT") || name.Contains("裝修面") || name.Contains("地坪"))
                                {
                                    double wFeet = structure.GetLayerWidth(i);
                                    if (wFeet > 0.001) return wFeet * 304.8;
                                }
                            }
                        }
                    }

                    // 4. 再其次匹配 Finish 層的結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        MaterialFunctionAssignment func = structure.GetLayerFunction(i);
                        if (func == MaterialFunctionAssignment.Finish1 || func == MaterialFunctionAssignment.Finish2)
                        {
                            double wFeet = structure.GetLayerWidth(i);
                            if (wFeet > 0.001) return wFeet * 304.8;
                        }
                    }

                    // 5. 若以上皆未匹配，且只有單層，返回該層厚度
                    if (count == 1)
                    {
                        double wFeet = structure.GetLayerWidth(0);
                        if (wFeet > 0.001) return wFeet * 304.8;
                    }

                    // 6. 萬一沒配到，但第一層厚度大於 0.001，作為後備 fallback
                    double firstLayerWidthFeet = structure.GetLayerWidth(0);
                    if (firstLayerWidthFeet > 0.001)
                    {
                        return firstLayerWidthFeet * 304.8;
                    }
                }
                {
                    return tFromName;
                }
            }
            catch { }
            return defaultMm;
        }

        private double GetFloorFinishThickness(Floor floor, ElementId targetMatId, double defaultMm)
        {
            try
            {
                // 1. 優先從 FloorType 名稱解析厚度 (例如 _8mm 或 _0.8cm)
                if (ParseThicknessFromName(floor.FloorType.Name, out double tFromName))
                {
                    return tFromName;
                }

                FloorType type = floor.FloorType;
                CompoundStructure structure = type.GetCompoundStructure();
                if (structure != null)
                {
                    int count = structure.LayerCount;

                    // 2. 優先匹配與目標材質相同的層，並讀取其結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        ElementId matId = structure.GetMaterialId(i);
                        if (matId == targetMatId && matId != ElementId.InvalidElementId)
                        {
                            double wFeet = structure.GetLayerWidth(i);
                            if (wFeet > 0.001) return wFeet * 304.8;
                        }
                    }

                    // 3. 其次匹配材質名稱包含關鍵字 (從頂部向下找) 并讀取其結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        ElementId matId = structure.GetMaterialId(i);
                        if (matId != ElementId.InvalidElementId)
                        {
                            Material mat = _doc.GetElement(matId) as Material;
                            if (mat != null)
                            {
                                string name = mat.Name;
                                if (name.Contains("磁磚") || name.Contains("瓷磚") || 
                                    name.Contains("Tile") || name.Contains("PAT") || 
                                    name.Contains("MAT") || name.Contains("裝修面") || name.Contains("地坪"))
                                {
                                    double wFeet = structure.GetLayerWidth(i);
                                    if (wFeet > 0.001) return wFeet * 304.8;
                                }
                            }
                        }
                    }

                    // 4. 再其次匹配 Finish 層的結構厚度
                    for (int i = 0; i < count; i++)
                    {
                        MaterialFunctionAssignment func = structure.GetLayerFunction(i);
                        if (func == MaterialFunctionAssignment.Finish1 || func == MaterialFunctionAssignment.Finish2)
                        {
                            double wFeet = structure.GetLayerWidth(i);
                            if (wFeet > 0.001) return wFeet * 304.8;
                        }
                    }

                    // 5. 若以上皆未匹配，且只有單層，返回該層厚度
                    if (count == 1)
                    {
                        double wFeet = structure.GetLayerWidth(0);
                        if (wFeet > 0.001) return wFeet * 304.8;
                    }

                    // 6. 萬一沒配到，但第一層厚度大於 0.001，作為後備 fallback
                    double firstLayerWidthFeet = structure.GetLayerWidth(0);
                    if (firstLayerWidthFeet > 0.001)
                    {
                        return firstLayerWidthFeet * 304.8;
                    }
                }
            }
            catch { }
            return defaultMm;
        }

        // 將選取的 DirectShape/瓷磚 轉換為原生可編輯的 Wall 或 Floor，並刪除原瓷磚
        public Element ConvertTileToEditableNative(
            Element tileElem, 
            List<XYZ> pts, 
            XYZ normal, 
            double thicknessMm, 
            ElementId materialId, 
            string matName, 
            string tileType, 
            string tileId, 
            string anchorId, 
            string roomId, 
            string surfaceId, 
            string hostId, 
            double area)
        {
            double tFeet = thicknessMm / 304.8;
            ElementId levelId = tileElem.LevelId;
            double tileZForNative = pts[0].Z;
            if (levelId == ElementId.InvalidElementId)
            {
                FilteredElementCollector lvlCollector = new FilteredElementCollector(_doc);
                lvlCollector.OfClass(typeof(Level));
                double minDiff = double.MaxValue;
                foreach (Level l in lvlCollector)
                {
                    double diff = Math.Abs(tileZForNative - l.Elevation);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        levelId = l.Id;
                    }
                }
            }
            Level lvl = _doc.GetElement(levelId) as Level;
            double lvlElev = lvl != null ? lvl.Elevation : 0;

            // 判斷是地板 (OST_Floors) 還是牆面 (OST_Walls)
            bool isFloor = Math.Abs(normal.Z) > 0.9;

            if (isFloor)
            {
                try
                {
                    ElementId floorTypeId = GetOrCreateTileFloorType(_doc, tFeet, materialId, matName);
                    if (floorTypeId != ElementId.InvalidElementId)
                    {
                        Floor floor = null;
                        using (Transaction t = new Transaction(_doc, "建立原地坪磁磚"))
                        {
                            t.Start();

                             // 建立局部水平與垂直方向的軸 (BasisU, BasisV) 用於計算矩形包圍盒，對齊排版面
                             XYZ basisU = XYZ.BasisX;
                             XYZ basisV = XYZ.BasisY;
                             if (Math.Abs(normal.Z) > 0.5)
                             {
                                 XYZ worldForward = XYZ.BasisY;
                                 basisU = worldForward.CrossProduct(normal).Normalize();
                                 if (basisU.GetLength() < 0.01) basisU = XYZ.BasisX;
                                 basisV = normal.CrossProduct(basisU).Normalize();
                             }

                             // 計算頂點在局部軸上的投影範圍，建構矩形 Profile，避免微小不共面或裁剪自相交導致建立失敗
                             XYZ refPt = pts[0];
                             double minU = double.MaxValue, maxU = double.MinValue;
                             double minV = double.MaxValue, maxV = double.MinValue;
                             foreach (XYZ pt in pts)
                             {
                                 double u = (pt - refPt).DotProduct(basisU);
                                 double v = (pt - refPt).DotProduct(basisV);
                                 if (u < minU) minU = u;
                                 if (u > maxU) maxU = u;
                                 if (v < minV) minV = v;
                                 if (v > maxV) maxV = v;
                             }

                             // 防呆最小尺寸限制 (Revit 最小線段)
                             if (maxU - minU < 0.01) maxU = minU + 0.01;
                             if (maxV - minV < 0.01) maxV = minV + 0.01;

                             // 計算四個角點在水平高程 (lvlElev) 的位置
                             XYZ p0 = refPt + minU * basisU + minV * basisV;
                             XYZ p1 = refPt + maxU * basisU + minV * basisV;
                             XYZ p2 = refPt + maxU * basisU + maxV * basisV;
                             XYZ p3 = refPt + minU * basisU + maxV * basisV;

                             XYZ pt0 = new XYZ(p0.X, p0.Y, lvlElev);
                             XYZ pt1 = new XYZ(p1.X, p1.Y, lvlElev);
                             XYZ pt2 = new XYZ(p2.X, p2.Y, lvlElev);
                             XYZ pt3 = new XYZ(p3.X, p3.Y, lvlElev);

                             CurveLoop loop = new CurveLoop();
                             loop.Append(Line.CreateBound(pt0, pt1));
                             loop.Append(Line.CreateBound(pt1, pt2));
                             loop.Append(Line.CreateBound(pt2, pt3));
                             loop.Append(Line.CreateBound(pt3, pt0));

                             List<CurveLoop> loops = new List<CurveLoop> { loop };
                             floor = Floor.Create(_doc, loops, floorTypeId, levelId);
                            if (floor != null)
                            {
                                // 設定高度偏移：讓磁磚頂面貼齊地坪完成面，座標已為 feet 單位，朝下生長厚度
                                double tileZ = pts[0].Z;
                                double heightOffset = tileZ - lvlElev;
                                
                                bool offsetSet = false;
                                Parameter offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                                }
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.LookupParameter("自標高高度偏差");
                                }
                                if (offsetParam == null || offsetParam.IsReadOnly)
                                {
                                    offsetParam = floor.LookupParameter("Height Offset From Level");
                                }
                                
                                if (offsetParam != null && !offsetParam.IsReadOnly)
                                {
                                    offsetSet = offsetParam.Set(heightOffset);
                                }

                                if (!offsetSet)
                                {
                                    // Fallback: 透過移動元素調整高度
                                    double deltaZ = heightOffset;
                                    if (Math.Abs(deltaZ) > 0.0001)
                                    {
                                        try
                                        {
                                            ElementTransformUtils.MoveElement(_doc, floor.Id, new XYZ(0, 0, deltaZ));
                                        }
                                        catch (Exception ex)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"透過 MoveElement 移動 Floor 失敗: {ex.Message}");
                                        }
                                    }
                                }

                                WriteMetaDataAndParams(floor, new TileData {
                                    Tile_ID = tileId, Anchor_ID = anchorId, Room_ID = roomId, Surface_ID = surfaceId, Tile_Type = tileType, Host_ID = hostId, Area = area, XYZBoundary = pts.Select(p => new XYZPoint(p.X, p.Y, p.Z)).ToList()
                                }, matName, thicknessMm);
                            }

                            t.Commit();
                        }
                        return floor;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"轉換為 Floor 失敗: {ex.Message}");
                    throw;
                }
            }
            else
            {
                try
                {
                    ElementId wallTypeId = GetOrCreateTileWallType(_doc, tFeet, materialId, matName);
                    if (wallTypeId != ElementId.InvalidElementId)
                    {
                        Wall wall = null;

                        // Transaction 1: 建立牆體本體與基本高度設定
                        using (Transaction t1 = new Transaction(_doc, "建立牆面磁磚載體"))
                        {
                            t1.Start();

                            // 1. 建立一堵直牆作為載體
                            double minZ = pts.Min(p => p.Z);
                            double maxZ = pts.Max(p => p.Z);
                            double wallHeight = maxZ - minZ;
                            if (wallHeight < 0.01) wallHeight = 0.5;

                            // 取 X、Y 座標在平面上距離最遠的兩個點作為基底線投影
                            XYZ ptStart = pts[0];
                            XYZ ptEnd = pts[1];
                            double maxDist = 0;
                            for (int i = 0; i < pts.Count; i++)
                            {
                                for (int j = i + 1; j < pts.Count; j++)
                                {
                                    double d = new XYZ(pts[i].X, pts[i].Y, 0).DistanceTo(new XYZ(pts[j].X, pts[j].Y, 0));
                                    if (d > maxDist)
                                    {
                                        maxDist = d;
                                        ptStart = pts[i];
                                        ptEnd = pts[j];
                                    }
                                }
                            }
                            if (maxDist < 0.05) ptEnd = ptStart + XYZ.BasisX * 0.5;

                            XYZ p0 = new XYZ(ptStart.X, ptStart.Y, lvlElev);
                            XYZ p1 = new XYZ(ptEnd.X, ptEnd.Y, lvlElev);
                            Curve baseLine = Line.CreateBound(p0, p1);

                            double baseOffset = minZ - lvlElev;
                            wall = Wall.Create(_doc, baseLine, wallTypeId, levelId, wallHeight, baseOffset, false, false);
                            if (wall != null)
                            {
                                // 設為未連接高度
                                Parameter topConstraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                                if (topConstraint != null && !topConstraint.IsReadOnly)
                                    topConstraint.Set(ElementId.InvalidElementId);

                                SafeDisallowWallJoin(wall);

                                // 設定定位線：讓牆的面（外側/內側）對齊 baseLine，使其朝內側（宿主牆方向）生長厚度
                                Parameter locParam = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
                                if (locParam != null && !locParam.IsReadOnly)
                                {
                                    XYZ wallDir = (p1 - p0).Normalize();
                                    XYZ wallNormal = new XYZ(wallDir.Y, -wallDir.X, 0);
                                    if (wallNormal.DotProduct(normal) >= 0)
                                        locParam.Set((int)WallLocationLine.FinishFaceExterior);
                                    else
                                        locParam.Set((int)WallLocationLine.FinishFaceInterior);
                                }
                            }

                            t1.Commit();
                        }

                        if (wall != null)
                        {
                            // 2. 利用 SketchEditScope 程式化設定牆的 Profile 輪廓
                            if (wall.CanHaveProfileSketch())
                            {
                                // 建立 Profile Sketch 需要在單獨一個 Transaction 中
                                using (Transaction t2 = new Transaction(_doc, "初始化牆草圖"))
                                {
                                    t2.Start();
                                    if (wall.SketchId == ElementId.InvalidElementId)
                                    {
                                        wall.CreateProfileSketch();
                                    }
                                    t2.Commit();
                                }

                                Sketch sketch = _doc.GetElement(wall.SketchId) as Sketch;
                                if (sketch != null)
                                {
                                    using (SketchEditScope scope = new SketchEditScope(_doc, "重寫牆 Profile 草圖"))
                                    {
                                        scope.Start(sketch.Id);
                                        using (Transaction t3 = new Transaction(_doc, "修改輪廓線條"))
                                        {
                                            t3.Start();

                                            // 刪除原有的所有線段
                                            System.Collections.Generic.ICollection<ElementId> sketchElementIds = sketch.GetAllElements();
                                            foreach (ElementId id in sketchElementIds)
                                            {
                                                Element e = _doc.GetElement(id);
                                                if (e is ModelCurve)
                                                {
                                                    _doc.Delete(id);
                                                }
                                            }

                                            // 投影點至草圖平面
                                            SketchPlane plane = sketch.SketchPlane;
                                            Plane planeObj = plane.GetPlane();
                                            XYZ planeNormal = planeObj.Normal;
                                            XYZ planeOrigin = planeObj.Origin;

                                            List<XYZ> projectedPts = new List<XYZ>();
                                            foreach (XYZ pt in pts)
                                            {
                                                XYZ v = pt - planeOrigin;
                                                double dist = v.DotProduct(planeNormal);
                                                projectedPts.Add(pt - dist * planeNormal);
                                            }

                                            // 過濾重複與過於接近的點 (Revit 最小線段限制為 1/128 呎 ≈ 2.4mm)
                                            List<XYZ> cleanProjPts = new List<XYZ>();
                                            foreach (XYZ p in projectedPts)
                                            {
                                                if (cleanProjPts.Count == 0)
                                                {
                                                    cleanProjPts.Add(p);
                                                }
                                                else
                                                {
                                                    double dist = p.DistanceTo(cleanProjPts[cleanProjPts.Count - 1]);
                                                    if (dist > 0.008) // 大於 2.4mm
                                                    {
                                                        cleanProjPts.Add(p);
                                                    }
                                                }
                                            }

                                            // 檢查首尾點距離
                                            if (cleanProjPts.Count > 2)
                                            {
                                                double dist = cleanProjPts[cleanProjPts.Count - 1].DistanceTo(cleanProjPts[0]);
                                                if (dist <= 0.008)
                                                {
                                                    cleanProjPts.RemoveAt(cleanProjPts.Count - 1);
                                                }
                                            }

                                            if (cleanProjPts.Count < 3)
                                            {
                                                throw new InvalidOperationException("投影至牆面草圖後，有效頂點少於 3 個，無法建立封閉輪廓。");
                                            }

                                            // 在 sketch.SketchPlane 上建立與 cleanProjPts 對應的閉合輪廓
                                            for (int i = 0; i < cleanProjPts.Count; i++)
                                            {
                                                XYZ pt0 = cleanProjPts[i];
                                                XYZ pt1 = cleanProjPts[(i + 1) % cleanProjPts.Count];
                                                Curve curve = Line.CreateBound(pt0, pt1);
                                                _doc.Create.NewModelCurve(curve, plane);
                                            }

                                            t3.Commit();
                                        }
                                        scope.Commit(new MyFailuresPreprocessor());
                                    }
                                }
                            }

                            // 3. 寫入 Metadata 參數
                            using (Transaction t4 = new Transaction(_doc, "寫入牆面磁磚參數"))
                            {
                                t4.Start();
                                WriteMetaDataAndParams(wall, new TileData {
                                    Tile_ID = tileId, Anchor_ID = anchorId, Room_ID = roomId, Surface_ID = surfaceId, Tile_Type = tileType, Host_ID = hostId, Area = area, XYZBoundary = pts.Select(p => new XYZPoint(p.X, p.Y, p.Z)).ToList()
                                }, matName, thicknessMm);
                                t4.Commit();
                            }

                            return wall;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"轉換為 Wall 失敗: {ex.Message}");
                    throw;
                }
            }

            return null;
        }
    }

    public class MyFailuresPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor f in failures)
            {
                FailureSeverity severity = f.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
