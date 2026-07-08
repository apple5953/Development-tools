using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace DevelopmentTools.Modules.DesignReview.Models
{
    public enum TrackingStatus
    {
        Created,
        Modified,
        Approved,
        Rejected
    }

    public enum ReviewType
    {
        Auto,
        SemiAuto,
        Manual
    }

    public class TrackingHistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public TrackingStatus FromStatus { get; set; }
        public TrackingStatus ToStatus { get; set; }
        public string ChangedBy { get; set; }
        public string Comment { get; set; }
    }

    public class XYZPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public XYZPoint() { }
        public XYZPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Autodesk.Revit.DB.XYZ ToXYZ()
        {
            return new Autodesk.Revit.DB.XYZ(X, Y, Z);
        }

        public static XYZPoint FromXYZ(Autodesk.Revit.DB.XYZ xyz)
        {
            if (xyz == null) return null;
            return new XYZPoint(xyz.X, xyz.Y, xyz.Z);
        }
    }

    public class ReviewResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool Passed { get; set; }
        public string Message { get; set; }
        public string ElementId { get; set; }
        public string LevelName { get; set; }
        public XYZPoint Location { get; set; }
    }

    public class ReviewItem : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string RuleCode { get; set; }
        public string RuleName { get; set; }
        public string Category { get; set; }
        public ReviewType Type { get; set; }
        public string Description { get; set; }
        public string Perspective { get; set; }

        private string _lawArticle;
        public string LawArticle
        {
            get => _lawArticle;
            set { _lawArticle = value; OnPropertyChanged(); }
        }

        private string _lawChapter;
        public string LawChapter
        {
            get => _lawChapter;
            set { _lawChapter = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public string DetectElements
        {
            get
            {
                switch (RuleCode)
                {
                    case "BCR-012": return "房間 (Rooms) 與樓層 (Levels)";
                    case "BCR-015": return "欄杆 (Railings)";
                    case "BCR-033": return "走廊房間 (Corridor Rooms)";
                    case "BCR-034": return "樓梯 (Stairs)";
                    case "BCR-037": return "樓梯 (Stairs)";
                    case "BCR-038": return "樓層 (Levels)";
                    case "BCR-074": return "房間 (Rooms) / 區劃 (Areas)";
                    case "BCR-076": return "門 (Doors)";
                    case "BCR-077": return "門 (Doors)";
                    case "BCR-090": return "防火門 (Fire Doors)";
                    case "BCR-092": return "避難層出入口門 (Doors)";
                    case "BCR-094": return "房間 (Rooms) 與樓梯 (Stairs)";
                    case "BCR-101": return "房間 (Rooms)、門 (Doors)";
                    case "BCR-102": return "無障礙/多功能房間 (Rooms)";
                    case "BCR-103": return "房間 (Rooms)、樓板、結構梁、風/水管/線槽";
                    case "BCR-104": return "風/水/線管線、結構牆、結構梁、套管 (Sleeve)";
                    case "BCR-105": return "房間 (Rooms)、地板 (Floors)、天花板 (Ceilings)";
                    case "BCR-111": return "無障礙坡道 (Ramps)";
                    case "BCR-117": return "無障礙通路 (Rooms/Corridors)";
                    case "BCR-118": return "無障礙出入口門 (Doors)";
                    case "BCR-119": return "無障礙廁所房間 (Rooms)";
                    case "BCR-120": return "停車位房間 (Rooms/Parking)";
                    default: return "未指定 (依相關品類元件)";
                }
            }
        }

        [JsonIgnore]
        public string CheckCriteria
        {
            get
            {
                switch (RuleCode)
                {
                    case "BCR-012": return "檢核房間的樓層關聯性，避免未關聯樓層的懸空房間。";
                    case "BCR-015": return "陽台與樓梯欄杆高度不得小於法規限制 (如 1.1m)。";
                    case "BCR-033": return "走廊寬度是否符合法規淨寬度限制 (如 1.2m)。";
                    case "BCR-034": return "檢核樓梯其實際級高 (<= 18cm) 與實際級深 (>= 26cm) 安全尺寸。";
                    case "BCR-037": return "樓梯淨寬度是否符合用途別之法規淨寬限值 (如 1.2m 或 1.4m)。";
                    case "BCR-038": return "確認相鄰樓層高度差是否符合居室最低淨高限制。";
                    case "BCR-074": return "檢核防火區劃面積是否小於等於法規限制之面積。";
                    case "BCR-076": return "防煙與防火區劃之門扇寬度與開啟方向是否合規。";
                    case "BCR-077": return "避難門扇之開啟方向是否朝避難方向開啟。";
                    case "BCR-090": return "檢核防火門之防火時效是否大於等於 1hr（甲種防火門）。";
                    case "BCR-092": return "避難層出入口之門扇淨寬是否大於等於 1.2m。";
                    case "BCR-094": return "檢核直通樓梯之步行避難距離是否在 30m 以內。";
                    case "BCR-101": return "計算房間最遠點至出口之避難路徑，不得超出法規 30m 限制。";
                    case "BCR-102": return "無障礙與多功能空間內切圓迴轉直徑是否大於等於 1.5m。";
                    case "BCR-103": return "利用 3D 射線干涉掃描房間淨高，篩選淨高低於 2.1m 的區域。";
                    case "BCR-104": return "比對管路與結構牆梁碰撞，檢核交接處是否遺漏配置套管。";
                    case "BCR-105": return "核對房間之裝修參數與實體地板、天花板繪製是否一致。";
                    case "BCR-111": return "無障礙坡道之坡度 (如 <= 1:12) 與淨寬是否符合國家標準。";
                    case "BCR-117": return "無障礙通路之淨寬度是否符合無障礙設計規範 (如 1.3m)。";
                    case "BCR-118": return "無障礙出入口門扇淨寬度是否符合無障礙規範 (如 0.9m) 且無門檻。";
                    case "BCR-119": return "無障礙廁所之內部淨空間是否可內切 1.5m x 1.5m 迴轉空間。";
                    case "BCR-120": return "停車位尺寸與無障礙車位尺寸與通路是否合規。";
                    default: return "依國家標準或專案法規規則定義。";
                }
            }
        }

        private TrackingStatus _status = TrackingStatus.Created;
        public TrackingStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); }
        }

        private string _comment;
        public string Comment
        {
            get => _comment;
            set { _comment = value; OnPropertyChanged(); }
        }

        private string _assignee;
        public string Assignee
        {
            get => _assignee;
            set { _assignee = value; OnPropertyChanged(); }
        }

        public List<ReviewResult> Results { get; set; } = new List<ReviewResult>();
        public List<TrackingHistoryEntry> History { get; set; } = new List<TrackingHistoryEntry>();

        public string StatusDisplay
        {
            get
            {
                switch (Status)
                {
                    case TrackingStatus.Created: return "未解決";
                    case TrackingStatus.Modified: return "已修改";
                    case TrackingStatus.Approved: return "已通過";
                    case TrackingStatus.Rejected: return "被拒絕";
                    default: return Status.ToString();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ReviewProject : INotifyPropertyChanged
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        
        private string _projectName;
        public string ProjectName
        {
            get => _projectName;
            set { _projectName = value; OnPropertyChanged(); }
        }

        private string _buildingType;
        public string BuildingType
        {
            get => _buildingType;
            set { _buildingType = value; OnPropertyChanged(); }
        }

        private string _reviewTemplate;
        public string ReviewTemplate
        {
            get => _reviewTemplate;
            set { _reviewTemplate = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ReviewItem> Items { get; set; } = new List<ReviewItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
