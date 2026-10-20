using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Text;

namespace ExampleHotAgent
{
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Console.InputEncoding = new UTF8Encoding(false);
                Console.OutputEncoding = new UTF8Encoding(false);
                var serializer = new JavaScriptSerializer();
                var request = serializer.Deserialize<Dictionary<string, object>>(Console.In.ReadToEnd());
                object title; request.TryGetValue("documentTitle", out title);
                Console.Out.Write(serializer.Serialize(new
                {
                    protocolVersion = 1,
                    status = "succeeded",
                    title = "熱替換範例 Agent",
                    message = "已由獨立 Worker 分析目前文件：" + Convert.ToString(title ?? "未開啟文件")
                }));
                return 0;
            }
            catch (Exception ex) { Console.Error.Write(ex.Message); return 1; }
        }
    }
}
