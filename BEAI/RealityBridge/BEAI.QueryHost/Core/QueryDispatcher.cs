using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.DTO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.Core
{
    public class QueryDispatcher
    {
        private static readonly Regex ElementParamRegex = new Regex(@"^/api/elements/(\d+)/parameters/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElementIdRegex = new Regex(@"^/api/elements/(\d+)/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static BeaiQueryResponse Dispatch(UIApplication app, BeaiQueryRequest request)
        {
            var sw = Stopwatch.StartNew();
            string path = request.Path != null ? request.Path.TrimEnd('/') : "";
            string method = request.HttpMethod != null ? request.HttpMethod.ToUpperInvariant() : "GET";
            int resultCount = 0;
            string status = "OK";

            try
            {
                Document doc = null;
                if (app != null && app.ActiveUIDocument != null)
                {
                    doc = app.ActiveUIDocument.Document;
                }

                if (doc != null)
                {
                    RuntimeState.Instance.DocumentOpen = true;
                    RuntimeState.Instance.DocumentTitle = doc.Title;
                    RuntimeState.Instance.ActiveView = doc.ActiveView != null ? doc.ActiveView.Name : "None";
                }
                else
                {
                    RuntimeState.Instance.DocumentOpen = false;
                    RuntimeState.Instance.DocumentTitle = "None";
                    RuntimeState.Instance.ActiveView = "None";
                }

                if (method == "PUT" || method == "DELETE" || method == "PATCH")
                {
                    status = ErrorCodes.WriteNotAllowed;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.WriteNotAllowed, "This query bridge is strictly READ-ONLY. No write or mutation allowed.")), 405);
                }

                // Canonical Reality Bridge endpoint. It uses the existing ExternalEvent queue.
                if (method == "POST" && path.Equals("/api/v1/execute", StringComparison.OrdinalIgnoreCase))
                {
                    RealityRequest realityRequest = string.IsNullOrWhiteSpace(request.Body)
                        ? null
                        : JsonConvert.DeserializeObject<RealityRequest>(request.Body);
                    RealityResponse realityResponse = RealityDispatcher.Dispatch(app, realityRequest);
                    resultCount = realityResponse.Success && realityResponse.Data != null ? 1 : 0;
                    status = realityResponse.Success ? "OK" : "FAILED";
                    return BeaiQueryResponse.Json(
                        JsonConvert.SerializeObject(realityResponse, Formatting.Indented),
                        realityResponse.Success ? 200 : 400);
                }

                // Route: GET /api/status
                if (method == "GET" && (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase) || path.Equals("/status", StringComparison.OrdinalIgnoreCase)))
                {
                    var statusDto = RuntimeState.Instance.ToStatusDto();
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(statusDto, Formatting.Indented));
                }

                // Route: POST /api/rules/reload
                if (method == "POST" && path.Equals("/api/rules/reload", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = HotReloadManager.Instance.ReloadAllRules(false);
                    if (ok)
                    {
                        return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = true, ruleGeneration = RuntimeState.Instance.RuleGeneration }));
                    }
                    else
                    {
                        return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.RuleReloadFailed, RuntimeState.Instance.LastError)), 500);
                    }
                }

                // Route: POST /api/worker/restart
                if (method == "POST" && path.Equals("/api/worker/restart", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = WorkerManager.Instance.RestartWorker();
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = ok, workerStatus = RuntimeState.Instance.WorkerStatus, workerVersion = RuntimeState.Instance.WorkerVersion }));
                }

                // Route: POST /api/worker/rollback
                if (method == "POST" && path.Equals("/api/worker/rollback", StringComparison.OrdinalIgnoreCase))
                {
                    bool ok = WorkerManager.Instance.RollbackWorker();
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = ok, workerStatus = RuntimeState.Instance.WorkerStatus, workerVersion = RuntimeState.Instance.WorkerVersion }));
                }

                // For model queries, document must be open
                if (doc == null)
                {
                    status = ErrorCodes.ModelNotAvailable;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.ModelNotAvailable, "No active Revit document is currently open.")), 404);
                }

                // Route: GET /api/model
                if (method == "GET" && (path.Equals("/api/model", StringComparison.OrdinalIgnoreCase) || path.Equals("/model", StringComparison.OrdinalIgnoreCase)))
                {
                    var modelInfo = GenericRevitReader.ReadModelInfo(doc);
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = true, data = modelInfo }, Formatting.Indented));
                }

                // Route: GET /api/categories
                if (method == "GET" && (path.Equals("/api/categories", StringComparison.OrdinalIgnoreCase) || path.Equals("/categories", StringComparison.OrdinalIgnoreCase)))
                {
                    var categories = GenericRevitReader.ReadCategories(doc);
                    resultCount = categories.Count;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = true, count = categories.Count, categories = categories }, Formatting.Indented));
                }

                // Route: POST /api/count
                if (method == "POST" && (path.Equals("/api/count", StringComparison.OrdinalIgnoreCase) || path.Equals("/count", StringComparison.OrdinalIgnoreCase)))
                {
                    CountRequestDto countReq = null;
                    if (!string.IsNullOrWhiteSpace(request.Body))
                    {
                        countReq = JsonConvert.DeserializeObject<CountRequestDto>(request.Body);
                    }
                    if (countReq == null) countReq = new CountRequestDto();

                    int count = GenericRevitReader.CountElements(doc, countReq);
                    resultCount = count;
                    var cResp = new CountResponseDto();
                    cResp.Success = true;
                    cResp.Count = count;
                    cResp.Category = countReq.Category;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(cResp, Formatting.Indented));
                }

                // Route: POST /api/elements/search
                if (method == "POST" && (path.Equals("/api/elements/search", StringComparison.OrdinalIgnoreCase) || path.Equals("/api/elements", StringComparison.OrdinalIgnoreCase)))
                {
                    SearchElementsRequestDto searchReq = null;
                    if (!string.IsNullOrWhiteSpace(request.Body))
                    {
                        searchReq = JsonConvert.DeserializeObject<SearchElementsRequestDto>(request.Body);
                    }
                    if (searchReq == null) searchReq = new SearchElementsRequestDto();

                    var searchResp = GenericRevitReader.SearchElements(doc, searchReq);
                    resultCount = searchResp.Items.Count;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(searchResp, Formatting.Indented));
                }

                // Route: GET /api/elements/{id}/parameters
                Match paramMatch = ElementParamRegex.Match(path);
                if (method == "GET" && paramMatch.Success)
                {
                    long elemId = long.Parse(paramMatch.Groups[1].Value);
                    var paramDto = GenericRevitReader.ReadElementParameters(doc, new ElementId(elemId));
                    if (paramDto == null)
                    {
                        status = ErrorCodes.ElementNotFound;
                        return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.ElementNotFound, string.Format("Element with ID {0} not found in current model.", elemId))), 404);
                    }
                    resultCount = paramDto.InstanceParameters.Count + paramDto.TypeParameters.Count;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(paramDto, Formatting.Indented));
                }

                // Route: GET /api/elements/{id}
                Match elemMatch = ElementIdRegex.Match(path);
                if (method == "GET" && elemMatch.Success)
                {
                    long elemId = long.Parse(elemMatch.Groups[1].Value);
                    Element elem = doc.GetElement(new ElementId(elemId));
                    if (elem == null)
                    {
                        status = ErrorCodes.ElementNotFound;
                        return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.ElementNotFound, string.Format("Element with ID {0} not found.", elemId))), 404);
                    }
                    var summary = GenericRevitReader.ReadElementSummary(doc, elem);
                    resultCount = 1;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(new { success = true, data = summary }, Formatting.Indented));
                }

                // Route: POST /api/parameters/search
                if (method == "POST" && path.Equals("/api/parameters/search", StringComparison.OrdinalIgnoreCase))
                {
                    ParameterSearchRequestDto pSearchReq = null;
                    if (!string.IsNullOrWhiteSpace(request.Body))
                    {
                        pSearchReq = JsonConvert.DeserializeObject<ParameterSearchRequestDto>(request.Body);
                    }
                    if (pSearchReq == null || pSearchReq.Parameter == null)
                    {
                        status = ErrorCodes.InvalidRequest;
                        return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.InvalidRequest, "Parameter search criteria must be provided.")), 400);
                    }

                    var pSearchResp = ExecuteParameterSearch(doc, pSearchReq);
                    resultCount = pSearchResp.Items.Count;
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(pSearchResp, Formatting.Indented));
                }

                // Route: POST /api/queryplan
                if (method == "POST" && path.Equals("/api/queryplan", StringComparison.OrdinalIgnoreCase))
                {
                    QueryPlanDto plan = null;
                    if (!string.IsNullOrWhiteSpace(request.Body))
                    {
                        plan = JsonConvert.DeserializeObject<QueryPlanDto>(request.Body);
                    }
                    if (plan == null) plan = new QueryPlanDto();

                    var planResp = ExecuteQueryPlan(doc, plan);
                    return BeaiQueryResponse.Json(JsonConvert.SerializeObject(planResp, Formatting.Indented));
                }

                status = "NOT_FOUND";
                return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail("NOT_FOUND", string.Format("Endpoint '{0} {1}' not found.", method, path))), 404);
            }
            catch (Exception ex)
            {
                status = ErrorCodes.InternalError;
                Logger.Error(string.Format("Unhandled error in QueryDispatcher for {0} {1}", method, path), ex);
                return BeaiQueryResponse.Json(JsonConvert.SerializeObject(ApiResponse<object>.Fail(ErrorCodes.InternalError, ex.Message)), 500);
            }
            finally
            {
                sw.Stop();
                Logger.Query(method, path, sw.ElapsedMilliseconds, resultCount, status);
            }
        }

        private static ParameterSearchResponseDto ExecuteParameterSearch(Document doc, ParameterSearchRequestDto request)
        {
            int pageSize = Math.Max(1, Math.Min(request.PageSize > 0 ? request.PageSize : 100, 500));
            int page = Math.Max(1, request.Page);

            var searchReq = new SearchElementsRequestDto();
            searchReq.Category = request.Category;
            searchReq.Page = 1;
            searchReq.PageSize = 2000;

            var elementsResp = GenericRevitReader.SearchElements(doc, searchReq);
            var matchedItems = new List<ParameterMatchItemDto>();

            string targetParamName = request.Parameter != null ? request.Parameter.Name : null;
            string targetBip = request.Parameter != null ? request.Parameter.BuiltInParameter : null;
            string targetGuid = request.Parameter != null ? request.Parameter.SharedParameterGuid : null;
            string op = request.Operator != null ? request.Operator.ToLowerInvariant() : "equals";
            string compareVal = request.Value != null ? request.Value.ToString() : "";

            foreach (var elemSummary in elementsResp.Items)
            {
                var paramDto = GenericRevitReader.ReadElementParameters(doc, new ElementId(elemSummary.ElementId));
                if (paramDto == null) continue;

                var allParams = new List<ParameterDto>();
                allParams.AddRange(paramDto.InstanceParameters);
                allParams.AddRange(paramDto.TypeParameters);

                foreach (var p in allParams)
                {
                    bool isTarget = false;
                    if (!string.IsNullOrEmpty(targetParamName) && string.Equals(p.Name, targetParamName, StringComparison.OrdinalIgnoreCase))
                    {
                        isTarget = true;
                    }
                    else if (!string.IsNullOrEmpty(targetBip) && string.Equals(p.BuiltInParameter, targetBip, StringComparison.OrdinalIgnoreCase))
                    {
                        isTarget = true;
                    }
                    else if (!string.IsNullOrEmpty(targetGuid) && string.Equals(p.SharedParameterGuid, targetGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        isTarget = true;
                    }

                    if (!isTarget) continue;

                    bool match = EvaluateCondition(p, op, compareVal);
                    if (match)
                    {
                        var matchItem = new ParameterMatchItemDto();
                        matchItem.Element = elemSummary;
                        matchItem.MatchedParameter = p;
                        matchedItems.Add(matchItem);
                        break;
                    }
                }
            }

            int total = matchedItems.Count;
            int skip = (page - 1) * pageSize;
            var pageItems = matchedItems.Skip(skip).Take(pageSize).ToList();

            var resp = new ParameterSearchResponseDto();
            resp.Success = true;
            resp.Total = total;
            resp.Page = page;
            resp.PageSize = pageSize;
            resp.HasMore = (skip + pageItems.Count) < total;
            resp.Items = pageItems;
            return resp;
        }

        private static bool EvaluateCondition(ParameterDto p, string op, string compareVal)
        {
            string actualVal = p.DisplayValue != null ? p.DisplayValue : (p.RawValue != null ? p.RawValue.ToString() : "");
            if (compareVal == null) compareVal = "";

            switch (op)
            {
                case "is_empty":
                    return !p.HasValue || string.IsNullOrWhiteSpace(actualVal);

                case "not_empty":
                    return p.HasValue && !string.IsNullOrWhiteSpace(actualVal);

                case "equals":
                    return string.Equals(actualVal, compareVal, StringComparison.OrdinalIgnoreCase);

                case "not_equals":
                    return !string.Equals(actualVal, compareVal, StringComparison.OrdinalIgnoreCase);

                case "contains":
                    return actualVal.IndexOf(compareVal, StringComparison.OrdinalIgnoreCase) >= 0;

                case "starts_with":
                    return actualVal.StartsWith(compareVal, StringComparison.OrdinalIgnoreCase);

                case "greater_than":
                    double a1, c1;
                    if (double.TryParse(actualVal, out a1) && double.TryParse(compareVal, out c1))
                    {
                        return a1 > c1;
                    }
                    return string.Compare(actualVal, compareVal, StringComparison.OrdinalIgnoreCase) > 0;

                case "less_than":
                    double a2, c2;
                    if (double.TryParse(actualVal, out a2) && double.TryParse(compareVal, out c2))
                    {
                        return a2 < c2;
                    }
                    return string.Compare(actualVal, compareVal, StringComparison.OrdinalIgnoreCase) < 0;

                default:
                    return string.Equals(actualVal, compareVal, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static object ExecuteQueryPlan(Document doc, QueryPlanDto plan)
        {
            var results = new List<object>();

            foreach (var cat in plan.Categories)
            {
                var sReq = new SearchElementsRequestDto();
                sReq.Category = cat;
                sReq.Page = plan.Page;
                sReq.PageSize = plan.PageSize;

                var search = GenericRevitReader.SearchElements(doc, sReq);

                var categoryResult = new List<object>();
                foreach (var item in search.Items)
                {
                    if (plan.Include != null && (plan.Include.InstanceParameters || plan.Include.TypeParameters))
                    {
                        var paramData = GenericRevitReader.ReadElementParameters(doc, new ElementId(item.ElementId));
                        if (plan.ParameterNames != null && plan.ParameterNames.Count > 0 && paramData != null)
                        {
                            paramData.InstanceParameters = paramData.InstanceParameters
                                .FindAll(delegate(ParameterDto p) { return plan.ParameterNames.Exists(delegate(string n) { return string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase); }); });
                            paramData.TypeParameters = paramData.TypeParameters
                                .FindAll(delegate(ParameterDto p) { return plan.ParameterNames.Exists(delegate(string n) { return string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase); }); });
                        }
                        categoryResult.Add(paramData);
                    }
                    else
                    {
                        categoryResult.Add(item);
                    }
                }

                results.Add(new { category = cat, total = search.Total, items = categoryResult });
            }

            return new { success = true, ruleGeneration = RuntimeState.Instance.RuleGeneration, results = results };
        }
    }
}
