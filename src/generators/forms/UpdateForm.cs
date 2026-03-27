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
        string? fieldsTemplatePath = null)
    {
        var endpoints = new List<UpdateEndpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != "management") continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.Update"))) continue;
            if (!string.Equals(parts[4], "Update", StringComparison.OrdinalIgnoreCase)) continue;

            string module   = parts[2];
            string resource = parts[3];

            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                string? requestRef  = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();

                string requestType  = requestRef  != null ? Formatters.FormatTypeName(requestRef.Split('/').Last())  : $"{module}_{resource}UpdateRequest";
                string responseType = responseRef != null ? Formatters.FormatTypeName(responseRef.Split('/').Last()) : $"{module}_{resource}UpdateResponse";

                endpoints.Add(new UpdateEndpoint(module, resource, requestType, responseType));
            }
        }

        int count = 0;
        foreach (var ep in endpoints)
        {
            string modulePascal = Formatters.ToPascalCase(ep.Module.ToLower());
            string prefix       = modulePascal + ep.Resource;
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

            var searchableResources = Formatters.BuildSearchableResources(paths, ep.Module);
            var orderedFields = UseFieldsGenerator.GetOrderedFields(ep.Resource, fieldLayout, properties, searchableResources);
            var fkFields = UseFieldsGenerator.CollectFkFields(ep.Module, modulePascal, orderedFields, properties, searchableResources);

            // UpdateForm.tsx
            File.WriteAllText(Path.Combine(dir, $"{prefix}UpdateForm.tsx"),
                ApplyTemplate(RenderForm(ep, prefix, modulePascal), formTemplatePath));

            // use{Prefix}UpdateFields.tsx
            File.WriteAllText(Path.Combine(dir, $"use{prefix}UpdateFields.tsx"),
                ApplyTemplate(RenderFields(prefix, ep.Resource, ep.RequestType, orderedFields, properties, requiredFields, fkFields, fieldLayout, searchableResources), fieldsTemplatePath));

            Console.WriteLine($"    ✓ {ep.Module}/{ep.Resource}");
            count++;
        }

        Console.WriteLine($"    {count} update form(s) generated.");
    }

    static string RenderForm(UpdateEndpoint ep, string prefix, string modulePascal)
    {
        string displayName  = Formatters.ToTitleCase(ep.Resource);
        string contextHook  = $"use{prefix}";
        string contextPath  = $"@/contexts/resources/{ep.Module.ToLower()}/{prefix}Context";
        string idField      = Formatters.GetIdFieldName(ep.Resource);
        string typesPath    = "@/types/api.types";

        var sb = new StringBuilder();

        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import { useState, useEffect } from \"react\"");
        sb.AppendLine("import { SubmitHandler, useForm } from \"react-hook-form\"");
        sb.AppendLine("import { Button, FormTemplate, FormValidationErrors, FilterBy, OrderBy, extractApiErrors } from \"@sseta/components\"");
        sb.AppendLine($"import {{ {contextHook} }} from \"{contextPath}\"");
        sb.AppendLine("import { useToast } from \"@/contexts/general/ToastContext\"");
        sb.AppendLine($"import {{ {ep.RequestType} }} from \"{typesPath}\"");
        sb.AppendLine($"import use{prefix}UpdateFields from \"./use{prefix}UpdateFields\"");
        sb.AppendLine();

        sb.AppendLine($"interface {prefix}UpdateFormProps {{");
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
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine($"export default function {prefix}UpdateForm(props: {prefix}UpdateFormProps) {{");
        sb.AppendLine("  const {");
        sb.AppendLine($"    {idField},");
        sb.AppendLine("    defaultValues,");
        sb.AppendLine("    disabledFields,");
        sb.AppendLine("    hiddenFields,");
        sb.AppendLine("    selectFilterBys = {},");
        sb.AppendLine("    selectOrderBys = {},");
        sb.AppendLine("    renderActionsInFooter = true,");
        sb.AppendLine("    className = \"px-6 py-4\",");
        sb.AppendLine("    loading: loadingOverride,");
        sb.AppendLine("    onUpdated,");
        sb.AppendLine("  } = props");
        sb.AppendLine();
        sb.AppendLine("  const [apiErrors, setApiErrors] = useState<string[]>([])");
        sb.AppendLine("  const [loading, setLoading] = useState(false)");
        sb.AppendLine("  const isLoading = loadingOverride ?? loading");
        sb.AppendLine();
        sb.AppendLine($"  const {{ retrieve, update }} = {contextHook}()");
        sb.AppendLine("  const { showToast } = useToast()");
        sb.AppendLine();
        sb.AppendLine("  const {");
        sb.AppendLine("    handleSubmit,");
        sb.AppendLine("    control,");
        sb.AppendLine("    reset,");
        sb.AppendLine("    formState: { errors, isSubmitting },");
        sb.AppendLine($"  }} = useForm<{ep.RequestType}>({{");
        sb.AppendLine("    mode: \"onBlur\",");
        sb.AppendLine("  })");
        sb.AppendLine();
        sb.AppendLine($"  const {{ fields, layout }} = use{prefix}UpdateFields({{ errors, disabledFields, selectFilterBys, selectOrderBys }})");
        sb.AppendLine();
        sb.AppendLine("  useEffect(() => {");
        sb.AppendLine("    const fetchRecord = async () => {");
        sb.AppendLine("      setLoading(true)");
        sb.AppendLine("      try {");
        sb.AppendLine($"        const record = await retrieve({idField})");
        sb.AppendLine("        if (!record) return");
        sb.AppendLine("        reset({ ...record, ...defaultValues })");
        sb.AppendLine("      } catch (error) {");
        sb.AppendLine($"        console.error(\"Failed to fetch {Formatters.ToTitleCase(ep.Resource).ToLower()}:\", error)");
        sb.AppendLine("      } finally {");
        sb.AppendLine("        setLoading(false)");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("    fetchRecord()");
        sb.AppendLine("  }, [])");
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
        sb.AppendLine("    } finally {");
        sb.AppendLine("      setLoading(false)");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  return (");
        sb.AppendLine("    <div className=\"flex flex-col flex-1 min-h-0\">");
        sb.AppendLine("      <FormValidationErrors errors={apiErrors} className=\"mx-auto max-w-4xl w-full mb-4\" />");
        sb.AppendLine("      <FormTemplate");
        sb.AppendLine("        control={control}");
        sb.AppendLine("        fields={fields}");
        sb.AppendLine("        layout={layout}");
        sb.AppendLine("        hiddenFields={hiddenFields}");
        sb.AppendLine("        renderActionsInFooter={renderActionsInFooter}");
        sb.AppendLine("        isLoading={isSubmitting || isLoading}");
        sb.AppendLine("        onSubmit={handleSubmit(onSubmit)}");
        sb.AppendLine("        className={className}");
        sb.AppendLine("        actions={");
        sb.AppendLine("          <div className=\"flex md:flex-row flex-col gap-2\">");
        sb.AppendLine("            <Button loading={isSubmitting} type=\"submit\" variant=\"orange\" size=\"mlg\" className=\"w-full md:w-40\">");
        sb.AppendLine("              Save");
        sb.AppendLine("            </Button>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        }");
        sb.AppendLine("      />");
        sb.AppendLine("    </div>");
        sb.AppendLine("  )");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string RenderFields(
        string prefix,
        string resource,
        string requestType,
        List<string> orderedFields,
        JsonObject? properties,
        HashSet<string> requiredFields,
        List<FkField> fkFields,
        JsonObject? fieldLayout,
        HashSet<string>? searchableResources = null)
    {
        var sb = new StringBuilder();
        bool hasSelects = fkFields.Count > 0;
        string kebabResource = Formatters.ToKebabCase(resource);

        sb.AppendLine("import { FieldErrors } from \"react-hook-form\"");
        if (hasSelects)
        {
            sb.AppendLine("import { FilterBy, OrderBy, FormLayout, useSelect } from \"@sseta/components\"");
            var seenContexts = new HashSet<string>();
            foreach (var fk in fkFields)
                if (seenContexts.Add(fk.ContextName))
                    sb.AppendLine($"import {{ {fk.HookName} }} from \"{fk.ContextImportPath}\"");
        }
        else
        {
            sb.AppendLine("import { FilterBy, OrderBy, FormLayout } from \"@sseta/components\"");
        }
        sb.AppendLine($"import {{ {requestType} }} from \"@/types/api.types\"");
        sb.AppendLine();

        // Props interface — always includes selectFilterBys/selectOrderBys for a stable contract
        sb.AppendLine($"interface Use{prefix}UpdateFieldsProps {{");
        sb.AppendLine($"  errors: FieldErrors<{requestType}>");
        sb.AppendLine("  disabledFields?: string[]");
        sb.AppendLine("  selectFilterBys?: Record<string, FilterBy[]>");
        sb.AppendLine("  selectOrderBys?: Record<string, OrderBy[]>");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine($"export default function use{prefix}UpdateFields(props: Use{prefix}UpdateFieldsProps) {{");
        sb.AppendLine($"  const {{ errors, disabledFields = [], selectFilterBys = {{}}, selectOrderBys = {{}} }} = props");
        sb.AppendLine();

        if (hasSelects)
        {
            var emittedContextDestructure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in fkFields)
            {
                if (emittedContextDestructure.Add(fk.ParentTable))
                {
                    string pluralPascal = Formatters.ToPascalCase(fk.Plural);
                    sb.AppendLine($"  const {{");
                    sb.AppendLine($"    items: {fk.Plural},");
                    sb.AppendLine($"    totalRows: {fk.Plural}TotalRows,");
                    sb.AppendLine($"    lastSearchTerm: {fk.Plural}LastSearchTerm,");
                    sb.AppendLine($"    fetchItems: fetch{pluralPascal},");
                    sb.AppendLine($"    loadMoreItems: loadMore{pluralPascal},");
                    sb.AppendLine($"  }} = {fk.HookName}()");
                    sb.AppendLine();
                }

                var firstFk = fkFields.First(f => string.Equals(f.ParentTable, fk.ParentTable, StringComparison.OrdinalIgnoreCase));
                string sharedPlural = firstFk.Plural;
                string sharedPluralPascal = Formatters.ToPascalCase(sharedPlural);

                sb.AppendLine($"  const {fk.SelectVar} = useSelect(");
                sb.AppendLine("    {");
                sb.AppendLine($"      items: {sharedPlural},");
                sb.AppendLine($"      totalRows: {sharedPlural}TotalRows,");
                sb.AppendLine($"      lastSearchTerm: {sharedPlural}LastSearchTerm,");
                sb.AppendLine($"      fetchItems: fetch{sharedPluralPascal},");
                sb.AppendLine($"      loadMoreItems: loadMore{sharedPluralPascal},");
                sb.AppendLine("    },");
                sb.AppendLine("    {");
                sb.AppendLine($"      idField: \"{fk.FieldName}\",");
                sb.AppendLine("      searchColumns: [\"name\"],");
                sb.AppendLine($"      filterBys: selectFilterBys.{fk.FieldName},");
                sb.AppendLine($"      orderBys: selectOrderBys.{fk.FieldName},");
                sb.AppendLine("    }");
                sb.AppendLine("  )");
                sb.AppendLine();
            }
        }

        var fkByField = fkFields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("  const fields = {");

        foreach (var fieldName in orderedFields)
        {
            if (properties == null || !properties.ContainsKey(fieldName)) continue;
            var prop = properties[fieldName]?.AsObject();
            if (prop == null) continue;

            string camel = Formatters.ToCamelCase(fieldName);
            string fieldType = UseFieldsGenerator.GetFieldType(fieldName, prop, searchableResources);
            string outputType = fieldType == "idnumber" ? "text" : fieldType;
            string heading = Formatters.GetFieldHeading(fieldName);
            string placeholder = GetPlaceholder(fieldName, fieldType);
            bool isRequired = requiredFields.Contains(fieldName);
            bool isSelect = fieldType == "select";
            fkByField.TryGetValue(fieldName, out var fk);

            sb.AppendLine($"    {camel}: {{");
            sb.AppendLine("      props: {");
            sb.AppendLine($"        id: \"{kebabResource}-{camel}\",");
            sb.AppendLine($"        name: \"{camel}\",");
            sb.AppendLine($"        heading: \"{heading}\",");
            sb.AppendLine($"        type: \"{outputType}\" as const,");
            sb.AppendLine($"        placeholder: \"{placeholder}\",");
            sb.AppendLine($"        disabled: disabledFields.includes(\"{camel}\"),");
            if (isSelect)
                sb.AppendLine($"        error: errors.{camel} as any,");
            else
                sb.AppendLine($"        error: errors.{camel},");
            if (fk != null)
                sb.AppendLine($"        ...{fk.SelectVar},");
            sb.AppendLine("      },");

            sb.Append("      rules: {");
            var ruleParts = new List<string>();
            if (isRequired)
                ruleParts.Add("required: \"Please fill in this field.\"");
            if ((fieldType == "text" || fieldType == "textarea") && prop["maxLength"]?.GetValue<int>() is int maxLen && maxLen > 0)
                ruleParts.Add($"maxLength: {{ value: {maxLen}, message: \"Must be less than {maxLen} characters.\" }}");
            if (ruleParts.Count > 0)
            {
                sb.AppendLine();
                foreach (var part in ruleParts)
                    sb.AppendLine($"        {part},");
                sb.AppendLine("      },");
            }
            else
            {
                sb.AppendLine(" },");
            }

            sb.AppendLine("    },");
        }

        sb.AppendLine("  }");
        sb.AppendLine();

        // Inline layout
        var groups = Formatters.BuildLayoutGroups(resource, fieldLayout, properties, searchableResources: searchableResources);
        sb.AppendLine("  const layout: FormLayout[] = [");
        foreach (var group in groups)
        {
            sb.AppendLine("    {");
            sb.AppendLine($"      groupName: \"{group.GroupName}\",");
            sb.AppendLine($"      totalColumns: {group.TotalColumns},");
            sb.AppendLine("      fields: [");
            foreach (var field in group.Fields)
            {
                string typeFragment = field.Type != null ? $", type: \"{field.Type}\"" : "";
                sb.AppendLine($"        {{ name: \"{field.Name}\", columns: {field.Columns}, heading: \"{field.Heading}\"{typeFragment} }},");
            }
            sb.AppendLine("      ],");
            sb.AppendLine("    },");
        }
        sb.AppendLine("  ]");
        sb.AppendLine();

        sb.AppendLine("  return { fields, layout }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string GetPlaceholder(string fieldName, string fieldType)
    {
        string heading = Formatters.GetFieldHeading(fieldName).ToLower();
        return fieldType switch
        {
            "select" => $"Select {heading}...",
            "date" or "datetime" => $"Select {heading}...",
            "checkbox" => $"{char.ToUpper(heading[0])}{heading[1..]}?",
            _ => $"Enter {heading}..."
        };
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;

    record UpdateEndpoint(string Module, string Resource, string RequestType, string ResponseType);
}
