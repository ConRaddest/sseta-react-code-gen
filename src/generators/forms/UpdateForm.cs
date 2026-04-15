using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates, for every resource that has an Update endpoint:
//   {Prefix}UpdateForm.tsx
//   use{Prefix}UpdateFields.tsx
//   use{Prefix}UpdateSelects.ts   (only when FK fields exist)
//   {Prefix}UpdateLayout.ts
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/update/
static class UpdateFormGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        JsonObject? fieldLayout,
        string formsOutputDir,
        HashSet<string>? blacklist = null,
        string? formTemplatePath = null,
        string? fieldsTemplatePath = null,
        HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        // Collect resources that have a Retrieve (view) endpoint — update forms require it.
        var viewResources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rawPath, _) in paths)
        {
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (string.Equals(parts[4], "Retrieve", StringComparison.OrdinalIgnoreCase))
                viewResources.Add($"{parts[2]}.{parts[3]}");
        }

        var endpoints = new List<UpdateEndpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.Update"))) continue;
            if (!string.Equals(parts[4], "Update", StringComparison.OrdinalIgnoreCase)) continue;

            string module = parts[2];
            string resource = parts[3];

            if (!viewResources.Contains($"{module}.{resource}"))
            {
                Console.WriteLine($"    ⚠ Skipping: {module}/{resource} update form — no view/retrieve endpoint found.");
                continue;
            }

            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                string? requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                string requestType = requestRef != null ? Formatters.FormatTypeName(requestRef.Split('/').Last()) : $"{module}_{resource}UpdateRequest";
                string responseType = responseRef != null ? Formatters.FormatTypeName(responseRef.Split('/').Last()) : $"{module}_{resource}UpdateResponse";

                endpoints.Add(new UpdateEndpoint(module, resource, requestType, responseType));
            }
        }

        int count = 0;
        foreach (var ep in endpoints)
        {
            string modulePascal = Formatters.ToPascalCase(ep.Module.ToLower());
            string prefix = modulePascal + ep.Resource;
            string kebabResource = Formatters.ToKebabCase(ep.Resource);
            string dir = Path.Combine(formsOutputDir, ep.Module.ToLower(), kebabResource, "update");
            Directory.CreateDirectory(dir);

            // Resolve update request schema
            var requestSchema = Formatters.FindSchema(schemas, $"{ep.Resource}{ep.Module}UpdateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{ep.Resource}UpdateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{ep.Module}_{ep.Resource}UpdateRequestModel");

            var properties = requestSchema?["properties"]?.AsObject();
            var requiredArray = requestSchema?["required"]?.AsArray();
            var requiredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (requiredArray != null)
                foreach (var r in requiredArray)
                    if (r?.GetValue<string>() is string s) requiredFields.Add(s);

            var searchableResources = Formatters.BuildSearchableResources(paths, ep.Module, apiPrefixes);
            var orderedFields = UseFieldsGenerator.GetOrderedFields(ep.Resource, fieldLayout, properties, searchableResources);
            var fkFields = UseFieldsGenerator.CollectFkFields(ep.Module, modulePascal, orderedFields, properties, searchableResources);

            // UpdateForm.tsx
            File.WriteAllText(Path.Combine(dir, $"UpdateForm.tsx"),
                ApplyTemplate(RenderForm(ep, prefix, modulePascal, fkFields, orderedFields), formTemplatePath));

            // useUpdateFields.tsx
            File.WriteAllText(Path.Combine(dir, $"useUpdateFields.tsx"),
                ApplyTemplate(UseFieldsGenerator.Render(prefix, Formatters.ToPascalCase(ep.Module.ToLower()), ep.Resource, ep.RequestType, orderedFields, properties, requiredFields, fkFields, fieldLayout, searchableResources, isUpdate: true), fieldsTemplatePath));

            Console.WriteLine($"    ✓ {ep.Module}/{ep.Resource}");
            count++;
        }

        Console.WriteLine($"    {count} update form(s) generated.");
    }

    static string RenderForm(UpdateEndpoint ep, string prefix, string modulePascal, List<FkField> fkFields, List<string> orderedFields)
    {
        bool hasDateRange = (orderedFields.Any(f => f.Equals("startDate", StringComparison.OrdinalIgnoreCase)) &&
                             orderedFields.Any(f => f.Equals("endDate", StringComparison.OrdinalIgnoreCase))) ||
                            (orderedFields.Any(f => f.Equals("startDateTime", StringComparison.OrdinalIgnoreCase)) &&
                             orderedFields.Any(f => f.Equals("endDateTime", StringComparison.OrdinalIgnoreCase)));
        string displayName = Formatters.ToTitleCase(ep.Resource);
        string contextHook = $"use{prefix}";
        string contextPath = $"@/contexts/resources/{ep.Module.ToLower()}/{prefix}Context";
        string idField = Formatters.GetIdFieldName(ep.Resource);
        string typesPath = "@/types/api.types";

        var sb = new StringBuilder();

        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import { ReactNode, useState, useEffect } from \"react\"");
        sb.AppendLine("import { SubmitHandler, useForm } from \"react-hook-form\"");
        sb.AppendLine("import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from \"@sseta/components\"");
        sb.AppendLine($"import {{ {contextHook} }} from \"{contextPath}\"");
        sb.AppendLine("import { useToast } from \"@/contexts/general/ToastContext\"");
        string entityType = $"{prefix}";
        sb.AppendLine($"import {{ {entityType}, {ep.RequestType} }} from \"{typesPath}\"");
        sb.AppendLine($"import use{prefix}Update from \"./useUpdateFields\"");
        sb.AppendLine();

        sb.AppendLine($"interface {prefix}UpdateProps {{");
        sb.AppendLine($"  {idField}: number");
        sb.AppendLine($"  defaultValues?: Partial<{ep.RequestType}>");
        sb.AppendLine("  disabledFields?: string[]");
        sb.AppendLine("  hiddenFields?: string[]");
        sb.AppendLine("  selectFilterBys?: Record<string, FilterBy[]>");
        sb.AppendLine("  selectOrderBys?: Record<string, OrderBy[]>");
        sb.AppendLine("  renderActionsInFooter?: boolean");
        sb.AppendLine("  className?: string");
        sb.AppendLine("  loading?: boolean");
        sb.AppendLine($"  onUpdated?: ({idField}: number) => void");
        sb.AppendLine($"  onRecordLoaded?: (record: {entityType}) => void");
        sb.AppendLine("  onDirty?: (isDirty: boolean) => void");
        sb.AppendLine("  children?: ReactNode");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine($"export default function {prefix}Update(props: {prefix}UpdateProps) {{");
        sb.AppendLine("  const {");
        sb.AppendLine($"    {idField},");
        sb.AppendLine("    defaultValues,");
        sb.AppendLine("    disabledFields,");
        sb.AppendLine("    hiddenFields,");
        sb.AppendLine("    selectFilterBys = {},");
        sb.AppendLine("    selectOrderBys = {},");
        sb.AppendLine("    renderActionsInFooter = true,");
        sb.AppendLine("    className = \"px-2 sm:px-6 py-3\",");
        sb.AppendLine("    loading: loadingOverride,");
        sb.AppendLine("    onUpdated,");
        sb.AppendLine("    onRecordLoaded,");
        sb.AppendLine("    onDirty,");
        sb.AppendLine("    children,");
        sb.AppendLine("  } = props");
        sb.AppendLine();
        sb.AppendLine("  const [apiErrors, setApiErrors] = useState<string[]>([])");
        sb.AppendLine("  const [scrollTrigger, setScrollTrigger] = useState(0)");
        sb.AppendLine("  const [loading, setLoading] = useState(false)");
        if (fkFields.Count > 0)
            sb.AppendLine("  const [selectedLabels, setSelectedLabels] = useState<Record<string, string | null>>({})");
        sb.AppendLine("  const isLoading = loadingOverride ?? loading");
        sb.AppendLine();
        sb.AppendLine($"  const {{ retrieve, update }} = {contextHook}()");
        sb.AppendLine("  const { showToast } = useToast()");
        sb.AppendLine();
        sb.AppendLine("  const {");
        sb.AppendLine("    handleSubmit,");
        sb.AppendLine("    control,");
        sb.AppendLine("    reset,");
        sb.AppendLine("    formState: { errors, isSubmitting, isDirty },");
        sb.AppendLine($"  }} = useForm<{ep.RequestType}>({{");
        sb.AppendLine("    mode: \"onChange\",");
        sb.AppendLine("  })");
        sb.AppendLine();

        string selectedLabelsArg = fkFields.Count > 0 ? ", selectedLabels" : "";
        string controlArg = hasDateRange ? ", control" : "";
        sb.AppendLine($"  const {{ fields, layout }} = use{prefix}Update({{ errors, disabledFields, selectFilterBys, selectOrderBys{selectedLabelsArg}{controlArg} }})");
        sb.AppendLine();
        sb.AppendLine("  useEffect(() => {");
        sb.AppendLine("    const fetchRecord = async () => {");
        sb.AppendLine("      setLoading(true)");
        sb.AppendLine("      try {");
        sb.AppendLine($"        const record = await retrieve({idField})");
        sb.AppendLine("        if (!record) return");
        sb.AppendLine("        reset({ ...(record as any), ...defaultValues })");
        sb.AppendLine("        onRecordLoaded?.(record)");
        if (fkFields.Count > 0)
        {
            sb.AppendLine("        setSelectedLabels({");
            foreach (var fk in fkFields)
            {
                string labelField = fk.FieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                    ? fk.FieldName[..^2] + "Name"
                    : fk.FieldName + "Name";
                sb.AppendLine($"          {fk.FieldName}: record.{labelField},");
            }
            sb.AppendLine("        })");
        }
        sb.AppendLine("      } catch (error) {");
        sb.AppendLine($"        console.error(\"Failed to fetch {Formatters.ToTitleCase(ep.Resource).ToLower()}:\", error)");
        sb.AppendLine("      } finally {");
        sb.AppendLine("        setLoading(false)");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("    fetchRecord()");
        sb.AppendLine("  }, [])");
        sb.AppendLine();
        sb.AppendLine("  useEffect(() => {");
        sb.AppendLine("    onDirty?.(isDirty)");
        sb.AppendLine("    return () => onDirty?.(false)");
        sb.AppendLine("  }, [isDirty])");
        sb.AppendLine();
        sb.AppendLine($"  const onSubmit: SubmitHandler<{ep.RequestType}> = async (data) => {{");
        sb.AppendLine("    setLoading(true)");
        sb.AppendLine("    setApiErrors([])");
        sb.AppendLine("    try {");
        sb.AppendLine("      await update(data)");
        sb.AppendLine($"      showToast(\"{displayName} successfully updated\", \"success\")");
        sb.AppendLine($"      onUpdated?.({idField})");
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
        sb.AppendLine("          <Button loading={isSubmitting} disabled={!isDirty} type=\"submit\" variant=\"orange\" size=\"mlg\" className=\"w-full md:w-40\">");
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

    record UpdateEndpoint(string Module, string Resource, string RequestType, string ResponseType);
}
