using System.Text.RegularExpressions;

namespace DT_WordPdfLink.Utils
{
    /// <summary>
    /// 圖紙名稱解析工具。
    /// 提供命名規則：Revit 圖紙名稱格式為「前綴-(圖紙顯示名稱)」，
    /// 例如：「進版封面-(A棟 B2FL 放樣圖及輕隔間)」。
    /// Word 文件中只顯示括弧內的顯示名稱：「A棟 B2FL 放樣圖及輕隔間」。
    /// 若圖紙名稱不符合此格式（不含括弧），則原樣返回完整名稱。
    /// </summary>
    public static class SheetNameUtils
    {
        // 匹配 Revit 圖紙名稱中最後一對括弧 () 裡的文字
        // 例如：「進版封面-(A棟 B2FL 放樣圖及輕隔間)」 → 「A棟 B2FL 放樣圖及輕隔間」
        private static readonly Regex _parenthesesPattern = new Regex(@"\(([^()]+)\)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 解析圖紙名稱，返回 Word 文件中應顯示的名稱。
        /// <para>若圖紙名稱結尾含有括弧群組（如 <c>進版封面-(A棟 1FL)</c>），
        /// 則返回括弧內的文字（<c>A棟 1FL</c>）。</para>
        /// <para>否則返回原始圖紙名稱。</para>
        /// </summary>
        /// <param name="sheetName">Revit 圖紙的完整 Name 屬性值。</param>
        /// <returns>Word 文件預留位 <c>[圖紙名稱]</c> 應填入的顯示文字。</returns>
        public static string ParseDisplayName(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName))
                return sheetName;

            Match match = _parenthesesPattern.Match(sheetName);

            if (match.Success)
                return match.Groups[1].Value.Trim();

            return sheetName;
        }
    }
}
