using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace BIMDrawingFactory.UI.Services
{
    /// <summary>
    /// 一棟建築在模型中的命名方式。
    /// Key：樓層名稱裡的棟別代碼（例如樓層 "OB_2FL" 的 "OB"）。
    /// DisplayName：圖紙與視圖名稱裡出現的棟別文字（例如「行政棟」）。
    /// </summary>
    [DataContract]
    public class BuildingConvention
    {
        [DataMember(Order = 1)] public string Key { get; set; }
        [DataMember(Order = 2)] public string DisplayName { get; set; }
        /// <summary>其他可辨識為這一棟的文字（例如「行政」「OFFICE」）。</summary>
        [DataMember(Order = 3)] public List<string> Aliases { get; set; }
        /// <summary>這一棟的圖名可能寫成「棟名 + B1F」的地下層變體（換棟時保留 B 後綴）。</summary>
        [DataMember(Order = 4)] public bool HasBasementVariant { get; set; }
    }

    /// <summary>某類圖紙（依圖名關鍵字）自動加的尺寸標註。</summary>
    [DataContract]
    public class DimensionRuleConvention
    {
        /// <summary>圖名含任一關鍵字即套用（由上往下第一條符合的規則生效）。</summary>
        [DataMember(Order = 1)] public List<string> SheetNameContains { get; set; }
        [DataMember(Order = 2)] public List<DimensionItemConvention> Items { get; set; }
    }

    [DataContract]
    public class DimensionItemConvention
    {
        /// <summary>ColumnCenter（柱心）、BeamEdge（梁邊）、FloorOpening（板開口）、WallOpening（牆開口）、WallEdge（牆邊）。</summary>
        [DataMember(Order = 1)] public string Semantic { get; set; }
        /// <summary>專案中的尺寸標註類型名稱。</summary>
        [DataMember(Order = 2)] public string StyleName { get; set; }
        [DataMember(Order = 3)] public string DisplayName { get; set; }
    }

    /// <summary>某類圖紙（依圖名關鍵字）自動加的標籤。</summary>
    [DataContract]
    public class TaggingRuleConvention
    {
        [DataMember(Order = 1)] public List<string> SheetNameContains { get; set; }
        [DataMember(Order = 2)] public List<string> SheetNameExcludes { get; set; }
        /// <summary>標籤族群與類型名稱（需已載入專案）。</summary>
        [DataMember(Order = 3)] public string FamilyName { get; set; }
        [DataMember(Order = 4)] public string TypeName { get; set; }
        /// <summary>Floors、Walls 或 Doors。</summary>
        [DataMember(Order = 5)] public string TargetCategory { get; set; }
    }

    /// <summary>
    /// 量化開圖的命名規則。預設值不綁任何特定專案；
    /// 事務所自己的慣例（棟別名稱、圖號前綴）寫在 %APPDATA%\DevelopmentTools\DrawingFactory\conventions.json。
    /// </summary>
    [DataContract]
    public class FactoryConventions
    {
        /// <summary>手動指定的棟別。留空時從樓層名稱前綴自動辨識（例如 A_1FL、B_1FL）。</summary>
        [DataMember(Order = 1)] public List<BuildingConvention> Buildings { get; set; }

        /// <summary>代表「全區／共用」樓層的代碼與文字。</summary>
        [DataMember(Order = 2)] public string SharedBuildingKey { get; set; }
        [DataMember(Order = 3)] public List<string> SharedAliases { get; set; }

        /// <summary>圖號系列前可有可無的前綴（例如 "FY-"）。留空表示沒有。</summary>
        [DataMember(Order = 4)] public string SeriesOptionalPrefix { get; set; }

        /// <summary>圖號中代表系列的正規式（例如 "SB\d+"）。留空時取圖號的前段（例如 "A-1" 取自 "A-1-01"）。</summary>
        [DataMember(Order = 5)] public string SeriesCorePattern { get; set; }

        /// <summary>沒有可用系列時的預設來源系列與目標系列。</summary>
        [DataMember(Order = 6)] public string DefaultSeriesCode { get; set; }
        [DataMember(Order = 7)] public string DefaultTargetSeries { get; set; }

        /// <summary>開啟時預設選取的來源系列（例如 "SB5"）。留空時選第一個。</summary>
        [DataMember(Order = 8)] public string PreferredSourceSeries { get; set; }

        /// <summary>產生圖紙後自動加的尺寸標註與標籤。預設不加，避免用到專案中不存在的類型。</summary>
        [DataMember(Order = 9)] public List<DimensionRuleConvention> DimensionRules { get; set; }
        [DataMember(Order = 10)] public List<TaggingRuleConvention> TaggingRules { get; set; }

        private static FactoryConventions _current;
        private static readonly object Sync = new object();
        // 自動辨識到的棟別代碼（整個 Revit 工作階段共用；反序列化不會執行欄位初始化，所以用 static）
        private static readonly List<string> _detectedKeys = new List<string>();

        public static string FilePath
        {
            get
            {
                return DtStorage.File(@"DrawingFactory\conventions.json", Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DevelopmentTools", "DrawingFactory", "conventions.json"));
            }
        }

        public static FactoryConventions Current
        {
            get
            {
                lock (Sync)
                {
                    if (_current == null) _current = Load();
                    return _current;
                }
            }
        }

        /// <summary>重新讀取設定檔（使用者修改 conventions.json 後呼叫）。</summary>
        public static void Reload()
        {
            lock (Sync) { _current = Load(); }
        }

        public static FactoryConventions CreateDefault()
        {
            return new FactoryConventions
            {
                Buildings = new List<BuildingConvention>(),
                SharedBuildingKey = "ALL",
                SharedAliases = new List<string> { "全區", "ALL" },
                SeriesOptionalPrefix = string.Empty,
                SeriesCorePattern = string.Empty,
                DefaultSeriesCode = "S1",
                DefaultTargetSeries = "S2",
                PreferredSourceSeries = string.Empty,
                DimensionRules = new List<DimensionRuleConvention>(),
                TaggingRules = new List<TaggingRuleConvention>()
            };
        }

        private static FactoryConventions Load()
        {
            FactoryConventions loaded = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                    {
                        loaded = (FactoryConventions)new DataContractJsonSerializer(typeof(FactoryConventions)).ReadObject(stream);
                    }
                }
                else
                {
                    // 第一次使用時寫出預設檔，方便使用者照格式修改
                    Save(CreateDefault());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[FactoryConventions] 讀取失敗，改用預設值：" + ex.Message);
            }
            return Normalize(loaded ?? CreateDefault());
        }

        public static void Save(FactoryConventions conventions)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = new MemoryStream())
                {
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  "))
                    {
                        new DataContractJsonSerializer(typeof(FactoryConventions)).WriteObject(writer, conventions);
                    }
                    File.WriteAllBytes(FilePath, stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[FactoryConventions] 寫入失敗：" + ex.Message);
            }
        }

        private static FactoryConventions Normalize(FactoryConventions c)
        {
            var d = CreateDefault();
            if (c.Buildings == null) c.Buildings = new List<BuildingConvention>();
            c.Buildings = c.Buildings.Where(b => b != null && !string.IsNullOrWhiteSpace(b.Key)).ToList();
            foreach (var b in c.Buildings)
            {
                b.Key = b.Key.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(b.DisplayName)) b.DisplayName = b.Key;
                if (b.Aliases == null) b.Aliases = new List<string>();
            }
            if (string.IsNullOrWhiteSpace(c.SharedBuildingKey)) c.SharedBuildingKey = d.SharedBuildingKey;
            c.SharedBuildingKey = c.SharedBuildingKey.Trim().ToUpperInvariant();
            if (c.SharedAliases == null) c.SharedAliases = d.SharedAliases;
            if (c.SeriesOptionalPrefix == null) c.SeriesOptionalPrefix = string.Empty;
            if (c.SeriesCorePattern == null) c.SeriesCorePattern = string.Empty;
            if (string.IsNullOrWhiteSpace(c.DefaultSeriesCode)) c.DefaultSeriesCode = d.DefaultSeriesCode;
            if (string.IsNullOrWhiteSpace(c.DefaultTargetSeries)) c.DefaultTargetSeries = d.DefaultTargetSeries;
            if (c.PreferredSourceSeries == null) c.PreferredSourceSeries = string.Empty;
            if (c.DimensionRules == null) c.DimensionRules = new List<DimensionRuleConvention>();
            if (c.TaggingRules == null) c.TaggingRules = new List<TaggingRuleConvention>();
            foreach (var r in c.DimensionRules)
            {
                if (r.SheetNameContains == null) r.SheetNameContains = new List<string>();
                if (r.Items == null) r.Items = new List<DimensionItemConvention>();
            }
            foreach (var r in c.TaggingRules)
            {
                if (r.SheetNameContains == null) r.SheetNameContains = new List<string>();
                if (r.SheetNameExcludes == null) r.SheetNameExcludes = new List<string>();
            }
            return c;
        }

        // ---------------------------------------------------------------
        // 棟別辨識
        // ---------------------------------------------------------------

        /// <summary>
        /// 從樓層名稱自動找出棟別代碼：名稱以「英文代碼_」或「英文代碼-」開頭（例如 OB_2FL、LB_2FL），
        /// 且至少有兩種代碼、每種至少出現兩次，才視為多棟模型。
        /// </summary>
        public void RegisterLevelNames(IEnumerable<string> levelNames)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in levelNames ?? Enumerable.Empty<string>())
            {
                var m = Regex.Match((name ?? string.Empty).Trim(), @"^([A-Za-z]{1,4})[_\-]");
                if (!m.Success) continue;
                string key = m.Groups[1].Value.ToUpperInvariant();
                if (IsFloorLikeToken(key)) continue;
                int n;
                counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
            }
            var keys = counts.Where(kv => kv.Value >= 2).Select(kv => kv.Key).ToList();
            lock (Sync)
            {
                if (keys.Count >= 2)
                {
                    foreach (var k in keys)
                        if (!_detectedKeys.Contains(k)) _detectedKeys.Add(k);
                }
            }
        }

        private static bool IsFloorLikeToken(string token)
        {
            // RF、F、PH 之類是樓層代碼，不是棟別
            return Regex.IsMatch(token, @"^(R|RF|F|FL|PH)$", RegexOptions.IgnoreCase);
        }

        /// <summary>所有棟別代碼（手動指定 + 自動辨識），不含全區代碼。</summary>
        public List<string> GetBuildingKeys()
        {
            var keys = Buildings.Select(b => b.Key).ToList();
            lock (Sync)
            {
                foreach (var k in _detectedKeys)
                    if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase)) keys.Add(k);
            }
            return keys.Where(k => !string.Equals(k, SharedBuildingKey, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>手動指定的棟別文字（用於圖名中的棟別替換）。</summary>
        public IEnumerable<BuildingConvention> NamedBuildings
        {
            get { return Buildings.Where(b => !string.Equals(b.DisplayName, b.Key, StringComparison.Ordinal)); }
        }

        public IEnumerable<string> AllBuildingNames
        {
            get { return NamedBuildings.Select(b => b.DisplayName).Where(n => !string.IsNullOrWhiteSpace(n)); }
        }

        public string GetDisplayName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var b = Buildings.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            return b != null ? b.DisplayName : key;
        }

        public bool IsKnownBuildingName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return NamedBuildings.Any(b => text.IndexOf(b.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public bool IsSpecificBuildingKey(string key)
        {
            return !string.IsNullOrEmpty(key) && !string.Equals(key, SharedBuildingKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasKeyToken(string upperText, string key)
        {
            return Regex.IsMatch(upperText, @"(^|[_\-\s])" + Regex.Escape(key.ToUpperInvariant()) + @"(?=[_\-\s]|B?\d|$)");
        }

        /// <summary>由樓層／視圖名稱判斷棟別代碼；判斷不出時回傳空字串。</summary>
        public string GetLevelBuildingKey(string text)
        {
            string value = (text ?? string.Empty).Trim();
            string upper = value.ToUpperInvariant();
            foreach (var b in Buildings)
            {
                if ((!string.Equals(b.DisplayName, b.Key, StringComparison.Ordinal) &&
                     value.IndexOf(b.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    HasKeyToken(upper, b.Key))
                    return b.Key;
            }
            if (SharedAliases.Any(a => !string.IsNullOrEmpty(a) && !IsAsciiWord(a) &&
                    value.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0) ||
                HasKeyToken(upper, SharedBuildingKey))
                return SharedBuildingKey;
            lock (Sync)
            {
                foreach (var k in _detectedKeys)
                    if (HasKeyToken(upper, k)) return k;
            }
            return string.Empty;
        }

        /// <summary>由使用者選的棟別名稱（卡片名稱）判斷棟別代碼。</summary>
        public string GetRequestedBuildingKey(string buildingName)
        {
            string value = (buildingName ?? string.Empty).Trim();
            foreach (var b in Buildings)
            {
                bool hasOwnName = !string.Equals(b.DisplayName, b.Key, StringComparison.Ordinal);
                if (hasOwnName && value.IndexOf(b.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0) return b.Key;
                if (!hasOwnName && HasKeyToken(value.ToUpperInvariant(), b.Key)) return b.Key;
                if (b.Aliases.Any(a => !string.IsNullOrEmpty(a) && value.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
                    return b.Key;
            }
            if (SharedAliases.Any(a => !string.IsNullOrEmpty(a) && value.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0))
                return SharedBuildingKey;
            return GetLevelBuildingKey(value);
        }

        private static bool IsAsciiWord(string s)
        {
            return Regex.IsMatch(s, @"^[A-Za-z0-9]+$");
        }

        // ---------------------------------------------------------------
        // 自動標註
        // ---------------------------------------------------------------

        private static bool ContainsAny(string text, IEnumerable<string> keywords)
        {
            return keywords.Any(k => !string.IsNullOrEmpty(k) && text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public DimensionRuleConvention FindDimensionRule(string sheetName)
        {
            string name = sheetName ?? string.Empty;
            return DimensionRules.FirstOrDefault(r => ContainsAny(name, r.SheetNameContains));
        }

        public TaggingRuleConvention FindTaggingRule(string sheetName)
        {
            string name = sheetName ?? string.Empty;
            return TaggingRules.FirstOrDefault(r => ContainsAny(name, r.SheetNameContains) && !ContainsAny(name, r.SheetNameExcludes));
        }

        // ---------------------------------------------------------------
        // 圖號系列
        // ---------------------------------------------------------------

        /// <summary>從圖號取出系列代碼；取不出時回傳 null。</summary>
        public string MatchSeriesCore(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber) || string.IsNullOrWhiteSpace(SeriesCorePattern)) return null;
            try
            {
                string prefix = string.IsNullOrEmpty(SeriesOptionalPrefix) ? string.Empty
                    : "(?:" + Regex.Escape(SeriesOptionalPrefix) + ")?";
                var m = Regex.Match(sheetNumber, "(?i)" + prefix + "(" + SeriesCorePattern + ")");
                return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
            }
            catch (ArgumentException)
            {
                return null; // 使用者填的正規式有誤時退回一般規則
            }
        }

        /// <summary>比對來源系列用的正規式（含可有可無的前綴）。</summary>
        public string BuildSeriesMatchPattern(string sourceSeriesId)
        {
            string core = Regex.Escape(sourceSeriesId ?? string.Empty);
            if (string.IsNullOrEmpty(SeriesOptionalPrefix)) return "(?i)" + core;
            return "(?i)(?:" + Regex.Escape(SeriesOptionalPrefix) + ")?" + core;
        }

        /// <summary>使用者輸入的系列前段若未含前綴與分隔符號，補上設定的前綴。</summary>
        public string ApplySeriesPrefix(string seriesPrefix)
        {
            if (string.IsNullOrEmpty(SeriesOptionalPrefix) || string.IsNullOrEmpty(seriesPrefix)) return seriesPrefix;
            if (seriesPrefix.StartsWith(SeriesOptionalPrefix, StringComparison.OrdinalIgnoreCase) || seriesPrefix.Contains("-"))
                return seriesPrefix;
            return SeriesOptionalPrefix + seriesPrefix;
        }

        /// <summary>預設目標系列的文字部分（例如 "S2" → "S"）。</summary>
        public string DefaultSeriesLetters
        {
            get
            {
                var m = Regex.Match(DefaultTargetSeries ?? string.Empty, @"^(.*?)(\d+)$");
                return m.Success ? m.Groups[1].Value : (DefaultTargetSeries ?? string.Empty);
            }
        }
    }
}
