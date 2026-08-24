using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DevelopmentTools.Core
{
    public static class ViewAnnotationStorageManager
    {
        private static readonly Guid SchemaGuid = new Guid("7C9A4F88-1CE3-4318-A785-5FA2147D3B5F");

        public static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("ViewAnnotationLogicSchema");
            builder.AddSimpleField("AnnotationConfigJson", typeof(string));

            return builder.Finish();
        }

        // 儲存或更新標註邏輯設定
        public static void SaveConfig(View view, string json)
        {
            Schema schema = GetOrCreateSchema();
            Entity entity = new Entity(schema);
            entity.Set("AnnotationConfigJson", json);
            view.SetEntity(entity);
        }

        // 讀取標註邏輯設定
        public static string LoadConfig(View view)
        {
            Schema schema = GetOrCreateSchema();
            if (schema == null) return string.Empty;

            Entity entity = view.GetEntity(schema);
            if (entity != null && entity.IsValid())
            {
                return entity.Get<string>("AnnotationConfigJson");
            }
            return string.Empty;
        }

        // 刪除標註邏輯設定
        public static void RemoveConfig(View view)
        {
            Schema schema = GetOrCreateSchema();
            if (schema != null)
            {
                view.DeleteEntity(schema);
            }
        }
    }
}
