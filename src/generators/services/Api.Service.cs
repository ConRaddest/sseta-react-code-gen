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

    // Auth path segments that are always rendered as a nested sub-object, even when there is only
    // a single endpoint under them. This ensures a consistent shape across portals.
    // e.g. GET /Auth/Profile → profile: { get: ... }  (not a flat profile: async () => ...)
    static readonly HashSet<string> AuthSubNamespaceSegments = ["Profile"];

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
        // Auth paths: /api/Auth/{Op} are flat; /api/Auth/{Sub}/{Op} are grouped under a sub-namespace.
        // e.g. GET /api/Auth/Profile and POST /api/Auth/Profile/PasswordUpdate →
        //   profile: { get: ..., passwordUpdate: ... }
        if (authEndpoints.Count > 0)
        {
            // Separate flat endpoints (3 segments: api/Auth/Op) from sub-namespaced (4+ segments)
            var flatAuthEndpoints = authEndpoints
                .Where(e => e.RawPath.TrimStart('/').Split('/').Length == 3)
                .ToList();
            var subAuthGroups = authEndpoints
                .Where(e => e.RawPath.TrimStart('/').Split('/').Length > 3)
                .GroupBy(e => e.RawPath.TrimStart('/').Split('/')[2]) // group by sub-namespace (e.g. "Profile")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Any flat endpoint whose operation name matches a sub-namespace key (either because deeper
            // children exist in this swagger, or because the segment is in AuthSubNamespaceSegments)
            // is promoted into that group and rendered as "get".
            var promotedKeys = new HashSet<string>(subAuthGroups.Keys, StringComparer.OrdinalIgnoreCase);
            var trueFlat = new List<Endpoint>();
            foreach (var ep in flatAuthEndpoints)
            {
                string opName = ep.RawPath.TrimStart('/').Split('/').Last();
                if (promotedKeys.Contains(opName))
                {
                    subAuthGroups[opName].Insert(0, ep); // insert at front so "get" renders first
                }
                else if (AuthSubNamespaceSegments.Contains(opName, StringComparer.OrdinalIgnoreCase))
                {
                    // No children in this swagger, but the segment is a known sub-namespace —
                    // create a new group so the shape stays consistent across portals.
                    subAuthGroups[opName] = [ep];
                }
                else
                {
                    trueFlat.Add(ep);
                }
            }

            sb.AppendLine("  // Auth");
            sb.AppendLine("  Auth: {");

            // Flat methods
            foreach (var ep in trueFlat)
                sb.AppendLine(RenderAuthMethod(ep, schemas, subNamespace: null));

            // Sub-namespace objects (e.g. profile: { get: ..., passwordUpdate: ... })
            foreach (var (subNs, subEps) in subAuthGroups)
            {
                string propName = Formatters.ToPascalCase(subNs);
                sb.AppendLine($"    {propName}: {{");
                foreach (var ep in subEps)
                    sb.AppendLine(RenderAuthMethod(ep, schemas, subNamespace: subNs));
                sb.AppendLine("    },");
            }

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

    // Renders a single Auth method.
    // When subNamespace is non-null the endpoint belongs to a nested sub-object (e.g. profile: { ... }).
    // A flat Auth endpoint that was promoted into a sub-namespace group because its path segment
    // matches the sub-namespace name renders as "get" instead of repeating the namespace name.
    static string RenderAuthMethod(Endpoint ep, JsonObject? schemas, string? subNamespace)
    {
        var parts = ep.RawPath.TrimStart('/').Split('/');
        // /api/Auth/Profile → parts = [api, Auth, Profile]
        // /api/Auth/Profile/PasswordUpdate → parts = [api, Auth, Profile, PasswordUpdate]
        string relPath = "/" + string.Join("/", parts.Skip(1)); // strip leading /api

        string operation;
        if (subNamespace != null && string.Equals(parts.Last(), subNamespace, StringComparison.OrdinalIgnoreCase))
            // This is the parent endpoint promoted into the sub-namespace group — use "get"
            operation = "get";
        else
            operation = Formatters.ToCamelCase(parts.Last());

        string indent = subNamespace != null ? "      " : "    ";
        string responseType = Formatters.AddAuthPrefix(ResolveResponseType(ep.ResponseSchemaRef, schemas));

        if (ep.Method == "get")
        {
            return $"{indent}{operation}: async (): Promise<ApiResponse<{responseType}>> => {{\n" +
                   $"{indent}  const response = await Client().get(\"{relPath}\")\n" +
                   $"{indent}  return response.data\n" +
                   $"{indent}}},";
        }

        // POST / PUT — resolve the request type
        string requestType = ep.RequestSchemaRef != null
            ? Formatters.AddAuthPrefix(Formatters.ResolveRequestType(ep.RequestSchemaRef))
            : "unknown";

        string httpMethod = ep.Method == "put" ? "put" : "post";

        return $"{indent}{operation}: async (payload: {requestType}): Promise<ApiResponse<{responseType}>> => {{\n" +
               $"{indent}  const response = await Client().{httpMethod}(\"{relPath}\", payload)\n" +
               $"{indent}  return response.data\n" +
               $"{indent}}},";
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
