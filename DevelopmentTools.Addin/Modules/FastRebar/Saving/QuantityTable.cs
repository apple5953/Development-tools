// 鋼筋數量統計表：列（樓層、構件類別…）× 號數，數值為重量（kg）。不引用 Revit API。
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DevelopmentTools.FastRebar.Saving
{
    public sealed class QuantityTable
    {
        private sealed class Cell { public double LengthMm; public int Bars; }

        private readonly Dictionary<string, Dictionary<string, Cell>> _rows = new Dictionary<string, Dictionary<string, Cell>>();
        private readonly Dictionary<string, double> _diameters = new Dictionary<string, double>();
        private readonly List<string> _rowOrder = new List<string>();

        public string RowTitle;

        public QuantityTable(string rowTitle) { RowTitle = rowTitle; }

        public void Add(string row, string size, double diameterMm, double lengthMm, int bars)
        {
            if (string.IsNullOrEmpty(row)) row = "（未指定）";
            if (string.IsNullOrEmpty(size)) size = "未知號數";
            Dictionary<string, Cell> cells;
            if (!_rows.TryGetValue(row, out cells))
            {
                cells = new Dictionary<string, Cell>();
                _rows[row] = cells;
                _rowOrder.Add(row);
            }
            Cell c;
            if (!cells.TryGetValue(size, out c)) { c = new Cell(); cells[size] = c; }
            c.LengthMm += lengthMm;
            c.Bars += bars;
            double d;
            if (!_diameters.TryGetValue(size, out d) || diameterMm > d) _diameters[size] = diameterMm;
        }

        public IList<string> Sizes { get { return _diameters.OrderBy(k => k.Value).ThenBy(k => k.Key).Select(k => k.Key).ToList(); } }

        public double Kg(string row, string size)
        {
            Dictionary<string, Cell> cells;
            Cell c;
            if (!_rows.TryGetValue(row, out cells) || !cells.TryGetValue(size, out c)) return 0;
            return c.LengthMm / 1000 * RebarWeights.KgPerM(_diameters[size]);
        }

        public double TotalKg
        {
            get { return _rowOrder.Sum(r => Sizes.Sum(s => Kg(r, s))); }
        }

        /// <param name="rowSort">列排序；null 依加入順序。</param>
        public void AppendCsv(StringBuilder sb, IComparer<string> rowSort)
        {
            var sizes = Sizes;
            var rows = rowSort == null ? _rowOrder.ToList() : _rowOrder.OrderBy(r => r, rowSort).ToList();
            var c = CultureInfo.InvariantCulture;
            sb.AppendLine(Csv(new[] { RowTitle + " \\ 號數 (kg)" }.Concat(sizes).Concat(new[] { "合計 (kg)" })));
            foreach (string r in rows)
            {
                var vals = sizes.Select(s => Kg(r, s)).ToList();
                sb.AppendLine(Csv(new[] { r }.Concat(vals.Select(v => v.ToString("0.0", c))).Concat(new[] { vals.Sum().ToString("0.0", c) })));
            }
            var totals = sizes.Select(s => rows.Sum(r => Kg(r, s))).ToList();
            sb.AppendLine(Csv(new[] { "合計" }.Concat(totals.Select(v => v.ToString("0.0", c))).Concat(new[] { totals.Sum().ToString("0.0", c) })));
        }

        private static string Csv(IEnumerable<string> cells)
        {
            return string.Join(",", cells.Select(v =>
                v != null && v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v ?? ""));
        }
    }
}
