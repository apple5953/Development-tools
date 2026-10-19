using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitPerspectiveMinimap.Models;

namespace RevitPerspectiveMinimap.Core
{
    public class DetectedLevelResult
    {
        public Level Level { get; set; }
        public double BaseElevationFeet { get; set; }
        public double HeightAboveFloorFeet { get; set; }
        public string DetectionMethod { get; set; }
    }

    public static class SmartLevelDetector
    {
        /// <summary>
        /// 穩健安全的樓層高程判定 (避免在模型載入期間觸發原生 C++ GRep 射線衝突)
        /// </summary>
        public static DetectedLevelResult DetectLevel(Document doc, XYZ eyePosition, View3D reference3DView)
        {
            DetectedLevelResult result = new DetectedLevelResult();
            result.BaseElevationFeet = 0;
            result.HeightAboveFloorFeet = (eyePosition != null) ? eyePosition.Z : 0;
            result.DetectionMethod = "預設地面 (0.00m)";

            if (doc == null || eyePosition == null) return result;

            try
            {
                List<Level> allLevels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Where(l => l != null)
                    .OrderBy(l => l.Elevation)
                    .ToList();

                if (allLevels.Count == 0) return result;

                // 直接以實體樓層線高程為基準，取得相機下方最近之樓層
                Level closestBelow = allLevels.LastOrDefault(l => l.Elevation <= eyePosition.Z + 0.05);
                if (closestBelow != null)
                {
                    result.Level = closestBelow;
                    result.BaseElevationFeet = closestBelow.Elevation;
                    result.HeightAboveFloorFeet = eyePosition.Z - closestBelow.Elevation;
                    result.DetectionMethod = string.Format("基準樓層 ({0})", closestBelow.Name);
                    return result;
                }

                Level lowest = allLevels[0];
                result.Level = lowest;
                result.BaseElevationFeet = lowest.Elevation;
                result.HeightAboveFloorFeet = eyePosition.Z - lowest.Elevation;
                result.DetectionMethod = string.Format("最低樓層 ({0})", lowest.Name);
                return result;
            }
            catch (Exception ex)
            {
                result.DetectionMethod = "判定錯誤: " + ex.Message;
                return result;
            }
        }

        public static List<LevelInfoModel> GetAllLevels(Document doc)
        {
            List<LevelInfoModel> result = new List<LevelInfoModel>();
            if (doc == null) return result;

            try
            {
                List<Level> levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .Where(l => l != null)
                    .OrderBy(l => l.Elevation)
                    .ToList();

                foreach (Level l in levels)
                {
                    try
                    {
                        LevelInfoModel m = new LevelInfoModel();
                        m.LevelId = l.Id;
                        m.Name = l.Name;
                        m.ElevationFeet = l.Elevation;
                        try { m.ProjectElevationFeet = l.ProjectElevation; } catch { m.ProjectElevationFeet = l.Elevation; }
                        result.Add(m);
                    }
                    catch { }
                }
            }
            catch { }

            return result;
        }
    }
}