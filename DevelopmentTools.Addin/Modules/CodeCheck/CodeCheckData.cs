using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DevelopmentTools.CodeCheck
{
    [DataContract(Namespace = "")]
    public class LawArticle
    {
        [DataMember(Name = "no")] public string No { get; set; }
        [DataMember(Name = "chapter")] public string Chapter { get; set; }
        [DataMember(Name = "text")] public string Text { get; set; }
    }

    [DataContract(Namespace = "")]
    public class Law
    {
        [DataMember(Name = "code")] public string Code { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "level")] public string Level { get; set; }
        [DataMember(Name = "modified")] public string Modified { get; set; }
        [DataMember(Name = "effective")] public string Effective { get; set; }
        [DataMember(Name = "source")] public string Source { get; set; }
        [DataMember(Name = "articles")] public List<LawArticle> Articles { get; set; }
    }

    [DataContract(Namespace = "")]
    public class LawFile
    {
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "laws")] public List<Law> Laws { get; set; }
    }

    [DataContract(Namespace = "")]
    public class CodeRule
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "law")] public string Law { get; set; }
        [DataMember(Name = "art")] public string Article { get; set; }
        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "severity")] public string Severity { get; set; }
        [DataMember(Name = "scope")] public string Scope { get; set; }
        [DataMember(Name = "where")] public string Where { get; set; }
        [DataMember(Name = "applies")] public string Applies { get; set; }
        [DataMember(Name = "measure")] public string Measure { get; set; }
        [DataMember(Name = "op")] public string Op { get; set; }
        [DataMember(Name = "limit")] public string Limit { get; set; }
        [DataMember(Name = "cases")] public List<List<string>> Cases { get; set; }
        [DataMember(Name = "unit")] public string Unit { get; set; }
        [DataMember(Name = "msg")] public string Message { get; set; }
    }

    [DataContract(Namespace = "")]
    public class RuleFile
    {
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "rules")] public List<CodeRule> Rules { get; set; }
    }

    [DataContract(Namespace = "")]
    public class CatalogItem
    {
        [DataMember(Name = "law")] public string Law { get; set; }
        [DataMember(Name = "no")] public string No { get; set; }
        /// <summary>auto / semi / manual / info</summary>
        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "reason")] public string Reason { get; set; }
        [DataMember(Name = "rules")] public List<string> Rules { get; set; }
        [DataMember(Name = "hint")] public string Hint { get; set; }
    }

    [DataContract(Namespace = "")]
    public class CatalogFile
    {
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "items")] public List<CatalogItem> Items { get; set; }
    }

    /// <summary>
    /// 法規資料：外掛資料夾 Rules\CodeCheck\ 的三個檔案。
    /// 使用者若在 %APPDATA%\DevelopmentTools\CodeCheck\ 放了同名檔案，以使用者版本為準（方便法規修正後先行更新）。
    /// </summary>
    public class CodeCheckData
    {
        public LawFile Laws { get; private set; }
        public RuleFile Rules { get; private set; }
        public CatalogFile Catalog { get; private set; }
        public string LoadedFrom { get; private set; }

        public static string UserFolder =>
            global::DevelopmentTools.Core.InstallLocation.DataFolder("CodeCheck",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevelopmentTools", "CodeCheck"));

        public static string AddinFolder
        {
            get
            {
                string asm = Assembly.GetExecutingAssembly().Location;
                return Path.Combine(Path.GetDirectoryName(asm) ?? "", "Rules", "CodeCheck");
            }
        }

        public static CodeCheckData Load()
        {
            var d = new CodeCheckData();
            d.Laws = Read<LawFile>("CodeCheck_Laws.json", out string f1);
            d.Rules = Read<RuleFile>("CodeCheck_Rules.json", out string f2);
            d.Catalog = Read<CatalogFile>("CodeCheck_Catalog.json", out string f3);
            d.LoadedFrom = string.Join("; ", new[] { f1, f2, f3 }.Distinct());
            return d;
        }

        private static T Read<T>(string name, out string from)
        {
            string user = Path.Combine(UserFolder, name);
            string addin = Path.Combine(AddinFolder, name);
            string path = File.Exists(user) ? user : addin;
            if (!File.Exists(path)) throw new FileNotFoundException("找不到法規資料檔：" + name + "（應在 " + AddinFolder + "）");
            from = Path.GetDirectoryName(path);
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var fs = File.OpenRead(path)) return (T)ser.ReadObject(fs);
        }

        public Law FindLaw(string code) => Laws.Laws.FirstOrDefault(l => l.Code == code);

        public LawArticle FindArticle(string law, string no) => FindLaw(law)?.Articles.FirstOrDefault(a => a.No == no);

        public static string ArticleLabel(string law, string no)
        {
            if (law == "ACC") return no;
            return "第 " + no.Replace("-", " 條之 ") + (no.Contains("-") ? "" : " 條");
        }
    }
}
