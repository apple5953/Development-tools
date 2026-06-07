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
            try
            {
                var lm = LanguageManager.Instance;
                foreach (var tab in ComponentManager.Ribbon.Tabs)
                {
                    if (tab.Title != null && tab.Title.Contains("DT｜") || tab.Title.Contains("Development tools"))
                    {
                        foreach (var panel in tab.Panels)
                        {
                            // 嘗試根據目前的 Title 或是內部 Id 辨識 Panel，並更新 Title
                            if (panel.Source.Id.Contains("Admin") || panel.Source.Id.Contains("系統管理") || panel.Source.Title == "管理面板" || panel.Source.Title == "系統管理" || panel.Source.Title == "Admin" || panel.Source.Title == "管理パネル")
                                panel.Source.Title = lm["Ribbon_Panel_Admin"];
                            else if (panel.Source.Id.Contains("排版") || panel.Source.Title == "空間排版" || panel.Source.Title == "Layout" || panel.Source.Title == "空間レイアウト")
                                panel.Source.Title = lm["Ribbon_Panel_Layout"];
                            else if (panel.Source.Id.Contains("粉刷") || panel.Source.Title == "粉刷裝修" || panel.Source.Title == "Finishes" || panel.Source.Title == "塗装仕上げ")
                                panel.Source.Title = lm["Ribbon_Panel_Finish"];
                            else if (panel.Source.Id.Contains("樓板") || panel.Source.Title == "樓板工具" || panel.Source.Title == "Floors" || panel.Source.Title == "床ツール")
                                panel.Source.Title = lm["Ribbon_Panel_Floor"];
                            else if (panel.Source.Id.Contains("圖紙") || panel.Source.Title == "圖紙工具" || panel.Source.Title == "Sheets" || panel.Source.Title == "シートツール")
                                panel.Source.Title = lm["Ribbon_Panel_Sheet"];
                            else if (panel.Source.Id.Contains("標註") || panel.Source.Title == "標註工具" || panel.Source.Title == "Tags" || panel.Source.Title == "注釈ツール")
                                panel.Source.Title = lm["Ribbon_Panel_Tag"];
                            else if (panel.Source.Id.Contains("語言") || panel.Source.Title == "語言切換" || panel.Source.Title == "Language" || panel.Source.Title == "言語設定")
                                panel.Source.Title = lm["Ribbon_Panel_Language"];

                            foreach (var item in panel.Source.Items)
                            {
                                UpdateRibbonItem(item, lm);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static void UpdateRibbonItem(Autodesk.Windows.RibbonItem item, LanguageManager lm)
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
            else if (id.EndsWith("SheetViewPlacer")) { item.Text = lm["Ribbon_Btn_SheetPlacer"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_SheetPlacer"] }; }
            else if (id.EndsWith("QuickDimension")) { item.Text = lm["Ribbon_Btn_QuickDim"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_QuickDim"] }; }
            else if (id.EndsWith("LanguageDropdown")) { item.Text = lm["Ribbon_Btn_LangDropdown"]; item.ToolTip = new RibbonToolTip { Title = item.Text, Content = lm["Ribbon_TT_LangDropdown"] }; }
            else if (id.EndsWith("LanguageZH")) { item.Text = lm["Ribbon_Lang_ZH"];  }
            else if (id.EndsWith("LanguageEN")) { item.Text = lm["Ribbon_Lang_EN"];  }
            else if (id.EndsWith("LanguageJA")) { item.Text = lm["Ribbon_Lang_JA"];  }


            // 如果是 Pulldown，也要更新子按鈕
            if (item is Autodesk.Windows.RibbonSplitButton splitBtn)
            {
                foreach (var child in splitBtn.Items)
                {
                    UpdateRibbonItem(child, lm);
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
