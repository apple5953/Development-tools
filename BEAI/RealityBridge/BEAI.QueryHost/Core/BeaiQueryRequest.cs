using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BEAI.QueryHost.Core
{
    public class BeaiQueryRequest
    {
        public BeaiQueryRequest()
        {
            Id = Guid.NewGuid().ToString("N");
            QueryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CompletionSource = new TaskCompletionSource<BeaiQueryResponse>();
            CreatedTime = DateTime.UtcNow;
        }

        public string Id { get; set; }
        public string HttpMethod { get; set; }
        public string Path { get; set; }
        public string Body { get; set; }
        public Dictionary<string, string> QueryParams { get; set; }
        public TaskCompletionSource<BeaiQueryResponse> CompletionSource { get; set; }
        public CancellationTokenSource Cts { get; set; }
        public DateTime CreatedTime { get; set; }
    }

    public class BeaiQueryResponse
    {
        public BeaiQueryResponse()
        {
            StatusCode = 200;
            ContentType = "application/json; charset=utf-8";
        }

        public int StatusCode { get; set; }
        public string ContentType { get; set; }
        public string Body { get; set; }

        public static BeaiQueryResponse Json(string json, int statusCode)
        {
            var r = new BeaiQueryResponse();
            r.StatusCode = statusCode;
            r.ContentType = "application/json; charset=utf-8";
            r.Body = json;
            return r;
        }

        public static BeaiQueryResponse Json(string json)
        {
            return Json(json, 200);
        }
    }
}
