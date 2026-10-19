using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace DevelopmentTools.CodeCheck
{
    [DataContract(Namespace = "")]
    public class ManualCheck
    {
        /// <summary>未檢 / 符合 / 不符合 / 不適用</summary>
        [DataMember(Name = "status")] public string Status { get; set; }
        [DataMember(Name = "note")] public string Note { get; set; }
        [DataMember(Name = "by")] public string By { get; set; }
        [DataMember(Name = "date")] public string Date { get; set; }
    }

    /// <summary>
    /// 每個專案的檢討條件（使用類組、分區、道路寬度…）與人工檢討紀錄。
    /// 存在 %APPDATA%\DevelopmentTools\CodeCheck\projects\&lt;專案路徑雜湊&gt;.json。
    /// </summary>
    [DataContract(Namespace = "")]
    public class ProjectSettings
    {
        [DataMember(Name = "projectTitle")] public string ProjectTitle { get; set; }
        [DataMember(Name = "useGroups")] public List<string> UseGroups { get; set; }
        [DataMember(Name = "fireClasses")] public List<string> FireClasses { get; set; }
        [DataMember(Name = "zone")] public string Zone { get; set; }
        [DataMember(Name = "inUrbanPlan")] public bool InUrbanPlan { get; set; }
        [DataMember(Name = "volumeControl")] public bool VolumeControl { get; set; }
        [DataMember(Name = "directCity")] public bool DirectCity { get; set; }
        [DataMember(Name = "roadWidthM")] public double RoadWidthM { get; set; }
        [DataMember(Name = "setbackM")] public double SetbackM { get; set; }
        [DataMember(Name = "siteAreaM2")] public double SiteAreaM2 { get; set; }
        [DataMember(Name = "buildingAreaM2")] public double BuildingAreaM2 { get; set; }
        [DataMember(Name = "maxCoverageRatio")] public double MaxCoverageRatio { get; set; }
        [DataMember(Name = "maxFarRatio")] public double MaxFarRatio { get; set; }
        [DataMember(Name = "slopeland")] public bool Slopeland { get; set; }
        [DataMember(Name = "fireproof")] public bool Fireproof { get; set; }
        [DataMember(Name = "sprinklerMode")] public string SprinklerMode { get; set; }   // auto / yes / no
        [DataMember(Name = "sprinklerFastResponse")] public bool SprinklerFastResponse { get; set; }
        [DataMember(Name = "accessibilityApplies")] public bool AccessibilityApplies { get; set; }
        [DataMember(Name = "publicBuilding")] public bool PublicBuilding { get; set; }
        [DataMember(Name = "isApartment")] public bool IsApartment { get; set; }
        [DataMember(Name = "isElderlyHousing")] public bool IsElderlyHousing { get; set; }
        [DataMember(Name = "hasVoid")] public bool HasVoid { get; set; }
        [DataMember(Name = "tc23Exception")] public bool Tc23Exception { get; set; }
        [DataMember(Name = "tc24Exception")] public bool Tc24Exception { get; set; }
        [DataMember(Name = "liveAudienceStudio")] public bool LiveAudienceStudio { get; set; }
        [DataMember(Name = "groundLevel")] public string GroundLevel { get; set; }
        [DataMember(Name = "floorsAboveOverride")] public int FloorsAboveOverride { get; set; }
        [DataMember(Name = "floorsBelowOverride")] public int FloorsBelowOverride { get; set; }
        [DataMember(Name = "heightOverrideM")] public double HeightOverrideM { get; set; }
        [DataMember(Name = "openableRatio")] public double OpenableRatio { get; set; }
        [DataMember(Name = "doorFrameDeductionMm")] public double DoorFrameDeductionMm { get; set; }
        [DataMember(Name = "parkingArea1")] public double ParkingArea1 { get; set; }
        [DataMember(Name = "parkingArea2")] public double ParkingArea2 { get; set; }
        [DataMember(Name = "parkingArea3")] public double ParkingArea3 { get; set; }
        [DataMember(Name = "parkingArea4")] public double ParkingArea4 { get; set; }
        [DataMember(Name = "requiredParkingOverride")] public int RequiredParkingOverride { get; set; }
        [DataMember(Name = "occupantsOverride")] public int OccupantsOverride { get; set; }
        [DataMember(Name = "wcBuildingType")] public int WcBuildingType { get; set; }   // 第 37 條表類別 1~9，0 = 自動
        [DataMember(Name = "travelDistance")] public bool TravelDistance { get; set; }
        [DataMember(Name = "manual")] public Dictionary<string, ManualCheck> Manual { get; set; }
        [DataMember(Name = "roomRoleOverrides")] public Dictionary<string, string> RoomRoleOverrides { get; set; }
        // 拆檔合併檢討
        [DataMember(Name = "sources")] public List<SourceEntry> Sources { get; set; }
        [DataMember(Name = "levelToleranceMm")] public double LevelToleranceMm { get; set; }   // 不同模型的樓層高程差在此範圍內視為同一層
        [DataMember(Name = "keepDuplicates")] public bool KeepDuplicates { get; set; }         // 不排除各模型重複的元素

        public void Normalize()
        {
            if (UseGroups == null) UseGroups = new List<string>();
            if (FireClasses == null) FireClasses = new List<string>();
            if (string.IsNullOrEmpty(Zone)) Zone = "住宅區";
            if (string.IsNullOrEmpty(SprinklerMode)) SprinklerMode = "auto";
            if (OpenableRatio <= 0 || OpenableRatio > 1) OpenableRatio = 0.5;
            if (Manual == null) Manual = new Dictionary<string, ManualCheck>();
            if (RoomRoleOverrides == null) RoomRoleOverrides = new Dictionary<string, string>();
            if (Sources == null) Sources = new List<SourceEntry>();
            if (LevelToleranceMm <= 0) LevelToleranceMm = 300;
        }

        public static ProjectSettings CreateDefault(string title)
        {
            var s = new ProjectSettings
            {
                ProjectTitle = title,
                InUrbanPlan = true,
                VolumeControl = true,
                Fireproof = true,
                AccessibilityApplies = true,
                TravelDistance = true,
                SprinklerMode = "auto",
                OpenableRatio = 0.5
            };
            s.Normalize();
            return s;
        }

        private static string FileFor(string projectKey)
        {
            using (var sha = SHA1.Create())
            {
                string h = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(projectKey ?? ""))).Replace("-", "").Substring(0, 16);
                return Path.Combine(CodeCheckData.UserFolder, "projects", h + ".json");
            }
        }

        public static ProjectSettings Load(string projectKey, string title)
        {
            string path = FileFor(projectKey);
            try
            {
                if (File.Exists(path))
                {
                    var ser = new DataContractJsonSerializer(typeof(ProjectSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                    using (var fs = File.OpenRead(path))
                    {
                        var s = (ProjectSettings)ser.ReadObject(fs);
                        s.Normalize();
                        return s;
                    }
                }
            }
            catch { }
            return CreateDefault(title);
        }

        public void Save(string projectKey)
        {
            string path = FileFor(projectKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var ser = new DataContractJsonSerializer(typeof(ProjectSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
            using (var ms = new MemoryStream())
            {
                using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                    ser.WriteObject(w, this);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        /// <summary>依使用類組推估消防場所分類（各類場所消防安全設備設置標準第 12 條）。可在設定中修改。</summary>
        public static List<string> SuggestFireClasses(IEnumerable<string> useGroups)
        {
            var map = new Dictionary<string, string>
            {
                { "A-1", "甲1" }, { "A-2", "乙1" }, { "B-1", "甲1" }, { "B-2", "甲4" }, { "B-3", "甲5" }, { "B-4", "甲3" },
                { "C-1", "丁2" }, { "C-2", "丁3" }, { "D-1", "甲2" }, { "D-2", "乙4" }, { "D-3", "乙3" }, { "D-4", "乙3" },
                { "D-5", "乙3" }, { "E", "乙5" }, { "F-1", "甲6" }, { "F-2", "乙6" }, { "F-3", "乙12" }, { "F-4", "乙6" },
                { "G-1", "乙2" }, { "G-2", "乙6" }, { "G-3", "乙6" }, { "H-1", "乙7" }, { "H-2", "乙7" }, { "I", "丁1" }
            };
            var list = new List<string>();
            foreach (var g in useGroups ?? Enumerable.Empty<string>())
                if (map.TryGetValue(g.Trim().ToUpperInvariant(), out string f) && !list.Contains(f)) list.Add(f);
            return list;
        }
    }

    [DataContract(Namespace = "")]
    public class RoleKeyword
    {
        [DataMember(Name = "role")] public string Role { get; set; }
        [DataMember(Name = "label")] public string Label { get; set; }
        [DataMember(Name = "habitable")] public bool Habitable { get; set; }
        [DataMember(Name = "keywords")] public List<string> Keywords { get; set; }
    }

    [DataContract(Namespace = "")]
    public class DeviceKeyword
    {
        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "label")] public string Label { get; set; }
        [DataMember(Name = "keywords")] public List<string> Keywords { get; set; }
    }

    /// <summary>房間用途與設備的辨識關鍵字（%APPDATA%\DevelopmentTools\CodeCheck\keywords.json，可自行修改）。</summary>
    [DataContract(Namespace = "")]
    public class KeywordConfig
    {
        [DataMember(Name = "roles")] public List<RoleKeyword> Roles { get; set; }
        [DataMember(Name = "devices")] public List<DeviceKeyword> Devices { get; set; }
        [DataMember(Name = "unknownRoomIsHabitable")] public bool UnknownRoomIsHabitable { get; set; }

        public static string FilePath => Path.Combine(CodeCheckData.UserFolder, "keywords.json");

        public static KeywordConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var ser = new DataContractJsonSerializer(typeof(KeywordConfig));
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var k = (KeywordConfig)ser.ReadObject(fs);
                        if (k?.Roles != null && k.Roles.Count > 0 && k.Devices != null) return k;
                    }
                }
            }
            catch { }
            var d = Default();
            try
            {
                Directory.CreateDirectory(CodeCheckData.UserFolder);
                var ser = new DataContractJsonSerializer(typeof(KeywordConfig));
                using (var ms = new MemoryStream())
                {
                    using (var w = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                        ser.WriteObject(w, d);
                    File.WriteAllBytes(FilePath, ms.ToArray());
                }
            }
            catch { }
            return d;
        }

        private static RoleKeyword R(string role, string label, bool hab, params string[] k) =>
            new RoleKeyword { Role = role, Label = label, Habitable = hab, Keywords = k.ToList() };

        private static DeviceKeyword D(string kind, string label, params string[] k) =>
            new DeviceKeyword { Kind = kind, Label = label, Keywords = k.ToList() };

        /// <summary>順序有意義：越前面越優先（例如「無障礙廁所」要排在「廁所」前面）。</summary>
        public static KeywordConfig Default()
        {
            return new KeywordConfig
            {
                UnknownRoomIsHabitable = true,
                Roles = new List<RoleKeyword>
                {
                    R("accessibleToilet", "無障礙廁所", false, "無障礙廁所", "無障礙盥洗", "無障礙化妝室", "無障礙WC"),
                    R("accessibleBath", "無障礙浴室", false, "無障礙浴室", "無障礙淋浴"),
                    R("accessibleGuestRoom", "無障礙客房", true, "無障礙客房"),
                    R("emergencyElevatorLobby", "緊急昇降機間", false, "緊急昇降機間", "緊急電梯間", "緊急升降機間"),
                    R("smokeLobby", "排煙室", false, "排煙室", "前室"),
                    R("elevatorMachineRoom", "昇降機機房", false, "昇降機機房", "電梯機房", "升降機機房"),
                    R("disasterCenter", "防災中心", false, "防災中心"),
                    R("roofRefuge", "屋頂避難平臺", false, "屋頂避難"),
                    R("stair", "樓梯間", false, "樓梯", "梯間", "安全梯", "STAIR", "Stair"),
                    R("elevatorLobby", "梯廳", false, "梯廳", "電梯廳", "昇降機間", "升降機間", "電梯間", "LOBBY", "Lobby"),
                    R("corridor", "走廊", false, "走廊", "廊道", "走道", "通道", "迴廊", "CORRIDOR", "Corridor"),
                    R("arcade", "騎樓", false, "騎樓"),
                    R("toilet", "廁所", false, "廁所", "化妝室", "洗手間", "盥洗", "衛浴", "WC", "Toilet"),
                    R("bath", "浴室", false, "浴室", "淋浴", "Bath"),
                    R("kitchen", "廚房", true, "廚房", "Kitchen"),
                    R("classroom", "教室", true, "教室", "Classroom"),
                    R("kindergarten", "幼兒活動室", true, "幼兒", "活動室"),
                    R("ward", "病房", true, "病房"),
                    R("bedroom", "臥室", true, "臥室", "寢室", "主臥", "次臥", "客房", "Bedroom"),
                    R("living", "客餐廳", true, "客廳", "起居", "客餐廳", "Living"),
                    R("auditorium", "觀眾席", true, "觀眾席", "禮堂", "演講廳", "演藝廳", "視聽", "Auditorium"),
                    R("parking", "停車空間", false, "停車", "車道", "車庫", "Parking"),
                    R("shelter", "防空避難室", false, "防空避難"),
                    R("factory", "作業廠房", true, "作業廠房", "廠房", "生產"),
                    R("storage", "儲藏室", false, "儲藏", "倉庫", "儲物", "庫房", "Storage"),
                    R("mech", "機電空間", false, "機房", "機械室", "電氣室", "變電", "發電機", "水箱", "泵浦", "幫浦", "空調機", "管道", "電信", "機電", "受電", "MEP", "PS", "EPS"),
                    R("lobby", "門廳", false, "門廳", "大廳", "玄關", "入口", "Entrance"),
                    R("balcony", "陽臺", false, "陽台", "陽臺", "露台", "露臺", "Balcony"),
                    R("closet", "衣帽間", false, "衣帽", "更衣"),
                    R("office", "辦公／會議", true, "辦公", "會議", "Office", "Meeting"),
                },
                Devices = new List<DeviceKeyword>
                {
                    D("extinguisher", "滅火器", "滅火器", "Extinguisher"),
                    D("hydrant", "室內消防栓", "消防栓", "Hydrant", "Fire Hose"),
                    D("standpipeOutlet", "連結送水管出水口", "出水口", "連結送水", "Landing Valve"),
                    D("smokeDetector", "偵煙探測器", "偵煙", "Smoke"),
                    D("heatDetector", "定溫／差動探測器", "差動", "定溫", "Heat"),
                    D("manualCallPoint", "火警發信機", "發信機", "手動報警", "Manual Call", "Pull Station"),
                    D("alarmBell", "火警警鈴", "警鈴", "Bell"),
                    D("speaker", "揚聲器", "揚聲器", "喇叭", "Speaker"),
                    D("exitSign", "出口標示燈", "出口標示", "出口燈", "EXIT", "Exit"),
                    D("directionSign", "避難方向指示燈", "避難方向", "方向指示", "Direction"),
                    D("evacuationSign", "避難指標", "避難指標"),
                    D("emergencyLight", "緊急照明", "緊急照明", "Emergency Light"),
                    D("emergencyOutlet", "緊急電源插座", "緊急電源插座", "緊急插座"),
                    D("lightning", "避雷設備", "避雷", "受雷", "Lightning"),
                    D("wc", "大便器", "大便器", "馬桶", "坐式", "蹲式", "Toilet", "WC", "Water Closet"),
                    D("elevator", "昇降機", "昇降機", "升降機", "電梯", "Elevator", "Lift")
                }
            };
        }
    }
}
