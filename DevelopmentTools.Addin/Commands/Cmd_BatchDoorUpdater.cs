using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using DevelopmentTools.Core;

namespace DevelopmentTools.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Cmd_BatchDoorUpdater : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!CommandAccessGuard.EnsureAuthorized("DT_BatchDoorUpdater", "門族群批量深化"))
            {
                return Result.Cancelled;
            }

            UIApplication uiApp = commandData.Application;
            Autodesk.Revit.ApplicationServices.Application app = uiApp.Application;

            // Step 1: Select Folder containing door families
            string folderPath = "";
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "請選擇包含要深化的門元件 (.rfa) 的資料夾";
                fbd.ShowNewFolderButton = false;
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    folderPath = fbd.SelectedPath;
                }
            }

            if (string.IsNullOrEmpty(folderPath))
            {
                TaskDialog.Show("批量深化", "已取消操作。");
                return Result.Cancelled;
            }

            // Step 2: Set Shared Parameters File Path
            // 共用參數檔由使用者每次選擇，不寫死在程式裡（避免把客戶資料路徑或內容帶進外掛）
            string sharedParamPath = DoorSharedParamFilePicker.Pick();
            if (string.IsNullOrEmpty(sharedParamPath))
            {
                TaskDialog.Show("批量深化", "未選擇共用參數檔，已取消操作。");
                return Result.Cancelled;
            }

            string oldSharedParamsFile = app.SharedParametersFilename;
            app.SharedParametersFilename = sharedParamPath;
            DefinitionFile defFile = null;
            try
            {
                defFile = app.OpenSharedParameterFile();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("批量深化", "無法開啟共用參數檔：" + ex.Message);
                app.SharedParametersFilename = oldSharedParamsFile;
                return Result.Failed;
            }

            if (defFile == null)
            {
                TaskDialog.Show("批量深化", "讀取共用參數檔定義失敗。");
                app.SharedParametersFilename = oldSharedParamsFile;
                return Result.Failed;
            }

            // Find all door families (.rfa) in folder
            string[] rfaFiles = Directory.GetFiles(folderPath, "*.rfa");
            if (rfaFiles.Length == 0)
            {
                TaskDialog.Show("批量深化", "在選擇的資料夾中找不到任何 .rfa 門元件檔案。");
                app.SharedParametersFilename = oldSharedParamsFile;
                return Result.Failed;
            }

            int successCount = 0;
            int failCount = 0;
            List<string> logs = new List<string>();

            // Process families
            foreach (string file in rfaFiles)
            {
                string fileName = Path.GetFileName(file);
                List<string> fileLogs = new List<string>();
                fileLogs.Add("========================================");
                fileLogs.Add("開始深化元件: " + fileName);
                
                Document doc = null;
                try
                {
                    doc = app.OpenDocumentFile(file);
                    if (doc == null || !doc.IsFamilyDocument)
                    {
                        fileLogs.Add("錯誤: 無法開啟檔案或該檔案不是 Revit 家族元件 (.rfa)！");
                        failCount++;
                        if (doc != null) doc.Close(false);
                        logs.AddRange(fileLogs);
                        continue;
                    }

                    FamilyManager fm = doc.FamilyManager;

                    using (Transaction trans = new Transaction(doc, "深化參數寫入"))
                    {
                        trans.Start();

                        // 1. 匯入共用參數與設定屬性類型 (類型/實體)
                        // 地坪完成面高程 -> Shared, Instance, 識別資料
                        FamilyParameter fpFloor = AddSharedParam(fm, defFile, "地坪完成面高程", GroupTypeId.IdentityData, true, fileLogs);
                        // 上崁縫 -> Shared, Type, 尺寸
                        FamilyParameter fpTopGap = AddSharedParam(fm, defFile, "上崁縫", GroupTypeId.Geometry, false, fileLogs);
                        // 左右崁縫 -> Shared, Type, 尺寸
                        FamilyParameter fpSideGap = AddSharedParam(fm, defFile, "左右崁縫", GroupTypeId.Geometry, false, fileLogs);
                        // 位於材質 -> Shared, Type, 識別資料
                        FamilyParameter fpMat = AddSharedParam(fm, defFile, "位於材質", GroupTypeId.IdentityData, false, fileLogs);
                        // 門框外凸厚度 -> Shared, Type, 尺寸
                        FamilyParameter fpFrameThickness = AddSharedParam(fm, defFile, "門框外凸厚度", GroupTypeId.Geometry, false, fileLogs);
                        
                        // 門編號 -> Shared, Type, 識別資料
                        FamilyParameter fpDoorNo = AddSharedParam(fm, defFile, "門編號", GroupTypeId.IdentityData, false, fileLogs);
                        
                        // 結構開口高度 -> Shared, Instance, 尺寸
                        FamilyParameter fpStructHeight = AddSharedParam(fm, defFile, "結構開口高度", GroupTypeId.Geometry, true, fileLogs);

                        // 2. 調整系統/現有家族參數
                        // 粗略高度 -> 轉為 Type, 尺寸
                        FamilyParameter fpRoughHeight = EnsureFamilyParam(fm, "粗略高度", SpecTypeId.Length, GroupTypeId.Geometry, false, fileLogs);
                        // 粗略寬度 -> 轉為 Type, 尺寸
                        FamilyParameter fpRoughWidth = EnsureFamilyParam(fm, "粗略寬度", SpecTypeId.Length, GroupTypeId.Geometry, false, fileLogs);

                        // 3. 填入約束公式
                        SetFormulaWithLog(fm, fpRoughHeight, "高度 + 上崁縫", fileLogs);
                        SetFormulaWithLog(fm, fpRoughWidth, "寬度 + 2 * 左右崁縫", fileLogs);
                        SetFormulaWithLog(fm, fpDoorNo, "類型備註", fileLogs);
                        SetFormulaWithLog(fm, fpStructHeight, "粗略高度 + 地坪完成面高程", fileLogs);

                        // 4. 深化輔助參數 (如果缺失則建立)
                        // 門扇寬度: Type, 尺寸
                        FamilyParameter fpDoorWidth = fm.get_Parameter("門扇寬度");
                        if (fpDoorWidth == null)
                        {
                            fpDoorWidth = EnsureFamilyParam(fm, "門扇寬度", SpecTypeId.Length, GroupTypeId.Geometry, false, fileLogs);
                        }
                        else
                        {
                            TryMakeType(fm, fpDoorWidth, fileLogs);
                        }

                        // 檢查元件目前使用的是「門框寬度」還是「裝飾外框寬度」並套用公式
                        bool hasFrameWidth = fm.get_Parameter("門框寬度") != null;
                        bool hasOuterFrameWidth = fm.get_Parameter("裝飾外框寬度") != null;
                        if (hasOuterFrameWidth)
                        {
                            SetFormulaWithLog(fm, fpDoorWidth, "寬度 - 2 * 裝飾外框寬度", fileLogs);
                        }
                        else if (hasFrameWidth)
                        {
                            SetFormulaWithLog(fm, fpDoorWidth, "寬度 - 2 * 門框寬度", fileLogs);
                        }

                        // 門把高度: Type, 尺寸
                        FamilyParameter fpHandleHeight = fm.get_Parameter("門把高度");
                        if (fpHandleHeight == null)
                        {
                            fpHandleHeight = EnsureFamilyParam(fm, "門把高度", SpecTypeId.Length, GroupTypeId.Geometry, false, fileLogs);
                        }
                        else
                        {
                            TryMakeType(fm, fpHandleHeight, fileLogs);
                        }
                        SetFormulaWithLog(fm, fpHandleHeight, "if(高度 < 1900 mm, 高度 / 2, 900 mm)", fileLogs);

                        // 單一嵌板寬度 (主要針對雙扇門，若有此參數則優化公式)
                        FamilyParameter fpPanelWidth = fm.get_Parameter("單一嵌板寬度");
                        if (fpPanelWidth != null)
                        {
                            TryMakeType(fm, fpPanelWidth, fileLogs);
                            if (hasFrameWidth)
                            {
                                SetFormulaWithLog(fm, fpPanelWidth, "(寬度 / 2) - 門框寬度", fileLogs);
                            }
                            else if (hasOuterFrameWidth)
                            {
                                SetFormulaWithLog(fm, fpPanelWidth, "(寬度 / 2) - 裝飾外框寬度", fileLogs);
                            }
                        }

                        trans.Commit();
                        fileLogs.Add("成功: 該元件所有參數與公式處理完成！");
                        successCount++;
                    }

                    // Save and Close
                    doc.Save();
                    logs.AddRange(fileLogs);
                }
                catch (Exception ex)
                {
                    fileLogs.Add("失敗: 深化出錯：" + ex.Message);
                    logs.AddRange(fileLogs);
                    failCount++;
                }
                finally
                {
                    if (doc != null)
                    {
                        try { doc.Close(false); } catch {}
                    }
                }
            }

            // Restore original shared parameter path
            app.SharedParametersFilename = oldSharedParamsFile;

            // Save processing report log
            string reportPath = Path.Combine(folderPath, "批量深化參數報告.txt");
            try
            {
                File.WriteAllLines(reportPath, logs.ToArray());
            }
            catch {}

            TaskDialog.Show("批量參數深化完成", string.Format("處理完成！\n成功: {0} 扇\n失敗: {1} 扇\n\n深化報告日誌已儲存至選擇的資料夾下：\n{2}", successCount, failCount, reportPath));
            return Result.Succeeded;
        }

        private FamilyParameter AddSharedParam(FamilyManager fm, DefinitionFile defFile, string paramName, ForgeTypeId groupTypeId, bool isInstance, List<string> fileLogs)
        {
            FamilyParameter fp = fm.get_Parameter(paramName);
            if (fp != null)
            {
                // Ensure type/instance scope matches
                if (isInstance) TryMakeInstance(fm, fp, fileLogs);
                else TryMakeType(fm, fp, fileLogs);
                fileLogs.Add(string.Format("  - 檢查共用參數: [{0}] 已存在", paramName));
                return fp;
            }

            ExternalDefinition def = null;
            foreach (DefinitionGroup dg in defFile.Groups)
            {
                def = dg.Definitions.get_Item(paramName) as ExternalDefinition;
                if (def != null) break;
            }

            if (def == null)
            {
                throw new Exception("在共用參數檔中找不到定義：" + paramName);
            }

            fp = fm.AddParameter(def, groupTypeId, isInstance);
            string scope = isInstance ? "實體" : "類型";
            fileLogs.Add(string.Format("  - 新增共用參數: [{0}] ({1})", paramName, scope));
            return fp;
        }

        private FamilyParameter EnsureFamilyParam(FamilyManager fm, string name, ForgeTypeId specTypeId, ForgeTypeId groupTypeId, bool isInstance, List<string> fileLogs)
        {
            FamilyParameter fp = fm.get_Parameter(name);
            if (fp == null)
            {
                fp = fm.AddParameter(name, groupTypeId, specTypeId, isInstance);
                string scope = isInstance ? "實體" : "類型";
                fileLogs.Add(string.Format("  - 新增家族參數: [{0}] ({1})", name, scope));
            }
            else
            {
                if (isInstance) TryMakeInstance(fm, fp, fileLogs);
                else TryMakeType(fm, fp, fileLogs);
                fileLogs.Add(string.Format("  - 檢查家族參數: [{0}] 已存在", name));
            }
            return fp;
        }

        private void SetFormulaWithLog(FamilyManager fm, FamilyParameter fp, string formula, List<string> fileLogs)
        {
            if (fp != null)
            {
                try
                {
                    fm.SetFormula(fp, formula);
                    fileLogs.Add(string.Format("  - 設定參數公式: [{0}] = {1}", fp.Definition.Name, formula));
                }
                catch (Exception ex)
                {
                    fileLogs.Add(string.Format("  - 錯誤: 無法為 [{0}] 設定公式 [{1}]。原因: {2}", fp.Definition.Name, formula, ex.Message));
                }
            }
        }

        private void TryMakeInstance(FamilyManager fm, FamilyParameter fp, List<string> fileLogs)
        {
            try
            {
                if (fp != null && !fp.IsInstance)
                {
                    fm.MakeInstance(fp);
                    fileLogs.Add(string.Format("  - 變更參數屬性: 將 [{0}] 轉為實體參數", fp.Definition.Name));
                }
            }
            catch (Exception)
            {
                fileLogs.Add(string.Format("  - 警告: 無法變更 [{0}] 為實體參數 (可能是系統內建參數限制)", fp.Definition.Name));
            }
        }

        private void TryMakeType(FamilyManager fm, FamilyParameter fp, List<string> fileLogs)
        {
            try
            {
                if (fp != null && fp.IsInstance)
                {
                    fm.MakeType(fp);
                    fileLogs.Add(string.Format("  - 變更參數屬性: 將 [{0}] 轉為類型參數", fp.Definition.Name));
                }
            }
            catch (Exception)
            {
                fileLogs.Add(string.Format("  - 警告: 無法變更 [{0}] 為類型參數 (可能是系統內建參數限制)", fp.Definition.Name));
            }
        }
    }
}
