using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Parses all swagger paths and generates a single api.service.ts file.
//
// URL convention assumed:
//   /api/Auth/{Operation}                              → Auth group (flat)
//   /api/{apiPrefix}/{MODULE}/{Resource}/{Operation}   → module → resource groups
//   /api/{apiPrefix}/{MODULE}/{Resource}/{Operation}/{id}
//
// Excluded tags: HealthCheck, sso
static class ApiServiceGenerator
{
    static readonly HashSet<string> ExcludedTags = ["HealthCheck", "sso"];

    // Auth paths that are handled externally and should not be generated
    static readonly HashSet<string> ExcludedPaths = ["/api/Auth/sso/login", "/api/Auth/sso/callback"];

    public static void Generate(JsonObject paths, JsonObject? schemas, string templatePath, string outputPath, HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        // ---------------------------------------------------------------
        // 1. Parse every path into a structured endpoint list
        // ---------------------------------------------------------------
        var endpoints = new List<Endpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;

            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                var tag = opNode["tags"]?[0]?.GetValue<string>();
                if (tag == null || ExcludedTags.Contains(tag) || ExcludedPaths.Contains(rawPath)) continue;

                var requestSchema = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                var responseSchema = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                // Extract path parameters e.g. {id}
                var pathParams = new List<string>();
                var paramNodes = opNode["parameters"]?.AsArray();
                if (paramNodes != null)
                {
                    foreach (var p in paramNodes)
                    {
                        if (p?["in"]?.GetValue<string>() == "path")
                            pathParams.Add(p["name"]!.GetValue<string>());
                    }
                }

                endpoints.Add(new Endpoint(
                    RawPath: rawPath,
                    Method: method.ToLower(),
                    Tag: tag,
                    RequestSchemaRef: requestSchema,
                    ResponseSchemaRef: responseSchema,
                    PathParams: pathParams
                ));
            }
        }

        // ---------------------------------------------------------------
        // 2. Group: Auth (flat) vs module/resource
        // ---------------------------------------------------------------
        var authEndpoints = endpoints.Where(e => e.RawPath.StartsWith("/api/Auth/")).ToList();

        // module → resource → endpoints
        var modules = new SortedDictionary<string, SortedDictionary<string, List<Endpoint>>>(StringComparer.Ordinal);

        foreach (var ep in endpoints.Where(e => apiPrefixes.Any(p => e.RawPath.StartsWith($"/api/{p}/"))))
        {
            // /api/{apiPrefix}/{MODULE}/{Resource}/...
            var parts = ep.RawPath.TrimStart('/').Split('/');
            // parts[0]=api, [1]=apiPrefix, [2]=MODULE, [3]=Resource, [4]=Operation, [5]={id}?
            if (parts.Length < 5) continue;

            string module = parts[2];
            string resource = parts[3];

            if (!modules.ContainsKey(module))
                modules[module] = new SortedDictionary<string, List<Endpoint>>(StringComparer.Ordinal);

            if (!modules[module].ContainsKey(resource))
                modules[module][resource] = [];

            modules[module][resource].Add(ep);
        }

        // ---------------------------------------------------------------
        // 3. Build the export object string
        // ---------------------------------------------------------------
        var sb = new StringBuilder();

        // Derive the export name from the output file name (e.g. management-api.service.ts → Api)
        string exportName = Formatters.DeriveExportName(Path.GetFileName(outputPath));

        sb.AppendLine($"export const {exportName} = {{");

        // Auth block
        if (authEndpoints.Count > 0)
        {
            sb.AppendLine("  // Auth");
            sb.AppendLine("  Auth: {");
            foreach (var ep in authEndpoints)
                sb.AppendLine(RenderAuthMethod(ep, schemas));
            sb.AppendLine("  },");
            sb.AppendLine();
        }

        // Module blocks
        foreach (var (module, resources) in modules)
        {
            sb.AppendLine($"  {module}: {{");
            foreach (var (resource, eps) in resources)
            {
                sb.AppendLine($"    {resource}: {{");
                foreach (var ep in eps)
                    sb.AppendLine(RenderManagementMethod(ep, schemas));
                sb.AppendLine("    },");
            }
            sb.AppendLine("  },");
            sb.AppendLine();
        }

        // Remove trailing comma+newline from last entry and close
        sb.AppendLine("}");

        // ---------------------------------------------------------------
        // 4. Collect all named types used so we can build the import block
        // ---------------------------------------------------------------
        // Collect types grouped by section (auth, then each module) to add blank lines between groups
        var typeGroups = new List<List<string>>();
        var usedTypesSet = new HashSet<string>(StringComparer.Ordinal);

        void collectGroup(IEnumerable<Endpoint> eps, bool isAuth)
        {
            var group = new List<string>();
            foreach (var ep in eps)
            {
                string rt = ResolveResponseType(ep.ResponseSchemaRef, schemas);
                if (isAuth) rt = Formatters.AddAuthPrefix(rt);
                if (rt != "unknown" && rt != "boolean" && !rt.StartsWith('"') && !IsStaticType(rt) && usedTypesSet.Add(rt))
                    group.Add(rt);

                if (ep.RequestSchemaRef != null && !ep.RequestSchemaRef.EndsWith("SearchRequestModel"))
                {
                    string reqT = ResolveRequestType(ep.RequestSchemaRef, ep.Tag,
                        ep.RawPath.TrimStart('/').Split('/').ElementAtOrDefault(2) ?? "", "");
                    if (isAuth) reqT = Formatters.AddAuthPrefix(reqT);
                    if (reqT != "unknown" && !reqT.StartsWith('"') && usedTypesSet.Add(reqT))
                        group.Add(reqT);
                }
            }
            if (group.Count > 0) typeGroups.Add(group);
        }

        collectGroup(authEndpoints, isAuth: true);
        foreach (var (_, resources) in modules)
            collectGroup(resources.Values.SelectMany(e => e), isAuth: false);

        // Determine which @sseta/components types are actually used
        bool usesFetchRequest    = endpoints.Any(e => e.RawPath.EndsWith("/Search", StringComparison.OrdinalIgnoreCase) && !ExcludedPaths.Contains(e.RawPath));
        bool usesSearchResponse  = usesFetchRequest;
        bool usesValidateResponse = endpoints.Any(e =>
            ResolveResponseType(e.ResponseSchemaRef, schemas) == "ValidateResponse");

        var ssetaImports = new List<string> { "ApiResponse" };
        if (usesFetchRequest)    ssetaImports.Add("FetchRequest");
        if (usesSearchResponse)  ssetaImports.Add("SearchResponse");
        if (usesValidateResponse) ssetaImports.Add("ValidateResponse");
        string ssetaImportLine = $"import {{ {string.Join(", ", ssetaImports)} }} from \"@sseta/components\"";

        // Derive the types file name from the service output file name
        // e.g. management-api.service.ts → management-api.types
        string typesFileName = Path.GetFileName(outputPath).Replace(".service.ts", ".types");
        string typeImportLine = typeGroups.Count > 0
            ? $"import type {{\n  {string.Join(",\n\n  ", typeGroups.Select(g => string.Join(",\n  ", g)))},\n}} from \"../types/{typesFileName}\""
            : string.Empty;

        // ---------------------------------------------------------------
        // 5. Inject into template and write output
        // ---------------------------------------------------------------
        string template = File.ReadAllText(templatePath);
        string output = template
            .Replace("// [[SSETA_IMPORTS]]", ssetaImportLine)
            .Replace("// [[API_EXPORT]]", sb.ToString().TrimEnd())
            .Replace("// [[TYPE_IMPORTS]]", typeImportLine);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"    ✓ {Path.GetFileName(outputPath)}  ({endpoints.Count} endpoints across {modules.Count} modules)");
    }

    // ---------------------------------------------------------------
    // Method renderers
    // ---------------------------------------------------------------

    static string RenderAuthMethod(Endpoint ep, JsonObject? schemas)
    {
        // /api/Auth/Profile → operation = Profile → camelCase = profile
        var parts = ep.RawPath.TrimStart('/').Split('/');
        string operation = Formatters.ToCamelCase(parts.Last());
        string relPath = "/" + string.Join("/", parts.Skip(1)); // strip leading /api

        string responseType = Formatters.AddAuthPrefix(ResolveResponseType(ep.ResponseSchemaRef, schemas));

        if (ep.Method == "get")
        {
            return $"    {operation}: async (): Promise<ApiResponse<{responseType}>> => {{\n" +
                   $"      const response = await Client().get(\"{relPath}\")\n" +
                   $"      return response.data\n" +
                   $"    }},";
        }

        // POST / PUT — resolve the request type
        string requestType = ep.RequestSchemaRef != null
            ? Formatters.AddAuthPrefix(Formatters.ResolveRequestType(ep.RequestSchemaRef))
            : "unknown";

        string httpMethod = ep.Method == "put" ? "put" : "post";

        return $"    {operation}: async (payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{\n" +
               $"      const response = await Client().{httpMethod}(\"{relPath}\", payload)\n" +
               $"      return response.data\n" +
               $"    }},";
    }

    static string RenderManagementMethod(Endpoint ep, JsonObject? schemas)
    {
        // /api/{apiPrefix}/{MODULE}/{Resource}/{Operation}/{id}?
        var parts = ep.RawPath.TrimStart('/').Split('/');
        // parts: api, apiPrefix, MODULE, Resource, Operation, {id}?
        string operation = Formatters.ToCamelCase(parts[4]);
        bool hasPathParam = parts.Length > 5 && parts[5].StartsWith("{");
        string pathParam = hasPathParam ? ep.PathParams.FirstOrDefault() ?? "id" : "";

        // Build relative URL (strip leading /api)
        string relPath = "/" + string.Join("/", parts.Skip(1));

        string requestType = ResolveRequestType(ep.RequestSchemaRef, ep.Tag, parts[2], operation);
        string responseType = ResolveResponseType(ep.ResponseSchemaRef, schemas);

        var lines = new StringBuilder();

        switch (operation)
        {
            case "create":
                lines.AppendLine($"      create: async (payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{");
                lines.AppendLine($"        const response = await Client().post(`{relPath}`, payload)");
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            case "update":
                if (hasPathParam)
                {
                    string updateUrl = relPath.Replace($"{{{pathParam}}}", $"${{{pathParam}}}");
                    lines.AppendLine($"      update: async ({pathParam}: number, payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{");
                    lines.AppendLine($"        const response = await Client().put(`{updateUrl}`, payload)");
                }
                else
                {
                    lines.AppendLine($"      update: async (payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{");
                    lines.AppendLine($"        const response = await Client().put(`{relPath}`, payload)");
                }
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            case "delete":
                string deleteUrl = relPath.Replace($"{{{pathParam}}}", $"${{{pathParam}}}");
                lines.AppendLine($"      delete: async ({pathParam}: number): Promise<ApiResponse<{responseType}>> => {{");
                lines.AppendLine($"        const response = await Client().delete(`{deleteUrl}`)");
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            case "retrieve":
                string retrieveUrl = relPath.Replace($"{{{pathParam}}}", $"${{{pathParam}}}");
                lines.AppendLine($"      retrieve: async ({pathParam}: number): Promise<ApiResponse<{responseType}>> => {{");
                lines.AppendLine($"        const response = await Client().get(`{retrieveUrl}`)");
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            case "summary":
                string summaryUrl = relPath.Replace($"{{{pathParam}}}", $"${{{pathParam}}}");
                lines.AppendLine($"      summary: async ({pathParam}: number): Promise<ApiResponse<{responseType}>> => {{");
                lines.AppendLine($"        const response = await Client().get(`{summaryUrl}`)");
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            case "search":
                lines.AppendLine($"      search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<{responseType}>>> => {{");
                lines.AppendLine($"        const response = await Client().post(`{relPath}`, payload)");
                lines.AppendLine($"        return response.data");
                lines.Append($"      }},");
                break;

            default:
                // Generic fallback for unknown operation names
                if (ep.Method == "get")
                {
                    lines.AppendLine($"      {operation}: async (): Promise<ApiResponse<{responseType}>> => {{");
                    lines.AppendLine($"        const response = await Client().get(`{relPath}`)");
                    lines.AppendLine($"        return response.data");
                    lines.Append($"      }},");
                }
                else
                {
                    lines.AppendLine($"      {operation}: async (payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{");
                    lines.AppendLine($"        const response = await Client().{ep.Method}(`{relPath}`, payload)");
                    lines.AppendLine($"        return response.data");
                    lines.Append($"      }},");
                }
                break;
        }

        // Indent for resource nesting (two extra spaces)
        return Formatters.IndentLines(lines.ToString(), 2);
    }

    // ---------------------------------------------------------------
    // Type resolution helpers
    // ---------------------------------------------------------------

    // Maps a schema name to its static @sseta/components type if one applies, otherwise null.
    // Any schema ending in "ValidateResponseModel" maps to the shared ValidateResponse type.
    static string? GetStaticTypeOverride(string schemaName) =>
        schemaName.EndsWith("ValidateResponseModel") ? "ValidateResponse" : null;

    // Returns true if the resolved TypeScript type name is a static type from @sseta/components
    // and should not be included in the generated import block.
    static bool IsStaticType(string tsTypeName) =>
        tsTypeName == "ValidateResponse";

    // Resolves the response TypeScript type from a $ref.
    // Strips the SETAApiResponse envelope, then for search responses unwraps the
    // search envelope too — returning just the row model with "SearchResponse" stripped.
    // e.g. RoleSPSearchResponseSETAApiResponse → SP_Role  (used as SearchResponse<SP_Role>)
    // For primitive data payloads (e.g. BooleanSETAApiResponse) returns the primitive directly.
    // For schemas listed in StaticTypeOverrides, returns the mapped static type name directly.
    static string ResolveResponseType(string? schemaRef, JsonObject? schemas) =>
        Formatters.ResolveResponseType(schemaRef, schemas);

    static string ResolveRequestType(string? schemaRef, string tag, string module, string operation)
    {
        if (schemaRef != null)
            return Formatters.ResolveRequestType(schemaRef);

        // Fallback: construct a conventional name
        return $"{module}_{tag}{Formatters.ToPascalCase(operation)}Request";
    }

    // ---------------------------------------------------------------
    // Data model
    // ---------------------------------------------------------------

    record Endpoint(
        string RawPath,
        string Method,
        string Tag,
        string? RequestSchemaRef,
        string? ResponseSchemaRef,
        List<string> PathParams
    );
}
