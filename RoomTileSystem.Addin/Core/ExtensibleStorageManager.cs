using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace RoomTileSystem.Core
{
    public static class ExtensibleStorageManager
    {
        private static readonly Guid SchemaGuid = new Guid("4A7E3E52-87C1-4D6F-BF42-5FA2147D3B51");

        public static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("RoomTileLayoutSchema");
            builder.AddSimpleField("LayoutDataJson", typeof(string));

            return builder.Finish();
        }

        // 將排版資料序列化並寫入元素 (通常為 RoomTileCoordinate 的 FamilyInstance)
        public static void SaveLayoutData(Element element, string json)
        {
            Schema schema = GetOrCreateSchema();
            Entity entity = new Entity(schema);
            entity.Set("LayoutDataJson", json);
            element.SetEntity(entity);
        }

        // 自元素讀取排版資料 JSON
        public static string LoadLayoutData(Element element)
        {
            Schema schema = GetOrCreateSchema();
            if (schema == null) return string.Empty;

            Entity entity = element.GetEntity(schema);
            if (entity != null && entity.IsValid())
            {
                return entity.Get<string>("LayoutDataJson");
            }
            return string.Empty;
        }
    }
}
