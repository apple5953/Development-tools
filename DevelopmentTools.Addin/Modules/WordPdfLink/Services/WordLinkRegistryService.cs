using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DT_WordPdfLink.Models;
using DT_WordPdfLink.Storage;
using DT_WordPdfLink.Utils;

namespace DT_WordPdfLink.Services
{
    public static class WordLinkRegistryService
    {
        private static readonly Guid SchemaGuid = new Guid("c8d76a21-9a74-4b53-a7c8-4720938221bb");
        private static Schema _schema;

        private static Schema GetOrCreateSchema()
        {
            if (_schema != null) return _schema;

            _schema = Schema.Lookup(SchemaGuid);
            if (_schema != null) return _schema;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("WordLinkRegistrySchema");
            builder.SetDocumentation("Registry mapping for Revit Word to PDF Linking Tool.");
            
            // Add a simple string field to store our serialized JSON registry
            builder.AddSimpleField("JsonData", typeof(string));
            _schema = builder.Finish();

            return _schema;
        }

        public static WordLinkRegistry LoadRegistry(Document doc)
        {
            try
            {
                Schema schema = GetOrCreateSchema();
                DataStorage storage = FindDataStorage(doc, schema);
                if (storage != null)
                {
                    Entity entity = storage.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        string json = entity.Get<string>("JsonData");
                        if (!string.IsNullOrEmpty(json))
                        {
                            WordLinkRegistry registry = JsonSerializer.Deserialize<WordLinkRegistry>(json);
                            if (registry != null && registry.Links != null)
                            {
                                return registry;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load WordLinkRegistry from Extensible Storage", ex);
            }

            return new WordLinkRegistry(); // Return empty registry on failure or if not exists
        }

        public static void SaveRegistry(Document doc, WordLinkRegistry registry)
        {
            // Note: Saving registry must run within a Revit transaction
            try
            {
                Schema schema = GetOrCreateSchema();
                DataStorage storage = FindDataStorage(doc, schema);
                if (storage == null)
                {
                    storage = DataStorage.Create(doc);
                    storage.Name = "WordLinkRegistry";
                }

                string json = JsonSerializer.Serialize(registry);

                Entity entity = new Entity(schema);
                entity.Set("JsonData", json);
                storage.SetEntity(entity);
                
                Logger.Info("Successfully saved WordLinkRegistry to Extensible Storage.");
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save WordLinkRegistry to Extensible Storage", ex);
                throw new Exception(string.Format("無法儲存連結設定資料。錯誤: {0}", ex.Message), ex);
            }
        }

        private static DataStorage FindDataStorage(Document doc, Schema schema)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ICollection<Element> elements = collector.OfClass(typeof(DataStorage)).ToElements();
            foreach (Element elem in elements)
            {
                DataStorage ds = elem as DataStorage;
                if (ds != null)
                {
                    Entity entity = ds.GetEntity(schema);
                    if (entity != null && entity.IsValid())
                    {
                        return ds;
                    }
                }
            }
            return null;
        }

        // Helper to add or update a record
        public static void AddOrUpdateRecord(Document doc, WordLinkRecord record)
        {
            // Load, update list, save. Must be wrapped in a transaction from caller.
            WordLinkRegistry registry = LoadRegistry(doc);
            
            // Normalize path for lookup
            string normPath = PathUtils.NormalizePath(record.SourceWordPath);
            
            // Find existing
            WordLinkRecord existing = null;
            foreach (WordLinkRecord r in registry.Links)
            {
                if (PathUtils.NormalizePath(r.SourceWordPath) == normPath && 
                    r.PageNumber == record.PageNumber && 
                    r.SheetUniqueId == record.SheetUniqueId)
                {
                    existing = r;
                    break;
                }
            }

            if (existing != null)
            {
                registry.Links.Remove(existing);
            }

            registry.Links.Add(record);
            SaveRegistry(doc, registry);
        }

        public static WordLinkRecord FindRecordByWordPathPageAndSheet(Document doc, string wordPath, int pageNumber, string sheetUniqueId)
        {
            WordLinkRegistry registry = LoadRegistry(doc);
            string normPath = PathUtils.NormalizePath(wordPath);
            foreach (WordLinkRecord r in registry.Links)
            {
                if (PathUtils.NormalizePath(r.SourceWordPath) == normPath && 
                    r.PageNumber == pageNumber && 
                    r.SheetUniqueId == sheetUniqueId)
                {
                    return r;
                }
            }
            return null;
        }

        public static WordLinkRecord FindRecordByWordPath(Document doc, string wordPath)
        {
            WordLinkRegistry registry = LoadRegistry(doc);
            string normPath = PathUtils.NormalizePath(wordPath);
            foreach (WordLinkRecord r in registry.Links)
            {
                if (PathUtils.NormalizePath(r.SourceWordPath) == normPath)
                {
                    return r;
                }
            }
            return null;
        }

        public static WordLinkRecord FindRecordByImageTypeUniqueId(Document doc, string uniqueId)
        {
            WordLinkRegistry registry = LoadRegistry(doc);
            foreach (WordLinkRecord r in registry.Links)
            {
                if (r.ImageTypeUniqueId == uniqueId)
                {
                    return r;
                }
            }
            return null;
        }
    }
}
