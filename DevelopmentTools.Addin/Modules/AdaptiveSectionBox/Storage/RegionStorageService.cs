using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System.Text.Json;

namespace DevelopmentTools.Modules.AdaptiveSectionBox.Storage
{
    public class RegionStorageService
    {
        private static RegionStorageService _instance;
        public static RegionStorageService Instance => _instance ?? (_instance = new RegionStorageService());

        private readonly Guid _schemaGuid = new Guid("31d3d8b2-5706-4d1e-8422-772b8ef99fef");
        private Schema _schema;

        private RegionStorageService()
        {
            InitializeSchema();
        }

        private void InitializeSchema()
        {
            _schema = Schema.Lookup(_schemaGuid);
            if (_schema == null)
            {
                var builder = new SchemaBuilder(_schemaGuid);
                builder.SetReadAccessLevel(AccessLevel.Public);
                builder.SetWriteAccessLevel(AccessLevel.Public);
                builder.SetSchemaName("AdaptiveRegionsStorage");
                builder.SetDocumentation("用於儲存智慧多邊形剖面盒的空間坐標點資訊。");

                builder.AddSimpleField("JsonData", typeof(string));
                _schema = builder.Finish();
            }
        }

        public List<Models.AdaptiveRegion> LoadRegions(Document doc)
        {
            var list = new List<Models.AdaptiveRegion>();
            if (doc == null || _schema == null) return list;

            try
            {
                var info = new FilteredElementCollector(doc)
                    .OfClass(typeof(ProjectInfo))
                    .FirstOrDefault();

                if (info != null)
                {
                    var entity = info.GetEntity(_schema);
                    if (entity.IsValid())
                    {
                        string json = entity.Get<string>("JsonData");
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var loaded = JsonSerializer.Deserialize<List<Models.AdaptiveRegion>>(json);
                            if (loaded != null)
                            {
                                list = loaded;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegionStorage] Load 失敗: {ex.Message}");
            }
            return list;
        }

        public void SaveRegions(Document doc, List<Models.AdaptiveRegion> regions)
        {
            if (doc == null || _schema == null) return;

            // 在外部調用時，必須確保是在一個合法的 Revit Transaction 內執行。
            try
            {
                var info = new FilteredElementCollector(doc)
                    .OfClass(typeof(ProjectInfo))
                    .FirstOrDefault();

                if (info != null)
                {
                    string json = JsonSerializer.Serialize(regions);
                    var entity = new Entity(_schema);
                    entity.Set("JsonData", json);
                    info.SetEntity(entity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegionStorage] Save 失敗: {ex.Message}");
            }
        }
    }
}
