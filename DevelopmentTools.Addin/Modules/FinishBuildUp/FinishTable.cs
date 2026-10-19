using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace DevelopmentTools.FinishBuildUp
{
    /// <summary>裝修表的一列：地坪裝修代碼與其厚度、是否退縮（埋入結構降板內）。</summary>
    [DataContract]
    public class FinishSpec
    {
        [DataMember(Name = "code")] public string Code { get; set; }              // 房間「地板裝修」填的代碼，例如 FA4、FI1-1
        [DataMember(Name = "floorType")] public string FloorType { get; set; }    // 對應的地坪樓板類型，例如 地坪_FA4(不退裝修)
        [DataMember(Name = "thicknessMm")] public double ThicknessMm { get; set; }
        [DataMember(Name = "recessed")] public bool Recessed { get; set; }        // true＝退縮（面層頂＝完成面）；false＝不退裝修（面層鋪在完成面上）
        [DataMember(Name = "flOffsetMm")] public double FlOffsetMm { get; set; }  // 完成面相對樓層的高差（例：浴廁 -20）
        [DataMember(Name = "description")] public string Description { get; set; }
        [DataMember(Name = "source")] public string Source { get; set; }          // 模型類型／匯入／手動

        /// <summary>面層底（結構＋增築要做到的高度）相對完成面的距離，mm（向下為負）。</summary>
        public double BottomBelowFlMm => Recessed ? -ThicknessMm : 0;
    }

    /// <summary>增築規則與裝修表，依專案存檔（%APPDATA%\DevelopmentTools\FinishBuildUp\projects）。</summary>
    [DataContract]
    public class BuildUpSettings
    {
        [DataMember(Name = "finishes")] public List<FinishSpec> Finishes { get; set; }
        [DataMember(Name = "typePattern")] public string TypePattern { get; set; }          // 增築樓板類型命名，{0}＝公分
        [DataMember(Name = "baseType")] public string BaseType { get; set; }                // 複製新厚度類型時的來源類型
        [DataMember(Name = "materialPattern")] public string MaterialPattern { get; set; }  // 增築材料命名，{0}＝公分（有就套用）
        [DataMember(Name = "infoParam")] public string InfoParam { get; set; }              // 寫入「板上增築6cm」的參數名
        [DataMember(Name = "worksetKeyword")] public string WorksetKeyword { get; set; }
        [DataMember(Name = "minGapMm")] public double MinGapMm { get; set; }                // 小於此值不做增築
        [DataMember(Name = "reviewGapMm")] public double ReviewGapMm { get; set; }          // 大於此值標示「需確認」
        [DataMember(Name = "searchDepthMm")] public double SearchDepthMm { get; set; }      // 從完成面往下找結構的深度
        [DataMember(Name = "roundMm")] public double RoundMm { get; set; }                  // 增築厚度取整
        [DataMember(Name = "excludeKeywords")] public List<string> ExcludeKeywords { get; set; } // 這些名稱的樓板不算結構（面層、增築、防水…）
        [DataMember(Name = "includeLinks")] public bool IncludeLinks { get; set; }
        [DataMember(Name = "createFinishFloors")] public bool CreateFinishFloors { get; set; }
        [DataMember(Name = "keepRoomsWithExisting")] public bool KeepRoomsWithExisting { get; set; }   // false＝已有人工增築的房間預設不勾
        public bool SkipRoomsWithExisting => !KeepRoomsWithExisting;

        public void Normalize()
        {
            if (Finishes == null) Finishes = new List<FinishSpec>();
            if (string.IsNullOrWhiteSpace(TypePattern)) TypePattern = "樓板_{0}cm 增築";
            if (string.IsNullOrWhiteSpace(MaterialPattern)) MaterialPattern = "增築_{0}cm";
            if (string.IsNullOrWhiteSpace(InfoParam)) InfoParam = "增築資訊";
            if (string.IsNullOrWhiteSpace(WorksetKeyword)) WorksetKeyword = "增築";
            if (MinGapMm <= 0) MinGapMm = 5;
            if (ReviewGapMm <= 0) ReviewGapMm = 300;
            if (SearchDepthMm <= 0) SearchDepthMm = 3500;
            if (RoundMm <= 0) RoundMm = 1;
            if (ExcludeKeywords == null || ExcludeKeywords.Count == 0)
                ExcludeKeywords = new List<string> { "增築", "地坪", "裝修", "面層", "防水", "粉刷", "鋪面", "地磚", "石材", "收邊", "回填" };
        }

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevelopmentTools", "FinishBuildUp");

        private static string FileFor(string key)
        {
            using (var sha = SHA1.Create())
            {
                string h = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key ?? ""))).Replace("-", "").Substring(0, 16);
                return Path.Combine(Folder, "projects", h + ".json");
            }
        }

        private static DataContractJsonSerializer Ser() =>
            new DataContractJsonSerializer(typeof(BuildUpSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static BuildUpSettings Load(string key)
        {
            try
            {
                string p = FileFor(key);
                if (File.Exists(p))
                    using (var fs = File.OpenRead(p))
                    {
                        var s = (BuildUpSettings)Ser().ReadObject(fs);
                        s.Normalize();
                        return s;
                    }
            }
            catch { }
            var d = new BuildUpSettings { IncludeLinks = true };
            d.Normalize();
            return d;
        }

        public void Save(string key)
        {
            string p = FileFor(key);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    Ser().WriteObject(w, this);
                File.WriteAllBytes(p, ms.ToArray());
            }
        }

        public string TypeName(double mm) => string.Format(CultureInfo.InvariantCulture, TypePattern, Cm(mm));
        public string MaterialName(double mm) => string.Format(CultureInfo.InvariantCulture, MaterialPattern, Cm(mm));
        public static string Cm(double mm) => (mm / 10.0).ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>裝修表的來源：模型中的地坪類型、CSV 匯入／匯出、代碼比對。</summary>
    public static class FinishTable
    {
        private const double FtToMm = 304.8;

        // 地坪_FA4(不退裝修)、地坪_室_F1-1、地坪_FA14(樓梯)
        private static readonly Regex TypeRx = new Regex(@"^(?:地坪|地板|樓地板)[_\-\s]*(?<室>室[_\-\s]*)?(?<code>[A-Za-z]{1,3}\d+[A-Za-z]?(?:-\d+[A-Za-z]?)?)(?<rest>.*)$");

        /// <summary>由模型中名稱為「地坪_代碼」的樓板類型建立裝修表（厚度取類型厚度，名稱含「不退」者為不退裝修）。</summary>
        public static List<FinishSpec> FromModel(Document doc)
        {
            var list = new List<FinishSpec>();
            foreach (FloorType ft in new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>())
            {
                var m = TypeRx.Match(ft.Name ?? "");
                if (!m.Success) continue;
                string rest = m.Groups["rest"].Value;
                if (rest.Contains("樓梯")) continue;   // 樓梯踏面另計
                string code = m.Groups["code"].Value.ToUpperInvariant();
                if (m.Groups["室"].Success && code.Length > 1 && !Regex.IsMatch(code, @"^[A-Z]I")) code = code.Substring(0, 1) + "I" + code.Substring(1);   // 室_F1-1 → FI1-1
                double t = 0;
                try { t = (ft.GetCompoundStructure()?.GetWidth() ?? 0) * FtToMm; } catch { }
                if (t <= 0) t = (ft.get_Parameter(BuiltInParameter.FLOOR_ATTR_DEFAULT_THICKNESS_PARAM)?.AsDouble() ?? 0) * FtToMm;
                string desc = ft.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "";
                if (list.Any(x => x.Code == code)) continue;
                list.Add(new FinishSpec
                {
                    Code = code, FloorType = ft.Name, ThicknessMm = Math.Round(t, 1), Recessed = !rest.Contains("不退"),
                    Description = desc, Source = "模型類型"
                });
            }
            return list.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
        }

        /// <summary>合併：已存的（使用者改過）優先，模型新出現的代碼補上。</summary>
        public static List<FinishSpec> Merge(List<FinishSpec> saved, List<FinishSpec> fromModel)
        {
            var result = saved.Select(x => x).ToList();
            foreach (var m in fromModel)
                if (!result.Any(x => string.Equals(x.Code, m.Code, StringComparison.OrdinalIgnoreCase))) result.Add(m);
            return result.OrderBy(x => x.Code, StringComparer.Ordinal).ToList();
        }

        /// <summary>依房間的地板裝修代碼找裝修表；容許 FI1-1 / F1-1、全形、空白差異。</summary>
        public static FinishSpec Find(List<FinishSpec> table, string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return null;
            string k = Norm(roomCode);
            var hit = table.FirstOrDefault(x => Norm(x.Code) == k);
            if (hit != null) return hit;
            // 室內代碼 FI1-1 與 F1-1 視為同一個
            hit = table.FirstOrDefault(x => StripI(Norm(x.Code)) == StripI(k));
            if (hit != null) return hit;
            // 房間填了多個代碼（例：FA4/FA6）時取第一個
            string first = Regex.Split(k, @"[/、,，+＋]").FirstOrDefault();
            return first != null && first != k ? Find(table, first) : null;
        }

        private static string StripI(string k) => Regex.Replace(k, @"^([A-Z])I(?=\d)", "$1");

        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s.Trim())
            {
                char ch = c >= '！' && c <= '～' ? (char)(c - 0xFEE0) : c;
                if (!char.IsWhiteSpace(ch)) sb.Append(char.ToUpperInvariant(ch));
            }
            return sb.ToString();
        }

        public static void ExportCsv(string path, IEnumerable<FinishSpec> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("代碼,地坪類型,厚度mm,退縮(是/否),完成面高差mm,說明");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", Q(r.Code), Q(r.FloorType), r.ThicknessMm.ToString("0.#", CultureInfo.InvariantCulture),
                    r.Recessed ? "是" : "否", r.FlOffsetMm.ToString("0.#", CultureInfo.InvariantCulture), Q(r.Description)));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>讀 CSV（Excel 另存 CSV）：代碼, 地坪類型, 厚度mm, 退縮, 完成面高差mm, 說明。第一列為標題。</summary>
        public static List<FinishSpec> ImportCsv(string path)
        {
            var list = new List<FinishSpec>();
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                var c = SplitCsv(lines[i]);
                if (c.Count < 3 || string.IsNullOrWhiteSpace(c[0])) continue;
                double.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double t);
                double off = 0;
                if (c.Count > 4) double.TryParse(c[4], NumberStyles.Float, CultureInfo.InvariantCulture, out off);
                string rec = c.Count > 3 ? c[3].Trim() : "是";
                list.Add(new FinishSpec
                {
                    Code = c[0].Trim(), FloorType = c[1].Trim(), ThicknessMm = t,
                    Recessed = !(rec == "否" || rec.Equals("N", StringComparison.OrdinalIgnoreCase) || rec.Contains("不退")),
                    FlOffsetMm = off, Description = c.Count > 5 ? c[5] : "", Source = "匯入"
                });
            }
            return list;
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private static List<string> SplitCsv(string line)
        {
            var r = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') q = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') q = true;
                else if (ch == ',') { r.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            r.Add(sb.ToString());
            return r;
        }
    }
}
