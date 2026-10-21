using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DevelopmentTools.FastRebar
{
    /// <summary>梁主筋在支承（柱）處的處理方式。</summary>
    public enum BeamBarEndMode
    {
        /// <summary>穿過柱，延伸到對向梁內（參考模型做法）。</summary>
        ThroughSupport = 0,
        /// <summary>錨定在柱內，到柱遠側面扣保護層，帶 90° 彎鉤。</summary>
        AnchorInSupport = 1,
        /// <summary>停在柱面（扣保護層），不加彎鉤。</summary>
        StopAtFace = 2
    }

    /// <summary>梁箍筋型式。</summary>
    public enum BeamStirrupForm
    {
        /// <summary>U 形箍（兩端 135°）＋頂部繫筋（135°/90° 逐組交替）。參考模型做法。</summary>
        UWithCapTie = 0,
        /// <summary>閉合箍（兩端 135°）。</summary>
        ClosedHoop = 1
    }

    /// <summary>兩向梁上層筋錯層：哪一向讓位（往內退一支主筋直徑）。</summary>
    public enum CrossingYield
    {
        None = 0,
        XDirectionBeamsYield = 1,
        YDirectionBeamsYield = 2
    }

    /// <summary>已有鋼筋的構件怎麼處理。</summary>
    public enum ExistingRebarPolicy
    {
        Skip = 0,
        Replace = 1,
        AddAnyway = 2
    }

    [DataContract(Namespace = "")]
    public class BeamRebarSettings
    {
        [DataMember(Name = "MainBarType")] public string MainBarType { get; set; }
        [DataMember(Name = "StirrupBarType")] public string StirrupBarType { get; set; }
        /// <summary>上層各層支數，例如 [7,7]。</summary>
        [DataMember(Name = "TopLayers")] public List<int> TopLayers { get; set; }
        /// <summary>下層各層支數，例如 [7,5]。</summary>
        [DataMember(Name = "BottomLayers")] public List<int> BottomLayers { get; set; }
        /// <summary>層與層之間的淨距（mm）。</summary>
        [DataMember(Name = "LayerClearMm")] public double LayerClearMm { get; set; }
        /// <summary>腰筋每側支數（0 = 不配）。</summary>
        [DataMember(Name = "SideBarsPerFace")] public int SideBarsPerFace { get; set; }
        [DataMember(Name = "SideBarType")] public string SideBarType { get; set; }
        [DataMember(Name = "EndMode")] public BeamBarEndMode EndMode { get; set; }
        /// <summary>穿過柱時伸入下一跨的長度（從柱遠側面算，mm）。</summary>
        [DataMember(Name = "ExtendPastSupportMm")] public double ExtendPastSupportMm { get; set; }
        [DataMember(Name = "StirrupForm")] public BeamStirrupForm StirrupForm { get; set; }
        /// <summary>每個位置放兩組箍筋（雙箍，相距一個箍筋直徑）。</summary>
        [DataMember(Name = "DoubleStirrups")] public bool DoubleStirrups { get; set; }
        /// <summary>第一支箍筋距柱面（mm）。</summary>
        [DataMember(Name = "FirstStirrupOffsetMm")] public double FirstStirrupOffsetMm { get; set; }
        /// <summary>端部加密區長度 = 係數 × 梁深。</summary>
        [DataMember(Name = "EndZoneDepthFactor")] public double EndZoneDepthFactor { get; set; }
        [DataMember(Name = "EndZoneSpacingMm")] public double EndZoneSpacingMm { get; set; }
        [DataMember(Name = "MidSpacingMm")] public double MidSpacingMm { get; set; }
        /// <summary>只在加密區配箍（參考模型只畫了柱邊加密區）。</summary>
        [DataMember(Name = "EndZonesOnly")] public bool EndZonesOnly { get; set; }
        [DataMember(Name = "CrossingYield")] public CrossingYield CrossingYield { get; set; }
    }

    [DataContract(Namespace = "")]
    public class ColumnRebarSettings
    {
        [DataMember(Name = "MainBarType")] public string MainBarType { get; set; }
        [DataMember(Name = "TieBarType")] public string TieBarType { get; set; }
        /// <summary>斷面長邊每面支數（含角筋）。</summary>
        [DataMember(Name = "BarsLongFace")] public int BarsLongFace { get; set; }
        /// <summary>斷面短邊每面支數（含角筋）。</summary>
        [DataMember(Name = "BarsShortFace")] public int BarsShortFace { get; set; }
        /// <summary>主筋頂端往上延伸（搭接到上層柱，mm）；0 = 停在柱頂扣保護層。</summary>
        [DataMember(Name = "ExtendAboveTopMm")] public double ExtendAboveTopMm { get; set; }
        /// <summary>圍束區長度下限（mm），實際取 max(斷面長邊, 淨高/6, 此值)。</summary>
        [DataMember(Name = "ConfinedZoneMinMm")] public double ConfinedZoneMinMm { get; set; }
        [DataMember(Name = "ConfinedSpacingMm")] public double ConfinedSpacingMm { get; set; }
        [DataMember(Name = "MidSpacingMm")] public double MidSpacingMm { get; set; }
        /// <summary>梁柱接頭區箍距（mm）。</summary>
        [DataMember(Name = "JointSpacingMm")] public double JointSpacingMm { get; set; }
        [DataMember(Name = "FirstTieOffsetMm")] public double FirstTieOffsetMm { get; set; }
        /// <summary>每支中間主筋都加繫筋；否則隔支加。</summary>
        [DataMember(Name = "TieEveryIntermediateBar")] public bool TieEveryIntermediateBar { get; set; }
    }

    [DataContract(Namespace = "")]
    public class FastRebarPreset
    {
        [DataMember(Name = "Name")] public string Name { get; set; }
        [DataMember(Name = "Note")] public string Note { get; set; }
        /// <summary>0 = 讀構件的保護層設定；&gt;0 = 用這個值（mm）。</summary>
        [DataMember(Name = "CoverOverrideMm")] public double CoverOverrideMm { get; set; }
        [DataMember(Name = "Hook135Name")] public string Hook135Name { get; set; }
        [DataMember(Name = "Hook90TieName")] public string Hook90TieName { get; set; }
        [DataMember(Name = "Hook90MainName")] public string Hook90MainName { get; set; }
        [DataMember(Name = "Beam")] public BeamRebarSettings Beam { get; set; }
        [DataMember(Name = "Column")] public ColumnRebarSettings Column { get; set; }

        public FastRebarPreset Clone()
        {
            var ser = new DataContractJsonSerializer(typeof(FastRebarPreset));
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, this);
                ms.Position = 0;
                var copy = (FastRebarPreset)ser.ReadObject(ms);
                copy.Normalize();
                return copy;
            }
        }

        /// <summary>DataContract 反序列化不跑建構子與初始值，讀檔後補齊缺漏欄位。</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Name)) Name = "未命名";
            if (Beam == null) Beam = FastRebarPresetStore.BuildReferencePreset().Beam;
            if (Column == null) Column = FastRebarPresetStore.BuildReferencePreset().Column;
            if (Beam.TopLayers == null || Beam.TopLayers.Count == 0) Beam.TopLayers = new List<int> { 4 };
            if (Beam.BottomLayers == null || Beam.BottomLayers.Count == 0) Beam.BottomLayers = new List<int> { 4 };
            if (Beam.LayerClearMm <= 0) Beam.LayerClearMm = 25;
            if (Beam.FirstStirrupOffsetMm <= 0) Beam.FirstStirrupOffsetMm = 50;
            if (Beam.EndZoneDepthFactor <= 0) Beam.EndZoneDepthFactor = 2;
            if (Beam.EndZoneSpacingMm <= 0) Beam.EndZoneSpacingMm = 100;
            if (Beam.MidSpacingMm <= 0) Beam.MidSpacingMm = 200;
            if (Column.BarsLongFace < 2) Column.BarsLongFace = 2;
            if (Column.BarsShortFace < 2) Column.BarsShortFace = 2;
            if (Column.ConfinedZoneMinMm <= 0) Column.ConfinedZoneMinMm = 450;
            if (Column.ConfinedSpacingMm <= 0) Column.ConfinedSpacingMm = 100;
            if (Column.MidSpacingMm <= 0) Column.MidSpacingMm = 150;
            if (Column.JointSpacingMm <= 0) Column.JointSpacingMm = Column.ConfinedSpacingMm;
            if (Column.FirstTieOffsetMm <= 0) Column.FirstTieOffsetMm = 50;
        }
    }

    [DataContract(Namespace = "")]
    internal class FastRebarPresetFile
    {
        [DataMember(Name = "Version")] public int Version { get; set; }
        [DataMember(Name = "LastUsed")] public string LastUsed { get; set; }
        [DataMember(Name = "Presets")] public List<FastRebarPreset> Presets { get; set; }
    }

    /// <summary>
    /// 樣板存在 %APPDATA%\DevelopmentTools\FastRebar\presets.json。
    /// 第一次使用時寫出「參考模型」與「一般 RC」兩組預設。
    /// </summary>
    public static class FastRebarPresetStore
    {
        public static string FilePath
        {
            get
            {
                string dir = global::DevelopmentTools.Core.InstallLocation.DataFolder("FastRebar",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevelopmentTools", "FastRebar"));
                return Path.Combine(dir, "presets.json");
            }
        }

        public static List<FastRebarPreset> Load(out string lastUsed)
        {
            lastUsed = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    var ser = new DataContractJsonSerializer(typeof(FastRebarPresetFile));
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var file = (FastRebarPresetFile)ser.ReadObject(fs);
                        if (file?.Presets != null && file.Presets.Count > 0)
                        {
                            foreach (var p in file.Presets) p.Normalize();
                            lastUsed = file.LastUsed;
                            return file.Presets;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FastRebarLog.Write("讀取樣板失敗，改用內建預設：" + ex.Message);
            }

            var defaults = new List<FastRebarPreset> { BuildReferencePreset(), BuildGeneralPreset() };
            Save(defaults, defaults[0].Name);
            lastUsed = defaults[0].Name;
            return defaults;
        }

        public static void Save(List<FastRebarPreset> presets, string lastUsed)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var file = new FastRebarPresetFile { Version = 1, LastUsed = lastUsed, Presets = presets };
                var ser = new DataContractJsonSerializer(typeof(FastRebarPresetFile));
                using (var ms = new MemoryStream())
                {
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    {
                        ser.WriteObject(writer, file);
                    }
                    File.WriteAllBytes(FilePath, ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                FastRebarLog.Write("儲存樣板失敗：" + ex.Message);
            }
        }

        /// <summary>依參考模型（80×90 梁、80×110 柱接頭）歸納出的做法。</summary>
        public static FastRebarPreset BuildReferencePreset()
        {
            return new FastRebarPreset
            {
                Name = "參考模型：耐震梁柱接頭",
                Note = "梁：主筋穿柱延伸、U 形箍 135° + 頂部繫筋 135°/90° 交替；柱：閉合箍 135°、繫筋 135°/90° 逐層交替。",
                CoverOverrideMm = 0,
                Beam = new BeamRebarSettings
                {
                    MainBarType = "32M",
                    StirrupBarType = "16M",
                    TopLayers = new List<int> { 7, 7 },
                    BottomLayers = new List<int> { 7, 5 },
                    LayerClearMm = 85,
                    SideBarsPerFace = 0,
                    SideBarType = "16M",
                    EndMode = BeamBarEndMode.ThroughSupport,
                    ExtendPastSupportMm = 4000,
                    StirrupForm = BeamStirrupForm.UWithCapTie,
                    DoubleStirrups = false,
                    FirstStirrupOffsetMm = 60,
                    EndZoneDepthFactor = 2,
                    EndZoneSpacingMm = 120,
                    MidSpacingMm = 200,
                    EndZonesOnly = false,
                    CrossingYield = CrossingYield.None
                },
                Column = new ColumnRebarSettings
                {
                    MainBarType = "36M",
                    TieBarType = "13M",
                    BarsLongFace = 11,
                    BarsShortFace = 7,
                    ExtendAboveTopMm = 0,
                    ConfinedZoneMinMm = 450,
                    ConfinedSpacingMm = 100,
                    MidSpacingMm = 150,
                    JointSpacingMm = 100,
                    FirstTieOffsetMm = 50,
                    TieEveryIntermediateBar = false
                }
            };
        }

        public static FastRebarPreset BuildGeneralPreset()
        {
            return new FastRebarPreset
            {
                Name = "一般 RC：D25 主筋／D13 箍筋",
                Note = "中小型梁柱的起始值，請依結構圖修改。",
                CoverOverrideMm = 0,
                Beam = new BeamRebarSettings
                {
                    MainBarType = "25M",
                    StirrupBarType = "13M",
                    TopLayers = new List<int> { 4 },
                    BottomLayers = new List<int> { 4 },
                    LayerClearMm = 25,
                    SideBarsPerFace = 0,
                    SideBarType = "13M",
                    EndMode = BeamBarEndMode.AnchorInSupport,
                    ExtendPastSupportMm = 0,
                    StirrupForm = BeamStirrupForm.ClosedHoop,
                    DoubleStirrups = false,
                    FirstStirrupOffsetMm = 50,
                    EndZoneDepthFactor = 2,
                    EndZoneSpacingMm = 100,
                    MidSpacingMm = 200,
                    EndZonesOnly = false,
                    CrossingYield = CrossingYield.None
                },
                Column = new ColumnRebarSettings
                {
                    MainBarType = "25M",
                    TieBarType = "13M",
                    BarsLongFace = 4,
                    BarsShortFace = 4,
                    ExtendAboveTopMm = 0,
                    ConfinedZoneMinMm = 450,
                    ConfinedSpacingMm = 100,
                    MidSpacingMm = 150,
                    JointSpacingMm = 100,
                    FirstTieOffsetMm = 50,
                    TieEveryIntermediateBar = true
                }
            };
        }

        public static List<int> ParseLayers(string text)
        {
            var list = new List<int>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(new[] { '+', ',', '，', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), out int n) && n > 0) list.Add(n);
            }
            return list;
        }

        public static string FormatLayers(IEnumerable<int> layers)
        {
            return layers == null ? "" : string.Join("+", layers.Select(n => n.ToString()));
        }
    }

    internal static class FastRebarLog
    {
        public static void Write(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(FastRebarPresetStore.FilePath);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "fast-rebar.log");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 2 * 1024 * 1024) info.Delete();
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // 記錄失敗不影響配筋
            }
        }
    }
}
