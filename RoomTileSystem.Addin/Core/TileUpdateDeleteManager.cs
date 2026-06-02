using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RoomTileSystem.Core
{
    public static class TileUpdateDeleteManager
    {
        // 刪除與該 Anchor_ID 關聯的所有 DirectShape 磁磚
        public static void DeleteExistingTiles(Document doc, string anchorId)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ICollection<Element> directShapes = collector
                .OfClass(typeof(DirectShape))
                .ToElements();

            List<ElementId> toDelete = new List<ElementId>();

            foreach (Element elem in directShapes)
            {
                Parameter commentParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (commentParam != null)
                {
                    string comment = commentParam.AsString();
                    if (comment != null && comment.Contains($"Anchor_ID:{anchorId}"))
                    {
                        toDelete.Add(elem.Id);
                    }
                }
            }

            if (toDelete.Count > 0)
            {
                doc.Delete(toDelete);
            }
        }
    }
}
