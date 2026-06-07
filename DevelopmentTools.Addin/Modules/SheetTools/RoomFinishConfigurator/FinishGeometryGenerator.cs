using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace DevelopmentTools.Modules.SheetTools.RoomFinishConfigurator
{
    public static class FinishGeometryGenerator
    {
        public static void GenerateRoomFinishes(
            Document doc, 
            Room room, 
            FinishConfigItem floorConfig, 
            FinishConfigItem wallConfig, 
            FinishConfigItem ceilingConfig,
            FinishConfigItem baseboardConfig, // 新增踢腳板配置
            int jointRelation) // 0: 地坪接牆 (牆作到底), 1: 牆接地坪 (地坪作到底)
        {
            // 1. 取得房間 Level 
            Level level = room.Level;
            if (level == null) return;
            ElementId levelId = level.Id;
            double lvlElev = level.Elevation;

            // 取得房間邊界
            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };
            var boundarySegments = room.GetBoundarySegments(options);
            if (boundarySegments == null || boundarySegments.Count == 0) return;

            // 計算天、地、牆高程與尺寸
            double floorThickFeet = (floorConfig != null ? floorConfig.Thickness : 0.0) / 304.8;
            double floorOffsetFeet = (floorConfig != null ? floorConfig.HeightOrOffset : 0.0) / 304.8; // 預設為 0.0

            double wallThickFeet = (wallConfig != null ? wallConfig.Thickness : 15.0) / 304.8;
            double wallOffsetFeet = (wallConfig != null ? wallConfig.HeightOrOffset : 0.0) / 304.8;

            double ceilingThickFeet = (ceilingConfig != null ? ceilingConfig.Thickness : 12.0) / 304.8;
            double ceilingOffsetFeet = (ceilingConfig != null ? ceilingConfig.HeightOrOffset : 0.0) / 304.8; // 例如 10cm (100mm) 施工預留空間

            // 房間淨高
            double roomHeightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT).AsDouble();
            double roomUnboundedHeight = roomHeightParam > 0.01 ? roomHeightParam : 2800.0 / 304.8;

            // 天花板高程 (包含施工空間)
            // 「天要包含施工空間 10cm」：表示天花板頂面高程 = 房間最高處 - 100mm
            double ceilingHeightFeet = roomUnboundedHeight - ceilingOffsetFeet;

            // A. 生成地坪粉刷
            Floor createdFloor = null;
            if (floorConfig != null && floorConfig.Thickness > 0)
            {
                FloorType ft = GetOrCreateFloorType(doc, floorConfig.Thickness, floorConfig.Material, floorConfig.Code);
                if (ft != null)
                {
                    List<CurveLoop> loops = ConvertToCurveLoops(boundarySegments);
                    if (loops.Count > 0)
                    {
                        createdFloor = Floor.Create(doc, loops, ft.Id, levelId);
                        if (createdFloor != null)
                        {
                            // 設定地坪高度偏移量 (降版高度)
                            Parameter pOffset = createdFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                            if (pOffset != null && !pOffset.IsReadOnly)
                            {
                                pOffset.Set(floorOffsetFeet);
                            }
                            WriteInstanceParams(createdFloor, "FloorFinish", floorConfig.Code);
                        }
                    }
                }
            }

            // B. 生成天花板粉刷 (使用薄板代替)
            if (ceilingConfig != null && ceilingConfig.Thickness > 0)
            {
                FloorType ct = GetOrCreateFloorType(doc, ceilingConfig.Thickness, ceilingConfig.Material, ceilingConfig.Code);
                if (ct != null)
                {
                    List<CurveLoop> loops = ConvertToCurveLoops(boundarySegments);
                    if (loops.Count > 0)
                    {
                        Floor ceilingFloor = Floor.Create(doc, loops, ct.Id, levelId);
                        if (ceilingFloor != null)
                        {
                            // 設定天花板高程為 ceilingHeightFeet
                            Parameter pOffset = ceilingFloor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                            if (pOffset != null && !pOffset.IsReadOnly)
                            {
                                pOffset.Set(ceilingHeightFeet);
                            }
                            WriteInstanceParams(ceilingFloor, "CeilingFinish", ceilingConfig.Code);
                        }
                    }
                }
            }

            // C. 生成牆面粉刷 (獨立薄牆 + Join 洞口扣減)
            if (wallConfig != null && wallConfig.Thickness > 0)
            {
                WallType wt = GetOrCreateWallType(doc, wallConfig.Thickness, wallConfig.Material, wallConfig.Code);
                if (wt != null)
                {
                    // 決定粉刷牆的底部與頂部高程
                    double wallBaseOffset;
                    if (jointRelation == 1) // 牆接地坪 (地坪作到底)
                    {
                        // 牆站在地板頂面
                        wallBaseOffset = floorOffsetFeet + floorThickFeet;
                    }
                    else // 地坪接牆 (牆作到底)
                    {
                        // 牆作到結構板底
                        wallBaseOffset = 0.0;
                    }

                    double wallHeightFeet = ceilingHeightFeet - wallBaseOffset;
                    if (wallHeightFeet < 0.05) wallHeightFeet = 2400.0 / 304.8; // 防呆

                    foreach (var segmentLoop in boundarySegments)
                    {
                        foreach (var seg in segmentLoop)
                        {
                            Curve curve = seg.GetCurve();
                            if (curve == null || curve.Length < 0.01) continue;

                            // 獲取宿主牆
                            Wall hostWall = null;
                            if (seg.ElementId != ElementId.InvalidElementId)
                            {
                                hostWall = doc.GetElement(seg.ElementId) as Wall;
                            }

                            // 平移定位線以對齊粉刷內表面與房間邊界
                            // 牆的方向是從 Start 到 End。其法線(右側)通常指向房間外或內。
                            // 我們可以稍微將線段朝房間外部(即向宿主牆內部)平移半牆厚度
                            XYZ wallDir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                            XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize(); // 2D 平面法線

                            // 檢查法向是否指向房間外部。如果 segment 的 Element 是牆，我們可以利用牆的 Orientation
                            if (hostWall != null)
                            {
                                XYZ hostOrient = hostWall.Orientation;
                                if (hostOrient.DotProduct(normal) < 0)
                                {
                                    normal = -normal;
                                }
                            }
                            // 平移半厚度，使粉刷牆的邊緣正好貼齊邊界
                            XYZ translation = normal * (wallThickFeet / 2.0);
                            Transform trans = Transform.CreateTranslation(translation);
                            Curve offsetCurve = curve.CreateTransformed(trans);

                            try
                            {
                                Wall finishWall = Wall.Create(doc, offsetCurve, wt.Id, levelId, wallHeightFeet, wallBaseOffset, false, false);
                                if (finishWall != null)
                                {
                                    // 關閉牆端點 Join，防長度自動縮小
                                    if (finishWall.Location is LocationCurve locCurve)
                                    {
                                        WallUtils.DisallowWallJoinAtEnd(finishWall, 0);
                                        WallUtils.DisallowWallJoinAtEnd(finishWall, 1);
                                    }

                                    // 與宿主牆 Join，自動扣減門窗洞口！
                                    if (hostWall != null)
                                    {
                                        try
                                        {
                                            JoinGeometryUtils.JoinGeometry(doc, hostWall, finishWall);
                                        }
                                        catch { }
                                    }

                                    WriteInstanceParams(finishWall, "WallFinish", wallConfig.Code);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }

            // D. 生成踢腳板粉刷 (使用矮薄牆代替)
            if (baseboardConfig != null && baseboardConfig.Thickness > 0 && baseboardConfig.HeightOrOffset > 0)
            {
                WallType wt = GetOrCreateWallType(doc, baseboardConfig.Thickness, baseboardConfig.Material, baseboardConfig.Code);
                if (wt != null)
                {
                    double baseboardBaseOffset;
                    if (createdFloor != null)
                    {
                        // 如果有地板粉刷，站在地板粉刷頂部
                        baseboardBaseOffset = floorOffsetFeet + floorThickFeet;
                    }
                    else
                    {
                        // 否則，站在結構板面
                        baseboardBaseOffset = 0.0;
                    }
 
                    double baseboardHeightFeet = baseboardConfig.HeightOrOffset / 304.8;
 
                    foreach (var segmentLoop in boundarySegments)
                    {
                        foreach (var seg in segmentLoop)
                        {
                            Curve curve = seg.GetCurve();
                            if (curve == null || curve.Length < 0.01) continue;
 
                            // 獲取宿主牆
                            Wall hostWall = null;
                            if (seg.ElementId != ElementId.InvalidElementId)
                            {
                                hostWall = doc.GetElement(seg.ElementId) as Wall;
                            }
 
                            // 平移定位線
                            XYZ wallDir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                            XYZ normal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();
 
                            if (hostWall != null)
                            {
                                XYZ hostOrient = hostWall.Orientation;
                                if (hostOrient.DotProduct(normal) < 0)
                                {
                                    normal = -normal;
                                }
                            }
 
                            double baseboardThickFeet = baseboardConfig.Thickness / 304.8;
                            XYZ translation = normal * (baseboardThickFeet / 2.0);
                            Transform trans = Transform.CreateTranslation(translation);
                            Curve offsetCurve = curve.CreateTransformed(trans);
 
                            try
                            {
                                Wall finishWall = Wall.Create(doc, offsetCurve, wt.Id, levelId, baseboardHeightFeet, baseboardBaseOffset, false, false);
                                if (finishWall != null)
                                {
                                    if (finishWall.Location is LocationCurve locCurve)
                                    {
                                        WallUtils.DisallowWallJoinAtEnd(finishWall, 0);
                                        WallUtils.DisallowWallJoinAtEnd(finishWall, 1);
                                    }
 
                                    // 與宿主牆 Join 扣門洞
                                    if (hostWall != null)
                                    {
                                        try
                                        {
                                            JoinGeometryUtils.JoinGeometry(doc, hostWall, finishWall);
                                        }
                                        catch { }
                                    }
 
                                    WriteInstanceParams(finishWall, "BaseboardFinish", baseboardConfig.Code);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private static List<CurveLoop> ConvertToCurveLoops(IList<IList<BoundarySegment>> segments)
        {
            var loops = new List<CurveLoop>();
            foreach (var segmentLoop in segments)
            {
                CurveLoop loop = new CurveLoop();
                foreach (var seg in segmentLoop)
                {
                    Curve curve = seg.GetCurve();
                    if (curve != null)
                    {
                        loop.Append(curve);
                    }
                }
                if (loop.IsOpen() == false)
                {
                    loops.Add(loop);
                }
            }
            return loops;
        }

        private static Material FindMaterial(Document doc, string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return null;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
        }

        private static FloorType GetOrCreateFloorType(Document doc, double thicknessMm, string materialName, string code)
        {
            string safeMatName = string.IsNullOrWhiteSpace(materialName) ? "DefaultFinish" : materialName;
            string safeCode = string.IsNullOrWhiteSpace(code) ? "NoCode" : code.Trim();
            string typeName = $"Finish_Floor_{safeCode}_{thicknessMm}mm_{safeMatName}";
            
            FloorType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                                     GetTypeCode(t).Equals(safeCode, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                WriteTypeCode(existing, code);
                return existing;
            }

            FloorType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .FirstOrDefault() as FloorType;
            if (baseType == null) return null;

            try
            {
                FloorType newType = baseType.Duplicate(typeName) as FloorType;
                if (newType != null)
                {
                    CompoundStructure cs = newType.GetCompoundStructure();
                    if (cs != null)
                    {
                        double thicknessFeet = thicknessMm / 304.8;
                        var layers = cs.GetLayers();
                        if (layers.Count > 0)
                        {
                            var layer = layers[0];
                            layer.Width = thicknessFeet;
                            Material mat = FindMaterial(doc, materialName);
                            if (mat != null) layer.MaterialId = mat.Id;
                            
                            cs.SetLayers(new List<CompoundStructureLayer> { layer });
                            newType.SetCompoundStructure(cs);
                        }
                    }
                    WriteTypeCode(newType, code);
                    return newType;
                }
            }
            catch { }
            return baseType;
        }

        private static WallType GetOrCreateWallType(Document doc, double thicknessMm, string materialName, string code)
        {
            string safeMatName = string.IsNullOrWhiteSpace(materialName) ? "DefaultFinish" : materialName;
            string safeCode = string.IsNullOrWhiteSpace(code) ? "NoCode" : code.Trim();
            string typeName = $"Finish_Wall_{safeCode}_{thicknessMm}mm_{safeMatName}";

            WallType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                                     GetTypeCode(t).Equals(safeCode, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                WriteTypeCode(existing, code);
                return existing;
            }

            WallType baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .FirstOrDefault() as WallType;
            if (baseType == null) return null;

            try
            {
                WallType newType = baseType.Duplicate(typeName) as WallType;
                if (newType != null)
                {
                    double thicknessFeet = thicknessMm / 304.8;
                    CompoundStructureLayer layer = new CompoundStructureLayer(thicknessFeet, MaterialFunctionAssignment.Finish1, ElementId.InvalidElementId);
                    Material mat = FindMaterial(doc, materialName);
                    if (mat != null) layer.MaterialId = mat.Id;

                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(new List<CompoundStructureLayer> { layer });
                    newType.SetCompoundStructure(cs);
                    WriteTypeCode(newType, code);
                    return newType;
                }
            }
            catch { }
            return baseType;
        }

        private static void WriteInstanceParams(Element elem, string role, string code)
        {
            try
            {
                Parameter commentParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentParam != null && !commentParam.IsReadOnly)
                {
                    commentParam.Set($"[自動粉刷生成] Role:{role}|Code:{code}");
                }
            }
            catch { }
        }

        private static void WriteTypeCode(ElementType elemType, string code)
        {
            if (elemType == null || string.IsNullOrWhiteSpace(code)) return;

            try
            {
                Parameter typeComments = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
                if (typeComments != null && !typeComments.IsReadOnly)
                {
                    typeComments.Set(code.Trim());
                }
            }
            catch { }
        }

        private static string GetTypeCode(ElementType elemType)
        {
            if (elemType == null) return string.Empty;

            try
            {
                Parameter typeComments = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);
                if (typeComments != null && typeComments.HasValue)
                {
                    return (typeComments.AsString() ?? string.Empty).Trim();
                }
            }
            catch { }

            return string.Empty;
        }
    }
}
