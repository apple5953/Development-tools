using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BEAI.QueryHost.DTO;
using Newtonsoft.Json.Linq;

namespace BEAI.QueryHost.Core
{
    public static class RealityDispatcher
    {
        private const int MaxElements = 500;
        private const int MaxElementsForParameters = 50;

        public static RealityResponse Dispatch(UIApplication app, RealityRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            string requestId = request != null && !string.IsNullOrWhiteSpace(request.RequestId)
                ? request.RequestId
                : Guid.NewGuid().ToString("N");

            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Tool))
                {
                    return Fail(requestId, "BEAI_INVALID_QUERY", "Request tool is required.", stopwatch);
                }

                string tool = request.Tool.Trim().ToLowerInvariant();
                FeatureFlags flags = BridgeConfiguration.Instance.GetFlags();
                Document doc = app != null && app.ActiveUIDocument != null
                    ? app.ActiveUIDocument.Document
                    : null;

                if (tool != RealityTools.SystemHealth && !flags.ExternalAccess)
                {
                    return Fail(requestId, "BEAI_PERMISSION_DENIED", "BEAI external access is disabled.", stopwatch);
                }

                if (tool == RealityTools.SystemHealth)
                {
                    return Ok(requestId, new
                    {
                        bridge = RuntimeState.Instance.ServerRunning ? "online" : "offline",
                        revit = app != null ? "connected" : "disconnected",
                        revitVersion = app != null && app.Application != null ? app.Application.VersionNumber : "2024",
                        document = doc != null ? doc.Title : null,
                        documentOpen = doc != null,
                        externalAccess = flags.ExternalAccess,
                        mode = "READ",
                        queue = "available"
                    }, stopwatch);
                }

                if (tool == RealityTools.SystemVersion)
                {
                    return Ok(requestId, new { name = "BEAI Reality Bridge", version = "0.1.0", contractVersion = "1.0" }, stopwatch);
                }

                if (tool == RealityTools.SystemCapabilities)
                {
                    return Ok(requestId, new
                    {
                        mode = "READ",
                        tools = new[]
                        {
                            RealityTools.SystemHealth,
                            RealityTools.SystemVersion,
                            RealityTools.SystemCapabilities,
                            RealityTools.DocumentGetActive,
                            RealityTools.LevelList,
                            RealityTools.ElementQuery,
                            RealityTools.ElementGetParameters
                        },
                        limits = new { maxElementsPerQuery = MaxElements, maxElementsPerParameterRequest = MaxElementsForParameters }
                    }, stopwatch);
                }

                if (!flags.Read)
                {
                    return Fail(requestId, "BEAI_PERMISSION_DENIED", "BEAI read tools are disabled.", stopwatch);
                }

                if (doc == null)
                {
                    return Fail(requestId, "BEAI_NO_DOCUMENT", "No active Revit model is currently open.", stopwatch);
                }

                if (tool == RealityTools.DocumentGetActive)
                {
                    return Ok(requestId, GenericRevitReader.ReadModelInfo(doc), stopwatch);
                }

                if (tool == RealityTools.LevelList)
                {
                    List<LevelDto> levels = GenericRevitReader.ReadLevels(doc);
                    return Ok(requestId, new { total = levels.Count, levels = levels }, stopwatch);
                }

                if (tool == RealityTools.ElementQuery)
                {
                    SearchElementsRequestDto query = BuildElementQuery(request.Args);
                    SearchElementsResponseDto result = GenericRevitReader.SearchElements(doc, query);
                    return Ok(requestId, result, stopwatch);
                }

                if (tool == RealityTools.ElementGetParameters)
                {
                    List<long> elementIds = ReadElementIds(request.Args);
                    if (elementIds.Count == 0)
                    {
                        return Fail(requestId, "BEAI_INVALID_QUERY", "At least one elementId is required.", stopwatch);
                    }
                    if (elementIds.Count > MaxElementsForParameters)
                    {
                        return Fail(requestId, "BEAI_INVALID_QUERY", "A maximum of 50 elements is allowed per parameter request.", stopwatch);
                    }

                    var items = new List<ElementParametersDto>();
                    var missing = new List<long>();
                    foreach (long elementId in elementIds.Distinct())
                    {
                        ElementParametersDto parameters = GenericRevitReader.ReadElementParameters(doc, new ElementId(elementId));
                        if (parameters == null) missing.Add(elementId);
                        else items.Add(parameters);
                    }

                    RealityResponse response = Ok(requestId, new { total = items.Count, items = items }, stopwatch);
                    if (missing.Count > 0)
                    {
                        response.Warnings.Add(string.Format("Elements not found: {0}", string.Join(", ", missing)));
                    }
                    return response;
                }

                return Fail(requestId, "BEAI_NOT_SUPPORTED", string.Format("Tool '{0}' is not supported by V0.1.", tool), stopwatch);
            }
            catch (Exception ex)
            {
                Logger.Error(string.Format("[RealityDispatcher] Request {0} failed.", requestId), ex);
                return Fail(requestId, "BEAI_REVIT_API_ERROR", "The Revit request could not be completed.", stopwatch);
            }
        }

        private static SearchElementsRequestDto BuildElementQuery(JObject args)
        {
            args = args ?? new JObject();
            int limit = Math.Max(1, Math.Min(ReadInt(args, "limit", 100), MaxElements));
            int page = Math.Max(1, ReadInt(args, "cursor", 1));

            return new SearchElementsRequestDto
            {
                Category = ReadString(args, "category"),
                CategoryId = ReadNullableLong(args, "categoryId"),
                FamilyName = ReadString(args, "family") ?? ReadString(args, "familyName"),
                TypeName = ReadString(args, "type") ?? ReadString(args, "typeName"),
                Level = ReadString(args, "level"),
                LevelId = ReadNullableLong(args, "levelId"),
                Workset = ReadString(args, "workset"),
                WorksetId = ReadNullableLong(args, "worksetId"),
                NameContains = ReadString(args, "nameContains"),
                Page = page,
                PageSize = limit
            };
        }

        private static List<long> ReadElementIds(JObject args)
        {
            var ids = new List<long>();
            if (args == null) return ids;

            JToken arrayToken = args["elementIds"];
            if (arrayToken is JArray)
            {
                foreach (JToken token in (JArray)arrayToken)
                {
                    long id;
                    if (long.TryParse(token.ToString(), out id) && id > 0) ids.Add(id);
                }
            }

            long? single = ReadNullableLong(args, "elementId");
            if (single.HasValue && single.Value > 0) ids.Add(single.Value);
            return ids;
        }

        private static string ReadString(JObject args, string name)
        {
            JToken token = args[name];
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        private static int ReadInt(JObject args, string name, int fallback)
        {
            int value;
            return args[name] != null && int.TryParse(args[name].ToString(), out value) ? value : fallback;
        }

        private static long? ReadNullableLong(JObject args, string name)
        {
            long value;
            return args[name] != null && long.TryParse(args[name].ToString(), out value) ? value : (long?)null;
        }

        private static RealityResponse Ok(string requestId, object data, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            return new RealityResponse { RequestId = requestId, Success = true, DurationMs = stopwatch.ElapsedMilliseconds, Data = data };
        }

        private static RealityResponse Fail(string requestId, string code, string message, Stopwatch stopwatch)
        {
            stopwatch.Stop();
            var response = new RealityResponse { RequestId = requestId, Success = false, DurationMs = stopwatch.ElapsedMilliseconds };
            response.Errors.Add(new RealityError { Code = code, Message = message });
            return response;
        }
    }
}
