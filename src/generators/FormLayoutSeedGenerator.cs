using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Seeds the form layout JSON file with missing fields from Swagger schemas.
//
// For each Module.Resource.Operation discovered in Swagger:
//   - If an entry already exists in the layout, missing schema fields are
//     appended to an "Additional Fields" group (preserving all existing config).
//   - If no entry exists, a new "Additional Fields" group is created with all
//     schema fields in default order.
//
// Existing groups and field order are never modified — only new fields are added.
// The file is written back in the flat "Module.Resource.Operation" key format.
static class FormLayoutSeedGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        string layoutPath,
        HashSet<string>? blacklist = null,
        HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];

        // Load existing layout (flat format)
        var layout = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        if (File.Exists(layoutPath))
        {
            var existing = JsonNode.Parse(File.ReadAllText(layoutPath))?.AsObject();
            if (existing != null)
                foreach (var (key, val) in existing)
                    if (val is JsonArray arr)
                        layout[key] = arr;
        }

        bool dirty = false;

        // Enumerate Swagger endpoints — one pass collects all (module, resource, op) triples
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;

            string module   = parts[2];
            string resource = parts[3];
            string op       = parts[4];

            if (!string.Equals(op, "Create",   StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(op, "Update",   StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase))
                continue;

            string normalizedOp = string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase) ? "View" : Formatters.ToPascalCase(op.ToLower());
            string modulePascal = Formatters.ToPascalCase(module.ToLower());

            if (blacklist != null && (blacklist.Contains($"{module}.{resource}") || blacklist.Contains($"{module}.{resource}.{normalizedOp}")))
                continue;

            string dedupeKey = $"{module}|{resource}|{normalizedOp}";
            if (!seen.Add(dedupeKey)) continue;

            string layoutKey = $"{modulePascal}.{resource}.{normalizedOp}";

            // Resolve schema properties for this operation
            JsonObject? properties = ResolveProperties(pathNode.AsObject(), schemas, resource, module, normalizedOp);
            if (properties == null || properties.Count == 0) continue;

            string pkField = Formatters.GetIdFieldName(resource);

            // Collect field names already present in the layout for this key
            var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (layout.TryGetValue(layoutKey, out var existingGroups))
            {
                foreach (var groupNode in existingGroups)
                {
                    var fields = groupNode?["fields"]?.AsArray();
                    if (fields == null) continue;
                    foreach (var fieldNode in fields)
                    {
                        string? name = fieldNode?["name"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(name)) configured.Add(name);
                    }
                }
            }

            // Build the list of schema fields not yet in the layout
            var missing = new List<string>();
            foreach (var (fieldName, _) in properties)
            {
                if (configured.Contains(fieldName)) continue;
                if (Formatters.ExcludedFormFields.Contains(fieldName)) continue;
                if (fieldName.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                missing.Add(fieldName);
            }

            if (missing.Count == 0) continue;

            dirty = true;

            // Build new field nodes with default column width
            var newFieldNodes = missing.Select(name =>
            {
                var node = new JsonObject
                {
                    ["name"] = JsonValue.Create(name),
                    ["columns"] = JsonValue.Create(2)
                };
                return (JsonNode)node;
            }).ToList();

            if (layout.TryGetValue(layoutKey, out var groups))
            {
                // Append to existing "Additional Fields" group if present, otherwise add new group
                JsonObject? additionalGroup = null;
                foreach (var groupNode in groups)
                {
                    string? groupName = groupNode?["groupName"]?.GetValue<string>();
                    if (string.Equals(groupName, "Additional Fields", StringComparison.OrdinalIgnoreCase))
                    {
                        additionalGroup = groupNode?.AsObject();
                        break;
                    }
                }

                if (additionalGroup != null)
                {
                    if (additionalGroup["fields"] is JsonArray fieldsArr)
                    {
                        foreach (var node in newFieldNodes)
                            fieldsArr.Add(node);
                    }
                    else
                    {
                        var newArr = new JsonArray();
                        foreach (var node in newFieldNodes)
                            newArr.Add(node);
                        additionalGroup["fields"] = newArr;
                    }
                }
                else
                {
                    groups.Add(BuildAdditionalGroup(newFieldNodes));
                }
            }
            else
            {
                // No entry yet — create one with a single "Additional Fields" group
                layout[layoutKey] = new JsonArray { BuildAdditionalGroup(newFieldNodes) };
            }

            Console.WriteLine($"    + {layoutKey}: {missing.Count} field(s) added");
        }

        if (!dirty)
        {
            Console.WriteLine($"    layout up-to-date, nothing to seed");
            return;
        }

        // Write back sorted by key.
        // Round-trip through string to avoid "node already has a parent" when nodes
        // from the parsed existing file are re-used in a new JsonObject.
        var sorted = new SortedDictionary<string, JsonArray>(layout, StringComparer.Ordinal);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(
            sorted.ToDictionary(kvp => kvp.Key, kvp => JsonNode.Parse(kvp.Value.ToJsonString())),
            opts);

        Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
        File.WriteAllText(layoutPath, json);
        Console.WriteLine($"    Seeded: {layoutPath}");
    }

    static JsonObject BuildAdditionalGroup(List<JsonNode> fieldNodes)
    {
        var fieldsArr = new JsonArray();
        foreach (var node in fieldNodes)
            fieldsArr.Add(node);

        return new JsonObject
        {
            ["groupName"]     = JsonValue.Create("Additional Fields"),
            ["totalColumns"]  = JsonValue.Create(2),
            ["fields"]        = fieldsArr
        };
    }

    static JsonObject? ResolveProperties(JsonObject pathNode, JsonObject? schemas, string resource, string module, string normalizedOp)
    {
        if (normalizedOp == "View")
        {
            foreach (var (_, opNode) in pathNode)
            {
                if (opNode == null) continue;
                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                if (responseRef == null) continue;
                string responseType = Formatters.ResolveResponseType(responseRef, schemas);
                var schema = Formatters.FindSchema(schemas, responseType)
                          ?? Formatters.FindSchema(schemas, $"{resource}{module}RetrieveResponseModel")
                          ?? Formatters.FindSchema(schemas, $"{resource}RetrieveResponseModel");
                return schema?["properties"]?.AsObject();
            }
            return null;
        }

        var requestSchema = normalizedOp == "Create"
            ? Formatters.FindSchema(schemas, $"{resource}{module}CreateRequestModel")
              ?? Formatters.FindSchema(schemas, $"{resource}CreateRequestModel")
              ?? Formatters.FindSchema(schemas, $"{module}_{resource}CreateRequestModel")
            : Formatters.FindSchema(schemas, $"{resource}{module}UpdateRequestModel")
              ?? Formatters.FindSchema(schemas, $"{resource}UpdateRequestModel")
              ?? Formatters.FindSchema(schemas, $"{module}_{resource}UpdateRequestModel");

        return requestSchema?["properties"]?.AsObject();
    }
}
