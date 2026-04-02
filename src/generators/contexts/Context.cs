using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates {Module}{Resource}Context.tsx for every module/resource group found in swagger.
//
// Output path: {contextsOutputDir}/{module-lower}-{kebab-resource}/{Module}{Resource}Context.tsx
//
// Search-related state (items, selectedItem, totalRows, lastSearchTerm, lastFetchRequest,
// loadMoreItems, selectItem, clearSelection, refresh) is only included when a `search`
// operation exists for the resource. All other operations are included only when present.
static class ContextGenerator
{
    public static void Generate(JsonObject paths, JsonObject? schemas, string contextsOutputDir, HashSet<string>? blacklist = null, string? templatePath = null, string apiPrefix = "management")
    {
        // module → resource → operations
        var modules = new SortedDictionary<string, SortedDictionary<string, ResourceOps>>(StringComparer.Ordinal);

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;

            var parts = rawPath.TrimStart('/').Split('/');
            // /api/{apiPrefix}/{MODULE}/{Resource}/{Operation}
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != apiPrefix) continue;

            string module = parts[2];
            string resource = parts[3];

            // Skip the whole resource when MODULE/Resource is blacklisted.
            if (blacklist != null && blacklist.Contains($"{module}.{resource}")) continue;
            string operation = parts[4].ToLower();

            // Skip this specific operation when MODULE/Resource/Operation is blacklisted.
            // e.g. "ECD/SystemUser/Update" suppresses the update op from the context.
            if (blacklist != null && blacklist.Contains($"{module}.{resource}.{parts[4]}")) continue;

            if (!modules.ContainsKey(module))
                modules[module] = new SortedDictionary<string, ResourceOps>(StringComparer.Ordinal);
            if (!modules[module].ContainsKey(resource))
                modules[module][resource] = new ResourceOps();

            var ops = modules[module][resource];

            foreach (var (_, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                string? requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                string requestType = Formatters.ResolveRequestType(requestRef);
                string responseType = Formatters.ResolveResponseType(responseRef, schemas);

                switch (operation)
                {
                    case "search":
                        ops.Search = new OpInfo(requestType, responseType);
                        break;
                    case "retrieve":
                        ops.Retrieve = new OpInfo(requestType, responseType);
                        break;
                    case "create":
                        ops.Create = new OpInfo(requestType, responseType);
                        break;
                    case "update":
                        ops.Update = new OpInfo(requestType, responseType);
                        break;
                    case "delete":
                        ops.Delete = new OpInfo(requestType, responseType);
                        break;
                    case "submit":
                        ops.Submit = new OpInfo(requestType, responseType);
                        break;
                    case "validate":
                        ops.Validate = new OpInfo(requestType, responseType);
                        break;
                    case "summary":
                        ops.Summary = new OpInfo(requestType, responseType);
                        break;
                }
            }
        }

        int count = 0;
        foreach (var (module, resources) in modules)
        {
            foreach (var (resource, ops) in resources)
            {
                string output = ApplyTemplate(RenderContext(module, resource, ops), templatePath);

                string dir = Path.Combine(contextsOutputDir, "resources", module.ToLower());
                Directory.CreateDirectory(dir);

                string prefix = Formatters.ToPascalCase(module.ToLower()) + resource;
                string filePath = Path.Combine(dir, $"{prefix}Context.tsx");
                File.WriteAllText(filePath, output);

                Console.WriteLine($"    ✓ {module}/{resource}");
                count++;
            }
        }

