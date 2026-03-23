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
            "boolean"             => "boolean",
            "string" when format == "date-time" => "string",
            "string"              => "string",
            "array"               => "any[]",
            "guid"                => "string",
            _                     => "any"
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
            "string"              => "\"\"",
            "integer" or "number" => "0",
            "boolean"             => "false",
            _                     => "undefined"
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
    public static readonly string[] Modules = ["ADMIN", "PMVR", "SPI", "ECD", "SP", "B"];

    // Reformats a raw schema name into the MODULE_ResourceOperation{Request|Response} convention.
    // e.g. SystemUserSPUpdateRequestModel → SP_SystemUserUpdateRequest
    //      DepartmentTypeADMINSearchResponseModel → ADMIN_DepartmentTypeSearchResponse
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
            name = $"{foundModule}_{name}";

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

    // Adds AUTH_ prefix to types that have no module prefix (i.e. Auth section types).
    // Leaves "unknown" and "boolean" sentinel values unchanged.
    public static string AddAuthPrefix(string name)
    {
        if (name == "unknown" || name == "boolean") return name;
        if (name.Contains('_')) return name; // already has a module prefix
        return $"AUTH_{name}";
    }

    // Maps a swagger primitive type string to its TypeScript equivalent.
    public static string MapPrimitive(string? type) => type switch
    {
        "integer" or "number" => "number",
        "boolean"             => "boolean",
        "string"              => "string",
        _                     => "unknown"
    };

    // Derives a PascalCase export name from a kebab-case file stem.
    // e.g. "management-api.service.ts" → "ManagementApi"
    public static string DeriveExportName(string fileName)
    {
        string stem = fileName.Replace(".service.ts", "").Replace(".ts", "");
        return string.Concat(stem.Split('-').Select(ToPascalCase));
    }

    // Indents every non-empty line in a block by extraSpaces additional spaces.
    public static string IndentLines(string block, int extraSpaces)
    {
        string indent = new string(' ', extraSpaces);
        return string.Join("\n", block.Split('\n').Select(l => l.Length > 0 ? indent + l : l));
    }
}
