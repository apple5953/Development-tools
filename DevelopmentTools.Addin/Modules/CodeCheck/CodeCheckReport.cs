using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace DevelopmentTools.CodeCheck
{
    /// <summary>匯出報告：自含 HTML（可直接寄送、列印）與 CSV（逐項結果）。</summary>
    public static class CodeCheckReport
    {
        private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        private static string Cls(CheckStatus s)
        {
            switch (s)
            {
                case CheckStatus.Fail: return "fail";
                case CheckStatus.Warning: return "warn";
                case CheckStatus.NoData: return "nodata";
                case CheckStatus.Pass: return "pass";
                case CheckStatus.NotApplicable: return "na";
                case CheckStatus.Manual: return "manual";
                default: return "info";
            }
        }

        public static void WriteHtml(string path, string projectTitle, ProjectSettings s, CodeCheckData data,
                                     List<ArticleResult> articles, IEnumerable<string> notes, string scope = null)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>").Append(H(projectTitle)).Append(" 法規檢討報告</title><style>");
            sb.Append(@"
:root{--bg:#fff;--fg:#1d2433;--mute:#5b6475;--line:#d9dee7;--card:#f6f8fb;--fail:#c0392b;--warn:#b7791f;--nodata:#6b46c1;--pass:#2f855a;--na:#718096;--manual:#2b6cb0}
@media (prefers-color-scheme:dark){:root{--bg:#14171c;--fg:#e6e9ef;--mute:#9aa4b2;--line:#2c323c;--card:#1b2028;--fail:#f56565;--warn:#ecc94b;--nodata:#b794f4;--pass:#68d391;--na:#a0aec0;--manual:#63b3ed}}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.6 'Microsoft JhengHei','PingFang TC','Noto Sans TC',sans-serif}
main{max-width:1100px;margin:0 auto;padding:24px 16px}
h1{font-size:22px;margin:0 0 4px}h2{font-size:18px;margin:28px 0 8px;border-bottom:1px solid var(--line);padding-bottom:4px}
.mute{color:var(--mute)}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:8px;margin:16px 0}
.card{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px}.card b{display:block;font-size:20px}
table{width:100%;border-collapse:collapse;margin:8px 0}th,td{border-bottom:1px solid var(--line);padding:6px 8px;text-align:left;vertical-align:top}
th{font-weight:600;color:var(--mute);font-size:12px}
.tag{display:inline-block;border-radius:10px;padding:0 8px;font-size:12px;border:1px solid currentColor;white-space:nowrap}
.fail{color:var(--fail)}.warn{color:var(--warn)}.nodata{color:var(--nodata)}.pass{color:var(--pass)}.na{color:var(--na)}.manual{color:var(--manual)}.info{color:var(--na)}
details{margin:4px 0}summary{cursor:pointer}.law{white-space:pre-wrap;color:var(--mute);font-size:13px;background:var(--card);padding:8px;border-radius:6px}
.wrap{overflow-x:auto}");
            sb.Append("</style></head><body><main>");
            sb.Append("<h1>").Append(H(projectTitle)).Append(" 法規檢討報告</h1>");
            sb.Append("<div class=\"mute\">產出時間 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("｜法規資料版本 ").Append(H(data.Rules.Version)).Append("</div>");

            // 專案條件
            sb.Append("<h2>專案條件</h2><table>");
            void Row(string k, string v) => sb.Append("<tr><th>").Append(H(k)).Append("</th><td>").Append(H(v)).Append("</td></tr>");
            if (!string.IsNullOrEmpty(scope)) Row("檢討範圍", scope);
            Row("使用類組", string.Join("、", s.UseGroups));
            Row("消防場所分類", string.Join("、", s.FireClasses));
            Row("土地使用分區", s.Zone + (s.VolumeControl ? "（實施容積管制）" : "（未實施容積管制）"));
            Row("面前道路寬度", s.RoadWidthM > 0 ? s.RoadWidthM + " m" : "未填");
            Row("防火構造", s.Fireproof ? "是" : "否");
            Row("無障礙設施適用", s.AccessibilityApplies ? "是" : "否");
            sb.Append("</table>");

            // 總覽
            var counted = articles.Where(a => a.Item.Kind != "info").ToList();
            sb.Append("<h2>總覽</h2><div class=\"cards\">");
            void Card(string label, int n, string cls) => sb.Append("<div class=\"card ").Append(cls).Append("\"><b>").Append(n).Append("</b>").Append(H(label)).Append("</div>");
            Card("收錄條文", articles.Count, "");
            Card("自動／半自動條文", articles.Count(a => a.Item.Kind == "auto" || a.Item.Kind == "semi"), "");
            Card("不符合", articles.Count(a => a.Status == CheckStatus.Fail), "fail");
            Card("需確認", articles.Count(a => a.Status == CheckStatus.Warning), "warn");
            Card("資料不足", articles.Count(a => a.Status == CheckStatus.NoData), "nodata");
            Card("符合", articles.Count(a => a.Status == CheckStatus.Pass), "pass");
            Card("人工檢討待辦", articles.Count(a => a.Status == CheckStatus.Manual), "manual");
            sb.Append("</div>");

            // 問題清單
            var issues = articles.Where(a => a.Status == CheckStatus.Fail || a.Status == CheckStatus.Warning || a.Status == CheckStatus.NoData).ToList();
            sb.Append("<h2>需處理項目（").Append(issues.Count).Append(" 條）</h2>");
            foreach (var a in issues)
            {
                sb.Append("<details open><summary><span class=\"tag ").Append(Cls(a.Status)).Append("\">").Append(CodeCheckEngine.StatusText(a.Status)).Append("</span> ")
                  .Append(H(a.Law?.Name)).Append(" ").Append(H(CodeCheckData.ArticleLabel(a.Item.Law, a.Item.No))).Append("</summary>");
                sb.Append("<div class=\"law\">").Append(H(a.Article?.Text)).Append("</div><div class=\"wrap\"><table><tr><th>狀態</th><th>規則</th><th>對象</th><th>結果</th><th>模型</th><th>元素 ID</th></tr>");
                foreach (var r in a.Results.Where(x => x.Status != CheckStatus.NotApplicable && x.Status != CheckStatus.Pass).Take(200))
                {
                    sb.Append("<tr><td class=\"").Append(Cls(r.Status)).Append("\">").Append(CodeCheckEngine.StatusText(r.Status)).Append("</td><td>")
                      .Append(H(r.Rule.Title)).Append("<div class=\"mute\">").Append(H(r.Rule.Message)).Append("</div></td><td>").Append(H(r.Target)).Append("</td><td>")
                      .Append(H(r.Detail)).Append("</td><td>").Append(H(r.Source?.Name)).Append("</td><td>").Append(r.ElementId?.IntegerValue.ToString(CultureInfo.InvariantCulture) ?? "").Append("</td></tr>");
                }
                sb.Append("</table></div>");
                if (s.Manual.TryGetValue(a.Key, out var m) && !string.IsNullOrEmpty(m.Note))
                    sb.Append("<div class=\"mute\">人工備註：").Append(H(m.Note)).Append("</div>");
                sb.Append("</details>");
            }

            // 全部條文
            sb.Append("<h2>全部條文檢討狀態</h2>");
            foreach (var g in articles.GroupBy(a => a.Item.Law))
            {
                var law = data.FindLaw(g.Key);
                sb.Append("<details><summary><b>").Append(H(law?.Name)).Append("</b> <span class=\"mute\">（").Append(g.Count()).Append(" 條，修正日期 ")
                  .Append(H(law?.Modified)).Append("）</span></summary><div class=\"wrap\"><table><tr><th>條號</th><th>檢討方式</th><th>狀態</th><th>摘要／說明</th></tr>");
                foreach (var a in g)
                {
                    string note = s.Manual.TryGetValue(a.Key, out var m) ? (m.Status + " " + m.Note).Trim() : "";
                    sb.Append("<tr><td>").Append(H(CodeCheckData.ArticleLabel(a.Item.Law, a.Item.No))).Append("</td><td>").Append(CodeCheckEngine.KindText(a.Item.Kind))
                      .Append("</td><td class=\"").Append(Cls(a.Status)).Append("\">").Append(CodeCheckEngine.StatusText(a.Status)).Append("</td><td>")
                      .Append(H(a.Item.Hint)).Append(a.Item.Reason != null ? "<div class=\"mute\">" + H(a.Item.Reason) + (note != "" ? "｜" + H(note) : "") + "</div>" : "").Append("</td></tr>");
                }
                sb.Append("</table></div></details>");
            }

            var nl = notes?.ToList() ?? new List<string>();
            if (nl.Count > 0)
            {
                sb.Append("<h2>量測說明</h2><ul>");
                foreach (string n in nl.Distinct()) sb.Append("<li>").Append(H(n)).Append("</li>");
                sb.Append("</ul>");
            }
            sb.Append("<p class=\"mute\">本報告由模型自動檢討產生；半自動項目依專案條件計算，人工項目須由建築師依圖說與文件檢討。正式送審以主管機關審查為準。法規條文來源：全國法規資料庫開放資料、內政部建築物無障礙設施設計規範（108.07.01）。</p>");
            sb.Append("</main></body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static void WriteCsv(string path, List<ArticleResult> articles)
        {
            var sb = new StringBuilder();
            sb.AppendLine("法規,條號,檢討方式,條文狀態,規則代碼,規則,對象,模型,元素ID,結果狀態,量測,門檻,說明");
            string Q(string x) => "\"" + (x ?? "").Replace("\"", "'") + "\"";
            foreach (var a in articles)
            {
                if (a.Results.Count == 0)
                {
                    sb.AppendLine(string.Join(",", Q(a.Law?.Name), Q(a.Item.No), Q(CodeCheckEngine.KindText(a.Item.Kind)), Q(CodeCheckEngine.StatusText(a.Status)), "", "", "", "", "", "", "", "", Q(a.Item.Reason)));
                    continue;
                }
                foreach (var r in a.Results)
                {
                    sb.AppendLine(string.Join(",", Q(a.Law?.Name), Q(a.Item.No), Q(CodeCheckEngine.KindText(a.Item.Kind)), Q(CodeCheckEngine.StatusText(a.Status)),
                        Q(r.Rule.Id), Q(r.Rule.Title), Q(r.Target), Q(r.Source?.Name), Q(r.ElementId?.IntegerValue.ToString(CultureInfo.InvariantCulture)),
                        Q(CodeCheckEngine.StatusText(r.Status)),
                        Q(r.Measured?.ToString("0.###", CultureInfo.InvariantCulture)), Q(r.Limit?.ToString("0.###", CultureInfo.InvariantCulture)), Q(r.Detail)));
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }
}