        Console.WriteLine($"    {count} context(s) generated.");
    }

    static string RenderContext(string module, string resource, ResourceOps ops)
    {
        string prefix = Formatters.ToPascalCase(module.ToLower()) + resource;
        string idField = Formatters.GetIdFieldName(resource);
        string apiPath = $"Api.{module}.{resource}";
        bool hasSearch = ops.Search != null;
        bool hasCreate = ops.Create != null;
        bool hasRetrieve = ops.Retrieve != null;
        bool hasUpdate = ops.Update != null;
        bool hasDelete = ops.Delete != null;
        bool hasSubmit = ops.Submit != null;
        bool hasValidate = ops.Validate != null;
        bool hasSummary = ops.Summary != null;

        // Resolve entity type (from retrieve response, fallback to prefix)
        string entityType = hasRetrieve ? ops.Retrieve!.ResponseType : prefix;
        string searchRowType = hasSearch ? ResolveSearchRowType(ops.Search!.ResponseType) : "";

        var sb = new StringBuilder();

        // ---- Imports ----
        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import {");
        sb.AppendLine("  ReactNode,");
        sb.AppendLine("  createContext,");
        sb.AppendLine("  useContext,");
        sb.AppendLine("  useState,");
        sb.AppendLine("} from \"react\"");
        sb.AppendLine("import { Api } from \"@/services/api.service\"");

        // Collect types needed
        // Only import concrete named types — skip sentinels and primitives
        static bool IsImportable(string t) =>
            !string.IsNullOrEmpty(t) && t != "unknown" && t != "boolean" && t != "string" && t != "number";

        var typeImports = new List<string>();
        void TryAdd(string t) { if (IsImportable(t) && !typeImports.Contains(t)) typeImports.Add(t); }

        if (hasRetrieve) TryAdd(entityType);
        if (hasSearch) TryAdd(searchRowType);
        if (hasCreate) { TryAdd(ops.Create!.RequestType); TryAdd(ops.Create.ResponseType); }
        if (hasUpdate) TryAdd(ops.Update!.RequestType);
        if (hasDelete) TryAdd(ops.Delete!.RequestType);
        if (hasSubmit) TryAdd(ops.Submit!.RequestType);
        if (hasValidate) TryAdd(ops.Validate!.RequestType);
        if (hasSummary) TryAdd(ops.Summary!.ResponseType);

        if (typeImports.Count > 0)
        {
            sb.AppendLine("import {");
            foreach (var t in typeImports)
                sb.AppendLine($"  {t},");
            sb.AppendLine("} from \"@/types/api.types\"");
        }

        // sseta/components imports — only add what is actually used
        var componentImports = new List<string>();
        if (hasSearch) { componentImports.Add("FetchRequest"); componentImports.Add("DEFAULT_PAGE_SIZE"); }
        if (hasValidate) componentImports.Add("ValidateResponse");

        if (componentImports.Count > 0)
            sb.AppendLine($"import {{ {string.Join(", ", componentImports)} }} from \"@sseta/components\"");
        sb.AppendLine();

        // ---- Context interface ----
        sb.AppendLine($"interface {prefix}ContextType {{");
        sb.AppendLine("  // State");
        if (hasSearch)
        {
            sb.AppendLine($"  items: {searchRowType}[]");
            sb.AppendLine("  totalRows: number");
            sb.AppendLine("  lastSearchTerm: string");
            sb.AppendLine("  lastFetchRequest: FetchRequest | null");
        }
        if (hasSummary)
        {
            sb.AppendLine($"  summaryData: {ops.Summary!.ResponseType} | null");
            sb.AppendLine("  summaryLoading: boolean");
        }
        if (hasSearch || hasSummary)
        {
            sb.AppendLine();
            sb.AppendLine("  // Operations");
            if (hasSearch)
                sb.AppendLine("  fetchItems: (fetchRequest: FetchRequest, shouldMerge?: boolean) => Promise<void>");
        }
        else
        {
            sb.AppendLine("  // Operations");
        }
        if (hasRetrieve)
            sb.AppendLine($"  retrieve: ({idField}: number) => Promise<{entityType} | null>");
        if (hasCreate)
            sb.AppendLine($"  create: (data: {ops.Create!.RequestType}) => Promise<{ops.Create.ResponseType} | null>");
        if (hasUpdate)
            sb.AppendLine($"  update: (data: {ops.Update!.RequestType}) => Promise<boolean>");
        if (hasDelete)
            sb.AppendLine($"  destroy: ({idField}: number) => Promise<boolean>");
        if (hasSubmit)
            sb.AppendLine($"  submit: (data: {ops.Submit!.RequestType}) => Promise<boolean>");
        if (hasValidate)
            sb.AppendLine($"  validate: (data: {ops.Validate!.RequestType}) => Promise<ValidateResponse | null>");
        if (hasSummary)
            sb.AppendLine($"  summary: ({idField}: number) => Promise<{ops.Summary!.ResponseType} | null>");
        if (hasSearch)
        {
            sb.AppendLine("  loadMoreItems: () => Promise<void>");
            sb.AppendLine();
            sb.AppendLine("  // Refresh");
            sb.AppendLine("  refresh: () => Promise<void>");
        }
        sb.AppendLine("}");
        sb.AppendLine();

        // ---- Context creation ----
        sb.AppendLine($"// Undefined default is intentional — enforced by the use{prefix} hook below.");
        sb.AppendLine($"const {prefix}Context = createContext<{prefix}ContextType | undefined>(undefined)");
        sb.AppendLine();

        // ---- Provider ----
        sb.AppendLine($"export function {prefix}Provider({{ children }}: {{ children: ReactNode }}) {{");

        // Initial state
        sb.Append("  const [state, setState] = useState<any>(");
        if (hasSearch || hasSummary)
        {
            sb.AppendLine("{");
            if (hasSearch)
            {
                sb.AppendLine("    items: [],");
                sb.AppendLine("    totalRows: 0,");
                sb.AppendLine("    lastSearchTerm: \"\",");
                sb.AppendLine("    lastFetchRequest: {");
                sb.AppendLine("      pageNumber: 1,");
                sb.AppendLine("      pageSize: DEFAULT_PAGE_SIZE,");
                sb.AppendLine("      orderByList: [],");
                sb.AppendLine("      filterByList: [],");
                sb.AppendLine("    },");
            }
            if (hasSummary)
            {
                sb.AppendLine("    summaryData: null,");
                sb.AppendLine("    summaryLoading: false,");
            }
            sb.AppendLine("  })");
        }
        else
        {
            sb.AppendLine("{})"); ;
        }
        sb.AppendLine();

        // ---- Function implementations ----

        // fetchItems
        if (hasSearch)
        {
            sb.AppendLine("  const fetchItems = async (body: FetchRequest, shouldMerge: boolean = false) => {");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await {apiPath}.search(body)");
            sb.AppendLine("      const searchFilter = body.filterByList?.find((f) => f.operator === \"search\")");
            sb.AppendLine("      const searchTerm = searchFilter ? (searchFilter.value as string).replace(/\\*/g, \"\") : \"\"");
            sb.AppendLine("      setState((prev: any) => ({");
            sb.AppendLine("        ...prev,");
            sb.AppendLine("        items: shouldMerge ? [...prev.items, ...(response.data.searchResults || [])] : response.data.searchResults || [],");
            sb.AppendLine("        totalRows: response.data?.totalRows || 0,");
            sb.AppendLine("        lastSearchTerm: searchTerm,");
            sb.AppendLine("        lastFetchRequest: body,");
            sb.AppendLine("      }))");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // retrieve
        if (hasRetrieve)
        {
            sb.AppendLine($"  const retrieve = async ({idField}: number): Promise<{entityType} | null> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await {apiPath}.retrieve({idField})");
            sb.AppendLine("      return response.data");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // create
        if (hasCreate)
        {
            sb.AppendLine($"  const create = async (data: {ops.Create!.RequestType}): Promise<{ops.Create.ResponseType} | null> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await {apiPath}.create(data)");
            if (hasSearch)
            {
                sb.AppendLine("      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)");
            }
            sb.AppendLine("      return response.data");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // update
        if (hasUpdate)
        {
            sb.AppendLine($"  const update = async (data: {ops.Update!.RequestType}): Promise<boolean> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      await {apiPath}.update(data)");
            if (hasSearch)
                sb.AppendLine("      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)");
            sb.AppendLine("      return true");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // destroy
        if (hasDelete)
        {
            sb.AppendLine($"  const destroy = async ({idField}: number): Promise<boolean> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      await {apiPath}.delete({idField})");
            if (hasSearch)
                sb.AppendLine("      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)");
            sb.AppendLine("      return true");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // submit
        if (hasSubmit)
        {
            sb.AppendLine($"  const submit = async (data: {ops.Submit!.RequestType}): Promise<boolean> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await {apiPath}.submit(data)");
            if (hasSearch)
                sb.AppendLine("      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)");
            sb.AppendLine("      return response.data ?? false");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // validate
        if (hasValidate)
        {
            sb.AppendLine($"  const validate = async (data: {ops.Validate!.RequestType}): Promise<ValidateResponse | null> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await {apiPath}.validate(data)");
            sb.AppendLine("      return response.data");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // summary
        if (hasSummary)
        {
            sb.AppendLine($"  const summary = async ({idField}: number): Promise<{ops.Summary!.ResponseType} | null> => {{");
            sb.AppendLine("    try {");
            sb.AppendLine("      setState((prev: any) => ({ ...prev, summaryLoading: true }))");
            sb.AppendLine($"      const response = await {apiPath}.summary({idField})");
            sb.AppendLine("      setState((prev: any) => ({ ...prev, summaryData: response.data, summaryLoading: false }))");
            sb.AppendLine("      return response.data");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      setState((prev: any) => ({ ...prev, summaryLoading: false }))");
            sb.AppendLine("      console.error(error)");
            sb.AppendLine("      throw error");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // search helpers
        if (hasSearch)
        {
            sb.AppendLine("  const refresh = async () => { if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest) }");
            sb.AppendLine("  const loadMoreItems = async () => {");
            sb.AppendLine("    if (state.lastFetchRequest) {");
            sb.AppendLine("      await fetchItems({ ...state.lastFetchRequest, pageNumber: state.lastFetchRequest.pageNumber + 1 }, true)");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();
        }

        // ---- Provider value ----
        sb.AppendLine("  // No useMemo — the React Compiler handles memoization.");
        sb.AppendLine("  return (");
        sb.AppendLine($"    <{prefix}Context.Provider");
        sb.AppendLine("      value={{");
        sb.AppendLine("        ...state,");
        if (hasSearch) sb.AppendLine("        fetchItems,");
        if (hasRetrieve) sb.AppendLine("        retrieve,");
        if (hasCreate) sb.AppendLine("        create,");
        if (hasUpdate) sb.AppendLine("        update,");
        if (hasDelete) sb.AppendLine("        destroy,");
        if (hasSubmit) sb.AppendLine("        submit,");
        if (hasValidate) sb.AppendLine("        validate,");
        if (hasSummary) sb.AppendLine("        summary,");
        if (hasSearch)
        {
            sb.AppendLine("        loadMoreItems,");
            sb.AppendLine("        refresh,");
        }
        sb.AppendLine("      }}");
        sb.AppendLine("    >");
        sb.AppendLine("      {children}");
        sb.AppendLine($"    </{prefix}Context.Provider>");
        sb.AppendLine("  )");
        sb.AppendLine("}");
        sb.AppendLine();

        // ---- Hook ----
        sb.AppendLine($"// Throws if used outside of {prefix}Provider to catch missing provider wrapping early.");
        sb.AppendLine($"export function use{prefix}() {{");
        sb.AppendLine($"  const context = useContext({prefix}Context)");
        sb.AppendLine("  if (context === undefined) {");
        sb.AppendLine($"    throw new Error(\"use{prefix} must be used within a {prefix}Provider\")");
        sb.AppendLine("  }");
        sb.AppendLine("  return context");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;

    // The search response type from swagger is the wrapper row type, e.g. AccessStaffRoleRequestSearchResponse.
    // It is already correctly named by FormatTypeName.
    static string ResolveSearchRowType(string responseType) => responseType;

    // ---- Data models ----

    class ResourceOps
    {
        public OpInfo? Search { get; set; }
        public OpInfo? Retrieve { get; set; }
        public OpInfo? Create { get; set; }
        public OpInfo? Update { get; set; }
        public OpInfo? Delete { get; set; }
        public OpInfo? Submit { get; set; }
        public OpInfo? Validate { get; set; }
        public OpInfo? Summary { get; set; }
    }

    record OpInfo(string RequestType, string ResponseType);
}
