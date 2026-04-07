using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReactCodegen.Legacy
{
    public static class SwaggerProcessor
    {
        public static Dictionary<string, List<ApiEndpoint>> GroupPathsByResource(JsonObject paths)
        {
            var resourceGroups = new Dictionary<string, List<ApiEndpoint>>();
            var pathRegex = new Regex(@"^/api/([^/]+)/([^/]+)(?:/(.*))?$");

            foreach (var path in paths)
            {
                var match = pathRegex.Match(path.Key);
                if (match.Success)
                {
                    string prefix = match.Groups[1].Value;
                    string resourceName = match.Groups[2].Value;
                    string pathSuffix = match.Groups[3].Value;

                    if (!resourceGroups.ContainsKey(resourceName))
                    {
                        resourceGroups[resourceName] = new List<ApiEndpoint>();
                    }

                    // Get HTTP methods for this path
                    var pathObject = path.Value?.AsObject();
                    if (pathObject != null)
                    {
                        foreach (var method in pathObject)
                        {
                            var endpoint = new ApiEndpoint
                            {
                                Path = path.Key,
                                Method = method.Key.ToUpper(),
                                PathSuffix = pathSuffix,
                                ResourceName = resourceName,
                                Prefix = prefix
                            };
                            resourceGroups[resourceName].Add(endpoint);
                        }
                    }
                }
            }

            return resourceGroups;
        }

        public static string DetermineOperationType(ApiEndpoint endpoint)
        {
            string lowerResource = endpoint.ResourceName.ToLower();

            if (endpoint.Method == "POST" && string.IsNullOrEmpty(endpoint.PathSuffix))
                return "create";

            if (endpoint.Method == "GET" && endpoint.PathSuffix == "{id}")
                return "get";

            if (endpoint.Method == "POST" && endpoint.PathSuffix?.ToLower() == "search")
                return "search";

            if (endpoint.Method == "PUT" && endpoint.PathSuffix == "{id}")
                return "update";

            if (endpoint.Method == "DELETE" && endpoint.PathSuffix == "{id}")
                return "delete";

            return "";
        }

        public static List<ReportingEndpoint> ExtractReportingEndpoints(JsonObject paths)
        {
            var endpoints = new List<ReportingEndpoint>();
            var pathRegex = new Regex(@"^/api/reporting/([^/]+)/(full|widget)/([^/]+)$", RegexOptions.IgnoreCase);

            foreach (var path in paths)
            {
                var match = pathRegex.Match(path.Key);
                if (!match.Success) continue;

                string department = match.Groups[1].Value.ToLower();
                string category = match.Groups[2].Value.ToLower();
                string reportKey = match.Groups[3].Value;

                var pathObject = path.Value?.AsObject();
                if (pathObject == null) continue;

                foreach (var method in pathObject)
                {
                    var methodObject = method.Value?.AsObject();
                    if (methodObject == null) continue;

                    var endpoint = new ReportingEndpoint
                    {
                        Path = path.Key,
                        Method = method.Key.ToUpper(),
                        Department = department,
                        Category = category,
                        ReportKey = reportKey,
                        RequestSchemaName = GetRequestSchemaName(methodObject),
                        ResponseSchemaName = GetResponseSchemaName(methodObject)
                    };

                    endpoints.Add(endpoint);
                }
            }

            return endpoints;
        }

        public static List<SetmisEndpoint> ExtractSetmisEndpoints(JsonObject paths)
        {
            var endpoints = new List<SetmisEndpoint>();
            var retrieveRegex = new Regex(@"^/api/setmis/([^/]+)/retrieve$", RegexOptions.IgnoreCase);

            // Collect all entity names that have a download route
            var downloadEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downloadRegex = new Regex(@"^/api/setmis/([^/]+)/download$", RegexOptions.IgnoreCase);
            foreach (var path in paths)
            {
                var downloadMatch = downloadRegex.Match(path.Key);
                if (downloadMatch.Success)
                    downloadEntities.Add(downloadMatch.Groups[1].Value);
            }

            foreach (var path in paths)
            {
                var match = retrieveRegex.Match(path.Key);
                if (!match.Success) continue;

                string entityName = match.Groups[1].Value;

                var pathObject = path.Value?.AsObject();
                if (pathObject == null) continue;

                foreach (var method in pathObject)
                {
                    var methodObject = method.Value?.AsObject();
                    if (methodObject == null) continue;

                    var endpoint = new SetmisEndpoint
                    {
                        Path = path.Key,
                        Method = method.Key.ToUpper(),
                        EntityName = entityName,
                        RequestSchemaName = GetRequestSchemaName(methodObject),
                        ResponseSchemaName = GetResponseSchemaName(methodObject),
                        HasDownloadRoute = downloadEntities.Contains(entityName)
                    };

                    endpoints.Add(endpoint);
                }
            }

            return endpoints;
        }

        private static string? GetRequestSchemaName(JsonObject methodObject)
        {
            var content = methodObject["requestBody"]?["content"]?.AsObject();
            return GetSchemaNameFromContent(content);
        }

        private static string? GetResponseSchemaName(JsonObject methodObject)
        {
            var responses = methodObject["responses"]?.AsObject();
            var response200 = responses?["200"]?.AsObject();
            var content = response200?["content"]?.AsObject();
            return GetSchemaNameFromContent(content);
        }

        private static string? GetSchemaNameFromContent(JsonObject? content)
        {
            if (content == null) return null;

            var preferredTypes = new[]
            {
                "application/json",
                "text/json",
                "text/plain",
                "application/xml",
                "text/xml"
            };

            foreach (var contentType in preferredTypes)
            {
                var schemaName = GetSchemaNameFromContentType(content, contentType);
                if (!string.IsNullOrEmpty(schemaName)) return schemaName;
            }

            var firstContent = content.FirstOrDefault().Value?.AsObject();
            if (firstContent == null) return null;

            return GetSchemaNameFromSchema(firstContent["schema"]?.AsObject());
        }

        private static string? GetSchemaNameFromContentType(JsonObject content, string contentType)
        {
            var schemaObject = content[contentType]?["schema"]?.AsObject();
            return GetSchemaNameFromSchema(schemaObject);
        }

        private static string? GetSchemaNameFromSchema(JsonObject? schemaObject)
        {
            var refValue = schemaObject?["$ref"]?.GetValue<string>();
            if (string.IsNullOrEmpty(refValue)) return null;

            var parts = refValue.Split('/');
            return parts.Length > 0 ? parts[^1] : null;
        }
    }
}
