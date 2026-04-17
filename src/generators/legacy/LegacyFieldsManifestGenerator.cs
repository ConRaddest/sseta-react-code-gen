using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy;

// Generates legacy-fields.json — a flat manifest of "Resource": [field, ...] entries.
//
// Mirrors what FieldHooksGenerator produces: fields ordered by fieldTemplates.json,
// with any remaining schema fields appended. Only covers resources that have a
// CreateRequestModel (same filter as field hook generation).
//
// Output: input/fields-manifest/legacy-fields.json
static class LegacyFieldsManifestGenerator
{
    public static void Generate(JsonObject? schemas, string fieldTemplatesPath, string outputPath)
    {
        if (schemas == null) return;

        JsonObject? fieldTemplates = null;
        if (File.Exists(fieldTemplatesPath))
            fieldTemplates = JsonNode.Parse(File.ReadAllText(fieldTemplatesPath))?.AsObject();

        // "Resource" → ordered field names
        var manifest = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (schemaKey, schemaNode) in schemas)
        {
            // Only process CreateRequestModel schemas
            if (!schemaKey.EndsWith("CreateRequestModel", StringComparison.OrdinalIgnoreCase)) continue;

            string resource = schemaKey[..^"CreateRequestModel".Length];
            var properties = schemaNode?["properties"]?.AsObject();
            if (properties == null || properties.Count == 0) continue;

            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Respect fieldTemplates order for this resource
            if (fieldTemplates != null && fieldTemplates[resource] is JsonArray groups)
            {
                foreach (var groupNode in groups)
                {
                    var fields = groupNode?["fields"]?.AsArray();
                    if (fields == null) continue;
                    foreach (var fieldNode in fields)
                    {
                        string? name = fieldNode?["name"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (!properties.ContainsKey(name)) continue;
                        if (seen.Add(name)) ordered.Add(name);
                    }
                }
            }

            // Append remaining schema fields not covered by the template
            foreach (var (fieldName, _) in properties)
                if (seen.Add(fieldName)) ordered.Add(fieldName);

            if (ordered.Count == 0) continue;
            manifest[resource] = ordered;
        }

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"    {manifest.Count} resource(s) written to {Path.GetFileName(outputPath)}");
    }
}
