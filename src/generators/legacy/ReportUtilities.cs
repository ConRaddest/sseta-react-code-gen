using System.Text;
using System.Text.RegularExpressions;

namespace ReactCodegen.Legacy
{
    public static class ReportUtilities
    {
        // -------------------------------------------------------------
        // ----------------------- DATA COLUMN TYPES -------------------
        // -------------------------------------------------------------
        public class DataColumn
        {
            public string Key { get; set; } = "";
            public string DbKey { get; set; } = "";
            public string Label { get; set; } = "";
            public string Type { get; set; } = "";
            public bool IsComplex { get; set; }
            public bool IsCustomRender { get; set; }
            public string? CustomRenderType { get; set; }
        }

        public class ReportMetadata
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Route { get; set; } = "";
            public string Category { get; set; } = "operational";
        }

        // -------------------------------------------------------------
        // ------------------- DEPARTMENT LANDING PAGE ----------------
        // -------------------------------------------------------------
        public static void GenerateDepartmentLandingPage(string departmentName, List<ReportMetadata> reports, string pageDir)
        {
            try
            {
                var sb = new StringBuilder();
                string departmentPascal = Utilities.ToPascalCase(departmentName);
                string departmentTitle = Utilities.ToTitleCase(departmentName);

                // Determine category for this landing page
                bool isSetmis = reports.Any(r => r.Category == "setmis");
                string categoryLabel = isSetmis ? "SETMIS" : "Operational";

                // Generate the landing page content
                sb.AppendLine("\"use client\"");
                sb.AppendLine();
                sb.AppendLine("// !!---------------------------------------------------------!!");
                sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
                sb.AppendLine("// !!---------------------------------------------------------!!");
                sb.AppendLine();
                sb.AppendLine("// -------------------- ICONS ------------------------");
                sb.AppendLine("import { TbReportAnalytics, TbChevronRight } from \"react-icons/tb\"");
                sb.AppendLine();
                sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
                sb.AppendLine("import { PageHeader } from \"@/components/legacy/PageHeader/PageHeader\"");
                sb.AppendLine();
                sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");
                sb.AppendLine("import { useRouter } from \"next/navigation\"");
                sb.AppendLine();
                sb.AppendLine("// -------------------------------------------------------------");
                sb.AppendLine("// ------------------------- TYPES -----------------------------");
                sb.AppendLine("// -------------------------------------------------------------");
                sb.AppendLine("interface ReportCard {");
                sb.AppendLine("  title: string");
                sb.AppendLine("  route: string");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("// -------------------------------------------------------------");
                sb.AppendLine("// ------------------------- CONSTS ----------------------------");
                sb.AppendLine("// -------------------------------------------------------------");
                sb.AppendLine("const reports: ReportCard[] = [");

                foreach (var report in reports.OrderBy(r => r.Title))
                {
                    sb.AppendLine("  {");
                    sb.AppendLine($"    title: \"{report.Title}\",");
                    sb.AppendLine($"    route: \"{report.Route}\",");
                    sb.AppendLine("  },");
                }

                sb.AppendLine("]");
                sb.AppendLine();
                sb.AppendLine("// -------------------------------------------------------------");
                sb.AppendLine("// ------------------------- PAGE ------------------------------");
                sb.AppendLine("// -------------------------------------------------------------");
                string pageTitle = isSetmis ? "SETMIS Reports" : $"{departmentTitle} {categoryLabel} Reports";
                string pageSubtitle = isSetmis
                    ? "Access SETMIS reporting and data exports."
                    : $"Access operational reporting and analytics for {departmentName.ToLower()} management.";

                sb.AppendLine($"export default function {departmentPascal}{categoryLabel}ReportsPage() {{");
                sb.AppendLine("  const router = useRouter()");
                sb.AppendLine();
                sb.AppendLine("  return (");
                sb.AppendLine("    <div className=\"max-w-7xl mx-auto\">");
                sb.AppendLine("      <PageHeader");
                sb.AppendLine("        icon={TbReportAnalytics}");
                sb.AppendLine($"        title=\"{pageTitle}\"");
                sb.AppendLine($"        subtitle=\"{pageSubtitle}\"");
                sb.AppendLine("      />");
                sb.AppendLine();
                sb.AppendLine("      <div className=\"grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 mt-6\">");
                sb.AppendLine("        {reports.map((report) => {");
                sb.AppendLine("          return (");
                sb.AppendLine("            <button");
                sb.AppendLine("              key={report.route}");
                sb.AppendLine("              onClick={() => router.push(report.route)}");
                sb.AppendLine("              className={`");
                sb.AppendLine("                group");
                sb.AppendLine("                relative");
                sb.AppendLine("                flex");
                sb.AppendLine("                items-center");
                sb.AppendLine("                justify-between");
                sb.AppendLine("                min-h-20");
                sb.AppendLine("                px-5");
                sb.AppendLine("                py-4");
                sb.AppendLine("                bg-white");
                sb.AppendLine("                border");
                sb.AppendLine("                border-neutral-200");
                sb.AppendLine("                rounded-lg");
                sb.AppendLine("                transition-all");
                sb.AppendLine("                duration-200");
                sb.AppendLine("                hover:border-orange-300");
                sb.AppendLine("                text-left");
                sb.AppendLine("                cursor-pointer");
                sb.AppendLine("              `}");
                sb.AppendLine("            >");
                sb.AppendLine("              <h3 className=\"text-base font-medium text-neutral-900 group-hover:text-orange-500 transition-colors leading-snug\">");
                sb.AppendLine("                {report.title}");
                sb.AppendLine("              </h3>");
                sb.AppendLine("              <div className=\"shrink-0 text-neutral-400 group-hover:text-orange-500 transition-colors\">");
                sb.AppendLine("                <TbChevronRight size={18} />");
                sb.AppendLine("              </div>");
                sb.AppendLine("            </button>");
                sb.AppendLine("          )");
                sb.AppendLine("        })}");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
                sb.AppendLine("  )");
                sb.AppendLine("}");

                // Determine the landing page directory based on report category
                string landingPageDir;

                if (isSetmis)
                {
                    // For SETMIS: app/home/setmis/page.tsx (pageDir is already app/home)
                    landingPageDir = Path.Combine(pageDir, departmentName.ToLower());
                }
                else
                {
                    // For Operational: app/home/{department}/reports/operational/page.tsx
                    string homeDir = Path.GetDirectoryName(pageDir) ?? pageDir;
                    string departmentDir = Path.Combine(homeDir, departmentName.ToLower());
                    string reportsDir = Path.Combine(departmentDir, "reports");
                    landingPageDir = Path.Combine(reportsDir, "operational");
                }

                Directory.CreateDirectory(landingPageDir);

                string landingPagePath = Path.Combine(landingPageDir, "page.tsx");
                File.WriteAllText(landingPagePath, sb.ToString());

                Console.WriteLine($"  ✓ {departmentTitle} {categoryLabel} landing page");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Error generating landing page: {ex.Message}");
            }
        }

