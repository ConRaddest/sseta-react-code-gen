using System.Text.Json;
using System.Text.Json.Nodes;
using ReactCodegen;

namespace ReactCodegen.Legacy
{
    static class LegacyProgram
    {
        // Swagger source — single legacy API (management portal)
        const string swaggerApiUrl = "https://localhost:7050/swagger/v1/swagger.json";
        const string apiBaseUrl = "https://localhost:7050/api/";
        const string authEmail = "admin@altron.com";
        const string authPassword = "123";

        const string configPath = "input/codegen.config.json";

        private static StreamWriter? _logWriter;
        private static TextWriter? _originalConsoleOut;

        public static async Task RunAsync()
        {
            // ---------------------------------------------------------------
            // Load portal output paths from config
            // ---------------------------------------------------------------
            var configNode = JsonNode.Parse(File.ReadAllText(configPath));
            var portalsArray = configNode?["portals"]?.AsArray()
                ?? throw new Exception("Missing portals array in config.");

            var portalBaseDirs = portalsArray
                .Where(p => p != null)
                .Select(p => p!["output"]?["baseDir"]?.GetValue<string>()
                    ?? throw new Exception($"Portal missing output.baseDir."))
                .ToArray();

            string managementBaseDir = portalsArray
                .Where(p => p?["name"]?.GetValue<string>() == "management")
                .Select(p => p!["output"]!["baseDir"]!.GetValue<string>())
                .First();

            // ---------------------------------------------------------------
            // Setup logging
            // ---------------------------------------------------------------
            Directory.CreateDirectory("output");
            string logFilePath = Path.Combine("output", "legacy-codegen-log.txt");
            _logWriter = new StreamWriter(logFilePath, false) { AutoFlush = true };
            _originalConsoleOut = Console.Out;

            _logWriter.WriteLine("=================================================================");
            _logWriter.WriteLine($"Legacy Code Generation Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logWriter.WriteLine("=================================================================");
            _logWriter.WriteLine();

            var multiWriter = new MultiTextWriter(Console.Out, _logWriter);
            Console.SetOut(multiWriter);

            try
            {
                // Fetch swagger from legacy API
                Console.WriteLine($"Fetching swagger: {swaggerApiUrl}");
                string swaggerJson = await FetchSwaggerFromApi();

                Directory.CreateDirectory("input/swagger");
                string swaggerCachePath = Path.Combine("input", "swagger", "legacy-swagger.json");
                await File.WriteAllTextAsync(swaggerCachePath, swaggerJson);
                Console.WriteLine();

                await GenerateCode(swaggerCachePath, portalBaseDirs, managementBaseDir);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
            finally
            {
                if (_originalConsoleOut != null)
                    Console.SetOut(_originalConsoleOut);

                _logWriter?.Close();
                _logWriter?.Dispose();

                _originalConsoleOut?.WriteLine($"\nLog saved to: {logFilePath}");
            }
        }

        static async Task<string> FetchSwaggerFromApi()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                var response = await client.GetAsync(swaggerApiUrl);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch swagger from API: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Request timed out while fetching swagger from API: {ex.Message}", ex);
            }
        }

