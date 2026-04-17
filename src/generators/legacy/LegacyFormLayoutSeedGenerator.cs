using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy;

// Seeds input/legacy/fieldTemplates.json with missing fields from Swagger schemas.
//
// For each resource that has a CreateRequestModel:
//   - If an entry already exists, missing schema fields are appended to an
//     "Additional Fields" group (preserving all existing config).
//   - If no entry exists, a new "Additional Fields" group is created with all
//     schema fields.
//
// Existing groups and field order are never modified — only new fields are added.
static class LegacyFormLayoutSeedGenerator
{
    public static void Generate(JsonObject? schemas, string fieldTemplatesPath)
    {
        if (schemas == null) return;

        // Load existing templates (keyed by ResourceName)
        var layout = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        if (File.Exists(fieldTemplatesPath))
        {
            var existing = JsonNode.Parse(File.ReadAllText(fieldTemplatesPath))?.AsObject();
            if (existing != null)
                foreach (var (key, val) in existing)
                    if (val is JsonArray arr)
                        layout[key] = arr;
        }

        bool dirty = false;

        foreach (var (schemaKey, schemaNode) in schemas)
        {
            if (!schemaKey.EndsWith("CreateRequestModel", StringComparison.OrdinalIgnoreCase)) continue;

            string resource = schemaKey[..^"CreateRequestModel".Length];
            var properties = schemaNode?["properties"]?.AsObject();
            if (properties == null || properties.Count == 0) continue;

            // Collect field names already configured across all groups
            var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (layout.TryGetValue(resource, out var existingGroups))
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

            // Find schema fields not yet in the layout
            var missing = new List<string>();
            foreach (var (fieldName, _) in properties)
            {
                if (!configured.Contains(fieldName))
                    missing.Add(fieldName);
            }

            if (missing.Count == 0) continue;

            dirty = true;

            var newFieldNodes = missing.Select(name => (JsonNode)new JsonObject
            {
                ["name"]    = JsonValue.Create(name),
                ["columns"] = JsonValue.Create(2)
            }).ToList();

            if (layout.TryGetValue(resource, out var groups))
            {
                // Append to existing "Additional Fields" group if present, otherwise add new group
                JsonObject? additionalGroup = null;
                foreach (var groupNode in groups)
                {
                    if (string.Equals(groupNode?["groupName"]?.GetValue<string>(), "Additional Fields", StringComparison.OrdinalIgnoreCase))
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
                layout[resource] = new JsonArray { BuildAdditionalGroup(newFieldNodes) };
            }

            Console.WriteLine($"    + {resource}: {missing.Count} field(s) added");
        }

        if (!dirty)
        {
            Console.WriteLine($"    layout up-to-date, nothing to seed");
            return;
        }

        var sorted = new SortedDictionary<string, JsonArray>(layout, StringComparer.Ordinal);

        // Round-trip through string to avoid "node already has a parent" when nodes
        // from the parsed existing file are re-used in a new JsonObject.
        var opts = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(
            sorted.ToDictionary(kvp => kvp.Key, kvp => JsonNode.Parse(kvp.Value.ToJsonString())),
            opts);

        Directory.CreateDirectory(Path.GetDirectoryName(fieldTemplatesPath)!);
        File.WriteAllText(fieldTemplatesPath, json);
        Console.WriteLine($"    Seeded: {fieldTemplatesPath}");
    }

    static JsonObject BuildAdditionalGroup(List<JsonNode> fieldNodes)
    {
        var fieldsArr = new JsonArray();
        foreach (var node in fieldNodes)
            fieldsArr.Add(node);

        return new JsonObject
        {
            ["groupName"]    = JsonValue.Create("Additional Fields"),
            ["totalColumns"] = JsonValue.Create(2),
            ["fields"]       = fieldsArr
        };
    }
}
