using System;
using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;

namespace BIMDrawingFactory.UI.Revit
{
    public class RevitApp : IExternalApplication
    {
        public static RevitApp Instance { get; private set; }
        public static UIControlledApplication UiControlledApp { get; private set; }
        public static RevitExternalEventHandler ExternalEventHandler { get; set; }
        public static ExternalEvent ExternalEvent { get; set; }

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;
            UiControlledApp = application;

            ExternalEventHandler = new RevitExternalEventHandler();
            ExternalEvent = ExternalEvent.Create(ExternalEventHandler);

            CreateRibbonPanel(application);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            OpenDrawingFactoryCommand.CloseWindowIfOpen();
            if (ExternalEvent != null)
            {
                ExternalEvent.Dispose();
                ExternalEvent = null;
            }
            ExternalEventHandler = null;
            return Result.Succeeded;
        }

        private void CreateRibbonPanel(UIControlledApplication application)
        {
            string tabName = "BIM 出圖工廠";
            try
            {
                application.CreateRibbonTab(tabName);
            }
            catch
            {
            }

            RibbonPanel panel = null;
            var panels = application.GetRibbonPanels(tabName);
            foreach (var p in panels)
            {
                if (p.Name == "圖紙視圖工廠")
                {
                    panel = p;
                    break;
                }
            }

            if (panel == null)
            {
                panel = application.CreateRibbonPanel(tabName, "圖紙視圖工廠");
            }

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;

            var pushButtonData = new PushButtonData(
                "Btn_BIMDrawingFactory_Open",
                "BIM Drawing\nFactory",
                thisAssemblyPath,
                typeof(OpenDrawingFactoryCommand).FullName)
            {
                ToolTip = "開啟 BIM Drawing Factory 視圖量化開圖工廠 (快速出圖、檢查與預覽)",
                LongDescription = "根據已完成的標準圖說，自動分析視圖樣板、範圍框與樓層關係，批次建立各棟各樓層之完整圖紙套組。"
            };

            panel.AddItem(pushButtonData);
        }
    }
}
