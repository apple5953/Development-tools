using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public class ReportSheetService
    {
        public void GenerateReportSheet(Document doc, ReviewProject project, UIDocument uidoc = null)
        {
            if (doc == null || project == null) return;

            using (Transaction t = new Transaction(doc, "生成法規檢核報告圖紙"))
            {
                t.Start();

                try
                {
                    // 1. 建立或取得 Drafting View
                    ViewDrafting draftingView = GetOrCreateDraftingView(doc);

                    // 2. 清除視圖內既有圖元
                    ClearViewContents(doc, draftingView);

                    // 3. 在 Drafting View 中繪製報表
                    DrawReportToView(doc, draftingView, project);

                    // 4. 建立或取得圖紙 (Sheet G-101)
                    ViewSheet sheet = GetOrCreateReportSheet(doc);

                    // 5. 將視圖放置到圖紙上
                    PlaceViewOnSheet(doc, sheet, draftingView);

                    t.Commit();

                    // 6. 切換至該圖紙視圖
                    if (uidoc != null)
                    {
                        uidoc.ActiveView = sheet;
                    }

                    TaskDialog.Show("DT Code Review", $"已成功生成法規檢核報告圖紙！\n圖紙編號: {sheet.SheetNumber}\n圖紙名稱: {sheet.Name}");
                }
                catch (Exception ex)
                {
                    t.RollBack();
                    TaskDialog.Show("生成圖紙錯誤", $"無法生成檢核圖紙：{ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private ViewDrafting GetOrCreateDraftingView(Document doc)
        {
            string viewName = "DT_CodeReview_Summary";
            ViewDrafting draftingView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .FirstOrDefault(v => v.Name == viewName);

            if (draftingView == null)
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                ViewFamilyType draftingViewType = collector
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);

                if (draftingViewType == null)
                    throw new InvalidOperationException("專案中找不到『繪圖視圖』的類型，無法建立報表。");

                draftingView = ViewDrafting.Create(doc, draftingViewType.Id);
                draftingView.Name = viewName;
                
                // 設定比例為 1:1，這樣繪製的英呎單位在圖紙上即為實際物理尺寸
                draftingView.Scale = 1;
            }

            return draftingView;
        }

        private void ClearViewContents(Document doc, ViewDrafting view)
        {
            var collector = new FilteredElementCollector(doc, view.Id)
                .WherePasses(new ElementMulticlassFilter(new List<Type> { typeof(CurveElement), typeof(TextNote) }))
                .Select(e => e.Id)
                .ToList();

            if (collector.Any())
            {
                doc.Delete(collector);
            }
        }

        private void DrawReportToView(Document doc, ViewDrafting view, ReviewProject project)
        {
            // 表格尺寸參數 (以英呎為單位，1 英呎 = 304.8 mm)
            // A1 圖紙寬度約 2.76 英呎 (841mm)，高度約 1.95 英呎 (594mm)
            // 我們設計表格總寬度為 2.3 英呎 (約 700mm)
            double startX = 0.0;
            double width = 2.3;
            double currentY = 0.0;

            // 1. 繪製標題與基本資訊
            DrawTitle(doc, view, project, startX, ref currentY);

            // 2. 繪製統計條 (Progress Bar)
            DrawStatistics(doc, view, project, startX, ref currentY);

            currentY -= 0.1; // 與表格的間距

            // 3. 表格欄位寬度分配 (總計 2.3)
            double colCode = 0.22;   // 法規編號
            double colName = 0.45;   // 法規項目
            double colCategory = 0.23; // 分類
            double colStatus = 0.35;   // 狀態
            double colAssignee = 0.25; // 負責人
            double colComment = 0.80;  // 說明與備註

            double[] colWidths = new[] { colCode, colName, colCategory, colStatus, colAssignee, colComment };
            string[] headers = new[] { "法規編號", "檢討項目", "分類", "審查狀態", "指派負責人", "缺失說明 / 備註" };

            // 4. 繪製表頭
            double headerHeight = 0.08;
            DrawRow(doc, view, startX, currentY, headerHeight, colWidths, headers, true);
            currentY -= headerHeight;

            // 5. 繪製表格內容
            double rowHeight = 0.12;
            foreach (var item in project.Items)
            {
                // 如果該項目沒有異常，或者為手動/半自動且已通過，顯示一列
                if (item.Results == null || !item.Results.Any())
                {
                    string[] rowData = new[]
                    {
                        item.RuleCode,
                        item.RuleName,
                        item.Category,
                        item.StatusDisplay,
                        item.Assignee ?? "無",
                        string.IsNullOrEmpty(item.Comment) ? "檢核通過或無異常" : item.Comment
                    };
                    DrawRow(doc, view, startX, currentY, rowHeight, colWidths, rowData, false);
                    currentY -= rowHeight;
                }
                else
                {
                    // 若有異常元件，則將異常逐條列出，以方便讀者看圖紙定位
                    foreach (var res in item.Results)
                    {
                        string statusText = item.StatusDisplay;
                        if (!res.Passed)
                        {
                            statusText = "🔴 未通過 (ID: " + res.ElementId + ")";
                        }

                        string[] rowData = new[]
                        {
                            item.RuleCode,
                            item.RuleName,
                            item.Category,
                            statusText,
                            item.Assignee ?? "無",
                            $"[{res.LevelName}] {res.Message}"
                        };
                        DrawRow(doc, view, startX, currentY, rowHeight, colWidths, rowData, false);
                        currentY -= rowHeight;
                    }
                }
            }

            // 6. 繪製底線封口
            DrawLine(doc, view, startX, currentY, startX + width, currentY, true);
        }

        private void DrawTitle(Document doc, ViewDrafting view, ReviewProject project, double startX, ref double currentY)
        {
            ElementId defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            // 1. 主標題
            TextNoteOptions titleOpts = new TextNoteOptions
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Top,
                TypeId = defaultTextTypeId
            };
            // 由於無法動態放大字體（Revit API 需要複製 TextNoteType），我們以底線與粗體標記主標題
            string titleText = "DT_CodeReview 建築法規檢核成果總表";
            TextNote titleNote = TextNote.Create(doc, view.Id, new XYZ(startX, currentY, 0), titleText, titleOpts);
            
            currentY -= 0.08;

            // 2. 畫一條標題底粗線
            DrawLine(doc, view, startX, currentY, startX + 2.3, currentY, true);
            currentY -= 0.04;

            // 3. 基本專案資訊 (兩列並排)
            string infoLeft = $"專案名稱：{project.ProjectName}\n建物類型：{project.BuildingType}";
            string infoRight = $"載入模板：{project.ReviewTemplate}\n產出時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";

            TextNoteOptions infoOpts = new TextNoteOptions
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Top,
                TypeId = defaultTextTypeId
            };
            TextNote.Create(doc, view.Id, new XYZ(startX, currentY, 0), infoLeft, infoOpts);
            TextNote.Create(doc, view.Id, new XYZ(startX + 1.2, currentY, 0), infoRight, infoOpts);

            currentY -= 0.12;
        }

        private void DrawStatistics(Document doc, ViewDrafting view, ReviewProject project, double startX, ref double currentY)
        {
            int totalItems = project.Items.Count;
            int approvedItems = 0;
            int failedItems = 0;
            int otherItems = 0;

            foreach (var item in project.Items)
            {
                if (item.Status == TrackingStatus.Approved) approvedItems++;
                else if (item.Status == TrackingStatus.Created || item.Status == TrackingStatus.Rejected) failedItems++;
                else otherItems++;
            }

            double approvedPercent = totalItems > 0 ? (double)approvedItems / totalItems * 100 : 0;
            
            // 使用 Unicode 字元拼裝一個 ProgressBar 條
            int totalChars = 20;
            int filledChars = (int)Math.Round((approvedPercent / 100) * totalChars);
            string bar = new string('█', filledChars) + new string('░', totalChars - filledChars);

            string statText = $"專案法規檢討進度： [{bar}] {approvedPercent:F1}%  (通過: {approvedItems} / 缺失: {failedItems} / 待審: {otherItems})";

            ElementId defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            TextNoteOptions statOpts = new TextNoteOptions
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Top,
                TypeId = defaultTextTypeId
            };

            TextNote.Create(doc, view.Id, new XYZ(startX, currentY, 0), statText, statOpts);
            currentY -= 0.06;
        }

        private void DrawRow(Document doc, ViewDrafting view, double startX, double y, double height, double[] colWidths, string[] texts, bool isHeader)
        {
            double currentX = startX;

            // 1. 繪製該列頂部的水平分割線
            DrawLine(doc, view, startX, y, startX + colWidths.Sum(), y, isHeader);

            // 2. 填入各儲存格文字與繪製右側垂直線
            ElementId defaultTextTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            TextNoteOptions textOpts = new TextNoteOptions
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
                VerticalAlignment = VerticalTextAlignment.Middle,
                TypeId = defaultTextTypeId
            };

            for (int i = 0; i < colWidths.Length; i++)
            {
                string text = i < texts.Length ? (texts[i] ?? "") : "";
                
                // 表格儲存格的文字對齊：水平 padding 0.01 英呎，垂直置中在 row 內
                double textX = currentX + 0.01;
                double textY = y - (height / 2.0);

                TextNote.Create(doc, view.Id, new XYZ(textX, textY, 0), text, textOpts);

                currentX += colWidths[i];

                // 繪製垂直儲存格邊線
                DrawLine(doc, view, currentX, y, currentX, y - height, false);
            }

            // 3. 繪製最左側的垂直外邊線
            DrawLine(doc, view, startX, y, startX, y - height, false);
        }

        private void DrawLine(Document doc, ViewDrafting view, double x1, double y1, double x2, double y2, bool isThick)
        {
            XYZ p1 = new XYZ(x1, y1, 0);
            XYZ p2 = new XYZ(x2, y2, 0);
            Line line = Line.CreateBound(p1, p2);
            doc.Create.NewDetailCurve(view, line);
            // 註：這裏採用 Revit 預設的 Detail Line。若專案有特定線型，可再調用線型樣式。
        }

        private ViewSheet GetOrCreateReportSheet(Document doc)
        {
            string sheetNumber = "G-101";
            string sheetName = "DT_CodeReview 法規檢核報告";

            ViewSheet sheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => s.SheetNumber == sheetNumber);

            if (sheet == null)
            {
                // 優先尋找圖框 (TitleBlock) 族群
                FilteredElementCollector tbCollector = new FilteredElementCollector(doc);
                Element titleBlock = tbCollector
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstOrDefault();

                if (titleBlock != null)
                {
                    sheet = ViewSheet.Create(doc, titleBlock.Id);
                }
                else
                {
                    // 無圖框時，建立一個空白無圖框圖紙
                    sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                }

                sheet.SheetNumber = sheetNumber;
                sheet.Name = sheetName;
            }

            return sheet;
        }

        private void PlaceViewOnSheet(Document doc, ViewSheet sheet, ViewDrafting view)
        {
            // 如果視圖已經在圖紙上，先刪除舊 Viewport
            var placedViews = sheet.GetAllPlacedViews();
            if (placedViews.Contains(view.Id))
            {
                var viewport = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(vp => vp.ViewId == view.Id);

                if (viewport != null)
                {
                    doc.Delete(viewport.Id);
                }
            }

            // 計算圖紙中心。標準 A1 圖紙中心坐標約為 (1.38, 0.97) 英呎。如果是無邊框，預設放 (1.0, 1.0)
            XYZ center = new XYZ(1.38, 0.97, 0);

            // 若有圖框，取得圖框大小以計算中心
            var titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .FirstOrDefault();

            if (titleBlock != null)
            {
                BoundingBoxXYZ bbox = titleBlock.get_BoundingBox(sheet);
                if (bbox != null)
                {
                    center = (bbox.Max + bbox.Min) / 2.0;
                }
            }

            Viewport.Create(doc, sheet.Id, view.Id, center);
        }
    }
}
