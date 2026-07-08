using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class IssueTrackerService
    {
        private static readonly Guid SchemaGuid = new Guid("F9A8A5E1-723A-4FBE-B388-B1F7623910A4");
        
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        private Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetSchemaName("DT_CodeReview_ProjectStorage");
            builder.SetDocumentation("DT Code Review Project Issue Tracking Data Storage Schema");

            builder.AddSimpleField("JsonData", typeof(string));
            return builder.Finish();
        }

        private DataStorage FindStorage(Document doc)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>();

            Schema schema = GetOrCreateSchema();
            return collector.FirstOrDefault(ds => ds.GetEntity(schema).IsValid());
        }

        public ReviewProject LoadOrCreateProject(Document doc, string projectName, string buildingType, string templateName, string rulesDir)
        {
            if (doc == null) return null;

            DataStorage storage = FindStorage(doc);
            ReviewProject loadedProject = null;
            if (storage != null)
            {
                try
                {
                    Schema schema = GetOrCreateSchema();
                    Entity entity = storage.GetEntity(schema);
                    string json = entity.Get<string>("JsonData");
                    if (!string.IsNullOrEmpty(json))
                    {
                        var project = JsonSerializer.Deserialize<ReviewProject>(json, _jsonOptions);
                        if (project != null && project.Items != null && project.Items.Count > 0)
                        {
                            loadedProject = project;
                        }
                    }
                }
                catch
                {
                }
            }

            var ruleEngine = new RuleEngine();
            ruleEngine.LoadRules(rulesDir);

            if (loadedProject != null)
            {
                // 自動比對，補全缺失的新法規/檢核條目，並遷移舊資料欄位
                bool updated = false;
                foreach (var rule in ruleEngine.RuleConfigs)
                {
                    var existingItem = loadedProject.Items.FirstOrDefault(item => item.RuleCode == rule.RuleCode);
                    if (existingItem == null)
                    {
                        var type = ReviewType.Manual;
                        if (rule.CheckType == "Auto") type = ReviewType.Auto;
                        else if (rule.CheckType == "SemiAuto") type = ReviewType.SemiAuto;

                        var item = new ReviewItem
                        {
                            RuleCode = rule.RuleCode,
                            RuleName = rule.RuleName,
                            Category = rule.Category,
                            Type = type,
                            Description = rule.Description,
                            Perspective = rule.Perspective,
                            LawArticle = rule.LawArticle,
                            LawChapter = rule.LawChapter,
                            Status = TrackingStatus.Created,
                            Comment = "系統升級載入"
                        };

                        item.History.Add(new TrackingHistoryEntry
                        {
                            Timestamp = DateTime.Now,
                            FromStatus = TrackingStatus.Created,
                            ToStatus = TrackingStatus.Created,
                            ChangedBy = "System",
                            Comment = "版本更新，自動補充新檢核條目"
                        });

                        loadedProject.Items.Add(item);
                        updated = true;
                    }
                    else
                    {
                        // 舊資料遷移：同步法規條號、章節，並在檢核類型變更時予以升級
                        bool itemChanged = false;
                        if (string.IsNullOrEmpty(existingItem.LawArticle) && !string.IsNullOrEmpty(rule.LawArticle))
                        {
                            existingItem.LawArticle = rule.LawArticle;
                            itemChanged = true;
                        }
                        if (string.IsNullOrEmpty(existingItem.LawChapter) && !string.IsNullOrEmpty(rule.LawChapter))
                        {
                            existingItem.LawChapter = rule.LawChapter;
                            itemChanged = true;
                        }
                        
                        var targetType = ReviewType.Manual;
                        if (rule.CheckType == "Auto") targetType = ReviewType.Auto;
                        else if (rule.CheckType == "SemiAuto") targetType = ReviewType.SemiAuto;
                        
                        if (existingItem.Type != targetType)
                        {
                            existingItem.Type = targetType;
                            itemChanged = true;
                        }

                        if (itemChanged)
                        {
                            updated = true;
                        }
                    }
                }

                if (updated)
                {
                    SaveProject(doc, loadedProject);
                }

                return loadedProject;
            }

            var newProject = new ReviewProject
            {
                ProjectName = string.IsNullOrEmpty(doc.Title) ? projectName : doc.Title,
                BuildingType = buildingType,
                ReviewTemplate = templateName,
                CreatedAt = DateTime.Now
            };

            foreach (var rule in ruleEngine.RuleConfigs)
            {
                var type = ReviewType.Manual;
                if (rule.CheckType == "Auto") type = ReviewType.Auto;
                else if (rule.CheckType == "SemiAuto") type = ReviewType.SemiAuto;

                var item = new ReviewItem
                {
                    RuleCode = rule.RuleCode,
                    RuleName = rule.RuleName,
                    Category = rule.Category,
                    Type = type,
                    Description = rule.Description,
                    Perspective = rule.Perspective,
                    LawArticle = rule.LawArticle,
                    LawChapter = rule.LawChapter,
                    Status = TrackingStatus.Created,
                    Comment = "初始建立"
                };

                item.History.Add(new TrackingHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    FromStatus = TrackingStatus.Created,
                    ToStatus = TrackingStatus.Created,
                    ChangedBy = "System",
                    Comment = "系統自動初始化"
                });

                newProject.Items.Add(item);
            }

            SaveProject(doc, newProject);
            return newProject;
        }

        public void SaveProject(Document doc, ReviewProject project)
        {
            if (project == null || doc == null) return;

            using (Transaction t = new Transaction(doc, "保存法規檢核追蹤資料"))
            {
                try
                {
                    t.Start();

                    string json = JsonSerializer.Serialize(project, _jsonOptions);
                    Schema schema = GetOrCreateSchema();

                    Entity entity = new Entity(schema);
                    entity.Set<string>("JsonData", json);

                    DataStorage storage = FindStorage(doc);
                    if (storage == null)
                    {
                        storage = DataStorage.Create(doc);
                    }
                    storage.SetEntity(entity);

                    t.Commit();
                }
                catch
                {
                    t.RollBack();
                }
            }
        }

        public void UpdateItemStatus(Document doc, ReviewProject project, ReviewItem item, TrackingStatus newStatus, string user, string comment)
        {
            if (item == null || doc == null || project == null) return;

            var oldStatus = item.Status;
            item.Status = newStatus;
            item.Comment = comment;
            if (!string.IsNullOrEmpty(user))
            {
                item.Assignee = user;
            }

            item.History.Add(new TrackingHistoryEntry
            {
                Timestamp = DateTime.Now,
                FromStatus = oldStatus,
                ToStatus = newStatus,
                ChangedBy = string.IsNullOrEmpty(user) ? "User" : user,
                Comment = comment
            });

            SaveProject(doc, project);
        }
    }
}
