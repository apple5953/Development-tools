using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Core;
using Autodesk.Windows; // Required to dynamically update Ribbon

namespace DevelopmentTools.Commands
{
    public abstract class Cmd_ChangeLanguageBase : IExternalCommand
    {
        protected abstract string TargetLanguage { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                // 1. 切換內部語言狀態
                LanguageManager.Instance.CurrentLanguage = TargetLanguage;

                // 2. 更新 Revit 原生工具列
                UpdateRibbonTexts();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        public static void UpdateRibbonTexts()
        {
            var lm = LanguageManager.Instance;

            // 1. 面板標題：App 建立面板時記下了每個面板對應的語系字串鍵
            foreach (var entry in App.TrackedPanels)
            {
                try { entry.Key.Title = lm[entry.Value]; } catch { }
            }

            // 2. 頁籤標題與按鈕文字
            try
            {
                foreach (var tab in ComponentManager.Ribbon.Tabs)
                {
                    if (tab == null || tab.Id == null) continue;
                    if (!RibbonCatalog.TabKeys.TryGetValue(tab.Id, out string tabKey)) continue;

                    tab.Title = lm[tabKey];
                    foreach (var panel in tab.Panels)
                    {
                        foreach (var item in panel.Source.Items)
                        {
                            try { UpdateRibbonItem(item, lm); } catch { }
                        }
                    }
                }
            }
            catch { }
        }


        private static void UpdateRibbonItem(Autodesk.Windows.RibbonItem item, LanguageManager lm, bool inMenu = false)
        {
            if (item == null) return;

            string id = item.Id;

            // 判斷按鈕的 ID 並更新 Text 與 ToolTip
            if (id.EndsWith("GoogleLogin")) { item.Text = lm["Ribbon_Btn_Login"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_Login"] }; }
            else if (id.EndsWith("SystemFeedback")) { item.Text = lm["Ribbon_Btn_Feedback"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_Feedback"] }; }
            else if (id.EndsWith("CheckForUpdates")) { item.Text = lm["Ribbon_Btn_Update"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_Update"] }; }
            else if (id.EndsWith("ShowControlPanel")) { item.Text = lm["Ribbon_Btn_TileSystem"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_TileSystem"] }; }
            else if (id.EndsWith("TileElevationGenerator")) { item.Text = lm["Ribbon_Btn_Elevation"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_Elevation"] }; }
            else if (id.EndsWith("WallFinishTool")) { item.Text = lm["Ribbon_Btn_WallFinish"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_WallFinish"] }; }
            else if (id.EndsWith("RoomFinishConfigurator")) { item.Text = lm["Ribbon_Btn_RoomConfig"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_RoomConfig"] }; }
            else if (id.EndsWith("FloorSnapToRoom")) { item.Text = lm["Ribbon_Btn_FloorSnap"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_FloorSnap"] }; }
            else if (id.EndsWith("BatchSheetRename")) { item.Text = lm["Ribbon_Btn_SheetRename"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_SheetRename"] }; }
            else if (id.EndsWith("QuickViewCreator")) { item.Text = lm["Ribbon_Btn_QuickView"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_QuickView"] }; }
            else if (id.EndsWith("FastDependentViews")) { item.Text = lm["Ribbon_Btn_FastDependentViews"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_FastDependentViews"] }; }
            else if (id.EndsWith("SheetViewPlacer")) { item.Text = lm["Ribbon_Btn_SheetPlacer"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_SheetPlacer"] }; }
            else if (id.EndsWith("QuickDimension")) { item.Text = lm["Ribbon_Btn_QuickDim"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_QuickDim"] }; }
            else if (id.EndsWith("AutoBoundary")) { item.Text = lm["Ribbon_Btn_AutoBoundary"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_AutoBoundary"] }; }
            else if (id.EndsWith("LanguageDropdown")) { item.Text = lm["Ribbon_Btn_LangDropdown"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_LangDropdown"] }; }
            else if (id.EndsWith("SheetDuplicator")) { item.Text = lm["Ribbon_Btn_SheetDuplicator"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_SheetDuplicator"] }; }
            else if (id.EndsWith("AIAssistant")) { item.Text = lm["Ribbon_Btn_AI"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_AI"] }; }
            else if (id.EndsWith("SheetTransfer")) { item.Text = lm["Ribbon_Btn_SheetTransfer"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_SheetTransfer"] }; }
            else if (id.EndsWith("ProjectCompareCopy")) { item.Text = lm["Ribbon_Btn_ProjectCompareCopy"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_ProjectCompareCopy"] }; }
            else if (id.EndsWith("BcrProject")) { item.Text = lm["Ribbon_Btn_CodeReview"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_CodeReview"] }; }
            else if (id.EndsWith("BcrExportReport")) { item.Text = lm["Ribbon_Btn_CodeReviewExport"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_CodeReviewExport"] }; }
            else if (id.EndsWith("AdaptiveRegion")) { item.Text = lm["Ribbon_Btn_AdaptiveRegion"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_AdaptiveRegion"] }; }
            else if (id.EndsWith("LanguageZH")) { item.Text = lm["Ribbon_Lang_ZH"];  }
            else if (id.EndsWith("LanguageEN")) { item.Text = lm["Ribbon_Lang_EN"];  }
            else if (id.EndsWith("LanguageJA")) { item.Text = lm["Ribbon_Lang_JA"];  }
            else
            {
                // 其他按鈕（含日後新增者）：依 RibbonCatalog 的命名規則找 Ribbon_Btn_ / Ribbon_TT_ 字串，有翻譯才更新
                string name = id.Substring(id.LastIndexOf('%') + 1);
                string key = RibbonCatalog.LangKey(name);
                string text = lm["Ribbon_Btn_" + key];
                if (text != "Ribbon_Btn_" + key)
                {
                    item.Text = text;
                    string tip = lm["Ribbon_TT_" + key];
                    if (tip != "Ribbon_TT_" + key) item.ToolTip = new RibbonToolTip { Title = text, Content = tip };
                }
            }


            // 下拉選單裡的項目用單行文字
            if (inMenu && item.Text != null) item.Text = RibbonCatalog.OneLine(item.Text);

            // 用 reflection 遍歷任何帶有 Items 的容器型別（PulldownButton / SplitButton 等）
            if (item is Autodesk.Windows.RibbonSplitButton splitBtn)
            {
                foreach (var child in splitBtn.Items)
                    UpdateRibbonItem(child, lm, true);
            }
            else
            {
                var itemsProp = item.GetType().GetProperty("Items");
                if (itemsProp != null)
                {
                    var children = itemsProp.GetValue(item) as System.Collections.IEnumerable;
                    if (children != null)
                    {
                        foreach (var child in children)
                        {
                            if (child is Autodesk.Windows.RibbonItem ribbonChild)
                                UpdateRibbonItem(ribbonChild, lm, true);
                        }
                    }
                }
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class Cmd_LanguageZH : Cmd_ChangeLanguageBase
    {
        protected override string TargetLanguage => "zh-TW";
    }

    [Transaction(TransactionMode.Manual)]
    public class Cmd_LanguageEN : Cmd_ChangeLanguageBase
    {
        protected override string TargetLanguage => "en-US";
    }

    [Transaction(TransactionMode.Manual)]
    public class Cmd_LanguageJA : Cmd_ChangeLanguageBase
    {
        protected override string TargetLanguage => "ja-JP";
    }
}
