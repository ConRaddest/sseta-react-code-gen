using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReactCodegen;

static class Formatters
{
    public static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;

        int firstLowerIndex = -1;
        for (int i = 0; i < str.Length; i++)
        {
            if (char.IsLower(str[i]))
            {
                firstLowerIndex = i;
                break;
            }
        }

        if (firstLowerIndex <= 1)
            return char.ToLower(str[0]) + str[1..];

        // Acronym: lowercase everything up to (but not including) the letter before firstLowerIndex
        return str[..(firstLowerIndex - 1)].ToLower() + str[(firstLowerIndex - 1)..];
    }

    public static string ToPascalCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToUpper(str[0]) + str[1..];
    }

    public static string ToKebabCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        string result = Regex.Replace(str, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "-");
        return result.ToLower();
    }

    public static string ToTitleCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        string spaced = Regex.Replace(str, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    public static string GetPluralName(string name)
    {
        if (name.EndsWith("y", StringComparison.OrdinalIgnoreCase))
            return name[..^1] + "ies";
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return name + "es";
        return name + "s";
    }

    public static bool StartsWithVowel(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        char first = char.ToLower(text[0]);
        return first is 'a' or 'e' or 'i' or 'o' or 'u';
    }

    public static string GetTypeScriptType(JsonObject propSchema)
    {
        string? type = propSchema["type"]?.GetValue<string>();
        string? format = propSchema["format"]?.GetValue<string>();

        return type switch
        {
            "integer" or "number" => "number",
            "boolean" => "boolean",
            "string" when format == "date-time" => "string",
            "string" => "string",
            "array" => "any[]",
            "guid" => "string",
            _ => "any"
        };
    }

    public static string NormalizeEnumKey(string name, string? idValue = null)
    {
        var withWordBreaks = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        var cleaned = Regex.Replace(withWordBreaks, @"[^A-Za-z0-9]+", " ").Trim();

        if (string.IsNullOrEmpty(cleaned))
            return !string.IsNullOrWhiteSpace(idValue) ? $"Value{idValue}" : "Value";

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var key = string.Concat(parts.Select(part =>
        {
            if (part.All(char.IsDigit)) return part;
            if (part.All(char.IsUpper) && part.Length <= 3) return part;
            return part.Length == 1
                ? part.ToUpper()
                : char.ToUpper(part[0]) + part[1..].ToLower();
        }));

        if (string.IsNullOrEmpty(key))
            return !string.IsNullOrWhiteSpace(idValue) ? $"Value{idValue}" : "Value";

        if (char.IsDigit(key[0]))
            key = $"Value{key}";

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

    public static string GetIdFieldName(string resourceName) =>
        $"{ToCamelCase(resourceName)}Id";

    public static string GetFieldHeading(string fieldName)
    {
        string name = fieldName;

        if (name.ToLower().EndsWith("id"))
            name = name[..^2];

        if (name.ToLower().EndsWith("name") &&
            !fieldName.ToLower().Contains("first") &&
            !fieldName.ToLower().Contains("last") &&
            !fieldName.ToLower().Contains("middle") &&
            name.ToLower() != "name")
        {
            name = name[..^4];
        }

        string titleCase = Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return string.IsNullOrEmpty(titleCase)
            ? titleCase
            : char.ToUpper(titleCase[0]) + titleCase[1..];
    }

    public static string ExtractSchemaName(string refValue)
    {
        var parts = refValue.Split('/');
        return parts.Length > 0 ? parts[^1] : refValue;
    }

    // ---------------------------------------------------------------
    // API type name formatter
    // ---------------------------------------------------------------

    // Known module tokens — ordered longest-first so "SPI" matches before "SP"
    public static readonly string[] Modules = ["ADMIN", "ACCESS", "PMVR", "SPI", "ECD", "SP", "B"];

    // Fields that are always excluded from generated form layouts, field hooks, and select hooks.
    // Status management is handled separately and should never appear in scaffolded forms.
    public static readonly HashSet<string> ExcludedFormFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "statusId",
        "statusReason",
    };

    // Fields excluded from view field hooks — audit/tracking fields that are never useful to display.
    public static readonly HashSet<string> ExcludedViewFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "modifiedOn",
        "createdOn",
        "deletedOn",
        "modifiedBySystemUserId",
        "createdBySystemUserId",
        "deletedBySystemUserId",
        "modifiedBySystemUserName",
        "createdBySystemUserName",
        "deletedBySystemUserName",
    };

    // Reformats a raw schema name into the ModuleResourceOperation{Request|Response} convention.
    // e.g. SystemUserSPUpdateRequestModel   → SpSystemUserUpdateRequest
    //      DepartmentTypeADMINSearchResponseModel → AdminDepartmentTypeSearchResponse
    //      StaffRoleRequestADMINRetrieveResponseModel → AdminStaffRoleRequest  (same as search row)
    public static string FormatTypeName(string name)
    {
        if (name == "Boolean") return "boolean";

        // Strip trailing "Model" only when it is NOT part of "ResponseModel" or "RequestModel"
        if (name.EndsWith("Model") && !name.EndsWith("ResponseModel") && !name.EndsWith("RequestModel"))
            name = name[..^"Model".Length];

        // Strip just "Model" from "ResponseModel" / "RequestModel" to get "Response" / "Request"
        if (name.EndsWith("ResponseModel"))
            name = name[..^"Model".Length];
        if (name.EndsWith("RequestModel"))
            name = name[..^"Model".Length];

        // RetrieveResponse is the canonical base model — strip the suffix so it becomes
        // the bare resource name (e.g. AdminStaffRoleRequest).
        // For auth types (no module prefix) the Response suffix is re-added after module
        // processing so the name stays consistent (e.g. AuthPersonAuthResponse).
        // SearchResponse is kept as a distinct type (e.g. AdminStaffRoleRequestSearchResponse).
        bool strippedRetrieveResponse = false;
        if (name.EndsWith("RetrieveResponse"))
        {
            name = name[..^"RetrieveResponse".Length];
            strippedRetrieveResponse = true;
        }

        string? foundModule = null;
        foreach (var mod in Modules)
        {
            int idx = name.IndexOf(mod, StringComparison.Ordinal);
            if (idx >= 0)
            {
                foundModule = mod;
                name = name[..idx] + name[(idx + mod.Length)..];
                break;
            }
        }

        if (foundModule != null)
            name = ToPascalCase(foundModule.ToLower()) + name;
        else if (strippedRetrieveResponse)
            name += "Response";

        return name;
    }

    // ---------------------------------------------------------------
    // Schema lookup
    // ---------------------------------------------------------------

    public static JsonObject? FindSchema(JsonObject? schemas, string name)
    {
        if (schemas == null || string.IsNullOrWhiteSpace(name)) return null;

        if (schemas[name] is JsonObject direct) return direct;

        foreach (var (key, val) in schemas)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && val is JsonObject obj)
                return obj;

        return null;
    }

    // Adds "Auth" prefix to types that have no module prefix (i.e. Auth section types).
    // Leaves "unknown" and "boolean" sentinel values unchanged.
    // Detects an existing module prefix by checking if the name starts with any known module (pascal-cased).
    public static string AddAuthPrefix(string name)
    {
        if (name == "unknown" || name == "boolean") return name;
        foreach (var mod in Modules)
            if (name.StartsWith(ToPascalCase(mod.ToLower()), StringComparison.Ordinal))
                return name; // already has a module prefix
        return $"Auth{name}";
    }

    // Maps a swagger primitive type string to its TypeScript equivalent.
    public static string MapPrimitive(string? type) => type switch
    {
        "integer" or "number" => "number",
        "boolean" => "boolean",
        "string" => "string",
        _ => "unknown"
    };

    // Derives a PascalCase export name from a kebab-case file stem.
    // e.g. "management-api.service.ts" → "Api"
    public static string DeriveExportName(string fileName)
    {
        string stem = fileName.Replace(".service.ts", "").Replace(".ts", "");
        return string.Concat(stem.Split('-').Select(ToPascalCase));
    }

    // Returns true when a property is a FK reference (ParentTable: description) whose parent
    // table has no Search endpoint — meaning no select can be populated for it.
    // Such fields should be excluded from field hooks, layouts, and selects entirely.
    public static bool IsUnsearchableFk(JsonObject? prop, HashSet<string>? searchableResources)
    {
        if (prop == null || searchableResources == null) return false;
        string? desc = prop["description"]?.GetValue<string>();
        if (string.IsNullOrEmpty(desc) || !desc.StartsWith("ParentTable:")) return false;
        string parentTable = desc["ParentTable:".Length..];
        return !searchableResources.Contains(parentTable);
    }

    // Returns the set of resource names that have a Search endpoint for the given module.
    // Used to filter FK fields — only include a select when the parent table is searchable.
    public static HashSet<string> BuildSearchableResources(JsonObject paths, string module, string apiPrefix = "management")
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawPath, _) in paths)
        {
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != apiPrefix) continue;
            if (!string.Equals(parts[2], module, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(parts[4], "Search", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(parts[3]);
        }
        return result;
    }

    // Indents every non-empty line in a block by extraSpaces additional spaces.
    public static string IndentLines(string block, int extraSpaces)
    {
        string indent = new string(' ', extraSpaces);
        return string.Join("\n", block.Split('\n').Select(l => l.Length > 0 ? indent + l : l));
    }

    // ---------------------------------------------------------------
    // Shared response/request type resolution (used by service + context generators)
    // ---------------------------------------------------------------

    // Unwraps a $ref schema name through the SETAApiResponse envelope and search envelope,
    // returning the final TypeScript type name ready for use in generated code.
    public static string ResolveResponseType(string? schemaRef, JsonObject? schemas)
    {
        if (schemaRef == null) return "unknown";

        string name = schemaRef.Split('/').Last();

        // Primitive-data envelope (e.g. BooleanSETAApiResponse) — resolve directly
        if (schemas != null && name.EndsWith("SETAApiResponse"))
        {
            var wrapperSchema = FindSchema(schemas, name);
            var dataProp = wrapperSchema?["properties"]?["data"];
            if (dataProp != null && dataProp["$ref"] == null)
            {
                string? primitiveType = dataProp["type"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(primitiveType))
                    return MapPrimitive(primitiveType);
            }
        }

        // Unwrap outer SETAApiResponse envelope
        if (name.EndsWith("SETAApiResponse"))
            name = name[..^"SETAApiResponse".Length];

        // ValidateResponse → static type from @sseta/components
        if (name.EndsWith("ValidateResponseModel") || name.EndsWith("ValidateResponse"))
            return "ValidateResponse";

        // Search envelope — return the row model type
        if (schemas != null)
        {
            var innerSchema = FindSchema(schemas, name);
            bool isSearchEnvelope = innerSchema?["properties"]?["searchResults"] != null
                                 && innerSchema?["properties"]?["totalRows"] != null;
            if (isSearchEnvelope)
            {
                string? rowRef = innerSchema!["properties"]!["searchResults"]!["items"]?["$ref"]?.GetValue<string>();
                string? rowSchemaName = rowRef?.Split('/').Last();
                if (!string.IsNullOrEmpty(rowSchemaName))
                    return FormatTypeName(rowSchemaName);
            }
        }

        return FormatTypeName(name);
    }

    // Resolves a request $ref to its TypeScript type name.
    public static string ResolveRequestType(string? schemaRef)
    {
        if (schemaRef == null) return "unknown";
        return FormatTypeName(schemaRef.Split('/').Last());
    }

    // Builds the ordered list of layout groups for a resource, driven by field-layout.json.
    // Fields not covered by the layout are appended in an "Additional Fields" group.
    public static List<LayoutGroup> BuildLayoutGroups(string resource, JsonObject? fieldLayout, JsonObject? properties, bool excludeFkFields = false, HashSet<string>? searchableResources = null, HashSet<string>? extraExclusions = null)
    {
        var groups = new List<LayoutGroup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pkField = GetIdFieldName(resource);

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
                    if (ExcludedFormFields.Contains(name)) continue;
                    if (extraExclusions != null && extraExclusions.Contains(name)) continue;
                    if (name.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                    if (excludeFkFields && name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) continue;
                    if (properties != null && !properties.ContainsKey(name)) continue;
                    if (IsUnsearchableFk(properties?[name]?.AsObject(), searchableResources))
                    {
                        if (excludeFkFields) Console.WriteLine($"    ⚠ {resource}.{name} — excluded from view (no search endpoint for parent table)");
                        continue;
                    }

                    string heading = GetFieldHeading(name);
                    string? type = properties != null ? GetLayoutType(name, properties[name]?.AsObject(), searchableResources) : null;

                    fields.Add(new LayoutField(name, columns, heading, type));
                    seen.Add(name);
                }

                if (fields.Count > 0)
                    groups.Add(new LayoutGroup(groupName, totalColumns, fields));
            }
        }

        // Append remaining schema fields not covered by the layout, with default ordering.
        if (properties != null)
        {
            var remaining = new List<(LayoutField Field, int Order)>();
            foreach (var (key, val) in properties)
            {
                if (seen.Contains(key)) continue;
                if (ExcludedFormFields.Contains(key)) continue;
                if (extraExclusions != null && extraExclusions.Contains(key)) continue;
                if (key.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                if (excludeFkFields && key.EndsWith("Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsUnsearchableFk(val?.AsObject(), searchableResources))
                {
                    if (excludeFkFields) Console.WriteLine($"    ⚠ {resource}.{key} — excluded from view (no search endpoint for parent table)");
                    continue;
                }
                string heading = GetFieldHeading(key);
                string? type = GetLayoutType(key, val?.AsObject(), searchableResources);

                // Ordering: name/title first, then selects, then text/number, then dates, then textareas, then booleans
                string lower = key.ToLower();
                int order = type switch
                {
                    "select"   => 1,
                    "date"     => 3,
                    "datetime" => 3,
                    "textarea" => 4,
                    "checkbox" => 5,
                    _          => 2,
                };
                if (lower.Contains("name") || lower.Contains("title")) order = 0;

                int columns = 2;

                remaining.Add((new LayoutField(key, columns, heading, type), order));
            }

            if (remaining.Count > 0)
            {
                var sorted = remaining.OrderBy(x => x.Order).Select(x => x.Field).ToList();
                groups.Add(new LayoutGroup("Additional Fields", 2, sorted));
            }
        }

        return groups;
    }

    // Returns the layout type string for a field, or null for plain text (omitted in output).
    public static string? GetLayoutType(string fieldName, JsonObject? prop, HashSet<string>? searchableResources = null)
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
        if (lower.Contains("identitynumber") || lower.Contains("idnumber")) return null;

        return type switch
        {
            "integer" or "number" => "number",
            "string" when format == "date-time" => "datetime",
            "string" when format == "date" => "date",
            "string" when maxLength > 500 => "textarea",
            "boolean" => "checkbox",
            _ => null
        };
    }
}

public record LayoutGroup(string GroupName, int TotalColumns, List<LayoutField> Fields);
public record LayoutField(string Name, int Columns, string Heading, string? Type);
