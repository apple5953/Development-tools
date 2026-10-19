using System;
using Newtonsoft.Json;

namespace BEAI.QueryHost.DTO
{
    public class ApiResponse<T>
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public T Data { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public ApiError Error { get; set; }

        [JsonProperty("ruleGeneration", NullValueHandling = NullValueHandling.Ignore)]
        public int? RuleGeneration { get; set; }

        public static ApiResponse<T> Ok(T data, int? ruleGen = null)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data,
                RuleGeneration = ruleGen
            };
        }

        public static ApiResponse<T> Fail(string code, string message)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = new ApiError { Code = code, Message = message }
            };
        }
    }

    public class ApiError
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }

    public static class ErrorCodes
    {
        public const string ServerStopped = "SERVER_STOPPED";
        public const string ModelNotAvailable = "MODEL_NOT_AVAILABLE";
        public const string ElementNotFound = "ELEMENT_NOT_FOUND";
        public const string CategoryNotFound = "CATEGORY_NOT_FOUND";
        public const string ParameterNotFound = "PARAMETER_NOT_FOUND";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string ResultTooLarge = "RESULT_TOO_LARGE";
        public const string QueryTimeout = "QUERY_TIMEOUT";
        public const string RevitBusy = "REVIT_BUSY";
        public const string WorkerUnavailable = "WORKER_UNAVAILABLE";
        public const string RuleReloadFailed = "RULE_RELOAD_FAILED";
        public const string WriteNotAllowed = "WRITE_NOT_ALLOWED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}
