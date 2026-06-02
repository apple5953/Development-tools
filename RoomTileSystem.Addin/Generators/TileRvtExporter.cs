using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RoomTileSystem.Core;

namespace RoomTileSystem.Generators
{
    public class TileRvtExporter
    {
        private Autodesk.Revit.ApplicationServices.Application _app;

        public TileRvtExporter(Autodesk.Revit.ApplicationServices.Application app)
        {
            _app = app;
        }

        // 導出磁磚幾何至獨立 RVT 並連結回主模型
        public string ExportTilesToNewRvt(Document mainDoc, string anchorId, List<TileData> tiles, string exportFolder)
        {
            if (!System.IO.Directory.Exists(exportFolder))
            {
                System.IO.Directory.CreateDirectory(exportFolder);
            }

            string newFilePath = System.IO.Path.Combine(exportFolder, $"{anchorId}_Tiles.rvt");

            // 1. 建立一個全新的公制專案檔案
            Document newDoc = _app.NewProjectDocument(UnitSystem.Metric);

            using (Transaction trans = new Transaction(newDoc, "Generate Exported Tiles"))
            {
                trans.Start();

                GeometryGenerator generator = new GeometryGenerator(newDoc);
                // 建立預設的磁磚材質，並在導出專案中使用
                ElementId tileMatId = generator.GetOrCreateTileMaterial("Tile_Export_Material", new Color(200, 200, 200));

                foreach (TileData tile in tiles)
                {
                    generator.GenerateTileSolid(tile, tileMatId);
                }

                trans.Commit();
            }

            // 2. 儲存並關閉新專案
            SaveAsOptions options = new SaveAsOptions { OverwriteExistingFile = true };
            newDoc.SaveAs(newFilePath, options);
            newDoc.Close(false);

            // 3. 將新專案連結回主模型 (需開啟 transaction)
            using (Transaction mainTrans = new Transaction(mainDoc, "Link Tile RVT"))
            {
                mainTrans.Start();

                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(newFilePath);
                RevitLinkOptions rvtOptions = new RevitLinkOptions(false);
                
                // 建立 Revit 連結類型
                LinkLoadResult loadResult = RevitLinkType.Create(mainDoc, modelPath, rvtOptions);
                
                // 建立連結實例 (放置在原點，因為磁磚在導出專案與主模型都使用相同的全域座標空間)
                RevitLinkInstance.Create(mainDoc, loadResult.ElementId);

                mainTrans.Commit();
            }

            return newFilePath;
        }
    }
}
