using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Text;

namespace HotAgentWorker
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
                serializer.Deserialize<Dictionary<string, object>>(Console.In.ReadToEnd());
                // 在此進行純 .NET／外部 AI 分析。禁止引用 RevitAPI 或直接修改模型。
                Console.Out.Write(serializer.Serialize(new { protocolVersion = 1, status = "succeeded", title = "Agent 完成", message = "請將這裡替換為結構化分析結果。" }));
                return 0;
            }
            catch (Exception ex) { Console.Error.Write(ex.Message); return 1; }
        }
    }
}
