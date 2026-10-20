// 釋疑擷取：截圖 → 帶入情境 → 表單 → 草稿或收件匣
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using View = Autodesk.Revit.DB.View;

namespace RfiDeckAgent.Core
{
    class RevitWindow : IWin32Window
    {
        readonly IntPtr h;
        public RevitWindow(IntPtr handle) { h = handle; }
        public IntPtr Handle { get { return h; } }
    }

    public static class CaptureCommand
    {
        public static Result Execute(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("釋疑擷取", "請先開啟模型。");
                return Result.Cancelled;
            }
            Document doc = uidoc.Document;
            View view = uidoc.ActiveView;
            if (view is ViewSchedule || view.ViewType == ViewType.ProjectBrowser || view.ViewType == ViewType.SystemBrowser)
            {
                TaskDialog.Show("釋疑擷取", "請切換到 3D、平面、剖面或立面視圖再擷取。");
                return Result.Cancelled;
            }

            RfiDraft d = Inbox.LoadDraft();
            bool isNew = d == null;
            if (isNew)
            {
                d = new RfiDraft();
                d.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                d.Author = Environment.UserName;
            }
            else if (d.Project.Length > 0 && d.Project != doc.Title)
            {
                TaskDialogResult r = TaskDialog.Show("釋疑擷取",
                    "有一筆未完成的草稿來自模型「" + d.Project + "」。\n是 = 繼續加到該草稿；否 = 放棄舊草稿重新開始。",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel);
                if (r == TaskDialogResult.Cancel) return Result.Cancelled;
                if (r == TaskDialogResult.No)
                {
                    Inbox.ClearDraft();
                    d = new RfiDraft();
                    d.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    d.Author = Environment.UserName;
                    isNew = true;
                }
            }

            ModelContext c = ModelContext.Collect(uidoc);
            d.Project = doc.Title;
            d.ProjectPath = doc.PathName;
            if (string.IsNullOrEmpty(d.Location)) d.Location = c.LocationText;
            if (string.IsNullOrEmpty(d.Level)) d.Level = c.LevelName;
            if (string.IsNullOrEmpty(d.Grid)) d.Grid = c.GridText;
            if (string.IsNullOrEmpty(d.Building)) d.Building = c.Building;
            if (string.IsNullOrEmpty(d.CellPos)) d.CellPos = c.CellPos;
            if (string.IsNullOrEmpty(d.Anchors)) d.Anchors = c.Anchors;
            if (string.IsNullOrEmpty(d.PositionNote)) d.PositionNote = c.PositionNote;
            foreach (string id in c.ElementIds) if (!d.ElementIds.Contains(id)) d.ElementIds.Add(id);
            foreach (string s in c.ElementDescriptions) if (!d.Elements.Contains(s)) d.Elements.Add(s);

            // 截圖
            string shotPath = Inbox.NewShotPath(d);
            string got = null;
            try { got = Capture.ActiveView(uidoc, shotPath); }
            catch (Exception ex) { TaskDialog.Show("釋疑擷取", "截圖失敗：" + ex.Message); }
            if (got != null)
            {
                ShotInfo si = new ShotInfo();
                si.File = Path.GetFileName(got);
                si.View = view.Name;
                si.Time = DateTime.Now.ToString("HH:mm:ss");
                si.Caption = DefaultCaption(view);
                d.Shots.Add(si);
            }
            string history = AutoAddDrawing(d);
            Inbox.SaveDraft(d);

            string note = string.Format("模型：{0}\n視圖：{1}　樓層：{2}　柱線：{3}\n選取元件：{4}" +
                (c.Anchors.Length > 0 ? "\n周圍結構柱：" + c.Anchors : "") + (history.Length > 0 ? "\n⚠ " + history : ""),
                d.Project, view.Name, c.LevelName.Length > 0 ? c.LevelName : "—",
                c.GridText.Length > 0 ? c.GridText + (c.PointSource.Length > 0 ? "（依" + c.PointSource + "）" : "") : "—",
                c.ElementDescriptions.Count > 0 ? string.Join("；", c.ElementDescriptions.GetRange(0, Math.Min(3, c.ElementDescriptions.Count))) +
                    (c.ElementDescriptions.Count > 3 ? string.Format(" 等 {0} 個", c.ElementDescriptions.Count) : "") : "（未選取，可先選取問題元件再擷取，會自動帶入柱線）");