        // -------------------------------------------------------------
        // -------------------- PATH NORMALIZATION ---------------------
        // -------------------------------------------------------------
        public static string NormalizePathParameters(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return segment;

            return Regex.Replace(segment, @"\{([^}]+)\}", match =>
            {
                var paramName = match.Groups[1].Value;
                return "{" + Utilities.ToCamelCase(paramName) + "}";
            });
        }

        // -------------------------------------------------------------
        // ---------------------- ROUTE GENERATION ---------------------
        // -------------------------------------------------------------
        public static void UpdateReportRoutes(int objectId, string reportRoute, string outputRoot)
        {
            try
            {
                // outputRoot is typically "C:/DEV/PMIS/react-frontend/src"
                // so we can directly use it to build the path to config/reportRoutes.ts
                var reportRoutesPath = Path.Combine(outputRoot, "config", "reportRoutes.ts");

                // Parse existing mappings if file exists
                var mappings = new Dictionary<int, string>();
                if (File.Exists(reportRoutesPath))
                {
                    var lines = File.ReadAllLines(reportRoutesPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*"))
                            continue;

                        // Parse mapping: 147: "/home/bursary/reports/bursary",
                        var match = Regex.Match(trimmed, @"^(\d+):\s*[""']([^""']+)[""']");
                        if (match.Success)
                        {
                            var id = int.Parse(match.Groups[1].Value);
                            var route = match.Groups[2].Value;
                            mappings[id] = route;
                        }
                    }
                }

                // Add or update the mapping
                mappings[objectId] = reportRoute;

                // Generate the complete file content
                var sb = new StringBuilder();
                sb.AppendLine("// !!---------------------------------------------------------!!");
                sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
                sb.AppendLine("// !!---------------------------------------------------------!!");
                sb.AppendLine();
                sb.AppendLine("// Maps fullReportObjectId to the corresponding report page route");
                sb.AppendLine();
                sb.AppendLine("/* This configuration is used by widgets to navigate to the full report");
                sb.AppendLine(" * when a user clicks on a chart section.");
                sb.AppendLine(" */");
                sb.AppendLine("export const REPORT_ROUTES: Record<number, string> = {");

                foreach (var mapping in mappings.OrderBy(m => m.Key))
                {
                    sb.AppendLine($"  {mapping.Key}: \"{mapping.Value}\",");
                }

                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("/**");
                sb.AppendLine(" * Get the route for a given fullReportObjectId");
                sb.AppendLine(" * @param reportObjectId - The fullReportObjectId from widget metadata");
                sb.AppendLine(" * @returns The route path, or null if not found");
                sb.AppendLine(" */");
                sb.AppendLine("export function getReportRoute(reportObjectId: number): string | null {");
                sb.AppendLine("  return REPORT_ROUTES[reportObjectId] ?? null");
                sb.AppendLine("}");

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(reportRoutesPath)!);

                // Completely overwrite the file
                File.WriteAllText(reportRoutesPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠ Error updating reportRoutes.ts: {ex.Message}");
            }
        }
    }
}
