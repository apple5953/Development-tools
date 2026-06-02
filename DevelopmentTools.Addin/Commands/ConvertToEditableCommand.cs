using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DevelopmentTools.Generators;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConvertToEditableCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData, 
            ref string message, 
            ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            return RunConvert(uiapp, uidoc, doc, ref message);
        }

        public static Result RunConvert(
            UIApplication uiapp, 
            UIDocument uidoc, 
            Document doc, 
            ref string message)
        {
            if (doc.IsFamilyDocument)
            {
                TaskDialog.Show("可編輯轉換", "此功能只能在專案環境（Project）中執行。");
                return Result.Cancelled;
            }

            try
            {
                // 1. 引導選取瓷磚元件
                Reference pickedRef = null;
                try
                {
                    pickedRef = uidoc.Selection.PickObject(
                        ObjectType.Element, 
                        new TileSelectionFilter(), 
                        "請選擇要轉換為可編輯的原生磁磚元件..."
                    );
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (pickedRef == null) return Result.Cancelled;

                Element tileElem = doc.GetElement(pickedRef.ElementId);
                if (tileElem == null) return Result.Failed;

                // 若本來就是 Wall 或 Floor，就已經是原生可編輯的
                if (tileElem is Wall || tileElem is Floor)
                {
                    TaskDialog.Show("提示", "該磁磚已是原生 Walls / Floors 元件。\n\n你可以在 Revit 中直接雙擊它（或點選編輯輪廓 / 編輯邊界）隨時進行修改，不需透過外掛轉換。");
                    return Result.Succeeded;
                }

                // 2. 讀取並解析頂點數據與 Metadata
                Parameter commentParam = tileElem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentParam == null || string.IsNullOrEmpty(commentParam.AsString()))
                {
                    TaskDialog.Show("可編輯轉換", "選取的元件沒有有效的磁磚中繼資料。");
                    return Result.Failed;
                }

                string metadata = commentParam.AsString();
                string boundaryKey = "BoundaryPoints:";
                int keyIdx = metadata.IndexOf(boundaryKey);
                if (keyIdx == -1)
                {
                    TaskDialog.Show("可編輯轉換", "無法在此元件中找到頂點邊界資料。");
                    return Result.Failed;
                }

                string ptsStr = metadata.Substring(keyIdx + boundaryKey.Length);
                if (ptsStr.Contains("|"))
                {
                    ptsStr = ptsStr.Substring(0, ptsStr.IndexOf('|'));
                }

                List<XYZ> pts = new List<XYZ>();
                string[] ptTokens = ptsStr.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in ptTokens)
                {
                    string[] coords = token.Split(',');
                    if (coords.Length == 3)
                    {
                        if (double.TryParse(coords[0], out double x) &&
                            double.TryParse(coords[1], out double y) &&
                            double.TryParse(coords[2], out double z))
                        {
                            pts.Add(new XYZ(x, y, z));
                        }
                    }
                }

                if (pts.Count < 3)
                {
                    TaskDialog.Show("可編輯轉換", "解析出的頂點數量少於 3 個，無法構成多邊形。");
                    return Result.Failed;
                }

                string tileId = ParseMetadataValue(metadata, "Tile_ID");
                string anchorId = ParseMetadataValue(metadata, "Anchor_ID");
                string roomId = ParseMetadataValue(metadata, "Room_ID");
                string surfaceId = ParseMetadataValue(metadata, "Surface_ID");
                string tileType = ParseMetadataValue(metadata, "Type");
                string hostId = ParseMetadataValue(metadata, "Host_ID");
                
                double area = 0.0;
                string areaStr = ParseMetadataValue(metadata, "Area");
                double.TryParse(areaStr, out area);

                // 讀取厚度與材質
                double thicknessMm = 10.0;
                Parameter thicknessParam = tileElem.LookupParameter("Tile_Thickness");
                if (thicknessParam != null)
                {
                    thicknessMm = thicknessParam.AsDouble() * 304.8;
                }

                string matName = "DefaultTileMat";
                Parameter matParam = tileElem.LookupParameter("Tile_Material");
                if (matParam != null && matParam.HasValue)
                {
                    matName = matParam.AsString();
                }

                // 取得法向
                XYZ normal = CalculatePolygonNormal(pts);

                // 3. 執行就地轉換
                Element newNativeElem = null;
                using (TransactionGroup tg = new TransactionGroup(doc, "磁磚轉換為原生可編輯"))
                {
                    tg.Start();

                    GeometryGenerator geomGen = new GeometryGenerator(doc);
                    ElementId matId = GetMaterialIdByName(doc, matName);

                    newNativeElem = geomGen.ConvertTileToEditableNative(
                        tileElem, pts, normal, thicknessMm, matId, matName, 
                        tileType, tileId, anchorId, roomId, surfaceId, hostId, area
                    );

                    if (newNativeElem != null)
                    {
                        using (Transaction t = new Transaction(doc, "刪除舊有幾何"))
                        {
                            t.Start();
                            doc.Delete(tileElem.Id);
                            t.Commit();
                        }
                        tg.Assimilate();
                    }
                    else
                    {
                        tg.RollBack();
                    }
                }

                if (newNativeElem != null)
                {
                    // 自動選中新建立的原生元件，引導使用者 Edit Profile
                    uidoc.Selection.SetElementIds(new List<ElementId> { newNativeElem.Id });
                    TaskDialog.Show("轉換成功", "磁磚已成功轉換為原生 Walls / Floors 元件。\n\n你現在可以直接雙擊它（或點選編輯輪廓 / 編輯邊界）隨時進行修改，修改將永久保留！");
                    return Result.Succeeded;
                }
                else
                {
                    TaskDialog.Show("轉換失敗", "無法建立原生元件，轉換已取消。");
                    return Result.Failed;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("可編輯轉換", $"轉換出錯: {ex.Message}\n{ex.StackTrace}");
                return Result.Failed;
            }
        }

        #region Helpers

        private static string ParseMetadataValue(string metadata, string key)
        {
            if (string.IsNullOrEmpty(metadata)) return "";
            string[] pairs = metadata.Split('|');
            foreach (string pair in pairs)
            {
                string[] kv = pair.Split(':');
                if (kv.Length >= 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Join(":", kv.Skip(1));
                }
            }
            return "";
        }

        private static XYZ CalculatePolygonNormal(List<XYZ> pts)
        {
            if (pts.Count < 3) return XYZ.BasisZ;
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                XYZ cur = pts[i];
                XYZ next = pts[(i + 1) % pts.Count];
                x += (cur.Y - next.Y) * (cur.Z + next.Z);
                y += (cur.Z - next.Z) * (cur.X + next.X);
                z += (cur.X - next.X) * (cur.Y + next.Y);
            }
            XYZ normal = new XYZ(x, y, z);
            if (normal.GetLength() < 0.001) return XYZ.BasisZ;
            return normal.Normalize();
        }

        private static ElementId GetMaterialIdByName(Document doc, string name)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Material));
            foreach (Material mat in collector)
            {
                if (mat.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return mat.Id;
            }
            return ElementId.InvalidElementId;
        }

        #endregion
    }

    public class TileSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem is DirectShape || elem is Wall || elem is Floor)
            {
                Parameter commentParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentParam != null && commentParam.HasValue)
                {
                    string comment = commentParam.AsString();
                    if (comment.Contains("Tile_ID:") && comment.Contains("BoundaryPoints:"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }
}
