using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DevelopmentTools.Modules.AIAssistant
{
    public class AICommandPayload
    {
        public string action { get; set; } // "set_parameter" | "select_elements" | "delete_elements"
        public List<long> element_ids { get; set; }
        public string parameter_name { get; set; }
        public string value { get; set; }
    }

    public class AICommandHandler : IExternalEventHandler
    {
        public string CommandJson { get; set; }
        public Action<string> OnExecutionCompleted { get; set; }

        public void Execute(UIApplication app)
        {
            if (string.IsNullOrWhiteSpace(CommandJson)) return;

            var uiDoc = app.ActiveUIDocument;
            if (uiDoc == null) return;
            var doc = uiDoc.Document;

            try
            {
                var payload = JsonSerializer.Deserialize<AICommandPayload>(CommandJson);
                if (payload == null)
                {
                    OnExecutionCompleted?.Invoke("❌ 無法解析 AI 指令。");
                    return;
                }

                if (payload.element_ids == null || payload.element_ids.Count == 0)
                {
                    OnExecutionCompleted?.Invoke("❌ 指令中沒有包含任何 Element ID。");
                    return;
                }

                List<ElementId> elementIds = new List<ElementId>();
                foreach (var idVal in payload.element_ids)
                {
#if REVIT2024 || REVIT2025 || REVIT2026
                    elementIds.Add(new ElementId(idVal));
#else
                    elementIds.Add(new ElementId((int)idVal));
#endif
                }

                if (payload.action == "select_elements")
                {
                    uiDoc.Selection.SetElementIds(elementIds);
                    OnExecutionCompleted?.Invoke($"✓ 已在 Revit 中自動選取 {elementIds.Count} 個元件。");
                    return;
                }

                using (var tx = new Transaction(doc, "AI Auto Action"))
                {
                    tx.Start();

                    if (payload.action == "delete_elements")
                    {
                        var deletedIds = doc.Delete(elementIds);
                        tx.Commit();
                        OnExecutionCompleted?.Invoke($"✓ 已成功在 Revit 中刪除 {deletedIds.Count} 個元件。");
                        return;
                    }

                    if (payload.action == "set_parameter")
                    {
                        if (string.IsNullOrWhiteSpace(payload.parameter_name))
                        {
                            tx.RollBack();
                            OnExecutionCompleted?.Invoke("❌ 參數修改指令缺少目標參數名稱 (parameter_name)。");
                            return;
                        }

                        int successCount = 0;
                        string failReason = "";

                        foreach (var id in elementIds)
                        {
                            var elem = doc.GetElement(id);
                            if (elem == null) continue;

                            var param = elem.LookupParameter(payload.parameter_name);
                            if (param == null || param.IsReadOnly)
                            {
                                failReason = $"參數 [{payload.parameter_name}] 不存在或為唯讀";
                                continue;
                            }

                            bool setSuccess = false;
                            switch (param.StorageType)
                            {
                                case StorageType.String:
                                    setSuccess = param.Set(payload.value);
                                    break;

                                case StorageType.Integer:
                                    if (int.TryParse(payload.value, out int intVal))
                                    {
                                        setSuccess = param.Set(intVal);
                                    }
                                    else if (bool.TryParse(payload.value, out bool boolVal))
                                    {
                                        setSuccess = param.Set(boolVal ? 1 : 0);
                                    }
                                    break;

                                case StorageType.Double:
                                    if (double.TryParse(payload.value, out double doubleVal))
                                    {
                                        // 判斷是否為長度單位。如果是，且使用者傳入 mm，則轉換為 Revit 英呎
                                        string pName = param.Definition.Name.ToLower();
                                        bool isLength = pName.Contains("width") || pName.Contains("offset") ||
                                                        pName.Contains("height") || pName.Contains("length") ||
                                                        pName.Contains("thickness") || pName.Contains("elevation") ||
                                                        pName.Contains("depth") || pName.Contains("extension") ||
                                                        pName.Contains("縫隙") || pName.Contains("厚度") ||
                                                        pName.Contains("尺寸") || pName.Contains("長度") ||
                                                        pName.Contains("高度") || pName.Contains("寬度");
                                        
                                        if (isLength)
                                        {
                                            doubleVal = doubleVal / 304.8; // mm 轉為英呎
                                        }

                                        setSuccess = param.Set(doubleVal);
                                    }
                                    break;

                                case StorageType.ElementId:
                                    if (long.TryParse(payload.value, out long idVal))
                                    {
#if REVIT2024 || REVIT2025 || REVIT2026
                                        setSuccess = param.Set(new ElementId(idVal));
#else
                                        setSuccess = param.Set(new ElementId((int)idVal));
#endif
                                    }
                                    break;
                            }

                            if (setSuccess)
                            {
                                successCount++;
                            }
                        }

                        if (successCount > 0)
                        {
                            tx.Commit();
                            OnExecutionCompleted?.Invoke($"✓ 成功修改 {successCount} 個元件的參數 [{payload.parameter_name}]。");
                        }
                        else
                        {
                            tx.RollBack();
                            OnExecutionCompleted?.Invoke($"❌ 無法套用修改：{failReason}");
                        }
                        return;
                    }

                    tx.RollBack();
                    OnExecutionCompleted?.Invoke($"❌ 未知的 AI 指令動作: {payload.action}");
                }
            }
            catch (Exception ex)
            {
                OnExecutionCompleted?.Invoke($"❌ 執行 AI 指令失敗：{ex.Message}");
            }
        }

        public string GetName() => "AICommandHandler";
    }
}
