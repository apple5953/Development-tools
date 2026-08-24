using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;

namespace DevelopmentTools.Modules.SheetTools.FastDependentViews
{
    public static class RevitUtils
    {
        /// <summary>
        /// 獲取空間元素 (Room 或 Space) 的最外圍閉合邊界 CurveLoop。
        /// </summary>
        public static CurveLoop GetSpatialElementBoundary(SpatialElement spatialElement, SpatialElementBoundaryLocation location)
        {
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = location
            };

            IList<IList<BoundarySegment>> boundaryLoops = spatialElement.GetBoundarySegments(options);
            if (boundaryLoops == null || boundaryLoops.Count == 0)
                return null;

            // 通常第一個 Loop (索引 0) 是最外圍的輪廓線，其餘的則是內部的柱子或洞口輪廓
            IList<BoundarySegment> outerLoopSegments = boundaryLoops[0];
            if (outerLoopSegments.Count == 0)
                return null;

            var curveLoop = new CurveLoop();
            foreach (var segment in outerLoopSegments)
            {
                Curve curve = segment.GetCurve();
                if (curve != null)
                {
                    curveLoop.Append(curve);
                }
            }

            return curveLoop;
        }

        /// <summary>
        /// 獲取樓板 (Floor) 的頂面最外圍閉合邊界 CurveLoop。
        /// </summary>
        public static CurveLoop GetFloorBoundary(Floor floor)
        {
            var options = new Options
            {
                ComputeReferences = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            GeometryElement geomElem = floor.get_Geometry(options);
            if (geomElem == null)
                return null;

            Solid floorSolid = null;
            foreach (GeometryObject geomObj in geomElem)
            {
                Solid solid = geomObj as Solid;
                if (solid != null && solid.Volume > 0)
                {
                    floorSolid = solid;
                    break;
                }
                else
                {
                    GeometryInstance geomInst = geomObj as GeometryInstance;
                    if (geomInst != null)
                    {
                        GeometryElement instGeom = geomInst.GetInstanceGeometry();
                        foreach (GeometryObject instObj in instGeom)
                        {
                            Solid s = instObj as Solid;
                            if (s != null && s.Volume > 0)
                            {
                                floorSolid = s;
                                break;
                            }
                        }
                    }
                }
            }

            if (floorSolid == null)
                return null;

            // 尋找 Z 軸朝上且高度 (Origin.Z) 最高的面（即頂面）
            PlanarFace topFace = null;
            double maxZ = -double.MaxValue;

            foreach (Face face in floorSolid.Faces)
            {
                PlanarFace planarFace = face as PlanarFace;
                if (planarFace != null)
                {
                    // 檢查法向量是否大致朝上 (Z > 0.9)
                    if (planarFace.FaceNormal.Z > 0.9)
                    {
                        double faceZ = planarFace.Origin.Z;
                        if (faceZ > maxZ)
                        {
                            maxZ = faceZ;
                            topFace = planarFace;
                        }
                    }
                }
            }

            if (topFace == null)
                return null;

            // 從頂面獲取所有 CurveLoop
            IList<CurveLoop> curveLoops = topFace.GetEdgesAsCurveLoops();
            if (curveLoops == null || curveLoops.Count == 0)
                return null;

            // 計算每個 CurveLoop 投影到 XY 平面的面積，選取面積最大的那一個作為最外圍邊界
            CurveLoop outerLoop = null;
            double maxArea = -1;

            foreach (var loop in curveLoops)
            {
                double area = ComputeAreaXY(loop);
                if (area > maxArea)
                {
                    maxArea = area;
                    outerLoop = loop;
                }
            }

            return outerLoop;
        }

        /// <summary>
        /// 計算 CurveLoop 投影在 XY 平面上的二維面積 (鞋帶公式)。
        /// </summary>
        private static double ComputeAreaXY(CurveLoop loop)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                points.Add(curve.GetEndPoint(0));
            }

            double area = 0;
            int n = points.Count;
            if (n < 3) return 0;

            for (int i = 0; i < n; i++)
            {
                XYZ p1 = points[i];
                XYZ p2 = points[(i + 1) % n];
                area += (p1.X * p2.Y) - (p2.X * p1.Y);
            }

            return Math.Abs(area) / 2.0;
        }

        /// <summary>
        /// 將 CurveLoop 平移並投影到指定的 Z 高度，使其與視圖平面平行。
        /// </summary>
        public static CurveLoop ProjectCurveLoopToZ(CurveLoop sourceLoop, double targetZ)
        {
            if (sourceLoop == null)
                return null;

            var projectedLoop = new CurveLoop();
            foreach (Curve curve in sourceLoop)
            {
                double curveZ = curve.GetEndPoint(0).Z;
                double offsetZ = targetZ - curveZ;

                if (Math.Abs(offsetZ) > 0.0001)
                {
                    Transform transform = Transform.CreateTranslation(new XYZ(0, 0, offsetZ));
                    projectedLoop.Append(curve.CreateTransformed(transform));
                }
                else
                {
                    projectedLoop.Append(curve);
                }
            }

            return projectedLoop;
        }

        /// <summary>
        /// 格式化從屬視圖名稱。
        /// 支援以下佔位符：
        /// - {ParentView}：主要視圖名稱
        /// - {Name}：元素名稱
        /// - {Number}：編號 (僅 Room/Space 有，Floor 為空)
        /// - {Id}：元素 ID
        /// </summary>
        public static string FormatViewName(string pattern, string parentViewName, string elemName, string elemNumber, string elemId)
        {
            string formatted = pattern
                .Replace("{ParentView}", parentViewName)
                .Replace("{Name}", elemName)
                .Replace("{Number}", elemNumber ?? "")
                .Replace("{Id}", elemId);

            // 移除不合法的 Revit 視圖命名字元 \ : { } [ ] | ; < > ? ` ~
            char[] invalidChars = new char[] { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            foreach (char c in invalidChars)
            {
                formatted = formatted.Replace(c.ToString(), "");
            }

            return formatted.Trim();
        }

        /// <summary>
        /// 檢查該視圖名稱在專案中是否已存在。
        /// </summary>
        public static bool IsViewNameUnique(Document doc, string viewName)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(View));

            return !collector.Any(v => v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 獲取唯一的視圖名稱（若重複則自動加上尾碼）。
        /// </summary>
        public static string GetUniqueViewName(Document doc, string baseName)
        {
            if (IsViewNameUnique(doc, baseName))
                return baseName;

            int index = 1;
            string newName;
            do
            {
                newName = string.Format("{0}_{1}", baseName, index);
                index++;
            } while (!IsViewNameUnique(doc, newName));

            return newName;
        }
    }
}
