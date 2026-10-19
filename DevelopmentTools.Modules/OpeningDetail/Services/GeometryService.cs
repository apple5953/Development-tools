using System;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using OpeningDetailAddin.Models;

namespace OpeningDetailAddin.Services
{
    /// <summary>
    /// 幾何運算與座標變換服務
    /// 完整支援「多種牆類型（厚度、長度、高度）」與「多種開口尺寸（35x35, 50x10.4, 117.5x20cm 等）」
    /// </summary>
    public static class GeometryService
    {
        public const double MmToFeetFactor = 1.0 / 304.8;
        public const double FeetToMmFactor = 304.8;

        public static double MmToFeet(double mm) { return mm * MmToFeetFactor; }
        public static double FeetToMm(double feet) { return feet * FeetToMmFactor; }

        /// <summary>
        /// 計算牆體長度 (mm)
        /// </summary>
        public static double GetWallLengthMm(Wall wall)
        {
            if (wall == null) return 0;
            LocationCurve locCurve = wall.Location as LocationCurve;
            if (locCurve != null && locCurve.Curve != null)
            {
                return FeetToMm(locCurve.Curve.Length);
            }
            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ d = bbox.Max - bbox.Min;
                return FeetToMm(Math.Sqrt(d.X * d.X + d.Y * d.Y));
            }
            return 3000;
        }

        /// <summary>
        /// 計算牆體高度 (mm)
        /// </summary>
        public static double GetWallHeightMm(Wall wall)
        {
            if (wall == null) return 3000;
            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            if (bbox != null)
            {
                double hFt = bbox.Max.Z - bbox.Min.Z;
                if (hFt > 0.5) return FeetToMm(hFt);
            }
            Parameter hp = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (hp != null && hp.HasValue)
            {
                return FeetToMm(hp.AsDouble());
            }
            return 3000;
        }

        /// <summary>
        /// 計算以「整道牆面」為基準的正立面剖面 Transform 矩陣
        /// 中心點置於牆體之幾何 3D 正中心 (包含長度中點與高度中點)，適應任何牆類型與高度
        /// </summary>
        public static Transform CalculateWallElevationTransform(Wall wall)
        {
            LocationCurve locCurve = wall.Location as LocationCurve;
            XYZ wallNormal = wall.Orientation.Normalize();
            XYZ upVector = XYZ.BasisZ;
            XYZ rightVector = upVector.CrossProduct(wallNormal).Normalize();

            BoundingBoxXYZ bbox = wall.get_BoundingBox(null);
            double centerZ = 0;
            if (bbox != null)
            {
                centerZ = (bbox.Min.Z + bbox.Max.Z) * 0.5;
            }

            XYZ originXY = XYZ.Zero;
            if (locCurve != null && locCurve.Curve != null)
            {
                originXY = locCurve.Curve.Evaluate(0.5, true);
            }
            else if (bbox != null)
            {
                originXY = (bbox.Min + bbox.Max) * 0.5;
            }

            XYZ origin = new XYZ(originXY.X, originXY.Y, centerZ);

            Transform transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = rightVector;
            transform.BasisY = upVector;
            transform.BasisZ = wallNormal;

            return transform;
        }

        /// <summary>
        /// 計算牆面剖面圖裁切框 (BoundingBoxXYZ)
        /// 動態適應任何牆體厚度 (wall.Width)、長度與高度，確保多種尺寸之牆類型皆完整入鏡
        /// </summary>
        public static BoundingBoxXYZ CalculateFullWallBoundingBox(Wall wall, double offsetMm)
        {
            Transform transform = CalculateWallElevationTransform(wall);

            double wallLengthFt = MmToFeet(GetWallLengthMm(wall));
            double wallHeightFt = MmToFeet(GetWallHeightMm(wall));
            double wallThicknessFt = wall != null ? wall.Width : MmToFeet(200);

            double offsetFt = MmToFeet(offsetMm);
            double halfLength = (wallLengthFt / 2.0) + offsetFt;
            double halfHeight = (wallHeightFt / 2.0) + offsetFt;
            // 深度涵蓋該牆類型之厚度 + 前後各 500mm 視圖容差
            double halfDepth = (wallThicknessFt / 2.0) + MmToFeet(500);

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-halfLength, -halfHeight, -halfDepth);
            sectionBox.Max = new XYZ(halfLength, halfHeight, halfDepth);

