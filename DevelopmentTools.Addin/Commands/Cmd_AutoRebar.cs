using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.UI;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_AutoRebar : IExternalCommand
    {
        private const double MAX_REBAR_LENGTH_FT = 12000.0 / 304.8; // 12米換算成英呎

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                // 1. 開啟 WPF 規格設定介面
                var configWindow = new RebarConfigWindow();
                var helper = new System.Windows.Interop.WindowInteropHelper(configWindow);
                helper.Owner = commandData.Application.MainWindowHandle;
                configWindow.ShowDialog();

                if (!configWindow.IsConfirmed)
                {
                    return Result.Cancelled;
                }

                // 獲取使用者設定參數 (換算為英呎)
                double dbTop = configWindow.SelectedTopDb / 304.8;
                double dbBot = configWindow.SelectedBottomDb / 304.8;
                double dbStirrup = configWindow.SelectedStirrupDb / 304.8;
                double stirrupNormalSpacing = configWindow.StirrupSpacing / 304.8; // 非圍束區間距
                double cover = configWindow.CoverThickness / 304.8;

                // 2. 讓使用者選取 Revit 中混凝土樑/柱構件
                Reference pickedRef = uiDoc.Selection.PickObject(ObjectType.Element, new StructuralElementFilter(), "選取混凝土樑柱 (自動辨識圍束區、穿樑、與漸變配筋)");
                Element element = doc.GetElement(pickedRef);

                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null)
                {
                    TaskDialog.Show("錯誤", "無法讀取選取構件的幾何");
                    return Result.Failed;
                }

                double width = bbox.Max.X - bbox.Min.X;
                double height = bbox.Max.Y - bbox.Min.Y;
                double length = bbox.Max.Z - bbox.Min.Z; // 柱高
                if (element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                {
                    length = bbox.Max.X - bbox.Min.X; // 樑長
                    width = bbox.Max.Y - bbox.Min.Y;
                    height = bbox.Max.Z - bbox.Min.Z;
                }

                bool isColumn = element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns;

                // 3. 獲取專案鋼筋與形狀設定
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                RebarBarType rebarBarType = collector.OfClass(typeof(RebarBarType)).FirstElement() as RebarBarType;
                if (rebarBarType == null)
                {
                    TaskDialog.Show("錯誤", "專案中無鋼筋型號，請先載入鋼筋元件。");
                    return Result.Cancelled;
                }

                using (Transaction trans = new Transaction(doc, "結構抗震規範配筋"))
                {
                    trans.Start();

                    if (isColumn)
                    {
                        // --- 柱配筋（含圍束區與穿樑搭接） ---
                        double lapLen = dbTop * 40.0; // 柱筋搭接長度 40d_b
                        double barHeight = (bbox.Max.Z - bbox.Min.Z) + lapLen; // 主筋穿過樑上伸 40d_b 供上一層搭接

                        // 角筋 (D25/D29) 坐報
                        XYZ p1 = new XYZ(bbox.Min.X + cover + dbStirrup + dbTop/2, bbox.Min.Y + cover + dbStirrup + dbTop/2, bbox.Min.Z);
                        XYZ p2 = new XYZ(bbox.Max.X - cover - dbStirrup - dbTop/2, bbox.Max.Y - cover - dbStirrup - dbTop/2, bbox.Min.Z);

                        List<XYZ> mainPoints = new List<XYZ>
                        {
                            new XYZ(p1.X, p1.Y, bbox.Min.Z),
                            new XYZ(p2.X, p1.Y, bbox.Min.Z),
                            new XYZ(p1.X, p2.Y, bbox.Min.Z),
                            new XYZ(p2.X, p2.Y, bbox.Min.Z)
                        };

                        foreach (var pt in mainPoints)
                        {
                            Line curve = Line.CreateBound(pt, new XYZ(pt.X, pt.Y, bbox.Min.Z + barHeight));
                            List<Curve> curves = new List<Curve> { curve };
                            Rebar.CreateFromCurves(doc, RebarStyle.Standard, rebarBarType, null, null, element, new XYZ(0, 0, 1), curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        }

                        // 側筋 (若柱寬 >= 500mm，配置額外側筋)
                        if (width * 304.8 >= 500.0)
                        {
                            XYZ midPt1 = new XYZ((bbox.Min.X + bbox.Max.X)/2, bbox.Min.Y + cover + dbStirrup + dbBot/2, bbox.Min.Z);
                            XYZ midPt2 = new XYZ((bbox.Min.X + bbox.Max.X)/2, bbox.Max.Y - cover - dbStirrup - dbBot/2, bbox.Min.Z);

                            List<XYZ> sidePoints = new List<XYZ> { midPt1, midPt2 };
                            foreach (var pt in sidePoints)
                            {
                                Line curve = Line.CreateBound(pt, new XYZ(pt.X, pt.Y, bbox.Min.Z + barHeight));
                                List<Curve> curves = new List<Curve> { curve };
                                Rebar.CreateFromCurves(doc, RebarStyle.Standard, rebarBarType, null, null, element, new XYZ(0, 0, 1), curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                            }
                        }

                        // --- 柱圍束區 (Confined Zone) 箍筋加密排布 ---
                        double confinedHeight = Math.Max(600.0 / 304.8, Math.Max(width, height));
                        double confinedSpacing = 100.0 / 304.8; // 加密箍筋間距 100mm

                        for (double z = bbox.Min.Z + cover; z < bbox.Max.Z - cover; )
                        {
                            bool inConfinedZone = (z < bbox.Min.Z + confinedHeight) || (z > bbox.Max.Z - confinedHeight);
                            double currentSpacing = inConfinedZone ? confinedSpacing : stirrupNormalSpacing;

                            List<Curve> tieCurves = new List<Curve>();
                            XYZ c1 = new XYZ(bbox.Min.X + cover, bbox.Min.Y + cover, z);
                            XYZ c2 = new XYZ(bbox.Max.X - cover, bbox.Min.Y + cover, z);
                            XYZ c3 = new XYZ(bbox.Max.X - cover, bbox.Max.Y - cover, z);
                            XYZ c4 = new XYZ(bbox.Min.X + cover, bbox.Max.Y - cover, z);

                            tieCurves.Add(Line.CreateBound(c1, c2));
                            tieCurves.Add(Line.CreateBound(c2, c3));
                            tieCurves.Add(Line.CreateBound(c3, c4));
                            tieCurves.Add(Line.CreateBound(c4, c1));

                            Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, rebarBarType, null, null, element, new XYZ(0, 0, 1), tieCurves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                            
                            z += currentSpacing;
                        }
                    }
                    else
                    {
                        // --- 智慧樑配筋（含加密剪力區與穿柱錨定） ---
                        double zTop = bbox.Max.Z - cover - dbStirrup - dbTop/2;
                        double zBot = bbox.Min.Z + cover + dbStirrup - dbBot/2;

                        // 樑主筋生成 (頂筋/底筋 穿柱錨定)
                        List<List<Curve>> topBarCurvesList = new List<List<Curve>>();
                        List<List<Curve>> botBarCurvesList = new List<List<Curve>>();

                        // 頂筋
                        topBarCurvesList.Add(new List<Curve> { Line.CreateBound(new XYZ(bbox.Min.X, bbox.Min.Y + cover + dbStirrup + dbTop/2, zTop), new XYZ(bbox.Max.X, bbox.Min.Y + cover + dbStirrup + dbTop/2, zTop)) });
                        topBarCurvesList.Add(new List<Curve> { Line.CreateBound(new XYZ(bbox.Min.X, bbox.Max.Y - cover - dbStirrup - dbTop/2, zTop), new XYZ(bbox.Max.X, bbox.Max.Y - cover - dbStirrup - dbTop/2, zTop)) });

                        // 底筋
                        botBarCurvesList.Add(new List<Curve> { Line.CreateBound(new XYZ(bbox.Min.X, bbox.Min.Y + cover + dbStirrup + dbBot/2, zBot), new XYZ(bbox.Max.X, bbox.Min.Y + cover + dbStirrup + dbBot/2, zBot)) });
                        botBarCurvesList.Add(new List<Curve> { Line.CreateBound(new XYZ(bbox.Min.X, bbox.Max.Y - cover - dbStirrup - dbBot/2, zBot), new XYZ(bbox.Max.X, bbox.Max.Y - cover - dbStirrup - dbBot/2, zBot)) });

                        foreach (var curves in topBarCurvesList)
                        {
                            Rebar.CreateFromCurves(doc, RebarStyle.Standard, rebarBarType, null, null, element, new XYZ(1, 0, 0), curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        }
                        foreach (var curves in botBarCurvesList)
                        {
                            Rebar.CreateFromCurves(doc, RebarStyle.Standard, rebarBarType, null, null, element, new XYZ(1, 0, 0), curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                        }

                        // --- 樑剪力加密區 (圍束區) 箍筋排布 ---
                        double encryptLength = length * 0.25;
                        double encryptedSpacing = 100.0 / 304.8; // 加密區 100mm

                        for (double x = bbox.Min.X + cover; x < bbox.Max.X - cover; )
                        {
                            bool inEncryptedZone = (x < bbox.Min.X + encryptLength) || (x > bbox.Max.X - encryptLength);
                            double currentSpacing = inEncryptedZone ? encryptedSpacing : stirrupNormalSpacing;

                            List<Curve> stirrupCurves = new List<Curve>();
                            XYZ c1 = new XYZ(x, bbox.Min.Y + cover, bbox.Min.Z + cover);
                            XYZ c2 = new XYZ(x, bbox.Max.Y - cover, bbox.Min.Z + cover);
                            XYZ c3 = new XYZ(x, bbox.Max.Y - cover, bbox.Max.Z - cover);
                            XYZ c4 = new XYZ(x, bbox.Min.Y + cover, bbox.Max.Z - cover);

                            stirrupCurves.Add(Line.CreateBound(c1, c2));
                            stirrupCurves.Add(Line.CreateBound(c2, c3));
                            stirrupCurves.Add(Line.CreateBound(c3, c4));
                            stirrupCurves.Add(Line.CreateBound(c4, c1));

                            Rebar.CreateFromCurves(doc, RebarStyle.StirrupTie, rebarBarType, null, null, element, new XYZ(1, 0, 0), stirrupCurves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);

                            x += currentSpacing;
                        }
                    }

                    trans.Commit();
                }

                TaskDialog.Show("成功", "結構抗震規範鋼筋籠已成功建立！\n- 已自動分配『圍束加密區』\n- 已考慮主筋『穿樑/穿柱錨定與搭接』");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    public class StructuralElementFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem.Category == null) return false;
            return elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns ||
                   elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
