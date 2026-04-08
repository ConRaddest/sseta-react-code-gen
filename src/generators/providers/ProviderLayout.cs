using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates the app/layout.tsx file for a portal, wrapping {children} with all
// generated resource context providers.
//
// The static shell (font, metadata, html/body, general providers) lives in the
// template. This generator injects:
//   // [[PROVIDER_IMPORTS]]  → one import line per provider
//   // [[PROVIDERS]]         → the nested JSX, innermost first
//
// Output path: {appOutputDir}/layout.tsx
static class ProviderLayoutGenerator
{
    public static void Generate(
        JsonObject paths,
        string appOutputDir,
        string templatePath,
        HashSet<string>? blacklist = null,
        HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        // Collect module/resource pairs that will have a generated context — mirroring
        // the same blacklist logic as ContextGenerator so the two stay in sync.
        // A resource is included only if it has at least one non-blacklisted operation.
        var resourceOps = new SortedDictionary<(string Module, string Resource), HashSet<string>>(
            Comparer<(string Module, string Resource)>.Create((a, b) =>
            {
                int c = string.Compare(a.Module, b.Module, StringComparison.Ordinal);
                return c != 0 ? c : string.Compare(a.Resource, b.Resource, StringComparison.Ordinal);
            })
        );

        foreach (var (rawPath, _) in paths)
        {
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;

            string module    = parts[2];
            string resource  = parts[3];
            string operation = parts[4];

            if (blacklist != null && blacklist.Contains($"{module}.{resource}")) continue;
            if (blacklist != null && blacklist.Contains($"{module}.{resource}.{operation}")) continue;

            var key = (module, resource);
            if (!resourceOps.ContainsKey(key)) resourceOps[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            resourceOps[key].Add(operation);
        }

        // Only keep resources that ended up with at least one operation
        var pairs = resourceOps.Keys.Where(k => resourceOps[k].Count > 0).ToList();

        // Build imports and nested JSX
        var imports = new StringBuilder();
        // Providers are listed outermost-first for the JSX nesting; we build
        // them innermost-first then reverse so the last provider wraps {children}.
        var providers = new List<string>();

        foreach (var (module, resource) in pairs.OrderBy(p => p.Module).ThenBy(p => p.Resource))
        {
            string modulePascal = Formatters.ToPascalCase(module.ToLower());
            string prefix       = modulePascal + resource;
            string importPath   = $"@/contexts/resources/{module.ToLower()}/{prefix}Context";

            imports.AppendLine($"import {{ {prefix}Provider }} from \"{importPath}\"");
            providers.Add(prefix);
        }

        // Render nested JSX — outermost provider first, innermost wraps {children}
        string nestedJsx = RenderNested(providers);

        string template = File.ReadAllText(templatePath);
        string output = template
            .Replace("// [[PROVIDER_IMPORTS]]", imports.ToString().TrimEnd())
            .Replace("                    {/* [[PROVIDERS]] */}", nestedJsx);

        Directory.CreateDirectory(appOutputDir);
        File.WriteAllText(Path.Combine(appOutputDir, "layout.tsx"), output);

        Console.WriteLine($"    ✓ layout.tsx  ({providers.Count} provider(s))");
    }

    // Renders the nested JSX with each provider indented relative to <ToastProvider>.
    // providers[0] is outermost (least indented), providers[^1] wraps {children}.
    // Base indent matches the existing <ToastProvider> indent level (18 spaces).
    static string RenderNested(List<string> providers)
    {
        if (providers.Count == 0) return "{children}";

        const int baseIndent = 20; // spaces to align with <ToastProvider> content
        var sb = new StringBuilder();

        for (int i = 0; i < providers.Count; i++)
        {
            string pad = new string(' ', baseIndent + i * 2);
            sb.AppendLine($"{pad}<{providers[i]}Provider>");
        }

        // Innermost content: {children}
        string innerPad = new string(' ', baseIndent + providers.Count * 2);
        sb.AppendLine($"{innerPad}{{children}}");

        for (int i = providers.Count - 1; i >= 0; i--)
        {
            string pad = new string(' ', baseIndent + i * 2);
            sb.Append($"{pad}</{providers[i]}Provider>");
            if (i > 0) sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
