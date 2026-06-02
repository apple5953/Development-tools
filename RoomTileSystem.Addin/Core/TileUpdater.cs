using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RoomTileSystem.Core;
using RoomTileSystem.Algorithms;
using RoomTileSystem.Generators;

namespace RoomTileSystem.Core
{
    public class TileUpdater : IUpdater
    {
        private AddInId _addInId;
        private UpdaterId _updaterId;

        public TileUpdater(AddInId addInId)
        {
            _addInId = addInId;
            _updaterId = new UpdaterId(_addInId, new Guid("8CA77C60-D09D-4804-A978-C2E6812FEE3B"));
        }

        public void Execute(UpdaterData data)
        {
            Document doc = data.GetDocument();
            
            // 取得活動的 3D 視圖，若無 3D 視圖則不更新 (ReferenceIntersector 必須)
            View3D view3D = doc.ActiveView as View3D;
            if (view3D == null) return;

            foreach (ElementId id in data.GetModifiedElementIds())
            {
                Element elem = doc.GetElement(id);
                if (elem is FamilyInstance fi && fi.Symbol.Family.Name.Equals("RoomTileCoordinate", StringComparison.OrdinalIgnoreCase))
                {
                    // 檢查是否啟用自動即時更新。若無此參數或值不為 0 (No) 則預設為 True
                    Parameter autoUpdateParam = fi.LookupParameter("Auto_Update");
                    bool shouldUpdate = true;
                    if (autoUpdateParam != null && autoUpdateParam.HasValue)
                    {
                        shouldUpdate = (autoUpdateParam.AsInteger() != 0);
                    }

                    if (shouldUpdate)
                    {
                        try
                        {
                            UpdateTileLayoutForAnchor(doc, view3D, fi);
                        }
                        catch
                        {
                            // 避免背景更新異常崩潰
                        }
                    }
                }
            }
        }

