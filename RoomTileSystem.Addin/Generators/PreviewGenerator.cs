using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RoomTileSystem.Core;

namespace RoomTileSystem.Generators
{
    public class PreviewGenerator
    {
        private Document _doc;

        public PreviewGenerator(Document doc)
        {
            _doc = doc;
        }

        // 清除專案中與特定 Anchor 相關的所有預覽線群組
        public void ClearPreviewLines(string anchorId)
        {
            FilteredElementCollector collector = new FilteredElementCollector(_doc);
            ICollection<Element> groups = collector.OfClass(typeof(Group)).ToElements();

            List<ElementId> toDelete = new List<ElementId>();
            foreach (Element elem in groups)
            {
                if (elem is Group g && g.GroupType.Name.Equals($"TilePreview_Group_{anchorId}", StringComparison.OrdinalIgnoreCase))
                {
                    toDelete.Add(g.Id);
                }
            }

            if (toDelete.Count > 0)
            {
                try
                {
                    _doc.Delete(toDelete);
                }
                catch
                {
                    // 忽略刪除失敗的異常
                }
            }
        }

        // 生成 Model Line 預覽並將其群組
        public Group GenerateModelLinePreview(List<TileData> tiles, string anchorId)
        {
            // 先清除舊有群組
            ClearPreviewLines(anchorId);

            List<ElementId> curveIds = new List<ElementId>();

            // 快取草圖平面，避免重複建立相同的 SketchPlane
            Dictionary<string, SketchPlane> sketchPlaneCache = new Dictionary<string, SketchPlane>();

            foreach (TileData tile in tiles)
            {
                int n = tile.XYZBoundary.Count;
                if (n < 3) continue;

                // 取得法向，以便將模型線稍微往外偏置 1 mm (0.003 呎) 避免被地坪/牆體實體遮擋
                XYZ norm = new XYZ(tile.Normal.X, tile.Normal.Y, tile.Normal.Z).Normalize();
                XYZ offset = norm * 0.003;

                // 1. 取得或建立磁磚面對應的 SketchPlane
                string cacheKey = $"{tile.Surface_ID}_{tile.Normal.X:F3}_{tile.Normal.Y:F3}_{tile.Normal.Z:F3}";
                SketchPlane sketchPlane = null;

                if (sketchPlaneCache.ContainsKey(cacheKey))
                {
                    sketchPlane = sketchPlaneCache[cacheKey];
                }
                else
                {
                    XYZ tileNormal = norm;
                    XYZ originPt = new XYZ(tile.XYZBoundary[0].X, tile.XYZBoundary[0].Y, tile.XYZBoundary[0].Z) + offset;
                    
                    Plane plane = Plane.CreateByNormalAndOrigin(tileNormal, originPt);
                    sketchPlane = SketchPlane.Create(_doc, plane);
                    sketchPlaneCache[cacheKey] = sketchPlane;
                }

                // 2. 繪製該磁磚邊界的所有 3D 模型線
                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    XYZ ptStart = new XYZ(tile.XYZBoundary[i].X, tile.XYZBoundary[i].Y, tile.XYZBoundary[i].Z) + offset;
                    XYZ ptEnd = new XYZ(tile.XYZBoundary[next].X, tile.XYZBoundary[next].Y, tile.XYZBoundary[next].Z) + offset;

                    // 確保兩點不重合
                    if (ptStart.IsAlmostEqualTo(ptEnd)) continue;

                    try
                    {
                        Line line = Line.CreateBound(ptStart, ptEnd);
                        ModelCurve mCurve = _doc.Create.NewModelCurve(line, sketchPlane);
                        
                        // 寫入 Comments 後設資料
                        Parameter commentParam = mCurve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (commentParam != null)
                        {
                            commentParam.Set($"TilePreview:{anchorId}|Tile_ID:{tile.Tile_ID}|Type:{tile.Tile_Type}");
                        }

                        curveIds.Add(mCurve.Id);
                    }
                    catch
                    {
                        // 避免微小精度誤差引起 Revit Curve Creation 異常而中斷整個排版
                    }
                }
            }

            // 3. 將所有生成的模型線進行 Group 群組化
            if (curveIds.Count > 0)
            {
                Group previewGroup = _doc.Create.NewGroup(curveIds);
                try
                {
                    // 先檢查專案中是否已有同名的 GroupType，若有則將該舊的且未使用的 GroupType 刪除
                    FilteredElementCollector typeCollector = new FilteredElementCollector(_doc);
                    ICollection<Element> groupTypes = typeCollector.OfClass(typeof(GroupType)).ToElements();
                    
                    string targetTypeName = $"TilePreview_Group_{anchorId}";
                    foreach (Element typeElem in groupTypes)
                    {
                        if (typeElem.Name.Equals(targetTypeName, StringComparison.OrdinalIgnoreCase))
                        {
                            _doc.Delete(typeElem.Id);
                            break;
                        }
                    }

                    previewGroup.GroupType.Name = targetTypeName;
                }
                catch
                {
                    // 忽略命名衝突或唯讀限制
                }
                return previewGroup;
            }

            return null;
        }
    }
}