        static async Task GenerateCode(string swaggerPath, string[] portalBaseDirs, string managementBaseDir)
        {
            string jsonString = File.ReadAllText(swaggerPath);
            JsonNode? jsonNode = JsonNode.Parse(jsonString);
            if (jsonNode == null)
            {
                Console.WriteLine("Failed to parse swagger JSON.");
                return;
            }

            var info = jsonNode["info"];
            if (info != null)
            {
                Console.WriteLine($"OpenAPI Version: {jsonNode["openapi"]?.GetValue<string>()}");
                Console.WriteLine($"Title:           {info["title"]?.GetValue<string>()}");
                Console.WriteLine($"Version:         {info["version"]?.GetValue<string>()}");
                Console.WriteLine();
            }

            var paths = jsonNode["paths"]?.AsObject();
            if (paths == null)
            {
                Console.WriteLine("No paths found in swagger.");
                return;
            }

            var schemas = jsonNode["components"]?["schemas"]?.AsObject();
            var resourceGroups = SwaggerProcessor.GroupPathsByResource(paths);
            var (coreResources, settingsResources) = BuildResourceLists(resourceGroups);

            // ---------------------------------------------------------------
            // Seed legacy field layout and generate legacy fields manifest
            // ---------------------------------------------------------------
            string fieldTemplatesPath = Path.Combine("input", "form-layout", "legacy-layout.json");
            string legacyManifestPath = Path.Combine("input", "fields-manifest", "legacy-fields.json");

            Console.WriteLine("=================================================================");
            Console.WriteLine("Legacy Form Layout Seed");
            Console.WriteLine("=================================================================");
            LegacyFormLayoutSeedGenerator.Generate(schemas, fieldTemplatesPath);
            Console.WriteLine();

            Console.WriteLine("=================================================================");
            Console.WriteLine("Legacy Fields Manifest");
            Console.WriteLine("=================================================================");
            LegacyFieldsManifestGenerator.Generate(schemas, fieldTemplatesPath, legacyManifestPath);
            Console.WriteLine();

            // ---------------------------------------------------------------
            // Management-only: reports and pages
            // ---------------------------------------------------------------
            string mgmtServiceDir = Path.Combine(managementBaseDir, "services", "legacy");
            string mgmtTypeDir = Path.Combine(managementBaseDir, "types", "legacy");
            string mgmtCrudPagesDir = Path.Combine(managementBaseDir, "components", "legacy", "Pages", "List");
            string mgmtOperationalReportPagesDir = Path.Combine(managementBaseDir, "components", "legacy", "Pages", "Reports", "Operational");
            string mgmtSetmisReportPagesDir = Path.Combine(managementBaseDir, "components", "legacy", "Pages", "Reports", "Setmis");
            string mgmtTargetDir = Path.Combine(managementBaseDir);

            Console.WriteLine("=================================================================");
            Console.WriteLine("Reports & Pages (management portal only)");
            Console.WriteLine("=================================================================");

            Directory.CreateDirectory(Path.Combine(mgmtServiceDir, "report"));
            Directory.CreateDirectory(Path.Combine(mgmtTypeDir, "report"));
            Directory.CreateDirectory(mgmtCrudPagesDir);

            Directory.CreateDirectory(mgmtOperationalReportPagesDir);
            Directory.CreateDirectory(mgmtSetmisReportPagesDir);

            Console.WriteLine("  Generating OPERATIONAL reporting types, services & pages");
            var operationalEndpoints = SwaggerProcessor.ExtractReportingEndpoints(paths);
            ReportingGenerator.GenerateReportingFiles(paths, schemas, mgmtServiceDir, mgmtTypeDir, mgmtOperationalReportPagesDir, apiBaseUrl, authEmail, authPassword, coreResources, settingsResources, mgmtTargetDir);

            Console.WriteLine("  Generating SETMIS reporting types, services & pages");
            var setmisEndpoints = SwaggerProcessor.ExtractSetmisEndpoints(paths);
            SetmisReportGenerator.GenerateSetmisFiles(paths, schemas, mgmtServiceDir, mgmtTypeDir, mgmtSetmisReportPagesDir);
            Console.WriteLine();

            // ---------------------------------------------------------------
            // All portals: services, types, field-hooks, utils, API files
            // ---------------------------------------------------------------
            foreach (string baseDir in portalBaseDirs)
            {
                Console.WriteLine("=================================================================");
                Console.WriteLine($"Portal: {baseDir}");
                Console.WriteLine("=================================================================");

                string serviceDir = Path.Combine(baseDir, "services", "legacy");
                string typeDir = Path.Combine(baseDir, "types", "legacy");
                string fieldHookDir = Path.Combine(baseDir, "field-hooks", "legacy");
                string utilDir = Path.Combine(baseDir, "utils", "legacy");
                string coreServiceDir = Path.Combine(serviceDir, "core");
                string settingsServiceDir = Path.Combine(serviceDir, "settings");
                string coreTypeDir = Path.Combine(typeDir, "core");
                string settingsTypeDir = Path.Combine(typeDir, "settings");

                Directory.CreateDirectory(coreServiceDir);
                Directory.CreateDirectory(settingsServiceDir);
                Directory.CreateDirectory(coreTypeDir);
                Directory.CreateDirectory(settingsTypeDir);
                Directory.CreateDirectory(fieldHookDir);
                Directory.CreateDirectory(utilDir);

                // CRUD pages only generated for management portal
                string crudPagesDir = baseDir == managementBaseDir ? mgmtCrudPagesDir : Path.Combine(baseDir, "components", "legacy", "Pages", "List");

                ProcessResources(resourceGroups, schemas, coreServiceDir, coreTypeDir, settingsServiceDir, settingsTypeDir, fieldHookDir, baseDir, crudPagesDir);

                Console.WriteLine($"  Generating utils");
                UtilsGenerator.GenerateLoadSettingsFile(settingsResources, utilDir);
                Console.WriteLine($"    ✓ loadSettings.ts");

                Console.WriteLine($"  Completed:");
                Console.WriteLine($"    ✓ Services:    {serviceDir}");
                Console.WriteLine($"    ✓ Types:       {typeDir}");
                Console.WriteLine($"    ✓ Field hooks: {fieldHookDir}");
                Console.WriteLine($"    ✓ Utils:       {utilDir}");
                Console.WriteLine();
            }

            await Task.CompletedTask;
        }

