using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReactCodegen.Legacy
{
    public static class ReportingGenerator
    {
        public static void GenerateReportingFiles(JsonObject paths, JsonObject? schemas, string serviceDir, string typeDir, string pageDir, string apiBaseUrl, string authEmail, string authPassword, List<string> coreResources, List<string> settingsResources, string outputRoot)
        {
            if (schemas == null) return;

            var endpoints = SwaggerProcessor.ExtractReportingEndpoints(paths);
            if (endpoints.Count == 0) return;

            var departmentGroups = endpoints
                .GroupBy(e => e.Department, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Create report subfolders
            string reportTypeDir = Path.Combine(typeDir, "report");
            string reportServiceDir = Path.Combine(serviceDir, "report");
            Directory.CreateDirectory(reportTypeDir);
            Directory.CreateDirectory(reportServiceDir);

            // Collect all endpoints and type imports for combined service file
            var allEndpointsForService = new List<ReportingEndpoint>();
            var allTypeImports = new Dictionary<string, string>(); // typeName -> departmentName

            foreach (var departmentGroup in departmentGroups)
            {
                var fullEndpoints = departmentGroup.Where(e => e.Category == "full").ToList();
                var widgetEndpoints = departmentGroup.Where(e => e.Category == "widget").ToList();
                var allEndpoints = departmentGroup.ToList();
                string departmentName = Utilities.ToPascalCase(departmentGroup.Key);

                // Add to combined service collection
                allEndpointsForService.AddRange(allEndpoints);

                // Generate type files only for full reports (widgets use static WidgetReportData)
                if (fullEndpoints.Count > 0)
                {
                    var schemaAliases = GetSchemaAliases(fullEndpoints, schemas);
                    var typeContent = TypeGenerator.GenerateInterfacesForSchemas(schemaAliases, schemas);
                    if (!string.IsNullOrWhiteSpace(typeContent))
                    {
                        string typePath = Path.Combine(reportTypeDir, $"Reporting{departmentName}.ts");
                        File.WriteAllText(typePath, typeContent);
                    }

                    // Collect type imports for service file
                    foreach (var endpoint in fullEndpoints)
                    {
                        if (!string.IsNullOrWhiteSpace(endpoint.RequestTypeName))
                            allTypeImports[endpoint.RequestTypeName] = departmentName;
                        if (!string.IsNullOrWhiteSpace(endpoint.ResponseDataTypeName) && endpoint.ResponseDataTypeName != "any")
                            allTypeImports[endpoint.ResponseDataTypeName] = departmentName;
                    }
                }

                // Generate pages only for full reports
                if (fullEndpoints.Count > 0)
                {
                    foreach (var endpoint in fullEndpoints)
                    {
                        GenerateReportingPage(endpoint, schemas, GetSchemaAliases(fullEndpoints, schemas), $"Reporting{departmentName}", pageDir, apiBaseUrl, authEmail, authPassword, coreResources, settingsResources, outputRoot).Wait();
                    }
                }
            }

            // Generate single combined service file for all operational reports
            ServiceGenerator.GenerateCombinedOperationalReportingServiceFile(allEndpointsForService, allTypeImports, reportServiceDir);
        }


        private static string GeneratePageContent(
            ReportingEndpoint endpoint,
            string typeFileBaseName,
            List<FilterField> filterFields,
            List<ReportUtilities.DataColumn> dataColumns,
            string arrayItemTypeName,
            string? primaryKeyField,
            string primaryLabel,
            bool hasSearchValue
            )
        {
            var sb = new StringBuilder();
            string reportTitle = $"{primaryLabel} Report";
            string reportSubtitle = $"Review {primaryLabel.ToLower()} with detailed reporting filters.";
            string functionName = Utilities.GetReportingFunctionName(endpoint);
            string componentName = Utilities.ToPascalCase(endpoint.ReportKey) + "ReportPage";
            string? resourceName = Utilities.RemoveIdSuffix(primaryKeyField);
            resourceName = Utilities.ToPascalCase(resourceName ?? "");

            bool hasResourceView = !string.IsNullOrWhiteSpace(resourceName);
            bool isBursaryApplicant = string.Equals(resourceName, "BursaryApplicant", StringComparison.OrdinalIgnoreCase);

            var optionalFilters = filterFields.Where(f => !f.IsMandatory).ToList();
            bool hasPerformancePlan = filterFields.Any(f => f.IsMandatory && f.CamelCaseName.Equals("performancePlanId", StringComparison.OrdinalIgnoreCase));

            bool needsBadges = dataColumns.Any(c =>
                c.CustomRenderType == "stringArrayBadges" || c.CustomRenderType == "complexArrayBadges");
            bool needsReportColumnOverrides = needsBadges;

            string departmentName = Utilities.ToPascalCase(endpoint.Department);

            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------------- ICONS ------------------------");
            sb.AppendLine("import { TbFileSearch } from \"react-icons/tb\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { TableColumn } from \"@/components/legacy/DataTable/DataTable\"");
            if (needsBadges)
            {
                sb.AppendLine("import { Badge } from \"@/components/legacy/Badge/Badge\"");
            }
            sb.AppendLine($"import {{ {functionName} }} from \"@/services/legacy/report/OperationalReportService\"");

            var typeImports = new List<string>();
            if (!string.IsNullOrWhiteSpace(arrayItemTypeName) && arrayItemTypeName != "any")
                typeImports.Add(arrayItemTypeName);
            if (!string.IsNullOrWhiteSpace(endpoint.RequestTypeName))
                typeImports.Add(endpoint.RequestTypeName);
            if (typeImports.Count > 0)
            {
                sb.AppendLine($"import {{ {string.Join(", ", typeImports)} }} from \"@/types/legacy/report/{typeFileBaseName}\"");
            }

            if (needsReportColumnOverrides)
            {
                sb.AppendLine("import { buildReportBadgesHtml } from \"@/utils/legacy/reportingPdf\"");
            }

            if (isBursaryApplicant)
            {
                sb.AppendLine("import ViewBursaryApplicantWithTabs from \"@/components/legacy/Forms/View/ViewBursaryApplicantWithTabs\"");
            }

            if (optionalFilters.Any())
            {
                sb.AppendLine("import { OperationalReportPage, OptionalFilterConfig } from \"@/components/legacy/ReportPages/OperationalReportPage\"");
            }
            else
            {
                sb.AppendLine("import { OperationalReportPage } from \"@/components/legacy/ReportPages/OperationalReportPage\"");
            }

            sb.AppendLine();
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ------------------------- CONFIG ----------------------------");
            sb.AppendLine("// -------------------------------------------------------------");

            // Helper functions for complex array columns
            var complexArrayColumns = dataColumns.Where(c => c.CustomRenderType == "complexArrayBadges").ToList();
            foreach (var column in complexArrayColumns)
            {
                string helperFunctionName = $"get{Utilities.ToPascalCase(column.Key)}Labels";
                string arrayType = column.Type;

                sb.AppendLine($"const {helperFunctionName} = (value: {arrayType} | undefined) => {{");
                sb.AppendLine("  if (!value || value.length == 0) return []");
                sb.AppendLine("  return value");
                sb.AppendLine("    .map((item) => {");
                sb.AppendLine("      const label = (item as any).name || (item as any).label || (item as any).description");
                sb.AppendLine("      if (!label) {");
                sb.AppendLine("        const entries = Object.entries(item)");
                sb.AppendLine("        for (const [key, val] of entries) {");
                sb.AppendLine("          if (typeof val === \"string\" && key.toLowerCase().includes(\"name\")) {");
                sb.AppendLine("            return val");
                sb.AppendLine("          }");
                sb.AppendLine("        }");
                sb.AppendLine("      }");
                sb.AppendLine("      return label");
                sb.AppendLine("    })");
                sb.AppendLine("    .filter((label): label is string => Boolean(label))");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("const columns: TableColumn[] = [");
            foreach (var column in dataColumns)
            {
                sb.AppendLine("  {");
                sb.AppendLine($"    key: \"{column.Key}\",");
                sb.AppendLine($"    dbKey: \"{column.DbKey}\",");
                sb.AppendLine($"    label: \"{column.Label}\",");

                if (column.CustomRenderType == "statusBadge")
                {
                    sb.AppendLine("    type: \"status\",");
                }
                else if (column.CustomRenderType == "stringArrayBadges")
                {
                    sb.AppendLine($"    render: (value: {column.Type} | undefined) => {{");
                    sb.AppendLine("      if (value?.length === 0) return \"-\"");
                    sb.AppendLine("      return (");
                    sb.AppendLine("        <div className=\"flex flex-col gap-1\">");
                    sb.AppendLine("          {value?.map((label, index) => (");
                    sb.AppendLine("            <Badge key={`${label}-${index}`} variant=\"neutral\" size=\"md\" className=\"w-fit\">");
                    sb.AppendLine("              {label}");
                    sb.AppendLine("            </Badge>");
                    sb.AppendLine("          ))}");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("      )");
                    sb.AppendLine("    },");
                }
                else if (column.CustomRenderType == "complexArrayBadges")
                {
                    string helperFunctionName = $"get{Utilities.ToPascalCase(column.Key)}Labels";
                    sb.AppendLine($"    render: (value: {column.Type} | undefined) => {{");
                    sb.AppendLine($"      const labels = {helperFunctionName}(value)");
                    sb.AppendLine("      if (labels.length === 0) return \"-\"");
                    sb.AppendLine("      return (");
                    sb.AppendLine("        <div className=\"flex flex-col gap-1 pb-2\">");
                    sb.AppendLine("          {labels.map((label, index) => (");
                    sb.AppendLine("            <Badge key={`${label}-${index}`} variant=\"neutral\" size=\"md\" className=\"w-fit\">");
                    sb.AppendLine("              {label}");
                    sb.AppendLine("            </Badge>");
                    sb.AppendLine("          ))}");
                    sb.AppendLine("        </div>");
                    sb.AppendLine("      )");
                    sb.AppendLine("    },");
                }

                sb.AppendLine("  },");
            }
            sb.AppendLine("]");
            sb.AppendLine();

            if (optionalFilters.Any())
            {
                GenerateOptionalFilterConfig(sb, optionalFilters);
            }

            if (needsReportColumnOverrides)
            {
                sb.AppendLine("const reportColumnOverrides = {");
                foreach (var column in dataColumns.Where(c =>
                    c.CustomRenderType == "stringArrayBadges" || c.CustomRenderType == "complexArrayBadges"))
                {
                    sb.AppendLine($"  {column.Key}: {{");
                    sb.AppendLine("    getHtml: (row: any) => {");
                    if (column.CustomRenderType == "stringArrayBadges")
                    {
                        sb.AppendLine($"      const labels = row.{column.Key} ?? []");
                    }
                    else
                    {
                        string helperFunctionName = $"get{Utilities.ToPascalCase(column.Key)}Labels";
                        sb.AppendLine($"      const labels = {helperFunctionName}(row.{column.Key})");
                    }
                    sb.AppendLine("      return buildReportBadgesHtml(labels)");
                    sb.AppendLine("    },");
                    sb.AppendLine("  },");
                }
                sb.AppendLine("}");
                sb.AppendLine();
            }

            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ------------------------- PAGE ------------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine($"export default function {componentName}() {{");
            sb.AppendLine("  return (");
            sb.AppendLine($"    <OperationalReportPage<{arrayItemTypeName}, {endpoint.RequestTypeName ?? "any"}>");
            sb.AppendLine($"      title=\"{reportTitle}\"");
            sb.AppendLine($"      subtitle=\"{reportSubtitle}\"");
            sb.AppendLine("      icon={TbFileSearch}");
            sb.AppendLine($"      fetchService={{{functionName}}}");
            sb.AppendLine($"      idField=\"{primaryKeyField ?? "id"}\"");
            sb.AppendLine("      columns={columns}");

            if (needsReportColumnOverrides)
            {
                sb.AppendLine("      reportColumnOverrides={reportColumnOverrides}");
            }

            if (optionalFilters.Any())
            {
                sb.AppendLine("      optionalFilters={optionalFilters}");
            }

            if (isBursaryApplicant)
            {
                string viewPropName = isBursaryApplicant
                    ? "bursaryApplicantId"
                    : Utilities.ToCamelCase(primaryKeyField ?? "resourceId");

                sb.AppendLine($"      detailComponent={{(id) => <ViewBursaryApplicantWithTabs {viewPropName}={{id}} />}}");

                string drawerTitle = Utilities.ToTitleCase(resourceName ?? "Details") + " Details";
                sb.AppendLine($"      detailDrawerTitle=\"{drawerTitle}\"");
            }

            sb.AppendLine($"      exportFilenameBase=\"{Utilities.ToKebabCase(endpoint.ReportKey)}\"");
            sb.AppendLine($"      enableSearch={{{hasSearchValue.ToString().ToLower()}}}");

            if (hasPerformancePlan)
            {
                sb.AppendLine($"      performancePlanRememberKey=\"operational-performance-plan-id\"");
            }

            sb.AppendLine("    />");
            sb.AppendLine("  )");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static async Task GenerateReportingPage(
            ReportingEndpoint endpoint,
            JsonObject schemas,
            Dictionary<string, string> schemaAliases,
            string typeFileBaseName,
            string pageDir,
            string apiBaseUrl,
            string authEmail,
            string authPassword,
            List<string> coreResources,
            List<string> settingsResources,
            string outputRoot
            )
        {
            // Fetch ObjectId and PrimaryLabel from Object table
            var storedProcName = ConstructStoredProcedureName(endpoint.Path);
            var (objectId, primaryLabel) = await FetchObjectInfoFromObjectTable(storedProcName, apiBaseUrl, authEmail, authPassword);

            // Fallback to endpoint key if no primary label found
            if (string.IsNullOrWhiteSpace(primaryLabel))
            {
                primaryLabel = Utilities.ToTitleCase(endpoint.ReportKey);
            }

            // Get the request schema to extract filter fields
            var requestSchema = Utilities.FindSchema(schemas, endpoint.RequestSchemaName ?? "");
            var responseDataSchema = Utilities.FindSchema(schemas, endpoint.ResponseDataSchemaName ?? "");

            if (requestSchema == null || responseDataSchema == null)
            {
                Console.WriteLine($"  ⚠ {endpoint.ReportKey}: Missing schemas");
                return;
            }

            // Extract filter fields from request schema
            var filterFields = ExtractFilterFields(requestSchema);

            // Check if searchValue exists in the request schema
            bool hasSearchValue = requestSchema["properties"]?.AsObject()?.ContainsKey("searchValue") ?? false;

            // GetResponseDataInfo already extracted the record type from reportData
            // So responseDataSchema is the record schema, not a wrapper
            // Use it directly as the item schema
            string arrayItemTypeName = endpoint.ResponseDataTypeName ?? "any";

            // Find the primary key field (typically entityNameId)
            string? primaryKeyField = Utilities.FindPrimaryKeyField(responseDataSchema, endpoint.ReportKey);

            // Extract data columns from the record schema
            var dataColumns = ExtractDataColumns(responseDataSchema, schemaAliases);

            // Generate the page content
            var pageContent = GeneratePageContent(
                endpoint,
                typeFileBaseName,
                filterFields,
                dataColumns,
                arrayItemTypeName,
                primaryKeyField,
                primaryLabel,
                hasSearchValue);

            // Generate reusable component in ReportPages/Operational folder
            string reportPascal = Utilities.ToPascalCase(endpoint.ReportKey);
            string componentFileName = $"Operational{reportPascal}ReportPage.tsx";
            string componentPath = Path.Combine(pageDir, componentFileName);

            Directory.CreateDirectory(pageDir);
            File.WriteAllText(componentPath, pageContent);

            primaryLabel ??= Utilities.ToTitleCase(endpoint.ReportKey) ?? endpoint.ReportKey ?? "Report";

            Console.WriteLine($"  ✓ {primaryLabel}");
        }

        private static (string Name, bool IsArray) GetResponseDataInfo(string? responseSchemaName, JsonObject schemas)
        {
            if (string.IsNullOrWhiteSpace(responseSchemaName))
                return ("any", false);

            var responseSchema = Utilities.FindSchema(schemas, responseSchemaName);
            if (responseSchema == null)
                return (responseSchemaName, false);

            // Navigate to properties.data
            var dataSchema = responseSchema["properties"]?["data"]?.AsObject();
            if (dataSchema == null)
                return (responseSchemaName, false);

            // If data has a $ref, follow it to get the actual data schema
            var dataRef = dataSchema["$ref"]?.GetValue<string>();
            JsonObject? actualDataSchema = dataSchema;

            if (!string.IsNullOrWhiteSpace(dataRef))
            {
                var dataSchemaName = Utilities.ExtractSchemaName(dataRef);
                actualDataSchema = Utilities.FindSchema(schemas, dataSchemaName);
                if (actualDataSchema == null)
                    return (dataSchemaName, false);
            }

            // Now look for reportData property within the data schema
            var reportDataSchema = actualDataSchema["properties"]?["reportData"]?.AsObject();
            if (reportDataSchema != null)
            {
                // reportData should be an array
                var reportDataType = reportDataSchema["type"]?.GetValue<string>();
                if (reportDataType == "array")
                {
                    var itemRef = reportDataSchema["items"]?["$ref"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(itemRef))
                        return (Utilities.ExtractSchemaName(itemRef), false); // Return record type, not as array since we want the individual record
                }
            }

            // Fallback to old logic for backward compatibility
            var type = dataSchema["type"]?.GetValue<string>();
            if (type == "array")
            {
                var itemRef = dataSchema["items"]?["$ref"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(itemRef))
                    return (Utilities.ExtractSchemaName(itemRef), true);

                return ("any", true);
            }

            if (!string.IsNullOrWhiteSpace(dataRef))
                return (Utilities.ExtractSchemaName(dataRef), false);

            return ("any", false);
        }

        private static Dictionary<string, string> GetSchemaAliases(List<ReportingEndpoint> endpoints, JsonObject schemas)
        {
            var schemaAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var endpoint in endpoints)
            {
                if (!string.IsNullOrWhiteSpace(endpoint.RequestSchemaName))
                {
                    endpoint.RequestTypeName = MapReportingTypeName(endpoint.RequestSchemaName, endpoint.Department, endpoint.Category);
                    if (!IsApiResponseSchema(endpoint.RequestSchemaName))
                        schemaAliases[endpoint.RequestSchemaName] = endpoint.RequestTypeName;
                }

                var responseData = GetResponseDataInfo(endpoint.ResponseSchemaName, schemas);
                endpoint.ResponseDataSchemaName = responseData.Name;
                endpoint.ResponseDataIsArray = responseData.IsArray;
                endpoint.ResponseDataTypeName = MapReportingTypeName(endpoint.ResponseDataSchemaName, endpoint.Department, endpoint.Category);

                if (!string.IsNullOrWhiteSpace(responseData.Name) && responseData.Name != "any")
                {
                    if (!IsApiResponseSchema(responseData.Name))
                        schemaAliases[responseData.Name] = endpoint.ResponseDataTypeName;
                }
            }

            return schemaAliases;
        }


        private static bool IsApiResponseSchema(string schemaName)
        {
            return schemaName.EndsWith("SETAApiResponse", StringComparison.OrdinalIgnoreCase);
        }

        private static string MapReportingTypeName(string schemaName, string department, string category)
        {
            if (string.IsNullOrWhiteSpace(schemaName)) return schemaName;

            string name = schemaName;

            // Remove common prefixes/suffixes to get the core name
            // Check for RequestModel first (before Request alone)
            if (name.EndsWith("RequestModel", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "RequestModel".Length);
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                return $"{name}Request";
            }

            if (name.EndsWith("Request", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "Request".Length);
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                return $"{name}Request";
            }

            if (name.EndsWith("WidgetReportData", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "WidgetReportData".Length);
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                return $"{name}";
            }

            // Check for ResponseModel first (before Response alone)
            if (name.EndsWith("ResponseModel", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "ResponseModel".Length);
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                return $"{name}Record";
            }

            if (name.EndsWith("Response", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - "Response".Length);
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                return $"{name}Record";
            }

            if (category.Equals("full", StringComparison.OrdinalIgnoreCase))
            {
                name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
                name = RemoveModelSuffix(name);
                return $"{name}Record";
            }

            name = Utilities.RemoveReportDepartmentAndCategoryPrefix(name, department, category);
            name = RemoveModelSuffix(name);
            return $"{name}";
        }

        private static string RemoveModelSuffix(string name)
        {
            if (name.EndsWith("Model", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - "Model".Length);
            return name;
        }

        private static string ConstructStoredProcedureName(string path)
        {
            // Convert /api/Reporting/bursary/full/bursaryApplications to sp_Bursary_Full_BursaryApplications
            var normalized = path.ToLower().Replace("/api/reporting/", "").Trim('/');
            var segments = normalized.Split('/');

            var result = "sp_" + string.Join("_", segments.Select(s => Utilities.ToPascalCase(s)));
            return result;
        }

        private static async Task<(int? ObjectId, string PrimaryLabel)> FetchObjectInfoFromObjectTable(string storedProcName, string apiBaseUrl, string authEmail, string authPassword)
        {
            try
            {
                using var apiClient = await AuthenticatedApiClient.CreateAsync(apiBaseUrl, authEmail, authPassword);
                if (apiClient == null)
                {
                    // Silent fail for login - not critical for generation
                    return (null, "");
                }

                // Search Object table
                var searchBody = new
                {
                    pageNumber = 1,
                    pageSize = 1,
                    filterByList = new[]
                    {
                        new
                        {
                            columnName = "name",
                            value = storedProcName,
                            @operator = "equal"
                        }
                    },
                    orderByList = Array.Empty<object>()
                };

                var searchBodyJson = JsonSerializer.Serialize(searchBody);
                var searchContent = new StringContent(searchBodyJson, Encoding.UTF8, "application/json");
                var searchUri = new Uri(apiClient.Client.BaseAddress!, "/api/settings/object/search");
                var effectiveCookieHeader = apiClient.GetEffectiveCookieHeader(searchUri);

                using var searchRequest = new HttpRequestMessage(HttpMethod.Post, searchUri);
                searchRequest.Content = searchContent;
                if (!string.IsNullOrWhiteSpace(effectiveCookieHeader))
                {
                    searchRequest.Headers.Add("Cookie", effectiveCookieHeader);
                }

                var searchResponse = await apiClient.Client.SendAsync(searchRequest);

                if (!searchResponse.IsSuccessStatusCode)
                {
                    // Silent fail for Object lookup - not critical
                    return (null, "");
                }

                var responseJson = await searchResponse.Content.ReadAsStringAsync();
                var jsonNode = JsonNode.Parse(responseJson);
                var searchResults = jsonNode?["data"]?["searchResults"]?.AsArray();

                if (searchResults != null && searchResults.Count > 0)
                {
                    var objectIdNode = searchResults[0]?["objectId"];
                    var primaryLabel = searchResults[0]?["primaryLabel"]?.GetValue<string>();

                    int? objectId = null;
                    if (objectIdNode != null)
                    {
                        objectId = objectIdNode.GetValue<int>();
                    }

                    if (!string.IsNullOrWhiteSpace(primaryLabel))
                    {
                        return (objectId, primaryLabel);
                    }
                }

                return (null, "");
            }
            catch
            {
                // Silent fail for Object fetch - not critical
                return (null, "");
            }
        }

        private static List<FilterField> ExtractFilterFields(JsonObject requestSchema)
        {
            var fields = new List<FilterField>();
            var properties = requestSchema["properties"]?.AsObject();
            if (properties == null) return fields;

            // Common mandatory fields that should be treated specially
            var mandatoryFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "performancePlanId", "pageNumber", "pageSize"
            };

            foreach (var prop in properties)
            {
                string fieldName = prop.Key;
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                // Skip pagination fields and searchValue (searchValue is handled separately as a state)
                if (fieldName.Equals("pageNumber", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.Equals("pageSize", StringComparison.OrdinalIgnoreCase) ||
                    fieldName.Equals("searchValue", StringComparison.OrdinalIgnoreCase))
                    continue;

                var typeInfo = Utilities.GetTypeScriptType(propSchema);
                bool isRequired = requestSchema["required"]?.AsArray()?.Any(r =>
                    r?.GetValue<string>()?.Equals(fieldName, StringComparison.OrdinalIgnoreCase) == true) ?? false;

                // PerformancePlanId is treated as mandatory
                bool isMandatory = fieldName.Equals("performancePlanId", StringComparison.OrdinalIgnoreCase);

                fields.Add(new FilterField
                {
                    Name = fieldName,
                    CamelCaseName = Utilities.ToCamelCase(fieldName),
                    PascalCaseName = Utilities.ToPascalCase(fieldName),
                    Type = typeInfo,
                    IsRequired = isRequired,
                    IsMandatory = isMandatory
                });
            }

            return fields;
        }

        private static List<ReportUtilities.DataColumn> ExtractDataColumns(JsonObject responseDataSchema, Dictionary<string, string> schemaAliases)
        {
            var columns = new List<ReportUtilities.DataColumn>();
            var properties = responseDataSchema["properties"]?.AsObject();
            if (properties == null) return columns;

            // Define fields to exclude
            var excludedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "statusId", "primaryLabel", "bursaryApplicantId"
            };

            foreach (var prop in properties)
            {
                string fieldName = prop.Key;
                string camelCaseFieldName = Utilities.ToCamelCase(fieldName);

                // Skip excluded fields
                if (excludedFields.Contains(fieldName))
                    continue;

                // Skip fields ending with "Id" except for statusId and identityNumber
                if (fieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                    !fieldName.Equals("statusId", StringComparison.OrdinalIgnoreCase) &&
                    !fieldName.Equals("identityNumber", StringComparison.OrdinalIgnoreCase))
                    continue;

                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                var typeInfo = Utilities.GetTypeScriptType(propSchema);

                // Generate display label - trim "Name" suffix except for firstName and lastName
                string displayLabel = Utilities.ToTitleCase(fieldName);
                if (fieldName.EndsWith("Name", StringComparison.OrdinalIgnoreCase) &&
                    !fieldName.Equals("firstName", StringComparison.OrdinalIgnoreCase) &&
                    !fieldName.Equals("lastName", StringComparison.OrdinalIgnoreCase))
                {
                    string withoutSuffix = fieldName.Substring(0, fieldName.Length - 4);
                    displayLabel = Utilities.ToTitleCase(withoutSuffix);
                }

                // Check if this is a string array by examining the schema
                bool isStringArray = false;
                var arrayType = propSchema["type"]?.GetValue<string>();
                if (arrayType == "array")
                {
                    var itemsSchema = propSchema["items"]?.AsObject();
                    if (itemsSchema != null)
                    {
                        var itemType = itemsSchema["type"]?.GetValue<string>();
                        isStringArray = itemType == "string";

                        // If it's a string array, update typeInfo
                        if (isStringArray)
                        {
                            typeInfo = "string[]";
                        }
                    }
                }

                // Check if this is a reference to another schema (for nested objects/arrays)
                bool isComplexArray = typeInfo.Contains("[]") &&
                                     schemaAliases.ContainsValue(typeInfo.TrimEnd('[', ']'));
                bool isComplex = isComplexArray || schemaAliases.ContainsValue(typeInfo);

                // Check if this field needs a status badge render (on statusName column)
                bool isStatus = fieldName.Equals("statusName", StringComparison.OrdinalIgnoreCase);

                // Determine custom render type
                string? customRenderType = null;
                bool needsCustomRender = false;

                if (isStatus)
                {
                    customRenderType = "statusBadge";
                    needsCustomRender = true;
                }
                else if (isStringArray)
                {
                    customRenderType = "stringArrayBadges";
                    needsCustomRender = true;
                }
                else if (isComplexArray)
                {
                    customRenderType = "complexArrayBadges";
                    needsCustomRender = true;
                }

                columns.Add(new ReportUtilities.DataColumn
                {
                    Key = camelCaseFieldName,
                    DbKey = camelCaseFieldName,
                    Label = displayLabel,
                    Type = typeInfo,
                    IsComplex = isComplex,
                    IsCustomRender = needsCustomRender,
                    CustomRenderType = customRenderType
                });
            }

            return columns;
        }

        private static void GenerateOptionalFilterConfig(StringBuilder sb, List<FilterField> optionalFilters)
        {
            if (!optionalFilters.Any()) return;

            sb.AppendLine("const optionalFilters: OptionalFilterConfig[] = [");

            foreach (var filter in optionalFilters)
            {
                string optionKey = Utilities.RemoveIdSuffix(filter.PascalCaseName);
                string labelBase = Utilities.RemoveIdSuffix(filter.Name);

                string label = Utilities.ToTitleCase(labelBase);

                sb.AppendLine("  {");
                sb.AppendLine($"    key: \"{filter.CamelCaseName}\",");
                sb.AppendLine($"    label: \"{label}\",");
                sb.AppendLine($"    optionKey: \"{Utilities.ToCamelCase(optionKey)}\",");
                sb.AppendLine($"    placeholder: \"Select {label.ToLower()}...\",");
                sb.AppendLine("  },");
            }

            sb.AppendLine("]");
            sb.AppendLine();
        }

        private class FilterField
        {
            public string Name { get; set; } = "";
            public string CamelCaseName { get; set; } = "";
            public string PascalCaseName { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsRequired { get; set; }
            public bool IsMandatory { get; set; }
        }
    }
}
