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
        /// <summary>樓層頭顯示標高 (Level.Elevation，相對於樓層「高程基準」：專案基準點或測量點)。</summary>
        public double DisplayElevationFeet { get; set; }
        public string DetectionMethod { get; set; }
    }

    public static class SmartLevelDetector
    {
        /// <summary>
        /// 取得樓層在 Revit 內部座標 (Internal Origin) 的 Z 值。
        /// ViewOrientation3D.EyePosition 為內部座標，必須與 Level.ProjectElevation 比較；
        /// Level.Elevation 會隨「高程基準」(專案基準點/測量點) 改變，不能直接與相機 Z 相加減。
        /// </summary>
        public static double GetInternalElevationFeet(Level level)
        {
            if (level == null) return 0;
            try { return level.ProjectElevation; }
            catch { return level.Elevation; }
        }

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
                    .OrderBy(l => GetInternalElevationFeet(l))
                    .ToList();

                if (allLevels.Count == 0) return result;

                // 直接以實體樓層線高程為基準，取得相機下方最近之樓層
                Level closestBelow = allLevels.LastOrDefault(l => GetInternalElevationFeet(l) <= eyePosition.Z + 0.05);
                if (closestBelow != null)
                {
                    result.Level = closestBelow;
                    result.BaseElevationFeet = GetInternalElevationFeet(closestBelow);
                    result.DisplayElevationFeet = closestBelow.Elevation;
                    result.HeightAboveFloorFeet = eyePosition.Z - result.BaseElevationFeet;
                    result.DetectionMethod = string.Format("基準樓層 ({0})", closestBelow.Name) + DescribeBaseOffset(result);
                    return result;
                }

                Level lowest = allLevels[0];
                result.Level = lowest;
                result.BaseElevationFeet = GetInternalElevationFeet(lowest);
                result.DisplayElevationFeet = lowest.Elevation;
                result.HeightAboveFloorFeet = eyePosition.Z - result.BaseElevationFeet;
                result.DetectionMethod = string.Format("最低樓層 ({0})", lowest.Name) + DescribeBaseOffset(result);
                return result;
            }
            catch (Exception ex)
            {
                result.DetectionMethod = "判定錯誤: " + ex.Message;
                return result;
            }
        }

        private static string DescribeBaseOffset(DetectedLevelResult r)
        {
            double offsetMm = (r.BaseElevationFeet - r.DisplayElevationFeet) * 304.8;
            if (Math.Abs(offsetMm) < 1.0) return string.Empty;
            return string.Format(" · 高程基準偏移 {0:+0;-0} mm", offsetMm);
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
                    .OrderBy(l => GetInternalElevationFeet(l))
                    .ToList();

                foreach (Level l in levels)
                {
                    try
                    {
                        LevelInfoModel m = new LevelInfoModel();
                        m.LevelId = l.Id;
                        m.Name = l.Name;
                        m.ElevationFeet = l.Elevation;
                        m.ProjectElevationFeet = GetInternalElevationFeet(l);
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