using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Linq;

namespace ReactCodegen.Legacy
{
    public static class Utilities
    {
        public static string ToCamelCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;

            // Handle acronyms: If we have multiple consecutive uppercase letters,
            // lowercase all but the last one (before a lowercase letter)
            // E.g., "SAQAQualificationId" -> "saqaQualificationId"

            int firstLowerIndex = -1;
            for (int i = 0; i < str.Length; i++)
            {
                if (char.IsLower(str[i]))
                {
                    firstLowerIndex = i;
                    break;
                }
            }

            // No lowercase letters at all (e.g. "ETQE") — lowercase the whole string
            if (firstLowerIndex == -1)
                return str.ToLower();

            if (firstLowerIndex <= 1)
            {
                // No acronym or single letter, just lowercase first char
                return char.ToLower(str[0]) + str.Substring(1);
            }

            // We have an acronym. Lowercase everything up to (but not including) the letter before firstLowerIndex
            // E.g., "SAQAQualification" -> firstLowerIndex=4 ('u'), so lowercase "SAQA" to "saqa"
            return str.Substring(0, firstLowerIndex - 1).ToLower() + str.Substring(firstLowerIndex - 1);
        }

        public static string ToPascalCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return char.ToUpper(str[0]) + str.Substring(1);
        }

        public static string ToKebabCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;

            // Convert PascalCase or camelCase to kebab-case
            // Handle acronyms: Insert hyphens at:
            // 1. Between lowercase and uppercase letters (standard camelCase boundary)
            // 2. Between uppercase letters when followed by lowercase (end of acronym)
            // E.g., "SAQAQualification" -> "saqa-qualification"
            string result = Regex.Replace(str, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "-");

            // Convert to lowercase
            return result.ToLower();
        }

        public static string GetPluralName(string tableName)
        {
            if (tableName.EndsWith("y", StringComparison.OrdinalIgnoreCase))
                return tableName.Substring(0, tableName.Length - 1) + "ies";
            if (tableName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return tableName + "es";
            return tableName + "s";
        }

        public static bool StartsWithVowel(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            char firstChar = char.ToLower(text[0]);
            return firstChar == 'a' || firstChar == 'e' || firstChar == 'i' || firstChar == 'o' || firstChar == 'u';
        }

        public static string GetTypeScriptType(JsonObject propSchema)
        {
            string? type = propSchema["type"]?.GetValue<string>();
            string? format = propSchema["format"]?.GetValue<string>();

            return type switch
            {
                "integer" => "number",
                "number" => "number",
                "string" when format == "date-time" => "string",
                "string" => "string",
                "boolean" => "boolean",
                "array" => "any[]", // Could be more specific based on items
                "guid" => "string",
                _ => "any"
            };
        }

        public static string NormalizeEnumKey(string name, string? idValue = null)
        {
            var withWordBreaks = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
            var cleaned = Regex.Replace(withWordBreaks, @"[^A-Za-z0-9]+", " ").Trim();
            if (string.IsNullOrEmpty(cleaned))
            {
                return !string.IsNullOrWhiteSpace(idValue) ? $"Value{idValue}" : "Value";
            }

            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var key = string.Concat(parts.Select(part =>
            {
                if (part.All(char.IsDigit))
                {
                    return part;
                }
                if (part.All(char.IsUpper) && part.Length <= 3)
                {
                    return part;
                }
                return part.Length == 1
                    ? part.ToUpper()
                    : char.ToUpper(part[0]) + part.Substring(1).ToLower();
            }));

            if (string.IsNullOrEmpty(key))
            {
                return !string.IsNullOrWhiteSpace(idValue) ? $"Value{idValue}" : "Value";
            }

            if (char.IsDigit(key[0]))
            {
                key = !string.IsNullOrWhiteSpace(idValue) ? $"Value{key}" : $"Value{key}";
            }

            return key;
        }

        public static string GetDefaultValueForType(JsonObject? propSchema)
        {
            if (propSchema == null) return "undefined";

            string? type = propSchema["type"]?.GetValue<string>();
            return type switch
            {
                "string" => "\"\"",
                "integer" or "number" => "0",
                "boolean" => "false",
                _ => "undefined"
            };
        }

        public static string GetIdFieldName(string resourceName)
        {
            return $"{ToCamelCase(resourceName)}Id";
        }

        public static JsonObject? FindSchema(JsonObject? schemas, string schemaName)
        {
            if (schemas == null || string.IsNullOrWhiteSpace(schemaName))
            {
                return null;
            }

            foreach (var schema in schemas)
            {
                if (schema.Key.Equals(schemaName, StringComparison.OrdinalIgnoreCase))
                {
                    return schema.Value?.AsObject();
                }
            }
            return null;
        }

        public static string GetFieldHeading(string fieldName)
        {

            string name = fieldName;

            // Remove "id" suffix if present
            if (name.ToLower().EndsWith("id"))
            {
                name = fieldName.Substring(0, fieldName.Length - 2);
            }

            // Remove "name" suffix if present (skipping first, middle last names)
            if (name.ToLower().EndsWith("name") &&

                !(fieldName.ToLower().Contains("first") ||
                fieldName.ToLower().Contains("last") ||
                fieldName.ToLower().Contains("middle")) &&

                (name.ToLower() != "name"))
            {
                name = fieldName.Substring(0, fieldName.Length - 4);
            }

            // Convert camelCase to Title Case while preserving acronyms
            // This regex splits on:
            // 1. A lowercase letter followed by an uppercase letter (camelCase boundary)
            // 2. Multiple uppercase letters followed by a lowercase letter (end of acronym)
            // But keeps consecutive uppercase letters together (acronyms like SDL, SIC, SETA)
            string titleCase = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

            // Capitalize first letter
            return string.IsNullOrEmpty(titleCase) ? titleCase : char.ToUpper(titleCase[0]) + titleCase.Substring(1);
        }

        public static string ToTitleCase(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;

            // Convert camelCase/PascalCase to Title Case with spaces
            string spaced = Regex.Replace(str, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

            // Capitalize first letter
            return char.ToUpper(spaced[0]) + spaced.Substring(1);
        }

        public static string ExtractSchemaName(string refValue)
        {
            var parts = refValue.Split('/');
            return parts.Length > 0 ? parts[^1] : refValue;
        }

        public static string RemoveReportDepartmentAndCategoryPrefix(string name, string department, string category)
        {
            // Remove "Reporting" prefix if present
            if (name.StartsWith("Reporting", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Reporting".Length);

            // Remove department name from the beginning (e.g., "Bursary")
            var departmentPascal = Utilities.ToPascalCase(department);
            if (name.StartsWith(departmentPascal, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(departmentPascal.Length);

            // Remove category from the beginning (e.g., "Full" or "Widget")
            if (category.Equals("full", StringComparison.OrdinalIgnoreCase) &&
                name.StartsWith("Full", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Full".Length);
            else if (category.Equals("widget", StringComparison.OrdinalIgnoreCase) &&
                     name.StartsWith("Widget", StringComparison.OrdinalIgnoreCase))
                name = name.Substring("Widget".Length);

            // Remove "Report" suffix if present
            return name.EndsWith("FullReport", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - "FullReport".Length)
                : name;
        }

        public static string? FindPrimaryKeyField(JsonObject itemSchema, string reportKey)
        {
            var properties = itemSchema["properties"]?.AsObject();
            if (properties == null) return null;

            // Look for fields ending with "Id" that could be the primary key
            var idFields = properties
                .Where(p => p.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Key)
                .ToList();

            if (idFields.Count == 0) return null;

            // Try multiple strategies to find the primary key
            // 1. Look for fields containing the report key (e.g., "bursaryApplicantId" for "bursary")
            var reportKeyLower = reportKey.ToLower().TrimEnd('s'); // "bursary" -> "bursarie"
            var primaryKey = idFields.FirstOrDefault(f =>
                f.ToLower().Contains(reportKeyLower));

            // 2. If not found, try without trimming 's' (for cases like "learners")
            if (primaryKey == null)
            {
                reportKeyLower = reportKey.ToLower();
                primaryKey = idFields.FirstOrDefault(f =>
                    f.ToLower().StartsWith(reportKeyLower));
            }

            // 3. Look for common primary key patterns
            if (primaryKey == null)
            {
                var commonPatterns = new[] { "id", "recordId", "itemId" };
                primaryKey = idFields.FirstOrDefault(f =>
                    commonPatterns.Any(pattern => f.Equals(pattern, StringComparison.OrdinalIgnoreCase)));
            }

            // 4. Fall back to the first ID field
            if (primaryKey == null)
            {
                primaryKey = idFields.First();
            }

            return Utilities.ToCamelCase(primaryKey);
        }

        public static string RemoveIdSuffix(string? primaryKeyField)
        {
            if (string.IsNullOrWhiteSpace(primaryKeyField)) return string.Empty;

            string resourceBase = primaryKeyField;
            if (resourceBase.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
            {
                resourceBase = resourceBase.Substring(0, resourceBase.Length - 2);
            }

            return resourceBase;
        }

        public static string GetReportingFunctionName(ReportingEndpoint endpoint)
        {
            string baseName = endpoint.ResponseDataTypeName ?? "";

            if (string.IsNullOrWhiteSpace(baseName) || baseName == "any")
            {
                baseName = ToPascalCase(endpoint.ReportKey);
            }

            // Remove "Response" and "Record" suffixes
            if (baseName.EndsWith("Response", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - "Response".Length);
            if (baseName.EndsWith("Record", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - "Record".Length);

            // Add category suffix
            string categorySuffix = endpoint.Category.Equals("widget", StringComparison.OrdinalIgnoreCase)
                ? "WidgetReport"
                : "FullReport";

            return $"get{baseName}{categorySuffix}";
        }
    }
}
