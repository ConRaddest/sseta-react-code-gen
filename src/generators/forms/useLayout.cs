using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates {Prefix}CreateLayout.ts for every resource that has a Create endpoint.
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/create/{Prefix}CreateLayout.ts
//
// The layout is driven by field-layout.json. Fields not in the layout are appended in
// an "Additional Fields" group at the end.
static class UseLayoutGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        JsonObject? fieldLayout,
        string formsOutputDir)
    {
        var createEndpoints = new List<(string Module, string Resource, string RequestType)>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != "management") continue;
            if (!string.Equals(parts[4], "Create", StringComparison.OrdinalIgnoreCase)) continue;

            string module = parts[2];
            string resource = parts[3];

            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (method != "post" || opNode == null) continue;
                string? requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string requestType = requestRef != null ? Formatters.FormatTypeName(requestRef.Split('/').Last()) : $"{module}_{resource}CreateRequest";
                createEndpoints.Add((module, resource, requestType));
            }
        }

        int count = 0;
        foreach (var (module, resource, _) in createEndpoints)
        {
            string modulePascal = Formatters.ToPascalCase(module.ToLower());
            string prefix = modulePascal + resource;
            string kebabResource = Formatters.ToKebabCase(resource);
            string dir = Path.Combine(formsOutputDir, module.ToLower(), kebabResource, "create");
            Directory.CreateDirectory(dir);

            // Resolve properties from the Create request schema so we can derive headings/types
            var requestSchema = Formatters.FindSchema(schemas, $"{resource}{module}CreateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{resource}CreateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{module}_{resource}CreateRequestModel");

            var properties = requestSchema?["properties"]?.AsObject();

            var searchableResources = Formatters.BuildSearchableResources(paths, module);
            string content = RenderLayout(prefix, resource, fieldLayout, properties, searchableResources);
            File.WriteAllText(Path.Combine(dir, $"{prefix}CreateLayout.ts"), content);

            Console.WriteLine($"    ✓ {module}/{resource}");
            count++;
        }

        Console.WriteLine($"    {count} layout(s) generated.");
    }

    static string RenderLayout(string prefix, string resource, JsonObject? fieldLayout, JsonObject? properties, HashSet<string>? searchableResources = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("import { FormLayout } from \"@sseta/components\"");
        sb.AppendLine();
        sb.AppendLine($"const {prefix}CreateLayout: FormLayout[] = [");

        var groups = BuildGroups(resource, fieldLayout, properties, searchableResources: searchableResources);

        foreach (var group in groups)
        {
            sb.AppendLine("  {");
            sb.AppendLine($"    groupName: \"{group.GroupName}\",");
            sb.AppendLine($"    totalColumns: {group.TotalColumns},");
            sb.AppendLine("    fields: [");

            foreach (var field in group.Fields)
            {
                string typeFragment = field.Type != null ? $", type: \"{field.Type}\"" : "";
                sb.AppendLine($"      {{ name: \"{field.Name}\", columns: {field.Columns}, heading: \"{field.Heading}\"{typeFragment} }},");
            }

            sb.AppendLine("    ],");
            sb.AppendLine("  },");
        }

        sb.AppendLine("]");
        sb.AppendLine();
        sb.AppendLine($"export default {prefix}CreateLayout");

        return sb.ToString();
    }

    internal static List<LayoutGroup> BuildGroups(string resource, JsonObject? fieldLayout, JsonObject? properties, bool excludeFkFields = false, HashSet<string>? searchableResources = null)
    {
        var groups = new List<LayoutGroup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pkField = Formatters.GetIdFieldName(resource);

        if (fieldLayout != null && fieldLayout[resource] is JsonArray layoutGroups)
        {
            foreach (var groupNode in layoutGroups)
            {
                if (groupNode == null) continue;
                string groupName = groupNode["groupName"]?.GetValue<string>() ?? "";
                int totalColumns = groupNode["totalColumns"]?.GetValue<int>() ?? 2;
                var fieldsArray = groupNode["fields"]?.AsArray();
                if (fieldsArray == null) continue;

                var fields = new List<LayoutField>();
                foreach (var fieldNode in fieldsArray)
                {
                    string? name = fieldNode?["name"]?.GetValue<string>();
                    int columns = fieldNode?["columns"]?.GetValue<int>() ?? 1;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (Formatters.ExcludedFormFields.Contains(name)) continue;
                    if (name.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                    if (excludeFkFields && name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (properties != null && !properties.ContainsKey(name)) continue;
                    if (Formatters.IsUnsearchableFk(properties?[name]?.AsObject(), searchableResources))
                    {
                        if (excludeFkFields) Console.WriteLine($"    ⚠ {resource}.{name} — excluded from view (no search endpoint for parent table)");
                        continue;
                    }

                    string heading = Formatters.GetFieldHeading(name);
                    string? type = properties != null ? GetLayoutType(name, properties[name]?.AsObject(), searchableResources) : null;

                    fields.Add(new LayoutField(name, columns, heading, type));
                    seen.Add(name);
                }

                if (fields.Count > 0)
                    groups.Add(new LayoutGroup(groupName, totalColumns, fields));
            }
        }

        // Append remaining schema fields not covered by the layout
        if (properties != null)
        {
            var remaining = new List<LayoutField>();
            foreach (var (key, val) in properties)
            {
                if (seen.Contains(key)) continue;
                if (Formatters.ExcludedFormFields.Contains(key)) continue;
                if (key.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                if (excludeFkFields && key.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (Formatters.IsUnsearchableFk(val?.AsObject(), searchableResources))
                {
                    if (excludeFkFields) Console.WriteLine($"    ⚠ {resource}.{key} — excluded from view (no search endpoint for parent table)");
                    continue;
                }
                string heading = Formatters.GetFieldHeading(key);
                string? type = GetLayoutType(key, val?.AsObject(), searchableResources);
                remaining.Add(new LayoutField(key, 1, heading, type));
            }

            if (remaining.Count > 0)
                groups.Add(new LayoutGroup("Additional Fields", 2, remaining));
        }

        return groups;
    }

    // Returns the type string to include in the layout entry, or null for plain text fields.
    // Only non-default types are emitted (matching the example format).
    internal static string? GetLayoutType(string fieldName, JsonObject? prop, HashSet<string>? searchableResources = null)
    {
        if (prop == null) return null;

        string lower = fieldName.ToLower();
        string? desc = prop["description"]?.GetValue<string>();
        string? type = prop["type"]?.GetValue<string>();
        string? format = prop["format"]?.GetValue<string>();
        int? maxLength = prop["maxLength"]?.GetValue<int>();

        if (!string.IsNullOrEmpty(desc) && desc.StartsWith("ParentTable:"))
        {
            string parentTable = desc["ParentTable:".Length..];
            if (searchableResources == null || searchableResources.Contains(parentTable)) return "select";
        }
        if (!string.IsNullOrEmpty(desc) && desc.StartsWith("FieldType:Currency")) return "currency";
        if (lower.Contains("mobilenumber") || lower.Contains("phone")) return "phone";
        if (lower.Contains("identitynumber") || lower.Contains("idnumber")) return null; // renders as text

        return type switch
        {
            "integer" or "number" => "number",
            "string" when format == "date-time" => "datetime",
            "string" when format == "date" => "date",
            "string" when maxLength > 500 => "textarea",
            "boolean" => "checkbox",
            _ => null // text — omit type field
        };
    }

    internal record LayoutGroup(string GroupName, int TotalColumns, List<LayoutField> Fields);
    internal record LayoutField(string Name, int Columns, string Heading, string? Type);
}