            CaptureForm form = new CaptureForm(d, note);
            form.ShowDialog(new RevitWindow(Process.GetCurrentProcess().MainWindowHandle));
            switch (form.Action)
            {
                case CaptureAction.Commit:
                    string folder = Inbox.Commit(d);
                    int pending = Inbox.ListPending().Count;
                    string sync = "";
                    string slideUrl = "";
                    if (form.SendToSlides)
                    {
                        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
                        Dictionary<string, object> r = null;
                        try { r = GSlides.Send(folder, d); }
                        catch (Exception ex) { r = new Dictionary<string, object>(); r["ok"] = false; r["error"] = ex.Message; }
                        System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
                        if (GSlides.Ok(r))
                        {
                            sync = "✔ 已同步到 Google 共用簡報：" + GSlides.Str(r, "no");
                            slideUrl = GSlides.Str(r, "url");
                        }
                        else sync = "✖ 同步 Google 簡報失敗：" + GSlides.Str(r, "error") + "\n（已存在收件匣，可在「產生簡報」清單補送）";
                    }
                    TaskDialog td = new TaskDialog("釋疑擷取");
                    td.MainInstruction = sync.StartsWith("✔") ? sync : "已存入收件匣";
                    td.MainContent = (sync.Length > 0 && !sync.StartsWith("✔") ? sync + "\n\n" : "") +
                        string.Format("收件匣：{0}\n收件匣目前共 {1} 筆。按「產生簡報」可勾選做成本機 PPT。", Path.GetFileName(folder), pending);
                    if (slideUrl.Length > 0) td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "開啟共用簡報，看這一頁");
                    td.CommonButtons = TaskDialogCommonButtons.Close;
                    if (td.Show() == TaskDialogResult.CommandLink1)
                        GSlides.OpenDeck(Process.GetCurrentProcess().MainWindowHandle, slideUrl);
                    break;
                case CaptureAction.KeepDraft:
                case CaptureAction.Cancel:
                    Inbox.SaveDraft(d);
                    break;
                case CaptureAction.Discard:
                    Inbox.ClearDraft();
                    break;
            }
            return Result.Succeeded;
        }

        /// <summary>自動加入對照圖說：索引最相關、且已對到柱線的平面圖（每筆擷取只自動加一次）</summary>
        static string AutoAddDrawing(RfiDraft d)
        {
            string history = "";
            try
            {
                if (string.IsNullOrEmpty(d.Grid)) return "";
                bool already = d.Shots.Exists(s => s.View == "圖說(自動)");
                string idx = Path.Combine(Config.Root, @"03_資料庫\圖說\圖說查找索引.db");
                if (!File.Exists(idx) && !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"DT_AIAgent\seek.json"))) return "";
                Dictionary<string, object> r = SeekForm.Run(d, d.Building);
                object ok, cs;
                if (r == null || !r.TryGetValue("ok", out ok) || !(ok is bool) || !(bool)ok) return "";
                history = SeekForm.HistoryText(r);
                if (already) return history;
                if (!r.TryGetValue("candidates", out cs) || !(cs is System.Collections.ArrayList)) return history;
                foreach (object o in (System.Collections.ArrayList)cs)
                {
                    Dictionary<string, object> c = o as Dictionary<string, object>;
                    if (c == null) continue;
                    object loc;
                    if (!c.TryGetValue("located", out loc) || !(loc is bool) || !(bool)loc) continue;
                    string png = Convert.ToString(c["png"]);
                    if (!File.Exists(png)) continue;
                    string dst = Inbox.NewShotPath(d);
                    File.Copy(png, dst, true);
                    ShotInfo si = new ShotInfo();
                    si.File = Path.GetFileName(dst);
                    si.Caption = Convert.ToString(c["caption"]);
                    si.View = "圖說(自動)";
                    si.Time = DateTime.Now.ToString("HH:mm:ss");
                    d.Shots.Add(si);
                    string no = Convert.ToString(c["drawing_no"]) + " " + Convert.ToString(c["title"]);
                    if (string.IsNullOrEmpty(d.Drawing)) d.Drawing = no.Trim();
                    break;          // 只自動加最相關的一張
                }
            }
            catch { }
            return history;
        }

        static string DefaultCaption(View v)
        {
            switch (v.ViewType)
            {
                case ViewType.ThreeD: return "3D 透視圖";
                case ViewType.FloorPlan: return "平面圖 - " + v.Name;
                case ViewType.CeilingPlan: return "天花平面圖 - " + v.Name;
                case ViewType.Section: return "剖面圖 - " + v.Name;
                case ViewType.Elevation: return "立面圖 - " + v.Name;
                default: return v.Name;
            }
        }
    }
}
