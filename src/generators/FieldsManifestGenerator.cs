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
    public static void Generate(JsonObject paths, JsonObject? schemas, JsonObject? fieldLayout, string outputPath, HashSet<string>? blacklist = null, HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        // "Module.Resource.Operation" → fields in layout order
        var manifest = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        // Collect one entry per (module, resource, operation) — last one wins if duplicates
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

            // Map Retrieve → View to match blacklist convention used by form generators
            string blacklistOp = string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase) ? "View" : op;
            if (blacklist != null && (blacklist.Contains($"{module}.{resource}") || blacklist.Contains($"{module}.{resource}.{blacklistOp}")))
                continue;

            string key = $"{module}|{resource}|{op}";
            if (!seen.Add(key)) continue;

            string modulePascal = Formatters.ToPascalCase(module.ToLower());
            var searchableResources = Formatters.BuildSearchableResources(paths, module, apiPrefixes);

            List<string>? fields = null;

            string outputOp = string.Equals(op, "Retrieve", StringComparison.OrdinalIgnoreCase) ? "View" : Formatters.ToPascalCase(op.ToLower());
            string layoutKey = $"{modulePascal}.{resource}.{outputOp}";

            if (string.Equals(op, "Create", StringComparison.OrdinalIgnoreCase))
            {
                var schema = Formatters.FindSchema(schemas, $"{resource}{module}CreateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{resource}CreateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{module}_{resource}CreateRequestModel");
                var properties = schema?["properties"]?.AsObject();
                fields = UseFieldsGenerator.GetOrderedFields(resource, fieldLayout, properties, searchableResources, layoutKey);
            }
            else if (string.Equals(op, "Update", StringComparison.OrdinalIgnoreCase))
            {
                var schema = Formatters.FindSchema(schemas, $"{resource}{module}UpdateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{resource}UpdateRequestModel")
                          ?? Formatters.FindSchema(schemas, $"{module}_{resource}UpdateRequestModel");
                var properties = schema?["properties"]?.AsObject();
                fields = UseFieldsGenerator.GetOrderedFields(resource, fieldLayout, properties, searchableResources, layoutKey);
            }
            else // Retrieve → View
            {
                var (retrieveSchema, properties) = ResolveRetrieveProperties(pathNode.AsObject(), schemas, resource, module);
                if (properties != null)
                {
                    var groups = Formatters.BuildLayoutGroups(resource, fieldLayout, properties, excludeFkFields: true, searchableResources: searchableResources, layoutKey: layoutKey);
                    fields = groups.SelectMany(g => g.Fields.Select(f => f.Name)).ToList();
                }
            }

            if (fields == null || fields.Count == 0) continue;

            manifest[layoutKey] = fields;
        }

        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);

        int total = manifest.Count;
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
