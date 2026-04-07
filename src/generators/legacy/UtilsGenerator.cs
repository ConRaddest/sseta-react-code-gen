using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class UtilsGenerator
    {
        public static void GenerateLoadSettingsFile(List<string> nonCrudResources, string outputDir)
        {
            var loadSettingsContent = GenerateLoadSettings(nonCrudResources);
            string fileName = "loadSettings.ts";
            string filePath = Path.Combine(outputDir, fileName);

            // Ensure utils directory exists
            Directory.CreateDirectory(outputDir);

            File.WriteAllText(filePath, loadSettingsContent);
        }

        public static string GenerateLoadSettings(List<string> nonCrudResources)
        {
            var sb = new StringBuilder();

            // Add header
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();

            // Add internal imports section
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");

            // Generate imports for each non-CRUD resource (settings subfolder)
            foreach (string resource in nonCrudResources.OrderBy(r => r))
            {
                if (resource == "sso") continue;
                string lowerResource = Utilities.ToCamelCase(resource);
                sb.AppendLine($"import {{ search{resource} }} from \"@/services/legacy/settings/{resource}Service\"");
            }

            // Add custom imports
            sb.AppendLine("import { LoadSettingsConfig, Setting, SettingsConfig } from \"@/types/legacy/custom/Setting\"");
            sb.AppendLine("import { SearchRequest } from \"@/types/legacy/custom/Api\"");
            sb.AppendLine();

            // Add external imports section
            sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");
            sb.AppendLine("import { AxiosError } from \"axios\"");
            sb.AppendLine();

            // Add interfaces section
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ----------------------- INTERFACES -------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            // Interfaces are now imported from @/types/legacy/custom/Setting, so we don't generate them here

            // Generate AllSettings interface
            sb.AppendLine("interface AllSettings {");
            foreach (string resource in nonCrudResources.OrderBy(r => r))
            {
                if (resource == "sso") continue;
                string lowerResource = Utilities.ToCamelCase(resource);
                sb.AppendLine($"  {lowerResource}: Setting[]");
            }
            sb.AppendLine("}");
            sb.AppendLine();

            // Add configs section
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ------------------------- CONFIGS --------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("const settingsConfigs: Record<keyof AllSettings, SettingsConfig> = {");

            // Generate configs for each resource
            var configEntries = new List<string>();
            foreach (string resource in nonCrudResources.OrderBy(r => r))
            {
                if (resource == "sso") continue;
                string lowerResource = Utilities.ToCamelCase(resource);
                string idField = $"{lowerResource}Id";

                var configEntry = new StringBuilder();
                configEntry.AppendLine($"  {lowerResource}: {{");
                configEntry.AppendLine($"    service: search{resource},");
                configEntry.AppendLine($"    permission: \"{resource}.Retrieve\",");
                configEntry.AppendLine($"    idField: \"{idField}\",");
                configEntry.Append("  },");

                configEntries.Add(configEntry.ToString());
            }
            sb.AppendLine(string.Join("\n", configEntries));
            sb.AppendLine("}");
            sb.AppendLine();

            // Add functions section
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ----------------------- FUNCTIONS --------------------------");
            sb.AppendLine("// -------------------------------------------------------------");

            // Generate loadAllSettings function
            sb.AppendLine("export async function loadAllSettings(");
            sb.AppendLine("  hasPermission: (permission: string) => boolean");
            sb.AppendLine("): Promise<AllSettings> {");
            sb.AppendLine("  const settings: AllSettings = {");

            foreach (string resource in nonCrudResources.OrderBy(r => r))
            {
                if (resource == "sso") continue;
                string lowerResource = Utilities.ToCamelCase(resource);
                sb.AppendLine($"    {lowerResource}: [],");
            }

            sb.AppendLine("  }");
            sb.AppendLine();

            sb.AppendLine("  const searchRequest: SearchRequest = {");
            sb.AppendLine("    pageNumber: -1,");
            sb.AppendLine("    pageSize: -1,");
            sb.AppendLine("    filterByList: [],");
            sb.AppendLine("    orderByList: [],");
            sb.AppendLine("  }");
            sb.AppendLine();

            sb.AppendLine("  const loadPromises = Object.entries(settingsConfigs).map(async ([key, config]) => {");
            sb.AppendLine("    if (hasPermission(config.permission)) {");
            sb.AppendLine("      try {");
            sb.AppendLine("        const response = await config.service(searchRequest)");
            sb.AppendLine("        const transformedData: Setting[] = response.data.searchResults.map((item: any) => ({");
            sb.AppendLine("          id: item[config.idField],");
            sb.AppendLine("          label: item.name,");
            sb.AppendLine("        }))");
            sb.AppendLine("        settings[key as keyof AllSettings] = transformedData");
            sb.AppendLine("      } catch (error) {");
            sb.AppendLine("        if (error instanceof AxiosError) {");
            sb.AppendLine("          console.error(`Error loading ${key}:`, error)");
            sb.AppendLine("        }");
            sb.AppendLine("      }");
            sb.AppendLine("    }");
            sb.AppendLine("  })");
            sb.AppendLine();

            sb.AppendLine("  await Promise.all(loadPromises)");
            sb.AppendLine("  return settings");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate loadSettings function
            sb.AppendLine("export async function loadSettings({");
            sb.AppendLine("  service,");
            sb.AppendLine("  idField,");
            sb.AppendLine("  labelField = \"name\",");
            sb.AppendLine("  orderBy = \"name\",");
            sb.AppendLine("  orderDescending = false,");
            sb.AppendLine("  pageSize = 9999,");
            sb.AppendLine("  filterByList = [],");
            sb.AppendLine("}: LoadSettingsConfig): Promise<Setting[]> {");
            sb.AppendLine("  try {");
            sb.AppendLine("    const searchBody: SearchRequest = {");
            sb.AppendLine("      pageNumber: 1,");
            sb.AppendLine("      pageSize: pageSize,");
            sb.AppendLine("      filterByList: filterByList,");
            sb.AppendLine("      orderByList: [");
            sb.AppendLine("        {");
            sb.AppendLine("          columnName: orderBy,");
            sb.AppendLine("          isDescending: orderDescending,");
            sb.AppendLine("        },");
            sb.AppendLine("      ],");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    const response = await service(searchBody)");
            sb.AppendLine();
            sb.AppendLine("    const transformedData: Setting[] = response.data.searchResults?.map((item: any) => ({");
            sb.AppendLine("      id: item[idField],");
            sb.AppendLine("      label: item[labelField],");
            sb.AppendLine("    }))");
            sb.AppendLine();
            sb.AppendLine("    return transformedData");
            sb.AppendLine("  } catch (error) {");
            sb.AppendLine("    if (error instanceof AxiosError) {");
            sb.AppendLine("      console.error(`Error loading options:`, error)");
            sb.AppendLine("    }");
            sb.AppendLine("    throw error");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();

            // Add exports
            sb.AppendLine("export type { AllSettings, Setting }");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
