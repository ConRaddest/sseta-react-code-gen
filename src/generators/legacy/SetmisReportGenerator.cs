using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class SetmisReportGenerator
    {
        public static void GenerateSetmisFiles(JsonObject paths, JsonObject? schemas, string serviceDir, string typeDir, string pageDir)
        {
            if (schemas == null) return;

            var endpoints = SwaggerProcessor.ExtractSetmisEndpoints(paths);
            if (endpoints.Count == 0) return;

            // Create setmis report subdirectories
            string setmisTypeDir = Path.Combine(typeDir, "report");
            string setmisServiceDir = Path.Combine(serviceDir, "report");
            Directory.CreateDirectory(setmisTypeDir);
            Directory.CreateDirectory(setmisServiceDir);

            // Extract record schemas for each endpoint
            foreach (var endpoint in endpoints)
            {
                var recordSchemaName = GetSetmisRecordSchemaName(endpoint.ResponseSchemaName, schemas);
                endpoint.RecordSchemaName = recordSchemaName;
                // Inline MapSetmisRecordTypeName logic: Setmis{Entity} -> Setmis{Entity}Record
                endpoint.RecordTypeName = string.IsNullOrWhiteSpace(recordSchemaName)
                    ? "any"
                    : $"{recordSchemaName}Record";
            }

            // Generate types file
            GenerateSetmisTypeFile(endpoints, schemas, setmisTypeDir);

            // Generate service file
            ServiceGenerator.GenerateSetmisReportServiceFile(endpoints, setmisServiceDir);

            // Generate pages for each endpoint
            foreach (var endpoint in endpoints)
            {
                GenerateSetmisPage(endpoint, schemas, pageDir);
            }
        }

        private static void GenerateSetmisPage(
            SetmisEndpoint endpoint,
            JsonObject schemas,
            string pageDir)
        {
            try
            {
                // Get record schema
                var recordSchema = Utilities.FindSchema(schemas, endpoint.RecordSchemaName ?? "");
                if (recordSchema == null)
                {
                    Console.WriteLine($"  ⚠ {endpoint.EntityName}: Missing record schema");
                    return;
                }

                // Extract title from entity name
                string title = $"SETMIS {Utilities.ToTitleCase(endpoint.EntityName)}";
                string entityCamel = Utilities.ToCamelCase(endpoint.EntityName);
                string entityPascal = Utilities.ToPascalCase(endpoint.EntityName);
                string recordTypeName = endpoint.RecordTypeName ?? "any";

                // Generate function names using new naming convention
                string functionName = $"get{entityPascal}SetmisReport";

                // Find primary key field
                string primaryKeyField = $"setmis{entityPascal}Id";

                // Extract all columns for display
                var columns = ExtractSetmisColumns(recordSchema);

                // Generate page content
                string? downloadFunctionName = null;
                if (endpoint.HasDownloadRoute)
                {
                    downloadFunctionName = $"download{entityPascal}SetmisReport";
                }

                var pageContent = GenerateSetmisPageContent(
                    endpoint.EntityName,
                    entityPascal,
                    entityCamel,
                    title,
                    recordTypeName,
                    functionName,
                    downloadFunctionName,
                    primaryKeyField,
                    columns);

                // Generate reusable component in ReportPages/Setmis folder
                string componentFileName = $"Setmis{entityPascal}ReportPage.tsx";
                string componentPath = Path.Combine(pageDir, componentFileName);

                Directory.CreateDirectory(pageDir);
                File.WriteAllText(componentPath, pageContent);

                Console.WriteLine($"  ✓ {title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Error generating {endpoint.EntityName}: {ex.Message}");
            }
        }

        private static List<ReportUtilities.DataColumn> ExtractSetmisColumns(JsonObject recordSchema)
        {
            var columns = new List<ReportUtilities.DataColumn>();
            var properties = recordSchema["properties"]?.AsObject();
            if (properties == null) return columns;

            // Fields to skip - only internal/technical fields, not data fields
            var skipFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "dateStamp",
                "performancePlanId",
                "quarterId"
            };

            foreach (var prop in properties)
            {
                var propKey = prop.Key;
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                // Skip only specific internal fields and filler fields
                // Skip the primary key field (setmis{Entity}Id pattern)
                if (skipFields.Contains(propKey) ||
                    propKey.StartsWith("filler", StringComparison.OrdinalIgnoreCase) ||
                    propKey.StartsWith("setmis", StringComparison.OrdinalIgnoreCase) && propKey.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    continue;

                string label = Utilities.ToTitleCase(propKey);
                string camelKey = Utilities.ToCamelCase(propKey);

                columns.Add(new ReportUtilities.DataColumn
                {
                    Key = camelKey,
                    DbKey = camelKey,
                    Label = label,
                    Type = "string",
                    IsComplex = false,
                    IsCustomRender = false
                });
            }

            return columns;
        }

        private static string GenerateSetmisPageContent(
            string entityName,
            string entityPascal,
            string entityCamel,
            string title,
            string recordTypeName,
            string functionName,
            string? downloadFunctionName,
            string primaryKeyField,
            List<ReportUtilities.DataColumn> columns)
        {
            var sb = new StringBuilder();

            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("// -------------------- ICONS ------------------------");
            sb.AppendLine("import { TbFileDatabase } from \"react-icons/tb\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { TableColumn } from \"@/components/legacy/DataTable/DataTable\"");
            sb.AppendLine("import { SetmisReportPage } from \"@/components/legacy/ReportPages/SetmisReportPage\"");

            // Build service imports — always include retrieve, conditionally include download
            if (downloadFunctionName != null)
            {
                sb.AppendLine($"import {{ {functionName}, {downloadFunctionName} }} from \"@/services/legacy/report/SetmisReportService\"");
                sb.AppendLine($"import {{ {recordTypeName} }} from \"@/types/legacy/report/ReportingSetmis\"");
            }
            else
            {
                sb.AppendLine($"import {{ {functionName} }} from \"@/services/legacy/report/SetmisReportService\"");
                sb.AppendLine($"import {{ {recordTypeName} }} from \"@/types/legacy/report/ReportingSetmis\"");
            }

            sb.AppendLine();
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ------------------------- COLUMNS ---------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("const columns: TableColumn[] = [");

            foreach (var col in columns)
            {
                // Format columns for better readability - keep each column on one line for simple columns
                if (col.Label.Length <= 20)
                {
                    sb.AppendLine($"  {{ key: \"{col.Key}\", dbKey: \"{col.DbKey}\", label: \"{col.Label}\" }},");
                }
                else
                {
                    // Multi-line format for longer labels
                    sb.AppendLine("  {");
                    sb.AppendLine($"    key: \"{col.Key}\",");
                    sb.AppendLine($"    dbKey: \"{col.DbKey}\",");
                    sb.AppendLine($"    label: \"{col.Label}\",");
                    sb.AppendLine("  },");
                }
            }

            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ------------------------- PAGE ------------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine($"export default function Setmis{entityPascal}ReportPage() {{");
            sb.AppendLine("  return (");
            sb.AppendLine($"    <SetmisReportPage<{recordTypeName}>");
            sb.AppendLine($"      title=\"{title} Report\"");
            sb.AppendLine($"      subtitle=\"View SETMIS {entityCamel} records for selected performance plan and quarter.\"");
            sb.AppendLine("      icon={TbFileDatabase}");
            sb.AppendLine($"      fetchService={{{functionName}}}");
            sb.AppendLine($"      idField=\"{primaryKeyField}\"");
            sb.AppendLine("      columns={columns}");

            if (downloadFunctionName != null)
            {
                sb.AppendLine($"      downloadService={{{downloadFunctionName}}}");
            }

            sb.AppendLine("      performancePlanRememberKey=\"setmis-performance-plan-id\"");
            sb.AppendLine("    />");
            sb.AppendLine("  )");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string? GetSetmisRecordSchemaName(string? responseSchemaName, JsonObject schemas)
        {
            if (string.IsNullOrWhiteSpace(responseSchemaName))
                return null;

            // Response schema: Setmis{Entity}ResponseModelSETAApiResponse
            // Contains: data -> Setmis{Entity}ResponseModel
            // Which contains: reportData -> array of Setmis{Entity}

            var responseSchema = Utilities.FindSchema(schemas, responseSchemaName);
            if (responseSchema == null) return null;

            // Navigate to properties.data
            var dataRef = responseSchema["properties"]?["data"]?["$ref"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(dataRef)) return null;

            var dataSchemaName = Utilities.ExtractSchemaName(dataRef);
            var dataSchema = Utilities.FindSchema(schemas, dataSchemaName);
            if (dataSchema == null) return null;

            // Navigate to properties.reportData.items
            var reportDataSchema = dataSchema["properties"]?["reportData"]?.AsObject();
            if (reportDataSchema == null) return null;

            var itemRef = reportDataSchema["items"]?["$ref"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(itemRef)) return null;

            return Utilities.ExtractSchemaName(itemRef);
        }

        private static void GenerateSetmisTypeFile(
            List<SetmisEndpoint> endpoints,
            JsonObject schemas,
            string typeDir)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();

            // Add hardcoded SetmisReportRequest interface
            sb.AppendLine("export interface SetmisReportRequest {");
            sb.AppendLine("  pageNumber: number");
            sb.AppendLine("  pageSize: number");
            sb.AppendLine("  performancePlanId: number");
            sb.AppendLine("  quarterId?: number");
            sb.AppendLine("}");
            sb.AppendLine();

            // Add hardcoded SetmisReportExportRequest interface (used by download routes)
            sb.AppendLine("export interface SetmisReportExportRequest {");
            sb.AppendLine("  performancePlanId: number");
            sb.AppendLine("  quarterId?: number");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate record types
            var schemaAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in endpoints)
            {
                if (!string.IsNullOrWhiteSpace(endpoint.RecordSchemaName) &&
                    !string.IsNullOrWhiteSpace(endpoint.RecordTypeName))
                {
                    schemaAliases[endpoint.RecordSchemaName] = endpoint.RecordTypeName;
                }
            }

            var typeContent = TypeGenerator.GenerateInterfacesForSchemas(schemaAliases, schemas);
            sb.Append(typeContent);

            string typePath = Path.Combine(typeDir, "ReportingSetmis.ts");
            File.WriteAllText(typePath, sb.ToString());
        }

    }
}