        public static void UpdateTileLayoutForAnchor(Document doc, View3D view3D, FamilyInstance anchor)
        {
            string anchorId = anchor.LookupParameter("Anchor_ID")?.AsString();
            if (string.IsNullOrEmpty(anchorId)) return;

            // 1. 嘗試從 Extensible Storage 中讀取既有的排版規格
            double floorW = 300.0; double floorH = 300.0; double floorJoint = 3.0; double floorThick = 10.0;
            double wallW = 300.0; double wallH = 600.0; double wallJoint = 3.0; double wallThick = 10.0;
            
            bool hasSavedSpec = false;
            try
            {
                string savedJson = ExtensibleStorageManager.LoadLayoutData(anchor);
                if (!string.IsNullOrEmpty(savedJson))
                {
                    RoomTileLayoutData savedData = System.Text.Json.JsonSerializer.Deserialize<RoomTileLayoutData>(savedJson);
                    if (savedData != null)
                    {
                        floorW = savedData.FloorTileWidth;
                        floorH = savedData.FloorTileHeight;
                        floorJoint = savedData.FloorJointWidth;
                        floorThick = savedData.FloorThickness;
                        
                        wallW = savedData.WallTileWidth;
                        wallH = savedData.WallTileHeight;
                        wallJoint = savedData.WallJointWidth;
                        wallThick = savedData.WallThickness;
                        hasSavedSpec = true;
                    }
                }
            }
            catch { }

            // 如果沒有既有儲存的規格，則從共享參數中讀取預設
            if (!hasSavedSpec)
            {
                floorW = GetLengthParam(anchor, "Tile_Width", 300.0);
                floorH = GetLengthParam(anchor, "Tile_Height", 300.0);
                floorJoint = GetLengthParam(anchor, "Joint_Width", 3.0);
                floorThick = GetLengthParam(anchor, "Tile_Thickness", 10.0);
                
                wallW = floorW;
                wallH = floorH > 300.1 ? floorH : 600.0;
                wallJoint = floorJoint;
                wallThick = floorThick;
            }

            double startH = GetLengthParam(anchor, "Wall_Start_Height", 0.0);
            double endH = GetLengthParam(anchor, "Wall_End_Height", 2400.0);

            XYZ anchorOrigin = null;
            BoundingBoxXYZ bbox = anchor.get_BoundingBox(null);
            if (bbox != null)
            {
                anchorOrigin = (bbox.Min + bbox.Max) * 0.5;
            }
            else
            {
                LocationPoint anchorLoc = anchor.Location as LocationPoint;
                if (anchorLoc == null) return;
                anchorOrigin = anchorLoc.Point;
            }

            // 3. 建立局部座標系統
            RoomLocalCoordinate localCoord = new RoomLocalCoordinate(anchor);

            // 4. 尋找控制器所在的房間
            FaceDrivenGeometryAnalyzer analyzer = new FaceDrivenGeometryAnalyzer(doc, view3D);
            Room room = analyzer.FindRoomForAnchor(anchor, view3D, localCoord);
            if (room == null) return;
            string roomId = $"{room.Number} - {room.Name}";

            // 5. 提取幾何與天花板高度
            WorksetId floorWorksetId = null;
            WorksetId wallWorksetId = null;
            if (doc.IsWorkshared)
            {
                try
                {
                    FilteredWorksetCollector wsCollector = new FilteredWorksetCollector(doc);
                    wsCollector.OfKind(WorksetKind.UserWorkset);
                    foreach (Workset ws in wsCollector)
                    {
                        if (ws.Name.Contains("地坪") || ws.Name.Contains("Floor") || ws.Name.Contains("裝修工程"))
                        {
                            floorWorksetId = ws.Id;
                        }
                        if (ws.Name.Contains("牆面") || ws.Name.Contains("Wall") || ws.Name.Contains("精裝修"))
                        {
                            wallWorksetId = ws.Id;
                        }
                    }
                }
                catch { }

                if (floorWorksetId == null) floorWorksetId = anchor.WorksetId;
                if (wallWorksetId == null) wallWorksetId = anchor.WorksetId;
            }

            FloorSurfaceGeometry floorGeo = analyzer.ExtractFloorFace(room, anchorOrigin, localCoord, floorWorksetId);
            double? ceilingHeight = analyzer.ExtractCeilingHeight(anchorOrigin);
            if (ceilingHeight.HasValue)
            {
                endH = ceilingHeight.Value;
            }

            List<WallSurfaceGeometry> wallGeos = analyzer.ExtractWallFaces(room, anchorOrigin, localCoord, startH, endH, wallWorksetId);

            // 6. 排版計算
            TileLayoutEngine engine = new TileLayoutEngine();
            RoomTileLayoutData layoutData = new RoomTileLayoutData
            {
                Room_ID = roomId,
                Anchor_ID = anchorId,
                FloorTileWidth = floorW,
                FloorTileHeight = floorH,
                FloorJointWidth = floorJoint,
                FloorThickness = floorThick,
                WallTileWidth = wallW,
                WallTileHeight = wallH,
                WallJointWidth = wallJoint,
                WallThickness = wallThick
            };

            // 檢查原先儲存的排版中是否存在地坪或牆面，以決定是否重算
            bool hadFloor = false;
            bool hadWalls = false;
            if (hasSavedSpec)
            {
                try
                {
                    string savedJson = ExtensibleStorageManager.LoadLayoutData(anchor);
                    if (!string.IsNullOrEmpty(savedJson))
                    {
                        RoomTileLayoutData savedData = System.Text.Json.JsonSerializer.Deserialize<RoomTileLayoutData>(savedJson);
                        if (savedData != null)
                        {
                            foreach (var s in savedData.Surfaces)
                            {
                                if (s.Surface_ID.Equals("Floor", StringComparison.OrdinalIgnoreCase))
                                    hadFloor = true;
                                if (s.Surface_ID.StartsWith("Wall_", StringComparison.OrdinalIgnoreCase))
                                    hadWalls = true;
                            }
                        }
                    }
                }
                catch { }
            }

            if (hadFloor && floorGeo != null)
            {
                TilePatternParams floorPat = new TilePatternParams
                {
                    Style = TilePatternStyle.Stack,
                    TileWidth = floorW,
                    TileHeight = floorH,
                    JointWidth = floorJoint,
                    Thickness = floorThick
                };
                SurfaceTileLayoutData floorLayout = engine.LayoutFloor(
                    floorGeo.BoundaryLoops, localCoord, floorPat, anchorId, roomId, floorGeo.HostElementId);
                layoutData.Surfaces.Add(floorLayout);
            }

            if (hadWalls && wallGeos.Count > 0)
            {
                TilePatternParams wallPat = new TilePatternParams
                {
                    Style = TilePatternStyle.Stack,
                    TileWidth = wallW,
                    TileHeight = wallH,
                    JointWidth = wallJoint,
                    Thickness = wallThick
                };
                for (int idx = 0; idx < wallGeos.Count; idx++)
                {
                    SurfaceTileLayoutData wallLayout = engine.LayoutWall(
                        wallGeos[idx], localCoord, wallPat, anchorId, roomId, idx,
                        0.0, 0.0);
                    layoutData.Surfaces.Add(wallLayout);
                }
            }

            // 6. 清除並重新繪製預覽模型線 (DMU Context 下由 Revit 託管，無須啟動 Transaction)
            List<TileData> allTiles = new List<TileData>();
            foreach (var s in layoutData.Surfaces)
            {
                allTiles.AddRange(s.Tiles);
            }

            PreviewGenerator previewGen = new PreviewGenerator(doc);
            previewGen.GenerateModelLinePreview(allTiles, anchorId);

            // 7. 根據 Generate_Mode 控制是否自動生成 3D 實體模型 (1 = 僅預覽線, 2 = 預覽與3D實體)
            Parameter genModeParam = anchor.LookupParameter("Generate_Mode");
            if (genModeParam != null && genModeParam.AsInteger() == 2)
            {
                TileUpdateDeleteManager.DeleteExistingTiles(doc, anchorId);
                GeometryGenerator geomGen = new GeometryGenerator(doc);
                ElementId matId = geomGen.GetOrCreateTileMaterial("Tile_Generic_Material", new Color(220, 220, 220));

                foreach (var surface in layoutData.Surfaces)
                {
                    foreach (var tile in surface.Tiles)
                    {
                        if (tile.Tile_Type == "Full") continue;
                        geomGen.GenerateTileSolid(tile, matId);
                    }
                }
            }

            // 8. 將排版 JSON 資料寫入 Extensible Storage
            string json = System.Text.Json.JsonSerializer.Serialize(layoutData);
            ExtensibleStorageManager.SaveLayoutData(anchor, json);
        }

        private static double GetLengthParam(Element elem, string paramName, double defaultMm)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param != null && param.HasValue)
            {
                double valFeet = param.AsDouble();
                return valFeet * 304.8;
            }
            return defaultMm;
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetAdditionalInformation() => "即時更新磁磚排版與3D實體幾何";
        public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
        public string GetUpdaterName() => "TileUpdater";
    }
}
