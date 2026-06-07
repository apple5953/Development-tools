using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace DevelopmentTools.Algorithms
{
    public static class TileDimensionDetector
    {
        /// <summary>
        /// 自動偵測裝修面元素（Wall 或 Floor）所關聯的磁磚尺寸。
        /// </summary>
        public static bool DetectDimensions(Element elem, Document doc, out double width, out double height)
        {
            width = 300.0; // 預設寬度
            height = 300.0; // 預設高度

            List<string> sources = new List<string>();

            // 1. 實例參數 "描述" (Description)
            Parameter descParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
            if (descParam != null && descParam.HasValue)
            {
                sources.Add(descParam.AsString());
            }

            // 2. 類型名稱與類型參數 "描述"
            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element elemType = doc.GetElement(typeId);
                if (elemType != null)
                {
                    sources.Add(elemType.Name);

                    Parameter typeDescParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (typeDescParam != null && typeDescParam.HasValue)
                    {
                        sources.Add(typeDescParam.AsString());
                    }

                    Parameter typeDescParamZh = elemType.LookupParameter("描述");
                    if (typeDescParamZh != null && typeDescParamZh.HasValue)
                    {
                        sources.Add(typeDescParamZh.AsString());
                    }
                }
            }

            // 3. Finish 層的材質名稱
            try
            {
                if (elem is Wall wall)
                {
                    CompoundStructure cs = wall.WallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
                else if (elem is Floor floor)
                {
                    CompoundStructure cs = floor.FloorType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 4. 遍歷所有可能包含尺寸的文字來源
            foreach (string text in sources)
            {
                if (string.IsNullOrEmpty(text)) continue;
                if (ParseDimensions(text, out double w, out double h))
                {
                    width = w;
                    height = h;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 使用 Regex 解析描述文字中的尺寸資訊 (例如 30*60cm, 60x60cm, 300*600)。
        /// </summary>
        public static bool ParseDimensions(string text, out double width, out double height)
        {
            width = 0;
            height = 0;

            // 匹配 [數字] * [數字] 或 [數字] x [數字] 等，並捕捉單位 (cm 或 mm)
            Match match = Regex.Match(
                text,
                @"(\d+(?:\.\d+)?)\s*[*xX×]\s*(\d+(?:\.\d+)?)\s*(cm|mm)?",
                RegexOptions.IgnoreCase
            );

            if (match.Success)
            {
                try
                {
                    double d1 = double.Parse(match.Groups[1].Value);
                    double d2 = double.Parse(match.Groups[2].Value);
                    string unit = match.Groups[3].Value.ToLower();

                    bool isCm = false;
                    if (unit == "cm")
                    {
                        isCm = true;
                    }
                    else if (unit == "mm")
                    {
                        isCm = false;
                    }
                    else
                    {
                        // 若無單位，且數字小於 100，合理推測為公分 (cm)
                        if (d1 < 100) isCm = true;
                    }

                    if (isCm)
                    {
                        width = d1 * 10.0;
                        height = d2 * 10.0;
                    }
                    else
                    {
                        width = d1;
                        height = d2;
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// 自動偵測裝修面元素（Wall 或 Floor）所關聯的排版樣式。
        /// 0: 對縫, 1: 1/2錯縫, 2: 1/3錯縫
        /// </summary>
        public static int DetectPatternType(Element elem, Document doc)
        {
            List<string> sources = new List<string>();

            // 1. 實例參數 "描述" (Description)
            Parameter descParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
            if (descParam != null && descParam.HasValue)
            {
                sources.Add(descParam.AsString());
            }

            // 2. 類型名稱與類型參數 "描述"
            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element elemType = doc.GetElement(typeId);
                if (elemType != null)
                {
                    sources.Add(elemType.Name);

                    Parameter typeDescParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (typeDescParam != null && typeDescParam.HasValue)
                    {
                        sources.Add(typeDescParam.AsString());
                    }

                    Parameter typeDescParamZh = elemType.LookupParameter("描述");
                    if (typeDescParamZh != null && typeDescParamZh.HasValue)
                    {
                        sources.Add(typeDescParamZh.AsString());
                    }
                }
            }

            // 3. Finish 層的材質名稱
            try
            {
                if (elem is Wall wall)
                {
                    CompoundStructure cs = wall.WallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
                else if (elem is Floor floor)
                {
                    CompoundStructure cs = floor.FloorType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            foreach (string text in sources)
            {
                if (string.IsNullOrEmpty(text)) continue;
                if (text.Contains("1/2錯縫") || text.Contains("1/2 錯縫") || text.Contains("半磚錯縫") || text.Contains("半磚"))
                {
                    return 1;
                }
                if (text.Contains("1/3錯縫") || text.Contains("1/3 錯縫"))
                {
                    return 2;
                }
            }

            return 0; // 預設對縫
        }

        /// <summary>
        /// 自動偵測裝修面元素（Wall 或 Floor）所關聯的完整排版與尺寸參數。
        /// </summary>
        public static DevelopmentTools.Core.TilePatternParams DetectPatternParams(Element elem, Document doc, double defaultW, double defaultH, double defaultJoint, double defaultThickness)
        {
            DevelopmentTools.Core.TilePatternParams parameters = new DevelopmentTools.Core.TilePatternParams
            {
                Style = DevelopmentTools.Core.TilePatternStyle.Stack,
                TileWidth = defaultW,
                TileHeight = defaultH,
                JointWidth = defaultJoint,
                Thickness = DetectThickness(elem, doc, defaultThickness),
                OffsetPercent = 50.0
            };

            // 優先讀取 Tile_Joint_Width 共享參數（由外掛加入按鈕寫入，單位 mm）
            try
            {
                Parameter jointParam = elem.LookupParameter("Tile_Joint_Width");
                if (jointParam != null && jointParam.HasValue)
                {
                    double jVal = jointParam.AsDouble();
                    // 啟發式自動判斷：如果數值非常小且大於 0（例如小於 0.1），
                    // 則高度懷疑它是 LENGTH 類型（以英呎為單位），需乘以 304.8 轉為公釐
                    if (jVal > 0.0 && jVal < 0.1)
                    {
                        jVal = jVal * 304.8;
                    }
                    if (jVal >= 0.0) parameters.JointWidth = jVal;
                }
            }
            catch { }

            List<string> sources = new List<string>();

            // 1. 實例參數 "描述" (Description)
            Parameter descParam = elem.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
            if (descParam != null && descParam.HasValue)
            {
                sources.Add(descParam.AsString());
            }

            // 2. 類型名稱與類型參數 "描述"
            ElementId typeId = elem.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element elemType = doc.GetElement(typeId);
                if (elemType != null)
                {
                    sources.Add(elemType.Name);

                    Parameter typeDescParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION);
                    if (typeDescParam != null && typeDescParam.HasValue)
                    {
                        sources.Add(typeDescParam.AsString());
                    }

                    Parameter typeDescParamZh = elemType.LookupParameter("描述");
                    if (typeDescParamZh != null && typeDescParamZh.HasValue)
                    {
                        sources.Add(typeDescParamZh.AsString());
                    }
                }
            }

            // 3. Finish 層的材質名稱
            try
            {
                if (elem is Wall wall)
                {
                    CompoundStructure cs = wall.WallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
                else if (elem is Floor floor)
                {
                    CompoundStructure cs = floor.FloorType.GetCompoundStructure();
                    if (cs != null)
                    {
                        for (int i = 0; i < cs.LayerCount; i++)
                        {
                            if (cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish1 ||
                                cs.GetLayerFunction(i) == MaterialFunctionAssignment.Finish2)
                            {
                                ElementId matId = cs.GetMaterialId(i);
                                if (matId != ElementId.InvalidElementId)
                                {
                                    Material mat = doc.GetElement(matId) as Material;
                                    if (mat != null) sources.Add(mat.Name);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 4. 解析尺寸
            foreach (string text in sources)
            {
                if (string.IsNullOrEmpty(text)) continue;
                if (ParseDimensions(text, out double w, out double h))
                {
                    parameters.TileWidth = w;
                    parameters.TileHeight = h;
                    break;
                }
            }

            // 5. 解析樣式與額外參數
            foreach (string text in sources)
            {
                if (string.IsNullOrEmpty(text)) continue;

                // 檢查雙高度
                if (text.Contains("雙高度") || text.Contains("雙高"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.DoubleHeightStack;
                    Match hMatch = Regex.Match(text, @"H(?:1)?[:_]?(\d+)[/_]?H(?:2)?[:_]?(\d+)", RegexOptions.IgnoreCase);
                    if (hMatch.Success)
                    {
                        parameters.TileHeight = double.Parse(hMatch.Groups[1].Value);
                        parameters.TileHeight2 = double.Parse(hMatch.Groups[2].Value);
                    }
                    else
                    {
                        parameters.TileHeight2 = parameters.TileHeight / 2.0;
                    }
                    continue;
                }

                // 檢查雙寬度
                if (text.Contains("雙寬度") || text.Contains("雙寬"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.DoubleWidthStack;
                    Match wMatch = Regex.Match(text, @"W(?:1)?[:_]?(\d+)[/_]?W(?:2)?[:_]?(\d+)", RegexOptions.IgnoreCase);
                    if (wMatch.Success)
                    {
                        parameters.TileWidth = double.Parse(wMatch.Groups[1].Value);
                        parameters.TileWidth2 = double.Parse(wMatch.Groups[2].Value);
                    }
                    else
                    {
                        parameters.TileWidth2 = parameters.TileWidth / 2.0;
                    }
                    continue;
                }

                // 檢查六邊形有縫/無縫
                if (text.Contains("六邊形-有縫") || text.Contains("六邊形有縫"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.HexagonJoint;
                    continue;
                }
                if (text.Contains("六邊形"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.HexagonSeamless;
                    continue;
                }

                // 檢查三角形
                if (text.Contains("四塊三角"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.FourTriangles;
                    continue;
                }
                if (text.Contains("二塊三角"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.TwoTriangles;
                    continue;
                }

                // 檢查人字拼
                if (text.Contains("人字拼"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.Herringbone;
                    continue;
                }

                // 檢查水平縫與垂直縫
                if (text.Contains("水平縫"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.HorizontalJoint;
                    continue;
                }
                if (text.Contains("垂直縫"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.VerticalJoint;
                    continue;
                }

                // 檢查錯位百分比
                Match offsetMatch = Regex.Match(text, @"(?:錯位|錯縫|交錯|偏移)\s*(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
                if (offsetMatch.Success)
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.RunningBond;
                    parameters.OffsetPercent = double.Parse(offsetMatch.Groups[1].Value);
                    continue;
                }

                // 傳統 1/2, 1/3, 1/4 錯縫
                if (text.Contains("1/2錯縫") || text.Contains("1/2 錯縫") || text.Contains("半磚錯縫") || text.Contains("半磚"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.RunningBond;
                    parameters.OffsetPercent = 50.0;
                    continue;
                }
                if (text.Contains("1/3錯縫") || text.Contains("1/3 錯縫"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.RunningBond;
                    parameters.OffsetPercent = 33.33;
                    continue;
                }
                if (text.Contains("1/4錯縫") || text.Contains("1/4 錯縫"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.RunningBond;
                    parameters.OffsetPercent = 25.0;
                    continue;
                }

                // 檢查堆疊分割 / 堆疊錯位
                if (text.Contains("堆疊分割"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.StackSplit;
                    continue;
                }
                if (text.Contains("堆疊錯位"))
                {
                    parameters.Style = DevelopmentTools.Core.TilePatternStyle.StackOffset;
                    continue;
                }
            }

            return parameters;
        }

        private static bool ParseThicknessFromName(string name, out double thicknessMm)
        {
            thicknessMm = 0;
            if (string.IsNullOrEmpty(name)) return false;

            // 改用更寬鬆的 Regex 匹配，例如 _8mm 或 _0.8cm，不限制在字串末尾，改用字詞邊界 \b
            Match match = Regex.Match(
                name, 
                @"_(\d+(?:\.\d+)?)\s*(mm|cm)\b", 
                RegexOptions.IgnoreCase
            );
            if (match.Success)
            {
                try
                {
                    double val = double.Parse(match.Groups[1].Value);
                    string unit = match.Groups[2].Value.ToLower();
                    if (unit == "cm")
                    {
                        thicknessMm = val * 10.0;
                    }
                    else
                    {
                        thicknessMm = val;
                    }
                    return true;
                }
                catch { }
            }
            return false;
        }

        public static bool TryParseThicknessFromNameFlexible(string name, out double thicknessMm)
        {
            thicknessMm = 0;

            if (ParseThicknessFromName(name, out thicknessMm))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            MatchCollection matches = Regex.Matches(
                name,
                @"(?<![xX*×])(\d+(?:\.\d+)?)\s*(mm|cm)\b",
                RegexOptions.IgnoreCase
            );

            if (matches.Count == 0)
            {
                return false;
            }

            try
            {
                Match match = matches[matches.Count - 1];
                double val = double.Parse(match.Groups[1].Value);
                string unit = match.Groups[2].Value.ToLower();

                thicknessMm = unit == "cm" ? val * 10.0 : val;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static double DetectThickness(Element elem, Document doc, double defaultThickness)
        {
            double thicknessMm = defaultThickness;
            try
            {
                thicknessMm = DetectThicknessInternal(elem, doc, defaultThickness);
            }
            catch { }

            // 物理合理性過濾：磁磚厚度絕對不可能大於 50mm (5公分)。若大於，則必定是誤讀了主體結構層的物理厚度，強制回退為 defaultThickness
            if (thicknessMm > 50.0)
            {
                thicknessMm = (defaultThickness > 50.0) ? 8.0 : defaultThickness;
            }
            return thicknessMm;
        }

        private static double DetectThicknessInternal(Element elem, Document doc, double defaultThickness)
        {
            try
            {
                // 1. 優先從宿主類型名稱中解析厚度 (例如 _8mm 或 _0.8cm)
                ElementId typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element elemType = doc.GetElement(typeId);
                    if (elemType != null && TryParseThicknessFromNameFlexible(elemType.Name, out double tFromName))
                    {
                        return tFromName;
                    }
                }

                // 2. 從複合物構造 (CompoundStructure) 的層中尋找
                if (elem is Wall wall)
                {
                    CompoundStructure cs = wall.WallType.GetCompoundStructure();
                    if (cs != null)
                    {
                        int count = cs.LayerCount;
                        // 2a. 優先檢查每一層的材質名稱是否包含厚度資訊
                        for (int i = 0; i < count; i++)
                        {
                            ElementId matId = cs.GetMaterialId(i);
                            if (matId != ElementId.InvalidElementId)
                            {
                                Material mat = doc.GetElement(matId) as Material;
                                if (mat != null && TryParseThicknessFromNameFlexible(mat.Name, out double tFromMatName))
                                {
                                    return tFromMatName;
                                }
                            }
                        }

                        // 2b. 其次檢查材質名稱包含關鍵字 (例如 磁磚, 瓷磚, Tile, 裝修面, 地坪) 的層厚度
                        for (int i = 0; i < count; i++)
                        {
                            ElementId matId = cs.GetMaterialId(i);
                            if (matId != ElementId.InvalidElementId)
                            {
                                Material mat = doc.GetElement(matId) as Material;
                                if (mat != null)
                                {
                                    string name = mat.Name;
                                    if (name.Contains("磁磚") || name.Contains("瓷磚") || 
                                        name.Contains("Tile") || name.Contains("PAT") || 
                                        name.Contains("MAT") || name.Contains("裝修面") || name.Contains("地坪"))
                                    {
                                        double wFeet = cs.GetLayerWidth(i);
                                        if (wFeet > 0.001) return wFeet * 304.8;
                                    }
                                }
                            }
                        }

                        // 2c. 再其次尋找 Function 為 Finish1 或 Finish2 的層厚度
                        for (int i = 0; i < count; i++)
                        {
                            MaterialFunctionAssignment func = cs.GetLayerFunction(i);
                            if (func == MaterialFunctionAssignment.Finish1 || func == MaterialFunctionAssignment.Finish2)
                            {
                                double wFeet = cs.GetLayerWidth(i);
                                if (wFeet > 0.001) return wFeet * 304.8;
                            }
                        }
                    }
                }
                else if (elem is Floor floor)
                {
                    CompoundStructure cs = floor.FloorType.GetCompoundStructure();
                    if (cs != null)
                    {
                        int count = cs.LayerCount;
                        // 2a. 優先檢查每一層的材質名稱是否包含厚度資訊
                        for (int i = 0; i < count; i++)
                        {
                            ElementId matId = cs.GetMaterialId(i);
                            if (matId != ElementId.InvalidElementId)
                            {
                                Material mat = doc.GetElement(matId) as Material;
                                if (mat != null && TryParseThicknessFromNameFlexible(mat.Name, out double tFromMatName))
                                {
                                    return tFromMatName;
                                }
                            }
                        }

                        // 2b. 其次檢查材質名稱包含關鍵字 (例如 磁磚, 瓷磚, Tile, 裝修面, 地坪) 的層厚度
                        for (int i = 0; i < count; i++)
                        {
                            ElementId matId = cs.GetMaterialId(i);
                            if (matId != ElementId.InvalidElementId)
                            {
                                Material mat = doc.GetElement(matId) as Material;
                                if (mat != null)
                                {
                                    string name = mat.Name;
                                    if (name.Contains("磁磚") || name.Contains("瓷磚") || 
                                        name.Contains("Tile") || name.Contains("PAT") || 
                                        name.Contains("MAT") || name.Contains("裝修面") || name.Contains("地坪"))
                                    {
                                        double wFeet = cs.GetLayerWidth(i);
                                        if (wFeet > 0.001) return wFeet * 304.8;
                                    }
                                }
                            }
                        }

                        // 2c. 再其次尋找 Function 為 Finish1 或 Finish2 的層厚度
                        for (int i = 0; i < count; i++)
                        {
                            MaterialFunctionAssignment func = cs.GetLayerFunction(i);
                            if (func == MaterialFunctionAssignment.Finish1 || func == MaterialFunctionAssignment.Finish2)
                            {
                                double wFeet = cs.GetLayerWidth(i);
                                if (wFeet > 0.001) return wFeet * 304.8;
                            }
                        }
                    }
                }
            }
            catch { }
            return defaultThickness;
        }
    }
}
