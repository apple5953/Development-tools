using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DevelopmentTools.Modules.DesignReview.Models;

namespace DevelopmentTools.Modules.DesignReview.Services
{
    public static class StairChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement stair, string buildingType = "集合住宅")
        {
            var results = new List<ReviewResult>();

            double riser = stair.GetDoubleParameter("最大級高") ?? stair.GetDoubleParameter("Type_最大級高") ?? stair.GetDoubleParameter("Actual Riser Height") ?? 0;
            double tread = stair.GetDoubleParameter("最小級深") ?? stair.GetDoubleParameter("Type_最小級深") ?? stair.GetDoubleParameter("Actual Tread Depth") ?? 0;
            double width = stair.GetDoubleParameter("寬度") ?? stair.GetDoubleParameter("Type_寬度") ?? stair.GetDoubleParameter("Actual Run Width") ?? 0;

            string stairName = stair.ElementName ?? string.Empty;
            double limitRiser = 180.0;
            double limitTread = 240.0;
            double limitWidth = 1200.0;
            string roleType = "集合住宅/辦公/常規類";

            // 依建物用途與名稱做更細緻之法規限制判斷
            if (buildingType.Contains("學校") || buildingType.Contains("公眾") || buildingType.Contains("Public") || stairName.Contains("公眾") || stairName.Contains("學校") || stairName.Contains("安全梯"))
            {
                limitRiser = 160.0;
                limitTread = 260.0;
                limitWidth = 1400.0;
                roleType = "供公眾使用/避難安全梯";
            }
            else if (stairName.Contains("其他") || stairName.Contains("House") || stairName.Contains("透天") || stairName.Contains("住宅"))
            {
                limitRiser = 200.0;
                limitTread = 220.0;
                limitWidth = 750.0;
                roleType = "其他用途/住宅類(三階以上)";
            }

            if (riser > limitRiser)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"[{roleType}] 級高為 {riser:F1}mm，不符合法規要求 (應 <= {limitRiser:F0}mm)。",
                    ElementId = stair.ElementId,
                    LevelName = stair.LevelName,
                    Location = XYZPoint.FromXYZ(stair.Location)
                });
            }
            if (tread > 0 && tread < limitTread)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"[{roleType}] 級深為 {tread:F1}mm，不符合法規要求 (應 >= {limitTread:F0}mm)。",
                    ElementId = stair.ElementId,
                    LevelName = stair.LevelName,
                    Location = XYZPoint.FromXYZ(stair.Location)
                });
            }
            if (width > 0 && width < limitWidth)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"[{roleType}] 樓梯淨寬度為 {width:F1}mm，不符合法規要求 (應 >= {limitWidth:F0}mm)。",
                    ElementId = stair.ElementId,
                    LevelName = stair.LevelName,
                    Location = XYZPoint.FromXYZ(stair.Location)
                });
            }

            return results;
        }
    }

    public static class CorridorChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement element, string buildingType = "集合住宅")
        {
            var results = new List<ReviewResult>();

            if (element.CategoryName.Contains("房間") || element.CategoryName.Contains("Room"))
            {
                string name = element.ElementName ?? string.Empty;
                if (name.Contains("走廊") || name.Contains("走道") || name.Contains("Corridor") || name.Contains("Hallway"))
                {
                    double width = element.GetDoubleParameter("廊道淨寬") ?? element.GetDoubleParameter("寬度") ?? element.GetDoubleParameter("Width") ?? 0;
                    
                    double limitWidth = 1200.0;
                    string sideType = "單側有居室";
                    bool isDoubleSide = name.Contains("雙側") || name.Contains("兩側") || name.Contains("Double");

                    if (buildingType.Contains("學校") || buildingType.Contains("公眾") || buildingType.Contains("Public"))
                    {
                        limitWidth = isDoubleSide ? 2400.0 : 1800.0; // 供公眾使用更嚴格
                        sideType = isDoubleSide ? "雙側有居室(供公眾)" : "單側有居室(供公眾)";
                    }
                    else
                    {
                        limitWidth = isDoubleSide ? 1600.0 : 1200.0;
                        sideType = isDoubleSide ? "雙側有居室" : "單側有居室";
                    }

                    if (width > 0 && width < limitWidth)
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"走廊「{name}」({sideType}) 淨寬為 {width:F1}mm，低於法規標準 (應 >= {limitWidth:F0}mm)。",
                            ElementId = element.ElementId,
                            LevelName = element.LevelName,
                            Location = XYZPoint.FromXYZ(element.Location)
                        });
                    }
                }
            }
            return results;
        }
    }

    public static class DoorChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement door, string ruleCode, string buildingType = "集合住宅")
        {
            var results = new List<ReviewResult>();
            string name = door.ElementName ?? string.Empty;

            double width = door.GetDoubleParameter("寬度") ?? door.GetDoubleParameter("Type_寬度") ?? door.GetDoubleParameter("Width") ?? door.GetDoubleParameter("Actual Width") ?? 0;

            if (ruleCode == "BCR-076") // 安全梯避難門寬度 (>= 900mm)
            {
                if (width > 0 && width < 900.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"避難安全門寬為 {width:F1}mm，小於法定逃生淨寬度限制 (>= 900mm)。",
                        ElementId = door.ElementId,
                        LevelName = door.LevelName,
                        Location = XYZPoint.FromXYZ(door.Location)
                    });
                }
            }
            else if (ruleCode == "BCR-092") // 避難層出入口淨寬 (>= 1200mm)
            {
                // 只檢查避難層出入口門（通常位於 1F，或名稱含避難層、出口、大門）
                bool isEgressLevelDoor = door.LevelName.Contains("1F") || door.LevelName.Contains("GL") || name.Contains("出入口") || name.Contains("大門") || name.Contains("Egress");
                if (isEgressLevelDoor && width > 0 && width < 1200.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"避難層出入口門扇寬度為 {width:F1}mm，小於法規淨寬限值 (應 >= 1200mm)。",
                        ElementId = door.ElementId,
                        LevelName = door.LevelName,
                        Location = XYZPoint.FromXYZ(door.Location)
                    });
                }
            }
            else if (ruleCode == "BCR-118") // 無障礙出入口門扇淨寬 (>= 900mm) 且無門檻
            {
                bool isAccessibleDoor = name.Contains("無障礙") || name.Contains("Accessible") || name.Contains("WC") || name.Contains("廁所");
                if (isAccessibleDoor)
                {
                    if (width > 0 && width < 900.0)
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"無障礙門扇寬度為 {width:F1}mm，小於無障礙設計規範 (應 >= 900mm)。",
                            ElementId = door.ElementId,
                            LevelName = door.LevelName,
                            Location = XYZPoint.FromXYZ(door.Location)
                        });
                    }

                    // 檢查門檻高差參數（半自動檢核）
                    double thresholdHeight = door.GetDoubleParameter("門檻高度") ?? door.GetDoubleParameter("Threshold Height") ?? 0;
                    if (thresholdHeight > 20.0)
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"無障礙門檻高度為 {thresholdHeight:F1}mm，超出規範上限 20mm (若介於 5-20mm 需作 1:2 斜角)。",
                            ElementId = door.ElementId,
                            LevelName = door.LevelName,
                            Location = XYZPoint.FromXYZ(door.Location)
                        });
                    }
                }
            }
            else if (ruleCode == "BCR-077") // 出入口門扇開啟方向 (應朝避難方向開啟)
            {
                // 半自動/人工：檢查門的開啟方向參數。若有填寫，則判定。若沒有，列為半自動人工核對。
                string direction = door.GetStringParameter("開啟方向") ?? door.GetStringParameter("Swing Direction") ?? string.Empty;
                if (!string.IsNullOrEmpty(direction))
                {
                    bool isOutward = direction.Contains("避難") || direction.Contains("向外") || direction.Contains("Outswing") || direction.Contains("朝外");
                    if (!isOutward && (name.Contains("避難") || name.Contains("安全") || name.Contains("門")))
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"避難門扇開啟方向為「{direction}」，未符合「朝避難逃生方向開啟」規定。",
                            ElementId = door.ElementId,
                            LevelName = door.LevelName,
                            Location = XYZPoint.FromXYZ(door.Location)
                        });
                    }
                }
            }
            else if (ruleCode == "BCR-090") // 防火門之防火時效 (>= 1.0hr / 60min)
            {
                bool isFireDoor = name.Contains("防火") || name.Contains("FD") || name.Contains("Fire") || door.GetStringParameter("Fire Rating") != null;
                if (isFireDoor)
                {
                    double rating = door.GetDoubleParameter("防火時效") ?? door.GetDoubleParameter("Fire Rating") ?? 0;
                    string ratingStr = door.GetStringParameter("防火時效") ?? door.GetStringParameter("Fire Rating") ?? string.Empty;
                    
                    bool isCompliant = false;
                    if (rating >= 60.0 || rating >= 1.0) // 可能是 60 分鐘或 1.0 小時
                    {
                        isCompliant = true;
                    }
                    else if (ratingStr.Contains("1") || ratingStr.Contains("甲") || ratingStr.Contains("60") || ratingStr.Contains("hr"))
                    {
                        isCompliant = true;
                    }

                    if (!isCompliant)
                    {
                        string displayRating = rating > 0 ? rating.ToString("F1") : (string.IsNullOrEmpty(ratingStr) ? "無" : ratingStr);
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"防火門「{name}」檢測防火時效為「{displayRating}」，未達法定甲種防火門 1.0hr (60分鐘) 標準。",
                            ElementId = door.ElementId,
                            LevelName = door.LevelName,
                            Location = XYZPoint.FromXYZ(door.Location)
                        });
                    }
                }
            }

            return results;
        }
    }

    public static class RampChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement ramp)
        {
            var results = new List<ReviewResult>();

            double slope = ramp.GetDoubleParameter("Ramp Max Slope") ?? ramp.GetDoubleParameter("Type_Ramp Max Slope") ?? ramp.GetDoubleParameter("坡度") ?? 0;
            double width = ramp.GetDoubleParameter("寬度") ?? ramp.GetDoubleParameter("Type_寬度") ?? ramp.GetDoubleParameter("Width") ?? 0;

            if (slope > 0.0834) // 大於 1/12 (0.0833)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"無障礙坡道坡度為 1:{(1.0/slope):F1} ({slope*100:F2}%)，超出無障礙規範 1:12 (8.33%) 限制。",
                    ElementId = ramp.ElementId,
                    LevelName = ramp.LevelName,
                    Location = XYZPoint.FromXYZ(ramp.Location)
                });
            }

            if (width > 0 && width < 1200.0) // 坡道淨寬應 >= 1.2m
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"無障礙坡道寬度為 {width:F1}mm，低於法規淨寬標準 (應 >= 1200mm)。",
                    ElementId = ramp.ElementId,
                    LevelName = ramp.LevelName,
                    Location = XYZPoint.FromXYZ(ramp.Location)
                });
            }

            return results;
        }
    }

    public static class LevelChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement room)
        {
            var results = new List<ReviewResult>();

            if (room.CategoryName.Contains("房間") || room.CategoryName.Contains("Room"))
            {
                double height = room.GetDoubleParameter("淨高") ?? room.GetDoubleParameter("Height") ?? room.GetDoubleParameter("Limit Offset") ?? 0;
                if (height > 0 && height < 2100.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"房間「{room.ElementName}」淨高為 {height:F1}mm，低於法規居室淨高限制 (>= 2100mm)。",
                        ElementId = room.ElementId,
                        LevelName = room.LevelName,
                        Location = XYZPoint.FromXYZ(room.Location)
                    });
                }
            }
            return results;
        }
    }

    public static class FloorHeightChecker
    {
        public static List<ReviewResult> Check(Document doc, List<ExtractedElement> levels)
        {
            var results = new List<ReviewResult>();
            if (levels == null || levels.Count < 2) return results;

            // 依高度由低到高排序
            var sortedLevels = levels
                .Select(l => {
                    double elev = l.GetDoubleParameter("立面") ?? l.GetDoubleParameter("Elevation") ?? 0;
                    return new { Element = l, Elevation = elev };
                })
                .OrderBy(l => l.Elevation)
                .ToList();

            for (int i = 0; i < sortedLevels.Count - 1; i++)
            {
                var current = sortedLevels[i];
                var next = sortedLevels[i + 1];
                double diff = next.Elevation - current.Elevation;

                // 換算成 mm (如果 Revit 中是以英呎為單位，0.3048 m = 304.8 mm)
                // 為了安全起見，若 diff 值很小 (如 < 50)，可能是以英呎計，將其乘以 304.8
                if (diff < 100.0)
                {
                    diff = diff * 304.8;
                }

                if (diff > 0 && diff < 2600.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"樓層高度差 (「{current.Element.ElementName}」至「{next.Element.ElementName}」) 為 {diff:F0}mm，低於基本樓層居室淨高度 2.6m 要求。",
                        ElementId = current.Element.ElementId,
                        LevelName = current.Element.LevelName,
                        Location = XYZPoint.FromXYZ(current.Element.Location)
                    });
                }
            }

            return results;
        }
    }

    public static class RailingChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement railing)
        {
            var results = new List<ReviewResult>();
            if (railing == null) return results;

            double height = railing.GetDoubleParameter("高度") ?? railing.GetDoubleParameter("Type_高度") ?? railing.GetDoubleParameter("Height") ?? railing.GetDoubleParameter("Type_Height") ?? 0;
            
            double limitHeight = 1100.0;
            string desc = "二層以上欄杆";

            string lvlName = railing.LevelName ?? string.Empty;
            bool isHighLevel = lvlName.Contains("10F") || lvlName.Contains("11F") || lvlName.Contains("12F") || lvlName.Contains("13F") || lvlName.Contains("14F") || lvlName.Contains("15F") || lvlName.Contains("16F") || lvlName.Contains("17F") || lvlName.Contains("18F") || lvlName.Contains("19F") || lvlName.Contains("20F") || lvlName.Contains("RF") || lvlName.Contains("屋頂");
            
            if (isHighLevel)
            {
                limitHeight = 1200.0;
                desc = "十層以上欄杆/屋頂防墜";
            }

            if (height > 0 && height < limitHeight)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"[{desc}]「{railing.ElementName}」高度為 {height:F1}mm，低於安全防護標準 (應 >= {limitHeight:F0}mm)。",
                    ElementId = railing.ElementId,
                    LevelName = railing.LevelName,
                    Location = XYZPoint.FromXYZ(railing.Location)
                });
            }
            return results;
        }
    }

    public static class ParkingChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement element)
        {
            var results = new List<ReviewResult>();
            string name = element.ElementName ?? string.Empty;

            double width = element.GetDoubleParameter("寬度") ?? element.GetDoubleParameter("Width") ?? element.GetDoubleParameter("Type_寬度") ?? element.GetDoubleParameter("Type_Width") ?? 0;
            double length = element.GetDoubleParameter("長度") ?? element.GetDoubleParameter("Length") ?? element.GetDoubleParameter("Type_長度") ?? element.GetDoubleParameter("Type_Length") ?? 0;

            bool isAccessible = name.Contains("無障礙") || name.Contains("Accessible");
            double limitWidth = isAccessible ? 3500.0 : 2500.0;
            double limitLength = 5500.0;

            if (width > 0 && width < limitWidth)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"{(isAccessible ? "無障礙" : "一般")}車位「{name}」寬度為 {width:F0}mm，低於法規標準 (應 >= {limitWidth:F0}mm)。",
                    ElementId = element.ElementId,
                    LevelName = element.LevelName,
                    Location = XYZPoint.FromXYZ(element.Location)
                });
            }

            if (length > 0 && length < limitLength)
            {
                results.Add(new ReviewResult
                {
                    Passed = false,
                    Message = $"車位「{name}」長度為 {length:F0}mm，低於法規標準 (應 >= {limitLength:F0}mm)。",
                    ElementId = element.ElementId,
                    LevelName = element.LevelName,
                    Location = XYZPoint.FromXYZ(element.Location)
                });
            }

            return results;
        }
    }

    public static class AccessibleToiletChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement room)
        {
            var results = new List<ReviewResult>();
            string name = room.ElementName ?? string.Empty;

            if (name.Contains("無障礙") && (name.Contains("廁") || name.Contains("盥洗") || name.Contains("衛浴") || name.Contains("Toilet") || name.Contains("WC")))
            {
                // 讀取長寬或由面積概估
                double width = room.GetDoubleParameter("寬度") ?? room.GetDoubleParameter("Width") ?? 0;
                double length = room.GetDoubleParameter("長度") ?? room.GetDoubleParameter("Length") ?? 0;
                double area = room.GetDoubleParameter("面積") ?? room.GetDoubleParameter("Area") ?? 0;

                // 若長寬有值，至少要 1500mm x 1500mm
                if (width > 0 && width < 1500.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"無障礙廁所「{name}」寬度為 {width:F0}mm，未達 1.5m 輪椅迴轉要求。",
                        ElementId = room.ElementId,
                        LevelName = room.LevelName,
                        Location = XYZPoint.FromXYZ(room.Location)
                    });
                }
                if (length > 0 && length < 1500.0)
                {
                    results.Add(new ReviewResult
                    {
                        Passed = false,
                        Message = $"無障礙廁所「{name}」長度為 {length:F0}mm，未達 1.5m 輪椅迴轉要求。",
                        ElementId = room.ElementId,
                        LevelName = room.LevelName,
                        Location = XYZPoint.FromXYZ(room.Location)
                    });
                }

                // 面積估算：若無長寬，以面積 2.25 sqm (1.5m x 1.5m) 估算
                // Revit 面積單位可能為平方英呎，若是 <= 24.2 平方英呎 (即 2.25 sqm)
                if (area > 0)
                {
                    double areaSqm = area;
                    if (area > 100) // 可能是 sq ft
                    {
                        areaSqm = area * 0.092903;
                    }
                    if (areaSqm < 2.25)
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"無障礙廁所「{name}」面積僅 {areaSqm:F2}㎡，低於輪椅迴轉所需之基本淨空間面積 (應 >= 2.25㎡)。",
                            ElementId = room.ElementId,
                            LevelName = room.LevelName,
                            Location = XYZPoint.FromXYZ(room.Location)
                        });
                    }
                }
            }

            return results;
        }
    }

    public static class FireDistrictAreaChecker
    {
        public static List<ReviewResult> Check(Document doc, ExtractedElement room)
        {
            var results = new List<ReviewResult>();
            string name = room.ElementName ?? string.Empty;

            if (name.Contains("防火區劃") || name.Contains("防火分區") || name.Contains("Fire Compartment"))
            {
                double area = room.GetDoubleParameter("面積") ?? room.GetDoubleParameter("Area") ?? 0;
                if (area > 0)
                {
                    double areaSqm = area;
                    if (area > 500) // 可能是平方英呎
                    {
                        areaSqm = area * 0.092903;
                    }

                    // 設有灑水設備可放寬為 3000 sqm，否則 1500 sqm
                    double limitArea = name.Contains("灑水") || name.Contains("自動滅火") ? 3000.0 : 1500.0;

                    if (areaSqm > limitArea)
                    {
                        results.Add(new ReviewResult
                        {
                            Passed = false,
                            Message = $"防火區劃「{name}」面積為 {areaSqm:F1}㎡，大於法規限制上限 {limitArea:F0}㎡。",
                            ElementId = room.ElementId,
                            LevelName = room.LevelName,
                            Location = XYZPoint.FromXYZ(room.Location)
                        });
                    }
                }
            }

            return results;
        }
    }
}
