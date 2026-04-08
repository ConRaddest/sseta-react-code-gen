using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates {Prefix}DeleteForm.tsx for every resource that has a Delete endpoint.
// Reuses the view layout from ../view/{Prefix}ViewLayout.
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/delete/{Prefix}DeleteForm.tsx
static class DeleteFormGenerator
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
        // Collect resources that have a Retrieve (view) endpoint — delete forms require it.
        var viewResources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rawPath, _) in paths)
        {
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (string.Equals(parts[4], "Retrieve", StringComparison.OrdinalIgnoreCase))
                viewResources.Add($"{parts[2]}.{parts[3]}");
        }

        var endpoints = new List<DeleteEndpoint>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.Delete"))) continue;
            if (!string.Equals(parts[4], "Delete", StringComparison.OrdinalIgnoreCase)) continue;

            string module = parts[2];
            string resource = parts[3];

            if (!viewResources.Contains($"{module}.{resource}"))
            {
                Console.WriteLine($"    ⚠ Skipping: {module}/{resource} delete form — no view/retrieve endpoint found.");
                continue;
            }

            foreach (var (_, opNode) in pathNode.AsObject())
            {
                if (opNode == null) continue;
                endpoints.Add(new DeleteEndpoint(module, resource));
                break;
            }
        }

        int count = 0;
        foreach (var ep in endpoints)
        {
            string modulePascal = Formatters.ToPascalCase(ep.Module.ToLower());
            string prefix = modulePascal + ep.Resource;
            string kebabResource = Formatters.ToKebabCase(ep.Resource);
            string dir = Path.Combine(formsOutputDir, ep.Module.ToLower(), kebabResource, "delete");
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, $"DeleteForm.tsx"),
                ApplyTemplate(RenderForm(ep, prefix), templatePath));

            Console.WriteLine($"    ✓ {ep.Module}/{ep.Resource}");
            count++;
        }

        Console.WriteLine($"    {count} delete form(s) generated.");
    }

    static string RenderForm(DeleteEndpoint ep, string prefix)
    {
        string contextHook = $"use{prefix}";
        string contextPath = $"@/contexts/resources/{ep.Module.ToLower()}/{prefix}Context";
        string idField = Formatters.GetIdFieldName(ep.Resource);
        string displayName = Formatters.ToTitleCase(ep.Resource);

        var sb = new StringBuilder();

        sb.AppendLine("\"use client\"");
        sb.AppendLine();
        sb.AppendLine("import { useState } from \"react\"");
        sb.AppendLine("import { DeleteTemplate, extractApiErrors } from \"@sseta/components\"");
        sb.AppendLine($"import {{ {contextHook} }} from \"{contextPath}\"");
        sb.AppendLine("import { useToast } from \"@/contexts/general/ToastContext\"");
        sb.AppendLine($"import {prefix}View from \"../view/ViewForm\"");
        sb.AppendLine();

        sb.AppendLine($"interface {prefix}DeleteProps {{");
        sb.AppendLine($"  {idField}: number");
        sb.AppendLine("  hiddenFields?: string[]");
        sb.AppendLine("  renderActionsInFooter?: boolean");
        sb.AppendLine("  className?: string");
        sb.AppendLine("  loading?: boolean");
        sb.AppendLine($"  onDeleted?: ({idField}: number) => void");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine($"export default function {prefix}Delete(props: {prefix}DeleteProps) {{");
        sb.AppendLine("  const {");
        sb.AppendLine($"    {idField},");
        sb.AppendLine("    hiddenFields,");
        sb.AppendLine("    renderActionsInFooter = true,");
        sb.AppendLine("    className = \"px-6 py-4\",");
        sb.AppendLine("    loading,");
        sb.AppendLine("    onDeleted,");
        sb.AppendLine("  } = props");
        sb.AppendLine();
        sb.AppendLine("  const [apiError, setApiError] = useState<string | undefined>()");
        sb.AppendLine("  const [scrollTrigger, setScrollTrigger] = useState(0)");
        sb.AppendLine("  const [isSubmitting, setIsSubmitting] = useState(false)");
        sb.AppendLine();
        sb.AppendLine($"  const {{ destroy }} = {contextHook}()");
        sb.AppendLine("  const { showToast } = useToast()");
        sb.AppendLine();
        sb.AppendLine("  const onDelete = async () => {");
        sb.AppendLine("    setApiError(undefined)");
        sb.AppendLine("    setIsSubmitting(true)");
        sb.AppendLine("    try {");
        sb.AppendLine($"      await destroy({idField})");
        sb.AppendLine($"      showToast(\"{displayName} successfully deleted\", \"success\")");
        sb.AppendLine($"      onDeleted?.({idField})");
        sb.AppendLine("    } catch (error: any) {");
        sb.AppendLine("      setApiError(extractApiErrors(error)[0])");
        sb.AppendLine("      setScrollTrigger((t) => t + 1)");
        sb.AppendLine("    } finally {");
        sb.AppendLine("      setIsSubmitting(false)");
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine();
        sb.AppendLine("  return (");
        sb.AppendLine("    <DeleteTemplate");
        sb.AppendLine($"      entityName=\"{displayName}\"");
        sb.AppendLine("      onDelete={onDelete}");
        sb.AppendLine("      renderActionsInFooter={renderActionsInFooter}");
        sb.AppendLine("      loading={loading ?? isSubmitting}");
        sb.AppendLine("      errorMessage={apiError}");
        sb.AppendLine("      scrollToTopTrigger={scrollTrigger}");
        sb.AppendLine("      className={className}");
        sb.AppendLine("    >");
        sb.AppendLine($"      <{prefix}View");
        sb.AppendLine($"        {idField}={{{idField}}}");
        sb.AppendLine("        hiddenFields={hiddenFields}");
        sb.AppendLine("        className=\"p-0\"");
        sb.AppendLine("      />");
        sb.AppendLine("    </DeleteTemplate>");
        sb.AppendLine("  )");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;

    record DeleteEndpoint(string Module, string Resource);
}
