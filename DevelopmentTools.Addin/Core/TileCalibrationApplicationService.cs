using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DevelopmentTools.Core
{
    /// <summary>
    /// 人工兩點校正與微調應用程式服務層
    /// </summary>
    public class TileCalibrationApplicationService
    {
        private readonly UIDocument _uiDoc;
        private readonly Document _doc;
        private readonly TileCalibrationPreviewService _previewService;

        // 當前校正程序的工作狀態
        public PlanarFace ActiveFace { get; private set; }
        public Element ActiveHost { get; private set; }
        public Reference ActiveFaceRef { get; private set; }
        
        public XYZ P0 { get; private set; }
        public XYZ P1 { get; private set; }

        // 即時微調參數
        public double OffsetU { get; set; } = 0.0; // mm
        public double OffsetV { get; set; } = 0.0; // mm
        public double RotationAngleDegrees { get; set; } = 0.0; // 角度

        public TileCalibrationApplicationService(UIDocument uiDoc, TileCalibrationPreviewService previewService)
        {
            _uiDoc = uiDoc;
            _doc = uiDoc.Document;
            _previewService = previewService;
        }

        /// <summary>
        /// 重設校正狀態
        /// </summary>
        public void Reset()
        {
            ActiveFace = null;
            ActiveHost = null;
            ActiveFaceRef = null;
            P0 = null;
            P1 = null;
            OffsetU = 0;
            OffsetV = 0;
            RotationAngleDegrees = 0;
            _previewService.ClearPreview();
        }

        /// <summary>
        /// 執行兩點點選與校正計算流程
        /// </summary>
        public bool RunCalibrationWizard(Reference faceRef, double tileW, double tileH, double jointW)
        {
            try
            {
                if (faceRef == null) return false;
                ActiveFaceRef = faceRef;
                ActiveHost = _doc.GetElement(faceRef.ElementId);
                ActiveFace = ActiveHost.GetGeometryObjectFromReference(faceRef) as PlanarFace;

                if (ActiveFace == null)
                {
                    TaskDialog.Show("錯誤", "選取的面必須是平面 (PlanarFace)。");
                    return false;
                }

                // Step 1: 點選點 P0
                TaskDialog.Show("校正精靈 - 步驟 1", "請點選任一磁磚縫交點 (作為起點原點 P0)。");
                try
                {
                    P0 = _uiDoc.Selection.PickPoint("請點選任一磁磚縫交點 P0");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return false;
                }

                // Step 2: 點選點 P1
                TaskDialog.Show("校正精靈 - 步驟 2", "請點選同一排磁磚縫上的另一個交點 (作為方向參考點 P1)。\n建議選在 P0 橫向右方的下一點。");
                try
                {
                    P1 = _uiDoc.Selection.PickPoint("請點選同一排磁磚縫上的另一個交點 P1");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return false;
                }

                // 將 P0, P1 投影到 PlanarFace
                P0 = ProjectPointToPlane(P0, ActiveFace);
                P1 = ProjectPointToPlane(P1, ActiveFace);

                // 驗證兩點向量
                XYZ dirVec = P1 - P0;
                double distanceMm = dirVec.GetLength() * 304.8;
                if (distanceMm < 50.0)
                {
                    TaskDialog.Show("警告", "選取的兩點距離過近，請重新校正。");
                    return false;
                }

                // 驗證模數合理性 (容差 50mm 以內)
                double pitchU = tileW + jointW;
                double multiplier = distanceMm / pitchU;
                double roundedMultiplier = Math.Round(multiplier);
                if (roundedMultiplier < 1) roundedMultiplier = 1;
                double diff = Math.Abs(distanceMm - (roundedMultiplier * pitchU));
                if (diff > 50.0)
                {
                    TaskDialog.Show("校正提示", $"選取的兩點距離為 {distanceMm:F0}mm。\n此長度與規格整數倍({roundedMultiplier}塊 = {roundedMultiplier * pitchU:F0}mm)之偏差大於 50mm。\n您仍可繼續，並在稍後以微調面版對齊。");
                }

                // 計算主軸 U 與 V
                XYZ uDir = dirVec.Normalize();
                XYZ normal = ActiveFace.FaceNormal;
                XYZ vDir = normal.CrossProduct(uDir).Normalize();

                // 繪製初始疊合預覽
                UpdatePreview(tileW, tileH, jointW);
                return true;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("校正錯誤", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 根據微調值重繪疊合預覽
        /// </summary>
        public void UpdatePreview(double tileW, double tileH, double jointW)
        {
            if (ActiveFace == null || P0 == null || P1 == null) return;

            XYZ normal = ActiveFace.FaceNormal;
            XYZ uBasis = (P1 - P0).Normalize();
            XYZ vBasis = normal.CrossProduct(uBasis).Normalize();

            // 套用旋轉微調 (繞著法向量旋轉)
            if (Math.Abs(RotationAngleDegrees) > 0.001)
            {
                double rad = RotationAngleDegrees * Math.PI / 180.0;
                Transform rotTrans = Transform.CreateRotation(normal, rad);
                uBasis = rotTrans.OfVector(uBasis).Normalize();
                vBasis = rotTrans.OfVector(vBasis).Normalize();
            }

            // 套用平移微調 (轉為 feet 單位)
            XYZ finalOrigin = P0 + (OffsetU / 304.8) * uBasis + (OffsetV / 304.8) * vBasis;

            _previewService.DrawPreviewGrid(ActiveFace, finalOrigin, uBasis, vBasis, tileW, tileH, jointW);
        }

        /// <summary>
        /// 將世界點投影到平面的輔助方法
        /// </summary>
        private XYZ ProjectPointToPlane(XYZ pt, PlanarFace face)
        {
            XYZ normal = face.FaceNormal;
            XYZ origin = face.Origin;
            double dist = (pt - origin).DotProduct(normal);
            return pt - dist * normal;
        }
    }
}
