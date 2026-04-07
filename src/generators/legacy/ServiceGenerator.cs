using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class ServiceGenerator
    {
        public static void GenerateServiceFile(string resourceName, List<ApiEndpoint> endpoints, JsonObject? schemas, string outputDir)
        {
            // Determine subfolder type from output directory
            string subfolder = outputDir.Contains(Path.Combine("legacy", "core")) ? "core" :
                             outputDir.Contains(Path.Combine("legacy", "settings")) ? "settings" : "";

            var serviceContent = GenerateService(resourceName, endpoints, schemas, subfolder);
            string fileName = $"{resourceName}Service.ts";
            string filePath = Path.Combine(outputDir, fileName);

            File.WriteAllText(filePath, serviceContent);
        }

        public static string GenerateService(string resourceName, List<ApiEndpoint> endpoints, JsonObject? schemas, string subfolder = "")
        {
            var sb = new StringBuilder();

            // Group endpoints by operation type
            var operations = new Dictionary<string, ApiEndpoint>();

            foreach (var endpoint in endpoints)
            {
                string operationType = SwaggerProcessor.DetermineOperationType(endpoint);
                if (!string.IsNullOrEmpty(operationType))
                {
                    operations[operationType] = endpoint;
                }
            }

            // Add imports
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("import { BaseResponse, SearchRequest, SearchResponse} from \"@/types/legacy/custom/Api\"");

            // Import specific types based on operations
            var importTypes = new List<string>();
            if (operations.ContainsKey("create"))
                importTypes.Add($"{resourceName}Create");
            if (operations.ContainsKey("update"))
                importTypes.Add($"{resourceName}Update");
            if (operations.ContainsKey("search") || operations.ContainsKey("get"))
                importTypes.Add($"{resourceName}");

            if (importTypes.Count > 0)
            {
                string typeImportPath = !string.IsNullOrEmpty(subfolder) ? $"@/types/legacy/{subfolder}/{resourceName}" : $"@/types/legacy/{resourceName}";
                sb.AppendLine($"import {{ {string.Join(", ", importTypes)} }} from \"{typeImportPath}\"");
            }

            sb.AppendLine($"import {{ ApiClient }} from \"@/services/legacy/custom/ApiService\"");
            sb.AppendLine();

            // Generate functions
            foreach (var op in new[] { "create", "get", "search", "update", "delete" })
            {
                if (operations.ContainsKey(op))
                {
                    sb.AppendLine(GenerateFunction(resourceName, op, operations[op].Prefix, schemas));
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        public static string GenerateFunction(string resourceName, string operation, string prefix, JsonObject? schemas)
        {
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string functionName = $"{operation}{resourceName}";

            switch (operation)
            {
                case "create":
                    return $"export async function {functionName}(body: {resourceName}Create): Promise<BaseResponse<{{ {lowerResource}Id: number }}>> {{\n    return ApiClient(\"POST\", \"/{prefix}/{lowerResource}\", body)\n}}";

                case "get":
                    return $"export async function {functionName}({lowerResource}Id: number): Promise<BaseResponse<{resourceName}>> {{\n    return ApiClient(\"GET\", `/{prefix}/{lowerResource}/${{{lowerResource}Id}}`)\n}}";

                case "search":
                    return $"export async function {functionName}(body: SearchRequest): Promise<BaseResponse<SearchResponse<{resourceName}>>> {{\n    return ApiClient(\"POST\", \"/{prefix}/{lowerResource}/search\", body)\n}}";

                case "update":
                    return GenerateUpdateFunction(resourceName, lowerResource, functionName, prefix, schemas);

                case "delete":
                    return $"export async function {functionName}({lowerResource}Id: number): Promise<BaseResponse<boolean>> {{\n    return ApiClient(\"DELETE\", `/{prefix}/{lowerResource}/${{{lowerResource}Id}}`)\n}}";

                default:
                    return "";
            }
        }

        private static string GenerateUpdateFunction(string resourceName, string lowerResource, string functionName, string prefix, JsonObject? schemas)
        {
            var sb = new StringBuilder();

            // Function signature with Partial<> type
            sb.AppendLine($"export async function {functionName}({lowerResource}Id: number, body: Partial<{resourceName}Update>): Promise<BaseResponse<boolean>> {{");

            // Fetch current data
            sb.AppendLine($"    // Fetch the current {lowerResource} data");
            sb.AppendLine($"    const current{resourceName} = await get{resourceName}({lowerResource}Id)");
            sb.AppendLine();
            sb.AppendLine($"    if (!current{resourceName}.data) {{");
            sb.AppendLine($"        throw new Error(\"Failed to fetch current {lowerResource} data\")");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Build the merge object
            sb.AppendLine($"    // Merge the partial update with the current data to create a complete update object");
            sb.AppendLine($"    const fullUpdate: {resourceName}Update = {{");

            // Get the update model schema
            var updateModel = Utilities.FindSchema(schemas, $"{resourceName}UpdateRequestModel");
            if (updateModel != null)
            {
                var properties = updateModel["properties"]?.AsObject();
                if (properties != null)
                {
                    foreach (var prop in properties)
                    {
                        var propSchema = prop.Value?.AsObject();
                        if (propSchema == null) continue;

                        string fieldName = Utilities.ToCamelCase(prop.Key);
                        string propType = propSchema["type"]?.GetValue<string>() ?? "string";
                        bool isRequired = updateModel["required"]?.AsArray()?.Any(r => r?.ToString() == prop.Key) ?? false;

                        // Determine default value based on type
                        string defaultValue = GetDefaultValueForType(propType, isRequired);

                        // Generate merge line
                        sb.AppendLine($"        {fieldName}: body.{fieldName} ?? current{resourceName}.data.{fieldName}{defaultValue},");
                    }
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    return ApiClient(\"PUT\", `/{prefix}/{lowerResource}/${{{lowerResource}Id}}`, fullUpdate)");
            sb.Append("}");

            return sb.ToString();
        }

        private static string GetDefaultValueForType(string propType, bool isRequired)
        {
            if (!isRequired)
            {
                return ""; // No fallback for optional fields (undefined)
            }

            return propType switch
            {
                "string" => " ?? \"\"",
                "number" or "integer" => " ?? 0",
                "boolean" => " ?? false",
                _ => ""
            };
        }

        // ========================================================================
        // =================== OPERATIONAL REPORTING SERVICES =====================
        // ========================================================================

        public static void GenerateCombinedOperationalReportingServiceFile(
            List<ReportingEndpoint> allEndpoints,
            Dictionary<string, string> typeImports,
            string serviceDir)
        {
            var serviceContent = GenerateCombinedOperationalReportingService(allEndpoints, typeImports);
            string servicePath = Path.Combine(serviceDir, "OperationalReportService.ts");
            File.WriteAllText(servicePath, serviceContent);
        }

        private static string GenerateCombinedOperationalReportingService(
            List<ReportingEndpoint> allEndpoints,
            Dictionary<string, string> typeImports)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("import { BaseResponse } from \"@/types/legacy/custom/Api\"");
            sb.AppendLine("import { ApiClient } from \"@/services/legacy/custom/ApiService\"");
            sb.AppendLine("import { FullReportData, WidgetReportData } from \"@/types/legacy/custom/Reporting\"");

            // Group type imports by department
            var importsByDepartment = typeImports
                .GroupBy(kvp => kvp.Value)
                .OrderBy(g => g.Key);

            // Generate imports grouped by department type file
            foreach (var departmentGroup in importsByDepartment)
            {
                var departmentName = departmentGroup.Key;
                var types = departmentGroup.Select(kvp => kvp.Key).OrderBy(t => t).ToList();

                if (types.Count > 0)
                {
                    if (types.Count > 1)
                    {
                        sb.AppendLine("import {");
                        foreach (var typeName in types)
                        {
                            sb.AppendLine($"  {typeName},");
                        }
                        sb.AppendLine($"}} from \"@/types/legacy/report/Reporting{departmentName}\"");
                    }
                    else
                    {
                        sb.AppendLine($"import {{ {types[0]} }} from \"@/types/legacy/report/Reporting{departmentName}\"");
                    }
                }
            }

            sb.AppendLine();

            // Generate functions grouped by department, then by category
            var endpointsByDepartment = allEndpoints
                .GroupBy(e => e.Department, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key);

            foreach (var departmentGroup in endpointsByDepartment)
            {
                string departmentName = Utilities.ToPascalCase(departmentGroup.Key);

                sb.AppendLine("// =========================================================");
                sb.AppendLine($"// ==================== {departmentName} Reports ====================");
                sb.AppendLine("// =========================================================");
                sb.AppendLine();

                var fullReports = departmentGroup.Where(e => e.Category == "full").OrderBy(e => e.ReportKey).ToList();
                var widgetReports = departmentGroup.Where(e => e.Category == "widget").OrderBy(e => e.ReportKey).ToList();

                if (fullReports.Count > 0)
                {
                    sb.AppendLine("// ---------------------------------------------------------");
                    sb.AppendLine("// -------------------- Full Reports -----------------------");
                    sb.AppendLine("// ---------------------------------------------------------");
                    sb.AppendLine();

                    foreach (var endpoint in fullReports)
                    {
                        sb.AppendLine(GenerateOperationalReportingFunction(endpoint));
                        sb.AppendLine();
                    }
                }

                if (widgetReports.Count > 0)
                {
                    sb.AppendLine("// ---------------------------------------------------------");
                    sb.AppendLine("// -------------------- Widget Reports ---------------------");
                    sb.AppendLine("// ---------------------------------------------------------");
                    sb.AppendLine();

                    foreach (var endpoint in widgetReports)
                    {
                        sb.AppendLine(GenerateOperationalReportingFunction(endpoint));
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        private static string GenerateOperationalReportingFunction(ReportingEndpoint endpoint)
        {
            string functionName = Utilities.GetReportingFunctionName(endpoint);
            string method = endpoint.Method;
            string relativePath = GetOperationalReportRelativePath(endpoint.Path);
            string returnType = GetOperationalReportReturnType(endpoint);

            if (!string.IsNullOrWhiteSpace(endpoint.RequestTypeName))
            {
                return $"export async function {functionName}(body: {endpoint.RequestTypeName}): {returnType} {{\n    return ApiClient(\"{method}\", \"{relativePath}\", body)\n}}";
            }

            return $"export async function {functionName}(): {returnType} {{\n    return ApiClient(\"{method}\", \"{relativePath}\")\n}}";
        }

        private static string GetOperationalReportReturnType(ReportingEndpoint endpoint)
        {
            // Widget endpoints use the static WidgetReportData type
            if (endpoint.Category == "widget")
            {
                return "Promise<BaseResponse<WidgetReportData>>";
            }

            string dataType = endpoint.ResponseDataTypeName ?? "any";
            if (string.IsNullOrWhiteSpace(dataType))
                dataType = "any";

            if (endpoint.ResponseDataIsArray)
                dataType += "[]";

            return $"Promise<BaseResponse<FullReportData<{dataType}>>>";
        }

        private static string GetOperationalReportRelativePath(string fullPath)
        {
            if (fullPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                return NormalizeOperationalReportingPath(fullPath.Substring(4));

            return NormalizeOperationalReportingPath(fullPath);
        }

        private static string NormalizeOperationalReportingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;

            var segments = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (segments.Count == 0) return path;

            if (segments[0].Equals("reporting", StringComparison.OrdinalIgnoreCase))
            {
                segments[0] = "reporting";
            }

            if (segments.Count >= 4 && segments[0] == "reporting")
            {
                // Keep department and category lower-case, and camelCase report key.
                segments[1] = segments[1].ToLower();
                segments[2] = segments[2].ToLower();
                segments[3] = Utilities.ToCamelCase(segments[3]);
            }

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i] = ReportUtilities.NormalizePathParameters(segments[i]);
            }

            return "/" + string.Join("/", segments);
        }

        // ========================================================================
        // ======================= SETMIS REPORT SERVICES =========================
        // ========================================================================

        public static void GenerateSetmisReportServiceFile(List<SetmisEndpoint> endpoints, string serviceDir)
        {
            var serviceContent = GenerateSetmisReportService(endpoints);
            string servicePath = Path.Combine(serviceDir, "SetmisReportService.ts");
            File.WriteAllText(servicePath, serviceContent);
        }

        private static string GenerateSetmisReportService(List<SetmisEndpoint> endpoints)
        {
            var sb = new StringBuilder();
            bool anyDownload = endpoints.Any(e => e.HasDownloadRoute);

            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("import { BaseResponse } from \"@/types/legacy/custom/Api\"");

            if (anyDownload)
                sb.AppendLine("import { ApiClient, ApiClientBlob } from \"@/services/legacy/custom/ApiService\"");
            else
                sb.AppendLine("import { ApiClient } from \"@/services/legacy/custom/ApiService\"");
            sb.AppendLine("import { FullReportData } from \"@/types/legacy/custom/Reporting\"");
            sb.AppendLine();

            // Collect import types
            var importTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            importTypes.Add("SetmisReportRequest");

            if (anyDownload)
                importTypes.Add("SetmisReportExportRequest");

            foreach (var endpoint in endpoints)
            {
                if (!string.IsNullOrWhiteSpace(endpoint.RecordTypeName) && endpoint.RecordTypeName != "any")
                    importTypes.Add(endpoint.RecordTypeName);
            }

            // Generate imports
            var orderedImports = importTypes.OrderBy(name => name).ToList();
            if (orderedImports.Count > 1)
            {
                sb.AppendLine("import {");
                foreach (var importName in orderedImports)
                {
                    sb.AppendLine($"  {importName},");
                }
                sb.AppendLine("} from \"@/types/legacy/report/ReportingSetmis\"");
            }
            else if (orderedImports.Count == 1)
            {
                sb.AppendLine($"import {{ {orderedImports[0]} }} from \"@/types/legacy/report/ReportingSetmis\"");
            }

            sb.AppendLine();

            // Generate GET functions
            sb.AppendLine("// =========================================================");
            sb.AppendLine("// ==================== Retrieve Reports ===================");
            sb.AppendLine("// =========================================================");
            sb.AppendLine();

            foreach (var endpoint in endpoints.OrderBy(e => e.EntityName))
            {
                sb.AppendLine(GenerateSetmisFunction(endpoint));
                sb.AppendLine();
            }

            // Generate DOWNLOAD functions (only if there are any)
            var endpointsWithDownload = endpoints.Where(e => e.HasDownloadRoute).OrderBy(e => e.EntityName).ToList();
            if (endpointsWithDownload.Count > 0)
            {
                sb.AppendLine("// =========================================================");
                sb.AppendLine("// ==================== Download Reports ===================");
                sb.AppendLine("// =========================================================");
                sb.AppendLine();

                foreach (var endpoint in endpointsWithDownload)
                {
                    sb.AppendLine(GenerateSetmisDownloadFunction(endpoint));
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static string GenerateSetmisFunction(SetmisEndpoint endpoint)
        {
            // Extract entity name, removing "Setmis" prefix and "Record" suffix
            string entityName = endpoint.EntityName;
            string functionName = $"get{Utilities.ToPascalCase(entityName)}SetmisReport";

            string method = endpoint.Method;

            string relativePath = endpoint.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                ? endpoint.Path.Substring(4)
                : endpoint.Path;

            string dataType = endpoint.RecordTypeName ?? "any";
            if (string.IsNullOrWhiteSpace(dataType))
                dataType = "any";
            string returnType = $"Promise<BaseResponse<FullReportData<{dataType}>>>";

            return $"export async function {functionName}(body: SetmisReportRequest): {returnType} {{\n    return ApiClient(\"{method}\", \"{relativePath}\", body)\n}}";
        }

        private static string GenerateSetmisDownloadFunction(SetmisEndpoint endpoint)
        {
            // Extract entity name for download function
            string entityName = endpoint.EntityName;
            string functionName = $"download{Utilities.ToPascalCase(entityName)}SetmisReport";

            string method = endpoint.Method;
            string downloadPath = (endpoint.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
                    ? endpoint.Path.Substring(4)
                    : endpoint.Path)
                .Replace("/retrieve", "/download", StringComparison.OrdinalIgnoreCase);

            return $"export async function {functionName}(body: SetmisReportExportRequest): Promise<Blob> {{\n    return ApiClientBlob(\"{method}\", \"{downloadPath}\", body)\n}}";
        }

    }
}