        static (List<string> coreResources, List<string> settingsResources) BuildResourceLists(Dictionary<string, List<ApiEndpoint>> resourceGroups)
        {
            var excludedResources = new HashSet<string> {
                "HealthCheck", "sso", "Document", "RoleObjectManagement"
            };

            var coreResources = new List<string>();
            var settingsResources = new List<string>();

            foreach (var resource in resourceGroups)
            {
                if (excludedResources.Contains(resource.Key)) continue;

                string resourcePrefix = resource.Value.FirstOrDefault()?.Prefix ?? "api";

                switch (resourcePrefix.ToLower())
                {
                    case "core": coreResources.Add(resource.Key); break;
                    case "settings": settingsResources.Add(resource.Key); break;
                }
            }

            return (coreResources, settingsResources);
        }

        static void ProcessResources(Dictionary<string, List<ApiEndpoint>> resourceGroups, JsonObject? schemas, string coreServiceDir, string coreTypeDir, string settingsServiceDir, string settingsTypeDir, string fieldHookDir, string baseDir, string crudPagesDir)
        {
            var excludedResources = new HashSet<string> {
                "HealthCheck", "sso", "Document", "RoleObjectManagement"
            };

            var coreResources = new List<string>();
            var authResources = new List<string>();
            var settingsResources = new List<string>();

            foreach (var resource in resourceGroups)
            {
                if (excludedResources.Contains(resource.Key)) continue;

                string resourcePrefix = resource.Value.FirstOrDefault()?.Prefix ?? "api";

                switch (resourcePrefix.ToLower())
                {
                    case "auth": authResources.Add(resource.Key); break;
                    case "core": coreResources.Add(resource.Key); break;
                    case "settings": settingsResources.Add(resource.Key); break;
                }
            }

            Console.WriteLine($"  Generating services, types and field-hooks for {resourceGroups.Count} resources:");

            foreach (var resource in resourceGroups)
            {
                string resourcePrefix = resource.Value.FirstOrDefault()?.Prefix ?? "api";

                if (excludedResources.Contains(resource.Key))
                {
                    Console.WriteLine($"    ⚠ {resource.Key}: skipped");
                    continue;
                }

                switch (resourcePrefix.ToLower())
                {
                    case "core":
                        Console.WriteLine($"    ✓ {resource.Key}");
                        ServiceGenerator.GenerateServiceFile(resource.Key, resource.Value, schemas, coreServiceDir);
                        TypeGenerator.GenerateTypeFile(resource.Key, schemas, coreTypeDir);
                        FieldHooksGenerator.GenerateFieldHooksFile(resource.Key, schemas, fieldHookDir, coreResources, settingsResources);
                        ComponentGenerator.GenerateComponentsForResource(resource.Key, schemas, baseDir);
                        PageGenerator.GeneratePagesForResource(resource.Key, schemas, crudPagesDir, coreResources, settingsResources);

                        break;

                    case "settings":
                        Console.WriteLine($"    ✓ {resource.Key}");
                        ServiceGenerator.GenerateServiceFile(resource.Key, resource.Value, schemas, settingsServiceDir);
                        TypeGenerator.GenerateTypeFile(resource.Key, schemas, settingsTypeDir);
                        break;
                }
            }

            // Write resource lists for reference
            string outputDir = "output";
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "coreResources.txt"), string.Join(Environment.NewLine, coreResources));
            File.WriteAllText(Path.Combine(outputDir, "authResources.txt"), string.Join(Environment.NewLine, authResources));
            File.WriteAllText(Path.Combine(outputDir, "settingsResources.txt"), string.Join(Environment.NewLine, settingsResources));
            var coreResourceFields = BuildCoreResourceFields(coreResources, schemas);
            File.WriteAllText(
                Path.Combine(outputDir, "coreResourceFields.json"),
                JsonSerializer.Serialize(coreResourceFields, new JsonSerializerOptions { WriteIndented = true })
            );
        }

        static Dictionary<string, List<string>> BuildCoreResourceFields(List<string> coreResources, JsonObject? schemas)
        {
            var result = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (schemas == null) return new Dictionary<string, List<string>>(result);

            foreach (var resource in coreResources.OrderBy(r => r))
            {
                var createModel = Utilities.FindSchema(schemas, $"{resource}CreateRequestModel");
                var properties = createModel?["properties"]?.AsObject();
                if (properties == null)
                {
                    result[resource] = new List<string>();
                    continue;
                }

                var fields = properties.Select(kvp => kvp.Key).OrderBy(k => k).ToList();
                result[resource] = fields;
            }

            return new Dictionary<string, List<string>>(result);
        }
    }
}
