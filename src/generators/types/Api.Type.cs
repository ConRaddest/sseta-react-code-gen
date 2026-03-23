using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates management-api.types.ts, grouping types in the same order and
// structure as the service file: Auth first, then each module section,
// with resource sub-comments separating each group.
//
// Type naming follows the same MODULE_ResourceOperation{Request|Response}
// convention used in the service generator.
//
// Skipped schemas (infrastructure / wrappers):
//   - *SETAApiResponse  (envelope wrappers)
//   - BooleanSETAApiResponse
//   - Filterbylist / Orderbylist  (covered by FetchRequest in @sseta/components)
static class ApiTypeGenerator
{
    static readonly HashSet<string> ExcludedTags = ["HealthCheck", "sso"];

    // Schemas that are part of the FetchRequest contract — no need to re-emit
    static readonly HashSet<string> SkippedSchemas = ["Filterbylist", "Orderbylist", "BooleanSETAApiResponse"];

    public static void Generate(JsonObject paths, JsonObject schemas, string templatePath, string outputPath)
    {
        // ---------------------------------------------------------------
        // 1. Walk paths in service order to collect type emit order
        // ---------------------------------------------------------------
        var authTypes = new List<TypeEntry>();

        // module → resource → types (preserving insertion order)
        var modules = new Dictionary<string, Dictionary<string, List<TypeEntry>>>();
        var moduleOrder = new List<string>();
        var resourceOrder = new Dictionary<string, List<string>>();

        var emitted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;

            foreach (var (_, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                var tag = opNode["tags"]?[0]?.GetValue<string>();
                if (tag == null || ExcludedTags.Contains(tag)) continue;

                var requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                var responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                string? requestSchemaName = requestRef?.Split('/').Last();
                string? responseSchemaName = responseRef?.Split('/').Last();

                bool isAuth = rawPath.StartsWith("/api/Auth/");

                if (isAuth)
                {
                    CollectTypes(requestSchemaName, responseSchemaName, schemas, emitted, authTypes, isAuth: true);
                }
                else
                {
                    // /api/management/{MODULE}/{Resource}/...
                    var parts = rawPath.TrimStart('/').Split('/');
                    if (parts.Length < 4) continue;
                    string module = parts[2];
                    string resource = parts[3];

                    if (!modules.ContainsKey(module))
                    {
                        modules[module] = new Dictionary<string, List<TypeEntry>>();
                        moduleOrder.Add(module);
                        resourceOrder[module] = new List<string>();
                    }

                    if (!modules[module].ContainsKey(resource))
                    {
                        modules[module][resource] = new List<TypeEntry>();
                        resourceOrder[module].Add(resource);
                    }

                    CollectTypes(requestSchemaName, responseSchemaName, schemas, emitted, modules[module][resource], isAuth: false);
                }
            }
        }

        // ---------------------------------------------------------------
        // 2. Render
        // ---------------------------------------------------------------
        var sb = new StringBuilder();

        // Auth section
        if (authTypes.Count > 0)
        {
            sb.AppendLine("// ---- Auth ----");
            sb.AppendLine();
            foreach (var t in authTypes)
                RenderType(sb, t, schemas, emitted);
        }

        // Module sections
        foreach (var module in moduleOrder)
        {
            sb.AppendLine($"// ======== {module} ========");
            sb.AppendLine();

            foreach (var resource in resourceOrder[module])
            {
                var types = modules[module][resource];
                if (types.Count == 0) continue;

                sb.AppendLine($"// -- {module} / {resource} --");
                sb.AppendLine();

                foreach (var t in types)
                    RenderType(sb, t, schemas, emitted);
            }
        }

        string template = File.ReadAllText(templatePath);
        string output = template.Replace("// [[TYPES]]", sb.ToString().TrimEnd());

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"  ✓ {Path.GetFileName(outputPath)}");
    }

    // ---------------------------------------------------------------
    // Collect request + response types for one endpoint into the list
    // ---------------------------------------------------------------

    static void CollectTypes(
        string? requestSchemaName,
        string? responseSchemaName,
        JsonObject schemas,
        HashSet<string> emitted,
        List<TypeEntry> list,
        bool isAuth)
    {
        // Request type — skip search requests (standard FetchRequest shape, covered by @sseta/components)
        bool isSearchRequest = requestSchemaName?.EndsWith("SearchRequestModel") ?? false;
        if (!string.IsNullOrEmpty(requestSchemaName) && !SkippedSchemas.Contains(requestSchemaName) && !isSearchRequest)
        {
            string tsName = Formatters.FormatTypeName(requestSchemaName);
            if (isAuth) tsName = Formatters.AddAuthPrefix(tsName);
            if (emitted.Add(tsName))
                list.Add(new TypeEntry(tsName, requestSchemaName, EntryKind.Direct));
        }

        if (string.IsNullOrEmpty(responseSchemaName)) return;

        // Response: unwrap the SETAApiResponse envelope to get the inner data schema
        string? innerName = UnwrapDataRef(responseSchemaName, schemas);
        if (string.IsNullOrEmpty(innerName)) return;

        string tsTName = Formatters.FormatTypeName(innerName);
        if (isAuth) tsTName = Formatters.AddAuthPrefix(tsTName);
        if (!emitted.Add(tsTName)) return;

        // Check if the inner schema is a search envelope (totalRows + searchResults)
        // If so, SearchResponse<T> from the component library covers the wrapper —
        // emit only the row model (T) so callers write SearchResponse<RowType>
        var innerSchema = Formatters.FindSchema(schemas, innerName);
        bool isSearchResponse = innerSchema?["properties"]?["searchResults"] != null
                             && innerSchema?["properties"]?["totalRows"] != null;

        if (isSearchResponse)
        {
            // Drop the envelope — SearchResponse<T> from @sseta/components covers totalRows + searchResults.
            // Emit only the row model as the T, named with "SearchResult" suffix to avoid collision
            // with the envelope name (which also resolves to "...SearchResponse" via FormatTypeName).
            emitted.Remove(tsTName);

            string? rowRef = innerSchema!["properties"]!["searchResults"]!["items"]?["$ref"]?.GetValue<string>();
            string? rowSchemaName = rowRef?.Split('/').Last();

            if (!string.IsNullOrEmpty(rowSchemaName))
            {
                // Strip "SearchResponse" suffix to get the bare resource name — matches the service output
                string tsRowName = Formatters.FormatTypeName(rowSchemaName);
                if (tsRowName.EndsWith("SearchResponse"))
                    tsRowName = tsRowName[..^"SearchResponse".Length];
                if (isAuth) tsRowName = Formatters.AddAuthPrefix(tsRowName);

                if (emitted.Add(tsRowName))
                    list.Add(new TypeEntry(tsRowName, rowSchemaName, EntryKind.Direct));
            }
        }
        else
        {
            list.Add(new TypeEntry(tsTName, innerName, EntryKind.Direct));
        }
    }

    // ---------------------------------------------------------------
    // Render a single type entry — also emits any inlined nested types
    // ---------------------------------------------------------------

    static void RenderType(StringBuilder sb, TypeEntry entry, JsonObject schemas, HashSet<string> emitted)
    {
        var schema = Formatters.FindSchema(schemas, entry.SchemaName);

        // Collect nested types that need to be emitted after this one
        var nested = new List<(string TsName, string SchemaName)>();

        sb.AppendLine($"export interface {entry.TsName} {{");
        if (schema != null)
            RenderProperties(sb, schema, schemas, entry.TsName, emitted, nested);
        sb.AppendLine("}");
        sb.AppendLine();

        // Emit nested types immediately after the parent
        foreach (var (nestedTsName, nestedSchemaName) in nested)
            RenderType(sb, new TypeEntry(nestedTsName, nestedSchemaName, EntryKind.Direct), schemas, emitted);
    }

    // ---------------------------------------------------------------
    // Property rendering
    // ---------------------------------------------------------------

    static void RenderProperties(
        StringBuilder sb,
        JsonObject schema,
        JsonObject allSchemas,
        string parentTsName,
        HashSet<string> emitted,
        List<(string TsName, string SchemaName)> nested)
    {
        var props = schema["properties"]?.AsObject();
        if (props == null) return;

        var requiredArray = schema["required"]?.AsArray();
        bool hasRequiredList = requiredArray != null;
        var required = requiredArray
            ?.Select(r => r?.GetValue<string>())
            .Where(r => r != null)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string?>();

        foreach (var (propName, propNode) in props)
        {
            if (propNode == null) continue;

            bool isNullable = propNode["nullable"]?.GetValue<bool>() ?? false;

            string optMark;
            string nullSuffix;

            if (hasRequiredList)
            {
                // Request models: all fields are always present
                // absent from required → | null (optional value, sent as null)
                // nullable → | null
                optMark = "";
                nullSuffix = (!required.Contains(propName) || isNullable) ? " | null" : "";
            }
            else
            {
                // Response models: API always sends every field, so no optional marker: ?
                //                  nullable fields are | null
                optMark = "";
                nullSuffix = isNullable ? " | null" : "";
            }
            string tsType = ResolvePropertyType(propNode.AsObject(), allSchemas, parentTsName, emitted, nested);
            sb.AppendLine($"  {propName}{optMark}: {tsType}{nullSuffix}");
        }
    }

    static string ResolvePropertyType(
        JsonObject prop,
        JsonObject allSchemas,
        string parentTsName,
        HashSet<string> emitted,
        List<(string TsName, string SchemaName)> nested)
    {
        // $ref — if the referenced type is not already emitted, nest it under the parent
        var refVal = prop["$ref"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(refVal))
        {
            string refSchemaName = refVal.Split('/').Last();
            string refTsName = Formatters.FormatTypeName(refSchemaName);

            if (!emitted.Contains(refTsName))
            {
                // Nest as {ParentTsName}{RefTsName}
                string nestedTsName = parentTsName + refTsName;
                if (emitted.Add(nestedTsName))
                    nested.Add((nestedTsName, refSchemaName));
                return nestedTsName;
            }

            return refTsName;
        }

        string? type = prop["type"]?.GetValue<string>();

        // array
        if (type == "array")
        {
            var itemRef = prop["items"]?["$ref"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(itemRef))
            {
                string refSchemaName = itemRef.Split('/').Last();
                string refTsName = Formatters.FormatTypeName(refSchemaName);

                if (!emitted.Contains(refTsName))
                {
                    string nestedTsName = parentTsName + refTsName;
                    if (emitted.Add(nestedTsName))
                        nested.Add((nestedTsName, refSchemaName));
                    return $"{nestedTsName}[]";
                }

                return $"{refTsName}[]";
            }

            string itemType = prop["items"]?["type"]?.GetValue<string>() ?? "unknown";
            return $"{Formatters.MapPrimitive(itemType)}[]";
        }

        // string enum
        var enumValues = prop["enum"]?.AsArray();
        if (enumValues != null)
            return string.Join(" | ", enumValues.Select(v => $"\"{v?.GetValue<string>()}\""));

        // date-time → string
        string? format = prop["format"]?.GetValue<string>();
        if (format == "date-time") return "string";

        return Formatters.MapPrimitive(type);
    }

    // ---------------------------------------------------------------
    // Schema helpers
    // ---------------------------------------------------------------

    static string? UnwrapDataRef(string wrapperSchemaName, JsonObject schemas)
    {
        var wrapper = Formatters.FindSchema(schemas, wrapperSchemaName);
        var dataRef = wrapper?["properties"]?["data"]?["$ref"]?.GetValue<string>();
        return dataRef?.Split('/').Last();
    }

    // ---------------------------------------------------------------
    // Data model
    // ---------------------------------------------------------------

    enum EntryKind { Direct }

    record TypeEntry(string TsName, string SchemaName, EntryKind Kind);
}
