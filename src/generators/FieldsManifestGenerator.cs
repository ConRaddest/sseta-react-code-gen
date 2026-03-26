using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates {portal}-fields.json for a portal's swagger spec.
//
// Fields in the manifest exactly mirror what the layout generators produce:
//   - Create/Update: uses GetOrderedFields (same pipeline as field hooks + create/update layouts)
//   - View:          uses BuildGroups with excludeFkFields:true (same pipeline as view layout)
//
// Output: src/_output/{portal}/{portal}-fields.json
static class FieldsManifestGenerator
{
    public static void Generate(JsonObject paths, JsonObject? schemas, JsonObject? fieldLayout, string outputPath)
    {
        // module (pascal) → resource → operation → fields in layout order
        var manifest = new SortedDictionary<string, SortedDictionary<string, Dictionary<string, List<string>>>>(StringComparer.Ordinal);

        // Collect one entry per (module, resource, operation) — last one wins if duplicates
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != "management") continue;

            string module   = parts[2];
            string resource = parts[3];
            string op       = parts[4];

            if (!string.Equals(op, "Create",   StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(op, "Update",   StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase))
                continue;

            string key = $"{module}|{resource}|{op}";
            if (!seen.Add(key)) continue;

            string modulePascal = Formatters.ToPascalCase(module.ToLower());
            var searchableResources = Formatters.BuildSearchableResources(paths, module);

            List<string>? fields = null;

            if (string.Equals(op, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var schema = Formatters.FindSchema(schemas, $"{resource}{module}CreateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{resource}CreateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{module}_{resource}CreateRequestModel");
                var properties = schema?["properties"]?.AsObject();
                fields = UseFieldsGenerator.GetOrderedFields(resource, fieldLayout, properties, searchableResources);
            }
            else if (string.Equals(op, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var schema = Formatters.FindSchema(schemas, $"{resource}{module}UpdateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{resource}UpdateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{module}_{resource}UpdateRequestModel");
                var properties = schema?["properties"]?.AsObject();
                fields = UseFieldsGenerator.GetOrderedFields(resource, fieldLayout, properties, searchableResources);
            }
            else // Retrieve → View
            {
                var (retrieveSchema, properties) = ResolveRetrieveProperties(pathNode.AsObject(), schemas, resource, module);
                if (properties != null)
                {
                    var groups = UseLayoutGenerator.BuildGroups(resource, fieldLayout, properties, excludeFkFields: true, searchableResources: searchableResources);
                    fields = groups.SelectMany(g => g.Fields.Select(f => f.Name)).ToList();
                }
            }

            if (fields == null || fields.Count == 0) continue;

            string outputOp = string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase) ? "View" : Formatters.ToPascalCase(op.ToLower());

            if (!manifest.ContainsKey(modulePascal))
                manifest[modulePascal] = new SortedDictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

            if (!manifest[modulePascal].ContainsKey(resource))
                manifest[modulePascal][resource] = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            manifest[modulePascal][resource][outputOp] = fields;
        }

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);

        int total = manifest.Values.Sum(m => m.Count);
        Console.WriteLine($"    {total} resource(s) written to {Path.GetFileName(outputPath)}");
    }

    static (JsonObject? schema, JsonObject? properties) ResolveRetrieveProperties(JsonObject pathNode, JsonObject? schemas, string resource, string module)
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

            return (schema, schema?["properties"]?.AsObject());
        }
        return (null, null);
    }
}
