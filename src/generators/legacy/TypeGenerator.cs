using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class TypeGenerator
    {
        public static void GenerateTypeFile(string resourceName, JsonObject? schemas, string outputDir)
        {
            if (schemas == null) return;

            var typeContent = GenerateInterfaces(resourceName, schemas);
            if (!string.IsNullOrEmpty(typeContent))
            {
                string fileName = $"{resourceName}.ts";
                string filePath = Path.Combine(outputDir, fileName);
                File.WriteAllText(filePath, typeContent);
            }
        }

        public static string GenerateInterfaces(string resourceName, JsonObject schemas)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();

            // Generate main interface - look for core model without wrappers
            var mainSchema = Utilities.FindSchema(schemas, $"{resourceName}RetrieveResponseModel") ??
                           Utilities.FindSchema(schemas, $"{resourceName}ResponseModel") ??
                           Utilities.FindSchema(schemas, resourceName);
            if (mainSchema != null)
            {
                sb.AppendLine($"export interface {resourceName} {{");
                GenerateInterfaceProperties(sb, mainSchema);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Generate Create interface - look for core model without wrappers
            var createSchema = Utilities.FindSchema(schemas, $"{resourceName}CreateRequestModel") ??
                             Utilities.FindSchema(schemas, $"{resourceName}CreateRequest") ??
                             Utilities.FindSchema(schemas, $"{resourceName}Create");
            if (createSchema != null)
            {
                sb.AppendLine($"export interface {resourceName}Create {{");
                GenerateInterfaceProperties(sb, createSchema);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            // Generate Update interface - look for core model without wrappers
            var updateSchema = Utilities.FindSchema(schemas, $"{resourceName}UpdateRequestModel") ??
                             Utilities.FindSchema(schemas, $"{resourceName}UpdateRequest") ??
                             Utilities.FindSchema(schemas, $"{resourceName}Update");
            if (updateSchema != null)
            {
                sb.AppendLine($"export interface {resourceName}Update {{");
                GenerateInterfaceProperties(sb, updateSchema);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string GenerateInterfacesForSchemas(IDictionary<string, string> schemaAliases, JsonObject schemas)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();

            foreach (var schemaName in schemaAliases.Keys.OrderBy(name => name))
            {
                var schema = Utilities.FindSchema(schemas, schemaName);
                if (schema == null) continue;

                var typeName = schemaAliases[schemaName];
                sb.AppendLine($"export interface {typeName} {{");
                GenerateInterfacePropertiesWithRefs(sb, schema, schemaAliases);
                sb.AppendLine("}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static void GenerateInterfaceProperties(StringBuilder sb, JsonObject schema, bool nullAsUnion = false)
        {
            var properties = schema["properties"]?.AsObject();
            if (properties == null) return;

            var required = schema["required"]?.AsArray()
                ?.Select(r => r?.GetValue<string>() ?? "")
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>();

            foreach (var prop in properties)
            {
                string propName = prop.Key;
                var propSchema = prop.Value?.AsObject();

                if (propSchema != null)
                {
                    string tsType = Utilities.GetTypeScriptType(propSchema);
                    string camelCaseProp = Utilities.ToCamelCase(propName);

                    bool isNullable = propSchema["nullable"]?.GetValue<bool>() ?? false;
                    bool isRequired = required.Contains(propName);

                    if (nullAsUnion)
                    {
                        // optional (not required) gets ?, nullable gets | null
                        string optionalMark = isRequired ? "" : "?";
                        string nullSuffix = isNullable ? " | null" : "";
                        sb.AppendLine($"  {camelCaseProp}{optionalMark}: {tsType}{nullSuffix};");
                    }
                    else
                    {
                        string optionalTag = isNullable ? "?" : "";
                        sb.AppendLine($"    {camelCaseProp}{optionalTag}: {tsType}");
                    }
                }
            }
        }

        private static void GenerateInterfacePropertiesWithRefs(
            StringBuilder sb,
            JsonObject schema,
            IDictionary<string, string> schemaAliases)
        {
            var properties = schema["properties"]?.AsObject();
            if (properties == null) return;

            foreach (var prop in properties)
            {
                string propName = prop.Key;
                var propSchema = prop.Value?.AsObject();

                if (propSchema != null)
                {
                    string tsType = GetTypeScriptTypeWithRefs(propSchema, schemaAliases);
                    string camelCaseProp = Utilities.ToCamelCase(propName);

                    bool isNullable = propSchema["nullable"]?.GetValue<bool>() ?? false;
                    string optionalTag = isNullable ? "?" : "";

                    sb.AppendLine($"    {camelCaseProp}{optionalTag}: {tsType}");
                }
            }
        }

        private static string GetTypeScriptTypeWithRefs(
            JsonObject propSchema,
            IDictionary<string, string> schemaAliases)
        {
            var refValue = propSchema["$ref"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(refValue))
            {
                var schemaName = Utilities.ExtractSchemaName(refValue);
                return schemaAliases.TryGetValue(schemaName, out var alias) ? alias : schemaName;
            }

            string? type = propSchema["type"]?.GetValue<string>();
            if (type == "array")
            {
                var itemsSchema = propSchema["items"]?.AsObject();
                if (itemsSchema != null)
                {
                    var itemRef = itemsSchema["$ref"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(itemRef))
                    {
                        var itemSchemaName = Utilities.ExtractSchemaName(itemRef);
                        var itemTypeName = schemaAliases.TryGetValue(itemSchemaName, out var alias)
                            ? alias
                            : itemSchemaName;
                        return $"{itemTypeName}[]";
                    }

                    string itemType = Utilities.GetTypeScriptType(itemsSchema);
                    return $"{itemType}[]";
                }

                return "any[]";
            }

            return Utilities.GetTypeScriptType(propSchema);
        }
    }
}
