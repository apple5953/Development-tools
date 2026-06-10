using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.SheetTransfer.Models;

namespace DevelopmentTools.Modules.SheetTransfer.Services
{
    public class SheetTransferAnalyzer
    {
        private readonly Document _sourceDoc;

        public SheetTransferAnalyzer(Document sourceDoc)
        {
            _sourceDoc = sourceDoc;
        }

        public List<TransferAsset> AnalyzeAssets()
        {
            var assets = new List<TransferAsset>();

            // 1. Sheets
            var sheets = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(x => !x.IsPlaceholder)
                .ToList();

            foreach (var sheet in sheets)
            {
                assets.Add(new TransferAsset
                {
                    Type = AssetType.Sheet,
                    ElementId = sheet.Id,
                    UniqueId = sheet.UniqueId,
                    Name = sheet.Name,
                    Number = sheet.SheetNumber,
                    IsSelected = false
                });
            }

            // 2. Drafting Views & Legends
            var views = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => !x.IsTemplate)
                .ToList();

            foreach (var view in views)
            {
                if (view.ViewType == ViewType.DraftingView)
                {
                    assets.Add(new TransferAsset
                    {
                        Type = AssetType.DraftingView,
                        ElementId = view.Id,
                        UniqueId = view.UniqueId,
                        Name = view.Name,
                        IsSelected = false
                    });
                }
                else if (view.ViewType == ViewType.Legend)
                {
                    assets.Add(new TransferAsset
                    {
                        Type = AssetType.Legend,
                        ElementId = view.Id,
                        UniqueId = view.UniqueId,
                        Name = view.Name,
                        IsSelected = false
                    });
                }
            }

            // 3. Schedules
            var schedules = new FilteredElementCollector(_sourceDoc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(x => !x.IsTemplate && !x.IsInternalKeynoteSchedule && !x.Name.Contains("<"))
                .ToList();

            foreach (var schedule in schedules)
            {
                assets.Add(new TransferAsset
                {
                    Type = AssetType.Schedule,
                    ElementId = schedule.Id,
                    UniqueId = schedule.UniqueId,
                    Name = schedule.Name,
                    IsSelected = false
                });
            }

            return assets;
        }
    }
}
