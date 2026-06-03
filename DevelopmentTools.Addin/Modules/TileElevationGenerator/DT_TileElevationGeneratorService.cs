using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Modules.TileElevationGenerator
{
    public class DT_TileElevationGeneratorService
    {
        public TileElevationResult GenerateElevationsForFloor(Document doc, Floor floor, GeneratorSettings settings)
        {
            var result = new TileElevationResult();
            try
            {
                // 1. 搜尋相鄰的牆面
                var adjacentWalls = AdjacentWallFinder.FindAdjacentWalls(doc, floor, settings.MinWallLength, settings.SkipShortWall);
                if (adjacentWalls.Count == 0)
                {
                    result.ErrorMessage = "在選定的地板周圍找不到任何相鄰牆體！";
                    return result;
                }

                // 2. 計算 Floor 的幾何中心點，並建立牆體幾何數據 (含順時針排序)
                XYZ floorCenter = GetFloorCenter(floor);
                var wallDataList = WallElevationDataBuilder.BuildData(doc, adjacentWalls, settings, floorCenter);

                // 3. 啟動 Transaction 建立剖面視圖
                using (var tx = new Transaction(doc, "Generate Tile Elevations (Floor Mode)"))
                {
                    tx.Start();
                    
                    int index = 0;
                    foreach (var wallData in wallDataList)
                    {
                        string viewName = ElevationNamingService.GenerateViewName(doc, settings.NamePrefix, index);
                        try
                        {
                            var view = WallElevationViewCreator.CreateElevationView(doc, wallData, settings, viewName);
                            if (view != null)
                            {
                                result.CreatedViewsCount++;
                                result.CreatedViewNames.Add(viewName);
                                index++;
                            }
                        }
                        catch (Exception)
                        {
                            result.SkippedWallsCount++;
                        }
                    }
                    
                    tx.Commit();
                }

                result.Success = result.CreatedViewsCount > 0;
                if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = "沒有成功建立任何展開圖視圖。";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public TileElevationResult GenerateElevationsForWalls(Document doc, List<Wall> walls, GeneratorSettings settings)
        {
            var result = new TileElevationResult();
            try
            {
                if (walls.Count == 0)
                {
                    result.ErrorMessage = "選取的牆體列表為空！";
                    return result;
                }

                // 計算所有牆體中點的平均值，作為參考中心點以判斷法線反向
                XYZ referenceCenter = GetWallsAverageCenter(walls);
                var wallDataList = WallElevationDataBuilder.BuildData(doc, walls, settings, referenceCenter);

                using (var tx = new Transaction(doc, "Generate Tile Elevations (Wall Mode)"))
                {
                    tx.Start();
                    
                    int index = 0;
                    foreach (var wallData in wallDataList)
                    {
                        string viewName = ElevationNamingService.GenerateViewName(doc, settings.NamePrefix, index);
                        try
                        {
                            var view = WallElevationViewCreator.CreateElevationView(doc, wallData, settings, viewName);
                            if (view != null)
                            {
                                result.CreatedViewsCount++;
                                result.CreatedViewNames.Add(viewName);
                                index++;
                            }
                        }
                        catch (Exception)
                        {
                            result.SkippedWallsCount++;
                        }
                    }
                    
                    tx.Commit();
                }

                result.Success = result.CreatedViewsCount > 0;
                if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = "沒有成功建立任何展開圖視圖。";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static XYZ GetFloorCenter(Floor floor)
        {
            var bbox = floor.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2.0;
            }
            var loc = floor.Location as LocationPoint;
            if (loc != null) return loc.Point;
            return XYZ.Zero;
        }

        private static XYZ GetWallsAverageCenter(List<Wall> walls)
        {
            double sumX = 0, sumY = 0, sumZ = 0;
            int count = 0;
            foreach (var wall in walls)
            {
                var wallLoc = wall.Location as LocationCurve;
                if (wallLoc != null && wallLoc.Curve != null)
                {
                    XYZ mid = (wallLoc.Curve.GetEndPoint(0) + wallLoc.Curve.GetEndPoint(1)) / 2.0;
                    sumX += mid.X;
                    sumY += mid.Y;
                    sumZ += mid.Z;
                    count++;
                }
            }
            if (count == 0) return XYZ.Zero;
            return new XYZ(sumX / count, sumY / count, sumZ / count);
        }
    }
}