            return sectionBox;
        }

        /// <summary>
        /// 計算以「開口元件」為中心的牆面正立面剖面裁切框 (BoundingBoxXYZ)
        /// 中心直接鎖定開口位置，並依出圖比例動態自適應寬度與高度上限 (確保 100% 納入 A3 左側工作區，絕不溢出圖紙)
        /// </summary>
        public static BoundingBoxXYZ CalculateOpeningWallSectionBoundingBox(Wall wall, FamilyInstance opening, int scale, double offsetMm)
        {
            if (opening == null)
            {
                return CalculateFullWallBoundingBox(wall, offsetMm);
            }

            LocationCurve locCurve = wall.Location as LocationCurve;
            Curve curve = locCurve != null ? locCurve.Curve : null;

            // 嚴格確保 wallNormal 屬於水平面單位向量
            XYZ wallOrientation = wall.Orientation;
            XYZ wallNormal = new XYZ(wallOrientation.X, wallOrientation.Y, 0).Normalize();
            if (wallNormal.IsZeroLength())
            {
                wallNormal = XYZ.BasisY;
            }

            XYZ upVector = XYZ.BasisZ;
            // 依右手法則嚴格建立正交基底：BasisX = rightVector, BasisY = upVector, BasisZ = wallNormal
            // rightVector x upVector = wallNormal，行列式值 strictly +1.0，保證正交且無反射
            XYZ rightVector = upVector.CrossProduct(wallNormal).Normalize();

            // 1. 取得開口位置
            XYZ opPos = XYZ.Zero;
            LocationPoint lp = opening.Location as LocationPoint;
            if (lp != null)
            {
                opPos = lp.Point;
            }
            else
            {
                BoundingBoxXYZ opBox = opening.get_BoundingBox(null);
                if (opBox != null) opPos = (opBox.Min + opBox.Max) * 0.5;
            }

            // 2. 開口投影至牆面中心線
            XYZ opCenterOnWall = opPos;
            double wallRightLimit = MmToFeet(2500);
            double wallLeftLimit = MmToFeet(2500);

            if (curve != null)
            {
                try
                {
                    IntersectionResult proj = curve.Project(opPos);
                    if (proj != null)
                    {
                        opCenterOnWall = proj.XYZPoint;
                    }

                    XYZ pt0 = curve.GetEndPoint(0);
                    XYZ pt1 = curve.GetEndPoint(1);
                    double proj0 = (pt0 - opCenterOnWall).DotProduct(rightVector);
                    double proj1 = (pt1 - opCenterOnWall).DotProduct(rightVector);

                    wallRightLimit = Math.Max(proj0, proj1);
                    wallLeftLimit = Math.Max(-proj0, -proj1);
                    if (wallRightLimit < 0) wallRightLimit = 0;
                    if (wallLeftLimit < 0) wallLeftLimit = 0;
                }
                catch
                {
                    wallRightLimit = MmToFeet(2500);
                    wallLeftLimit = MmToFeet(2500);
                }
            }
            else
            {
                double totalLen = MmToFeet(GetWallLengthMm(wall));
                wallRightLimit = totalLen * 0.5;
                wallLeftLimit = totalLen * 0.5;
            }

            // 牆體高度與垂直中心
            BoundingBoxXYZ wallBBox = wall.get_BoundingBox(null);
            double wallBottomZ = (wallBBox != null) ? wallBBox.Min.Z : (opPos.Z - MmToFeet(1500));
            double wallTopZ = (wallBBox != null) ? wallBBox.Max.Z : (opPos.Z + MmToFeet(1500));
            double wallHeightFt = wallTopZ - wallBottomZ;
            if (wallHeightFt < MmToFeet(2000)) wallHeightFt = MmToFeet(3000);

            // 垂直中心
            double centerZ = (wallBottomZ + wallTopZ) * 0.5;
            if (wallHeightFt > MmToFeet(5000))
            {
                centerZ = opPos.Z;
                wallHeightFt = MmToFeet(4000); // 針對超高長牆，限縮於開口上下各 2 米
            }

            // 3. 建立 Transform 矩陣 (保證右手法則正交，絕不引發 set_Transform 例外)
            XYZ origin = new XYZ(opCenterOnWall.X, opCenterOnWall.Y, centerZ);
            Transform transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = rightVector;
            transform.BasisY = upVector;
            transform.BasisZ = wallNormal;

            // 4. 計算水平裁切邊界 (依據 A3 左側工作區最大可用寬度約 190mm 自適應)
            int effectiveScale = scale > 0 ? scale : 30;
            double maxSectionWidthOnSheetFt = MmToFeet(190.0);
            double maxHalfLengthFt = (maxSectionWidthOnSheetFt * effectiveScale) / 2.0;

            double offsetFt = MmToFeet(offsetMm > 0 ? offsetMm : 300);

            // 若整道牆長度在工作區允許範圍內 (小於約 5 米)，則左右皆涵蓋至牆端點，完整呈現整道牆！
            // 若為超長牆體，則以開口為中心向左、向右各延伸至上限
            double leftExtent = Math.Min(wallLeftLimit + offsetFt, maxHalfLengthFt);
            double rightExtent = Math.Min(wallRightLimit + offsetFt, maxHalfLengthFt);

            // 確保開口兩側至少留有基本背景 (至少各 1.0 米，除非牆端點已達)
            double minSideBuffer = MmToFeet(1000);
            if (wallLeftLimit >= minSideBuffer && leftExtent < minSideBuffer) leftExtent = minSideBuffer;
            if (wallRightLimit >= minSideBuffer && rightExtent < minSideBuffer) rightExtent = minSideBuffer;
            if (leftExtent < offsetFt) leftExtent = offsetFt;
            if (rightExtent < offsetFt) rightExtent = offsetFt;

            // 5. 計算垂直裁切邊界 (A3 高度上限約 220mm)
            double maxSectionHeightOnSheetFt = MmToFeet(220.0);
            double maxHalfHeightFt = (maxSectionHeightOnSheetFt * effectiveScale) / 2.0;
            double halfHeight = Math.Min((wallHeightFt / 2.0) + offsetFt, maxHalfHeightFt);
            if (halfHeight < offsetFt) halfHeight = offsetFt;

            // 深度涵蓋該牆類型之厚度 + 前後各 500mm 視圖容差
            double wallThicknessFt = wall.Width > 0 ? wall.Width : MmToFeet(200);
            double halfDepth = (wallThicknessFt / 2.0) + MmToFeet(500);

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ();
            sectionBox.Transform = transform;
            sectionBox.Min = new XYZ(-leftExtent, -halfHeight, -halfDepth);
            sectionBox.Max = new XYZ(rightExtent, halfHeight, halfDepth);

            return sectionBox;
        }

        /// <summary>
        /// 精確取得開口元件的尺寸 (寬、高，單位 mm)
        /// 完整支援多種開孔規格 (如「牆開孔 - 35x35cm」、「牆開孔 - 50x10.4cm」、「牆開孔 - 117.5x20cm」等)
        /// </summary>
        public static OpeningDimensionInfo GetOpeningDimensionsMm(FamilyInstance opening)
        {
            if (opening == null)
            {
                return new OpeningDimensionInfo(300, 300);
            }

            string typeName = opening.Symbol != null ? opening.Symbol.Name : string.Empty;

            // 1. 優先精準解析類型名稱中的尺寸標示 (例如: "牆開孔 - 35x35cm", "50x10.4cm", "117.5x20cm")
            Match match = Regex.Match(typeName, @"(\d+(?:\.\d+)?)\s*[xX*×]\s*(\d+(?:\.\d+)?)\s*(cm|mm)?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                double w = double.Parse(match.Groups[1].Value);
                double h = double.Parse(match.Groups[2].Value);
                string unit = match.Groups[3].Value.ToLower();

                if (unit == "mm")
                {
                    return new OpeningDimensionInfo(w, h);
                }
                else
                {
                    // 預設為 cm (如 35x35cm -> 350x350 mm, 50x10.4cm -> 500x104 mm)
                    return new OpeningDimensionInfo(w * 10.0, h * 10.0);
                }
            }

            // 2. 搜尋族群類型參數
            ElementType type = opening.Document.GetElement(opening.GetTypeId()) as ElementType;
            if (type != null)
            {
                Parameter pw = type.get_Parameter(BuiltInParameter.WINDOW_WIDTH)
                               ?? type.get_Parameter(BuiltInParameter.DOOR_WIDTH)
                               ?? type.get_Parameter(BuiltInParameter.GENERIC_WIDTH)
                               ?? type.LookupParameter("寬度")
                               ?? type.LookupParameter("寬")
                               ?? type.LookupParameter("百葉寬度");

                Parameter ph = type.get_Parameter(BuiltInParameter.WINDOW_HEIGHT)
                               ?? type.get_Parameter(BuiltInParameter.DOOR_HEIGHT)
                               ?? type.get_Parameter(BuiltInParameter.GENERIC_HEIGHT)
                               ?? type.LookupParameter("高度")
                               ?? type.LookupParameter("高")
                               ?? type.LookupParameter("百葉總高")
                               ?? type.LookupParameter("百葉高度");

                if (pw != null && pw.HasValue && ph != null && ph.HasValue)
                {
                    return new OpeningDimensionInfo(FeetToMm(pw.AsDouble()), FeetToMm(ph.AsDouble()));
                }
            }

            // 3. 搜尋實體 BoundingBox
            BoundingBoxXYZ bbox = opening.get_BoundingBox(null);
            if (bbox != null)
            {
                XYZ diff = bbox.Max - bbox.Min;
                double approxW = FeetToMm(Math.Sqrt(diff.X * diff.X + diff.Y * diff.Y));
                double approxH = FeetToMm(diff.Z);
                if (approxW > 50 && approxH > 50)
                {
                    return new OpeningDimensionInfo(approxW, approxH);
                }
            }

            return new OpeningDimensionInfo(300, 300);
        }

        /// <summary>
        /// 取得開口元件的窗台/底面高度 (Sill Height，單位 mm)
        /// </summary>
        public static double GetOpeningSillHeightMm(FamilyInstance opening)
        {
            if (opening == null) return 0;

            Parameter sillParam = opening.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)
                                  ?? opening.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM)
                                  ?? opening.LookupParameter("窗台高度")
                                  ?? opening.LookupParameter("底層偏移")
                                  ?? opening.LookupParameter("百葉高程");

            if (sillParam != null && sillParam.HasValue)
            {
                return FeetToMm(sillParam.AsDouble());
            }

            return 0;
        }
    }
}
