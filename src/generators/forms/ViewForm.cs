using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates, for every resource that has a Retrieve endpoint:
//   {Prefix}ViewForm.tsx
//   use{Prefix}ViewFields.ts
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/view/
static class ViewFormGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        JsonObject? fieldLayout,
        string formsOutputDir,
        HashSet<string>? blacklist = null,
        string? formTemplatePath = null,
        string? layoutTemplatePath = null,
        string apiPrefix = "management")
    {
        var endpoints = new List<ViewEndpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || parts[1] != apiPrefix) continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.View"))) continue;
            if (!string.Equals(parts[4], "Retrieve", StringComparison.OrdinalIgnoreCase)) continue;

            string module   = parts[2];
            string resource = parts[3];

            foreach (var (_, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;

                string? responseRef = opNode["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string responseType = Formatters.ResolveResponseType(responseRef, schemas);

                endpoints.Add(new ViewEndpoint(module, resource, responseType));
                break; // one operation per path
            }
        }

        int count = 0;
        foreach (var ep in endpoints)
        {
            string modulePascal  = Formatters.ToPascalCase(ep.Module.ToLower());
            string prefix        = modulePascal + ep.Resource;
            string kebabResource = Formatters.ToKebabCase(ep.Resource);
            string dir = Path.Combine(formsOutputDir, ep.Module.ToLower(), kebabResource, "view");
            Directory.CreateDirectory(dir);

            // Retrieve response schema — the responseType is the bare resource name (e.g. "AccessStaffRoleRequest")
            var retrieveSchema = Formatters.FindSchema(schemas, ep.ResponseType)
                              ?? Formatters.FindSchema(schemas, $"{ep.Resource}{ep.Module}RetrieveResponseModel")
                              ?? Formatters.FindSchema(schemas, $"{ep.Resource}RetrieveResponseModel");

            var properties = retrieveSchema?["properties"]?.AsObject();

            var searchableResources = Formatters.BuildSearchableResources(paths, ep.Module, apiPrefix);

            File.WriteAllText(Path.Combine(dir, $"ViewForm.tsx"),
                ApplyTemplate(RenderForm(ep, prefix), formTemplatePath));

            File.WriteAllText(Path.Combine(dir, $"useViewFields.ts"),
                ApplyTemplate(RenderViewFields(prefix, ep.Resource, fieldLayout, properties, searchableResources), layoutTemplatePath));

            Console.WriteLine($"    ✓ {ep.Module}/{ep.Resource}");
            count++;
        }

        Console.WriteLine($"    {count} view form(s) generated.");
    }

    static string RenderForm(ViewEndpoint ep, string prefix)
    {
        string contextHook  = $"use{prefix}";
        string contextPath  = $"@/contexts/resources/{ep.Module.ToLower()}/{prefix}Context";
        string idField      = Formatters.GetIdFieldName(ep.Resource);
        string typesPath    = "@/types/api.types";
        string displayName  = Formatters.ToTitleCase(ep.Resource);

        var sb = new StringBuilder();

        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import { ReactNode, useState, useEffect } from \"react\"");
        sb.AppendLine("import { ViewTemplate } from \"@sseta/components\"");
        sb.AppendLine($"import {{ {contextHook} }} from \"{contextPath}\"");
        sb.AppendLine($"import {{ {ep.ResponseType} }} from \"{typesPath}\"");
        sb.AppendLine($"import use{prefix}View from \"./useViewFields\"");
        sb.AppendLine();

        sb.AppendLine($"interface {prefix}ViewProps {{");
        sb.AppendLine($"  {idField}: number");
        sb.AppendLine("  hiddenFields?: string[]");
        sb.AppendLine("  className?: string");
        sb.AppendLine("  loading?: boolean");
        sb.AppendLine("  children?: ReactNode");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine($"export default function {prefix}View(props: {prefix}ViewProps) {{");
        sb.AppendLine($"  const {{ {idField}, hiddenFields, className = \"px-6 py-4\", loading: loadingOverride, children }} = props");
        sb.AppendLine();
        sb.AppendLine($"  const [record, setRecord] = useState<{ep.ResponseType} | null>(null)");
        sb.AppendLine("  const [loading, setLoading] = useState(false)");
        sb.AppendLine("  const isLoading = loadingOverride ?? loading");
        sb.AppendLine();
        sb.AppendLine($"  const {{ retrieve }} = {contextHook}()");
        sb.AppendLine($"  const {{ layout }} = use{prefix}View()");
        sb.AppendLine();
        sb.AppendLine("  useEffect(() => {");
        sb.AppendLine("    const fetchRecord = async () => {");
        sb.AppendLine("      setLoading(true)");
        sb.AppendLine("      try {");
        sb.AppendLine($"        const result = await retrieve({idField})");
        sb.AppendLine("        if (result) setRecord(result)");
        sb.AppendLine("      } catch (error) {");
        sb.AppendLine($"        console.error(\"Failed to fetch {displayName.ToLower()}:\", error)");
        sb.AppendLine("      } finally {");
        sb.AppendLine("        setLoading(false)");
        sb.AppendLine("      }");
        sb.AppendLine("    }");
        sb.AppendLine("    fetchRecord()");
        sb.AppendLine("  }, [])");
        sb.AppendLine();
        sb.AppendLine("  return (");
        sb.AppendLine("    <ViewTemplate");
        sb.AppendLine("      layout={layout}");
        sb.AppendLine("      record={record}");
        sb.AppendLine("      isLoading={isLoading}");
        sb.AppendLine("      hiddenFields={hiddenFields}");
        sb.AppendLine("      className={className}");
        sb.AppendLine("    >");
        sb.AppendLine("      {children}");
        sb.AppendLine("    </ViewTemplate>");
        sb.AppendLine("  )");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string RenderViewFields(string prefix, string resource, JsonObject? fieldLayout, JsonObject? properties, HashSet<string>? searchableResources = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { FormLayout } from \"@sseta/components\"");
        sb.AppendLine();

        sb.AppendLine($"export default function use{prefix}View() {{");

        var groups = Formatters.BuildLayoutGroups(resource, fieldLayout, properties, excludeFkFields: true, searchableResources: searchableResources, extraExclusions: Formatters.ExcludedViewFields);
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
        sb.AppendLine("  return { layout }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;

    record ViewEndpoint(string Module, string Resource, string ResponseType);
}
