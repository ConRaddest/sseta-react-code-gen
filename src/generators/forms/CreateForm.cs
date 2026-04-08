using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates {Module}{Resource}CreateForm.tsx for every resource that has a Create endpoint.
// e.g. ACCESS + StaffRoleRequest → AccessStaffRoleRequestCreateForm.tsx
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/create/{Module}{Resource}CreateForm.tsx
//
// The generated file is a minimal scaffold — it wires up the context, types,
// fields hook and layout that the developer fills in separately.
static class CreateFormGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        string formsOutputDir,
        HashSet<string>? blacklist = null,
        string? templatePath = null,
        HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        // Collect every (module, resource) pair that has a Create endpoint
        var createEndpoints = new List<CreateEndpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;

            // Only portal paths: /api/{apiPrefix}/{MODULE}/{Resource}/Create
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.Create"))) continue;
            if (!string.Equals(parts[4], "Create", StringComparison.OrdinalIgnoreCase)) continue;

            string module = parts[2];
            string resource = parts[3];

            // Find the POST operation
            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (method != "post" || opNode == null) continue;

                string? requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                string requestType = requestRef != null ? Formatters.FormatTypeName(requestRef.Split('/').Last()) : $"{module}_{resource}CreateRequest";
                string responseType = responseRef != null ? Formatters.FormatTypeName(responseRef.Split('/').Last()) : $"{module}_{resource}CreateResponse";

                createEndpoints.Add(new CreateEndpoint(module, resource, requestType, responseType));
            }
        }

        foreach (var ep in createEndpoints)
        {
            string output = ApplyTemplate(RenderCreateForm(ep), templatePath);

            string kebabResource = Formatters.ToKebabCase(ep.Resource);
            string dir = Path.Combine(formsOutputDir, ep.Module.ToLower(), kebabResource, "create");
            Directory.CreateDirectory(dir);

            string prefix = Formatters.ToPascalCase(ep.Module.ToLower()) + ep.Resource;
            string filePath = Path.Combine(dir, $"CreateForm.tsx");
            File.WriteAllText(filePath, output);

            Console.WriteLine($"    ✓ {ep.Module}/{ep.Resource}");
        }

        Console.WriteLine($"    {createEndpoints.Count} create form(s) generated.");
    }

    static string RenderCreateForm(CreateEndpoint ep)
    {
        string resource = ep.Resource;
        string module = ep.Module;
        string requestType = ep.RequestType;
        string responseType = ep.ResponseType;

        // PascalCase module + resource prefix used for all identifiers: file, component, props, hooks, layout
        // e.g. ACCESS + StaffRoleRequest → AccessStaffRoleRequest
        string prefix = Formatters.ToPascalCase(module.ToLower()) + resource;

        // Derive display name for toasts, e.g. "StaffRoleRequest" → "Staff Role Request"
        string displayName = Formatters.ToTitleCase(resource);

        // Derive context hook name, e.g. "useACCESS_StaffRoleRequest" → kept as-is with module prefix
        string contextHook = $"use{prefix}";
        string contextPath = $"@/contexts/resources/{module.ToLower()}/{prefix}Context";

        // Derive the id field, e.g. "staffRoleRequestId"
        string idField = Formatters.GetIdFieldName(resource);

        // Create responses always return the id of the created record.
        bool hasIdInResponse = true;

        string typesImportPath = "@/types/api.types";

        var sb = new StringBuilder();

        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import { ReactNode, useState, useEffect } from \"react\"");
        sb.AppendLine("import { SubmitHandler, useForm } from \"react-hook-form\"");
        sb.AppendLine("import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from \"@sseta/components\"");
        sb.AppendLine($"import {{ {contextHook} }} from \"{contextPath}\"");
        sb.AppendLine("import { useToast } from \"@/contexts/general/ToastContext\"");
        sb.AppendLine($"import {{ {requestType} }} from \"{typesImportPath}\"");
        sb.AppendLine($"import use{prefix}Create from \"./useCreateFields\"");
        sb.AppendLine();

        // Props interface
        sb.AppendLine($"interface {prefix}CreateProps {{");
        sb.AppendLine($"  defaultValues?: Partial<{requestType}>");
        sb.AppendLine("  disabledFields?: string[]");
        sb.AppendLine("  hiddenFields?: string[]");
        sb.AppendLine("  selectFilterBys?: Record<string, FilterBy[]>");
        sb.AppendLine("  selectOrderBys?: Record<string, OrderBy[]>");
        sb.AppendLine("  renderActionsInFooter?: boolean");
        sb.AppendLine("  className?: string");
        sb.AppendLine("  loading?: boolean");
        if (hasIdInResponse)
            sb.AppendLine($"  onCreated?: ({idField}: number) => void");
        else
            sb.AppendLine("  onCreated?: () => void");
        sb.AppendLine("  children?: ReactNode");
        sb.AppendLine("}");
        sb.AppendLine();

        // Component
        sb.AppendLine($"export default function {prefix}Create(props: {prefix}CreateProps) {{");
        sb.AppendLine("  const {");
        sb.AppendLine("    defaultValues,");
        sb.AppendLine("    disabledFields,");
        sb.AppendLine("    hiddenFields,");
        sb.AppendLine("    selectFilterBys = {},");
        sb.AppendLine("    selectOrderBys = {},");
        sb.AppendLine("    renderActionsInFooter = true,");
        sb.AppendLine("    className = \"px-2 sm:px-6 py-3\",");
        sb.AppendLine("    loading: loadingOverride,");
        sb.AppendLine("    onCreated,");
        sb.AppendLine("    children,");
        sb.AppendLine("  } = props");
        sb.AppendLine();
        sb.AppendLine("  const [apiErrors, setApiErrors] = useState<string[]>([])");
        sb.AppendLine("  const [scrollTrigger, setScrollTrigger] = useState(0)");
        sb.AppendLine("  const [loading, setLoading] = useState(false)");
        sb.AppendLine("  const isLoading = loadingOverride ?? loading");
        sb.AppendLine();
        sb.AppendLine($"  const {{ create }} = {contextHook}()");
        sb.AppendLine("  const { showToast } = useToast()");
        sb.AppendLine();
        sb.AppendLine("  const {");
        sb.AppendLine("    handleSubmit,");
        sb.AppendLine("    control,");
        sb.AppendLine("    reset,");
        sb.AppendLine("    formState: { errors, isSubmitting },");
        sb.AppendLine($"  }} = useForm<{requestType}>({{");
        sb.AppendLine("    mode: \"onChange\",");
        sb.AppendLine("  })");
        sb.AppendLine();
        sb.AppendLine($"  const {{ fields, layout }} = use{prefix}Create({{ errors, disabledFields, selectFilterBys, selectOrderBys }})");
        sb.AppendLine();
        sb.AppendLine("  useEffect(() => {");
        sb.AppendLine("    if (defaultValues && Object.keys(defaultValues).length > 0) {");
        sb.AppendLine("      reset((formValues) => ({ ...formValues, ...defaultValues }))");
        sb.AppendLine("    }");
        sb.AppendLine("  }, [])");
        sb.AppendLine();
        sb.AppendLine($"  const onSubmit: SubmitHandler<{requestType}> = async (data) => {{");
        sb.AppendLine("    setLoading(true)");
        sb.AppendLine("    setApiErrors([])");
        sb.AppendLine("    try {");
        sb.AppendLine("      const result = await create(data)");
        sb.AppendLine($"      showToast(\"{displayName} successfully created\", \"success\")");
        if (hasIdInResponse)
            sb.AppendLine($"      if (result?.{idField}) onCreated?.(result.{idField})");
        else
            sb.AppendLine("      onCreated?.()");
        sb.AppendLine("    } catch (error: any) {");
        sb.AppendLine("      setApiErrors(extractApiErrors(error))");
        sb.AppendLine("      setScrollTrigger((t) => t + 1)");
        sb.AppendLine("    } finally {");
        sb.AppendLine("      setLoading(false)");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  return (");
        sb.AppendLine("    <FormTemplate");
        sb.AppendLine("      control={control as any}");
        sb.AppendLine("      fields={fields}");
        sb.AppendLine("      layout={layout}");
        sb.AppendLine("      hiddenFields={hiddenFields}");
        sb.AppendLine("      renderActionsInFooter={renderActionsInFooter}");
        sb.AppendLine("      isLoading={isSubmitting || isLoading}");
        sb.AppendLine("      onSubmit={handleSubmit(onSubmit)}");
        sb.AppendLine("      className={className}");
        sb.AppendLine("      scrollToTopTrigger={scrollTrigger}");
        sb.AppendLine("      actions={");
        sb.AppendLine("        <div className=\"flex md:flex-row flex-col gap-2\">");
        sb.AppendLine("          <Button loading={isSubmitting} type=\"submit\" variant=\"orange\" size=\"mlg\" className=\"w-full md:w-40\">");
        sb.AppendLine("            Save");
        sb.AppendLine("          </Button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      }");
        sb.AppendLine("    >");
        sb.AppendLine("      <FormValidationErrors errors={apiErrors} className=\"mx-auto max-w-4xl w-full mb-4\" />");
        sb.AppendLine("      {children}");
        sb.AppendLine("    </FormTemplate>");
        sb.AppendLine("  )");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;

    record CreateEndpoint(string Module, string Resource, string RequestType, string ResponseType);
}
