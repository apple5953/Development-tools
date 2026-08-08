using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Core
{
    /// <summary>
    /// 提供臨時預覽模型線生成與清除服務，供兩點校正與微調階段展示使用。
    /// </summary>
    public class TileCalibrationPreviewService
    {
        private readonly Document _doc;
        private readonly List<ElementId> _previewLineIds = new List<ElementId>();

        public TileCalibrationPreviewService(Document doc)
        {
            _doc = doc;
        }

        /// <summary>
        /// 清除當前所有的校正預覽線
        /// </summary>
        public void ClearPreview()
        {
            if (_previewLineIds.Count == 0) return;

            using (Transaction t = new Transaction(_doc, "清除校正預覽線"))
            {
                t.Start();
                List<ElementId> toDelete = new List<ElementId>();
                foreach (var id in _previewLineIds)
                {
                    if (_doc.GetElement(id) != null)
                    {
                        toDelete.Add(id);
                    }
                }
                if (toDelete.Count > 0)
                {
                    try { _doc.Delete(toDelete); } catch { }
                }
                _previewLineIds.Clear();
                t.Commit();
            }
        }

        /// <summary>
        /// 生成 2D 疊加校正預覽線 (在起點點位周邊生成一個 5x5 的格網作為參考)
        /// </summary>
        public void DrawPreviewGrid(PlanarFace face, XYZ origin, XYZ uDir, XYZ vDir, double tileW, double tileH, double jointW)
        {
            // 先清空舊有的
            ClearPreview();

            XYZ normal = face.FaceNormal;
            Plane plane = Plane.CreateByNormalAndOrigin(normal, origin);

            using (Transaction t = new Transaction(_doc, "建立校正預覽線"))
            {
                t.Start();

                SketchPlane sketchPlane = SketchPlane.Create(_doc, plane);

                double pitchU = (tileW + jointW) / 304.8; // 轉為 feet
                double pitchV = (tileH + jointW) / 304.8;
                double halfJoint = (jointW / 2.0) / 304.8;
                double tileWFeet = tileW / 304.8;
                double tileHFeet = tileH / 304.8;

                // 畫 5x5 區間
                for (int i = -3; i <= 3; i++)
                {
                    for (int j = -3; j <= 3; j++)
                    {
                        // 計算磁磚的四個角點 (考量縫寬)
                        XYZ tileMin = origin + (i * pitchU + halfJoint) * uDir + (j * pitchV + halfJoint) * vDir;
                        XYZ p0 = tileMin;
                        XYZ p1 = tileMin + tileWFeet * uDir;
                        XYZ p2 = tileMin + tileWFeet * uDir + tileHFeet * vDir;
                        XYZ p3 = tileMin + tileHFeet * vDir;

                        try
                        {
                            AddLine(p0, p1, sketchPlane);
                            AddLine(p1, p2, sketchPlane);
                            AddLine(p2, p3, sketchPlane);
                            AddLine(p3, p0, sketchPlane);
                        }
                        catch { }
                    }
                }

                t.Commit();
            }
        }

        private void AddLine(XYZ start, XYZ end, SketchPlane sketchPlane)
        {
            if (start.DistanceTo(end) < 0.001) return;
            Line line = Line.CreateBound(start, end);
            ModelCurve modelCurve = _doc.Create.NewModelCurve(line, sketchPlane);
            if (modelCurve != null)
            {
                _previewLineIds.Add(modelCurve.Id);
                // 寫入特別標記，方便未來若發生意外能被清理
                Parameter commentParam = modelCurve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentParam != null)
                {
                    commentParam.Set("CalibrationPreviewLine");
                }
            }
        }
    }
}
