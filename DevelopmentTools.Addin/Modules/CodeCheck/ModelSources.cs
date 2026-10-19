using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>每個模型要讀哪些內容（拆檔時例如機電模型只讀設備、結構模型只用來量淨高）。</summary>
    [Flags]
    public enum SourceParts
    {
        None = 0,
        Spaces = 1,       // 樓層、房間、樓板面積、女兒牆
        Openings = 2,     // 門、窗
        Circulation = 4,  // 樓梯、坡道、欄杆
        Equipment = 8,    // 消防與機電設備、昇降機
        Parking = 16,     // 停車位
        Areas = 32,       // 防火區劃（面積配置）
        All = Spaces | Openings | Circulation | Equipment | Parking | Areas
    }

    /// <summary>專案設定中記錄的檢討範圍（一筆一個模型）。</summary>
    [DataContract]
    public class SourceEntry
    {
        [DataMember(Name = "key")] public string Key { get; set; }
        [DataMember(Name = "kind")] public string Kind { get; set; }        // host / link / file
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "path")] public string Path { get; set; }
        [DataMember(Name = "include")] public bool Include { get; set; }
        [DataMember(Name = "parts")] public int Parts { get; set; }

        // 執行期資訊（不存檔）
        public string Status;
        public bool Available;
        public int Depth;

        public SourceParts PartFlags
        {
            get => (SourceParts)Parts;
            set => Parts = (int)value;
        }

        public string KindText => Kind == "host" ? "目前檔案" : Kind == "link" ? (Depth > 1 ? "巢狀連結" : "已連結") : "未連結檔案";
    }

    /// <summary>
    /// 一個參與檢討的模型：目前檔案、已載入的 Revit 連結，或檢討時暫時連結進來的模型檔。
    /// Transform 把該模型內部座標轉成目前檔案的內部座標，所有量測都在目前檔案座標下進行。
    /// </summary>
    public class ModelSource
    {
        public int Index;
        public string Key;
        public string Name;
        public Document Doc;
        public Transform T;
        public ElementId LinkInstanceId = ElementId.InvalidElementId;   // 頂層連結實體（目前檔案中可選取的那一個）
        public bool IsHost, IsTemporary;
        public string FilePath;
        public SourceParts Parts;

        public bool Reads(SourceParts p) => (Parts & p) == p;
        public XYZ P(XYZ p) => p == null ? null : (T == null ? p : T.OfPoint(p));
        public XYZ V(XYZ v) => v == null ? null : (T == null ? v : T.OfVector(v));
        public double Z(double z) => T == null ? z : T.OfPoint(new XYZ(0, 0, z)).Z;

        /// <summary>把元素外框轉到目前檔案座標後重新求軸向外框。</summary>
        public BoundingBoxXYZ Box(BoundingBoxXYZ bb)
        {
            if (bb == null || T == null || T.IsIdentity) return bb;
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new XYZ((i & 1) == 0 ? bb.Min.X : bb.Max.X, (i & 2) == 0 ? bb.Min.Y : bb.Max.Y, (i & 4) == 0 ? bb.Min.Z : bb.Max.Z);
                var p = T.OfPoint(c);
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); z0 = Math.Min(z0, p.Z);
                x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); z1 = Math.Max(z1, p.Z);
            }
            return new BoundingBoxXYZ { Min = new XYZ(x0, y0, z0), Max = new XYZ(x1, y1, z1) };
        }
    }

    /// <summary>找出可參與檢討的模型、依設定建立 ModelSource（必要時暫時連結未連結的模型檔）。</summary>
    public static class ModelSources
    {
        private static readonly string[] StructureKeys = { "STR", "結構", "_S_", "-S-", "_ST_", "-ST-", "STRUCT" };
        private static readonly string[] MepKeys = { "MEP", "機電", "消防", "FP_", "_FP", "-FP", "給排水", "空調", "HVAC", "電氣", "ELE", "PLB", "水電", "弱電", "MECH", "_M_", "_E_", "_P_" };

        /// <summary>依檔名猜這個模型該讀的內容：結構模型只參與淨高量測，機電模型只讀設備，其餘（建築）全部讀。</summary>
        public static SourceParts DefaultParts(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            if (StructureKeys.Any(k => n.Contains(k.ToUpperInvariant()))) return SourceParts.None;
            if (MepKeys.Any(k => n.Contains(k.ToUpperInvariant()))) return SourceParts.Equipment;
            return SourceParts.All;
        }

        public static string PartsText(SourceParts p)
        {
            if (p == SourceParts.All) return "全部";
            if (p == SourceParts.None) return "僅供淨高量測";
            var list = new List<string>();
            if ((p & SourceParts.Spaces) != 0) list.Add("房間樓層");
            if ((p & SourceParts.Openings) != 0) list.Add("門窗");
            if ((p & SourceParts.Circulation) != 0) list.Add("樓梯坡道欄杆");
            if ((p & SourceParts.Equipment) != 0) list.Add("設備");
            if ((p & SourceParts.Parking) != 0) list.Add("車位");
            if ((p & SourceParts.Areas) != 0) list.Add("防火區劃");
            return string.Join("、", list);
        }

        private static string FileNameOf(Document d, string fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(d?.PathName)) return Path.GetFileNameWithoutExtension(d.PathName);
            }
            catch { }
            return d?.Title ?? fallback;
        }

        /// <summary>列出目前檔案、所有連結（含巢狀）與設定中記錄的未連結檔案，並套上先前的勾選。</summary>
        public static List<SourceEntry> Discover(Document host, ProjectSettings s)
        {
            var saved = (s.Sources ?? new List<SourceEntry>()).ToDictionary(e => e.Key, e => e);
            var list = new List<SourceEntry>();

            SourceEntry Merge(SourceEntry e, bool defaultInclude)
            {
                if (saved.TryGetValue(e.Key, out var old))
                {
                    e.Include = old.Include && e.Available;
                    e.Parts = old.Parts;
                }
                else
                {
                    e.Include = defaultInclude && e.Available;
                    e.PartFlags = e.Kind == "host" ? SourceParts.All : DefaultParts(e.Name);
                }
                list.Add(e);
                return e;
            }

            Merge(new SourceEntry { Key = "host", Kind = "host", Name = FileNameOf(host, "目前檔案"), Path = host.PathName, Available = true, Status = "開啟中", Depth = 0 }, true);

            var nameCount = new Dictionary<string, int>();
            void Walk(Document d, string prefix, int depth)
            {
                if (depth > 4) return;
                foreach (RevitLinkInstance inst in new FilteredElementCollector(d).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = null;
                    try { ld = inst.GetLinkDocument(); } catch { }
                    var type = d.GetElement(inst.GetTypeId()) as RevitLinkType;
                    if (depth == 1 && type != null && type.IsNestedLink) continue;   // 巢狀連結由上層模型展開
                    string name = ld != null ? FileNameOf(ld, type?.Name) : Path.GetFileNameWithoutExtension(type?.Name ?? inst.Name);
                    string key = prefix + "/" + inst.UniqueId;
                    nameCount[name] = nameCount.TryGetValue(name, out int c) ? c + 1 : 1;
                    string shown = nameCount[name] > 1 ? $"{name}（第 {nameCount[name]} 個）" : name;
                    string path = null;
                    try { path = ld?.PathName; } catch { }
                    Merge(new SourceEntry
                    {
                        Key = key, Kind = "link", Name = shown, Path = path, Depth = depth,
                        Available = ld != null, Status = ld != null ? "已載入" : "未載入（請在「管理連結」重新載入）"
                    }, true);
                    if (ld != null) Walk(ld, key, depth + 1);
                }
            }
            Walk(host, "link:", 1);

            foreach (var e in s.Sources ?? new List<SourceEntry>())
            {
                if (e.Kind != "file") continue;
                bool exists = !string.IsNullOrEmpty(e.Path) && File.Exists(e.Path);
                bool alreadyLinked = list.Any(x => x.Kind != "file" && !string.IsNullOrEmpty(x.Path) &&
                                                   string.Equals(SafeFull(x.Path), SafeFull(e.Path), StringComparison.OrdinalIgnoreCase));
                var ne = new SourceEntry
                {
                    Key = e.Key, Kind = "file", Name = e.Name, Path = e.Path, Depth = 0, Available = exists && !alreadyLinked,
                    Status = !exists ? "找不到檔案" : alreadyLinked ? "已是連結（改用連結那一筆）" : "檢討時暫時連結"
                };
                Merge(ne, true);
            }
            return list;
        }

        private static string SafeFull(string p)
        {
            try { return Path.GetFullPath(p); } catch { return p; }
        }

        public static SourceEntry NewFileEntry(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return new SourceEntry
            {
                Key = "file:" + path.ToLowerInvariant(), Kind = "file", Name = name, Path = path, Include = true,
                PartFlags = DefaultParts(name), Available = File.Exists(path), Status = "檢討時暫時連結"
            };
        }

        /// <summary>
        /// 在已開啟的交易中建立 ModelSource 清單。未連結的模型檔會暫時以共用座標連結進來（失敗則原點對原點），
        /// 交易回復後這些暫時連結就會消失。
        /// </summary>
        public static List<ModelSource> Build(Document host, List<SourceEntry> entries, List<string> notes, Action<string> progress)
        {
            var result = new List<ModelSource>();
            var byKey = entries.ToDictionary(e => e.Key, e => e);

            void Add(string key, string name, Document d, Transform t, ElementId topInst, bool isHost, bool temp, string path)
            {
                if (!byKey.TryGetValue(key, out var e) || !e.Include) return;
                result.Add(new ModelSource
                {
                    Index = result.Count, Key = key, Name = name, Doc = d, T = t ?? Transform.Identity, LinkInstanceId = topInst ?? ElementId.InvalidElementId,
                    IsHost = isHost, IsTemporary = temp, FilePath = path, Parts = e.PartFlags
                });
            }

            Add("host", byKey["host"].Name, host, Transform.Identity, null, true, false, host.PathName);

            void Walk(Document d, string prefix, Transform parentT, ElementId topInst, int depth)
            {
                if (depth > 4) return;
                foreach (RevitLinkInstance inst in new FilteredElementCollector(d).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document ld = null;
                    try { ld = inst.GetLinkDocument(); } catch { }
                    if (ld == null) continue;
                    var type = d.GetElement(inst.GetTypeId()) as RevitLinkType;
                    if (depth == 1 && type != null && type.IsNestedLink) continue;
                    Transform t = parentT.Multiply(inst.GetTotalTransform());
                    ElementId top = topInst ?? inst.Id;
                    string key = prefix + "/" + inst.UniqueId;
                    string name = byKey.TryGetValue(key, out var e) ? e.Name : FileNameOf(ld, inst.Name);
                    Add(key, name, ld, t, top, false, false, SafePath(ld));
                    Walk(ld, key, t, top, depth + 1);
                }
            }
            Walk(host, "link:", Transform.Identity, null, 1);

            // 未連結的模型檔：暫時連結
            foreach (var e in entries.Where(x => x.Kind == "file" && x.Include && x.Available))
            {
                progress?.Invoke($"暫時連結模型檔：{e.Name}…");
                RevitLinkInstance inst = TempLink(host, e.Path, notes);
                if (inst == null) continue;
                Document ld = null;
                try { ld = inst.GetLinkDocument(); } catch { }
                if (ld == null) { notes.Add($"{e.Name}：暫時連結後無法讀取模型內容。"); continue; }
                Transform t = inst.GetTotalTransform();
                Add(e.Key, e.Name, ld, t, inst.Id, false, true, e.Path);
                foreach (RevitLinkInstance sub in new FilteredElementCollector(ld).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document sd = null;
                    try { sd = sub.GetLinkDocument(); } catch { }
                    if (sd == null) continue;
                    string key = e.Key + "/" + sub.UniqueId;
                    if (!byKey.ContainsKey(key))
                    {
                        // 巢狀模型沒有在清單上：與上層同樣的讀取設定
                        byKey[key] = new SourceEntry { Key = key, Kind = "link", Name = FileNameOf(sd, sub.Name), Include = true, Parts = e.Parts };
                    }
                    Add(key, byKey[key].Name, sd, t.Multiply(sub.GetTotalTransform()), inst.Id, false, true, SafePath(sd));
                }
            }

            // 同一個檔案、同一位置被連結兩次（例如 A 連 B、目前檔案也連 B）只算一次
            var unique = new List<ModelSource>();
            foreach (var src in result)
            {
                var dup = unique.FirstOrDefault(u => u.Doc.Equals(src.Doc) && SameTransform(u.T, src.T));
                if (dup != null)
                {
                    notes.Add($"「{src.Name}」與「{dup.Name}」是同一個模型且位置相同，只讀取一次。");
                    continue;
                }
                src.Index = unique.Count;
                unique.Add(src);
            }
            return unique;
        }

        private static string SafePath(Document d)
        {
            try { return d.PathName; } catch { return null; }
        }

        private static bool SameTransform(Transform a, Transform b)
        {
            return a.Origin.IsAlmostEqualTo(b.Origin) && a.BasisX.IsAlmostEqualTo(b.BasisX) && a.BasisY.IsAlmostEqualTo(b.BasisY);
        }

        private static RevitLinkInstance TempLink(Document host, string path, List<string> notes)
        {
            string name = Path.GetFileName(path);
            try
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
                var opts = new RevitLinkOptions(false, new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                LinkLoadResult res = RevitLinkType.Create(host, mp, opts);
                if (res == null || res.ElementId == ElementId.InvalidElementId)
                {
                    notes.Add($"{name}：無法連結（{res?.LoadResult}）。若此檔正在 Revit 中開啟，請先關閉再檢討。");
                    return null;
                }
                try
                {
                    return RevitLinkInstance.Create(host, res.ElementId, ImportPlacement.Shared);
                }
                catch
                {
                    notes.Add($"{name}：與目前檔案沒有共用座標，改以「原點對原點」對位，請確認兩個模型的原點一致。");
                    return RevitLinkInstance.Create(host, res.ElementId, ImportPlacement.Origin);
                }
            }
            catch (Exception ex)
            {
                notes.Add($"{name}：暫時連結失敗（{ex.Message}）。若此檔正在 Revit 中開啟，請先關閉再檢討。");
                return null;
            }
        }
    }

    /// <summary>檢討過程中不跳出 Revit 警告對話框（交易最後一律回復）。</summary>
    public class QuietFailures : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (FailureMessageAccessor f in a.GetFailureMessages())
                if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
            return FailureProcessingResult.Continue;
        }
    }
}
