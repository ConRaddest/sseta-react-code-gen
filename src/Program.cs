using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen
{
    // Reads Swagger/OpenAPI specs from running backends and generates
    // TypeScript services, types, contexts, and form components for each portal.
    class Program
    {
        // Database connection (shared — enums are the same across portals)
        const string dbConnectionString =
            "server=localhost;database=PMVR;TrustServerCertificate=True;Integrated Security=True;";

        // Template paths (shared)
        const string serviceTemplatePath = "src/templates/services/api.service.ts";
        const string typeTemplatePath = "src/templates/types/api.type.ts";
        const string enumTemplatePath = "src/templates/types/enums.ts";

        // Log path
        const string logFilePath = "src/_output/codegen-log.txt";

        // Portal configurations
        static readonly PortalConfig[] Portals =
        [
            new PortalConfig(
                Name:              "management",
                SwaggerUrl:        "https://localhost:7222/swagger/v1/swagger.json",
                SwaggerCachePath:  "input/swagger/management.json",
                FieldLayoutPath:   "input/form-layout/field-layout.json",
                OutputDir:         "src/_output/management"
            ),
            new PortalConfig(
                Name:              "partner",
                SwaggerUrl:        "https://localhost:7223/swagger/v1/swagger.json",
                SwaggerCachePath:  "input/swagger/partner.json",
                FieldLayoutPath:   "input/form-layout/field-layout.json",
                OutputDir:         "src/_output/partner"
            ),
            new PortalConfig(
                Name:              "learner",
                SwaggerUrl:        "https://localhost:7224/swagger/v1/swagger.json",
                SwaggerCachePath:  "input/swagger/learner.json",
                FieldLayoutPath:   "input/form-layout/field-layout.json",
                OutputDir:         "src/_output/learner"
            ),
        ];

        private static StreamWriter? _logWriter;
        private static TextWriter? _originalConsoleOut;

        static async Task Main()
        {
            Directory.CreateDirectory("src/_output");
            _logWriter = new StreamWriter(logFilePath, false) { AutoFlush = true };
            _originalConsoleOut = Console.Out;

            _logWriter.WriteLine("=================================================================");
            _logWriter.WriteLine($"Code Generation Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logWriter.WriteLine("=================================================================");
            _logWriter.WriteLine();

            var multiWriter = new MultiTextWriter(Console.Out, _logWriter);
            Console.SetOut(multiWriter);

            try
            {
                foreach (var portal in Portals)
                {
                    Console.WriteLine($"=================================================================");
                    Console.WriteLine($"Portal: {portal.Name}");
                    Console.WriteLine($"=================================================================");

                    try
                    {
                        Console.WriteLine($"Fetching swagger: {portal.SwaggerUrl}");
                        string swaggerJson = await FetchSwaggerFromApi(portal.SwaggerUrl);

                        Directory.CreateDirectory("input/swagger");
                        await File.WriteAllTextAsync(portal.SwaggerCachePath, swaggerJson);
                        Console.WriteLine();

                        await GenerateFrontendCode(portal);
                    }
                    catch
                    {
                        Console.WriteLine($"  Skipping portal and continuing (ensure api is running and endpoints are correctly configured)");
                    }

                    Console.WriteLine();
                }
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

        static async Task<string> FetchSwaggerFromApi(string url)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            using var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                var response = await client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                throw new Exception($"Failed to fetch swagger from {url}: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                throw new Exception($"Request timed out while fetching swagger from {url}: {ex.Message}", ex);
            }
        }

        static async Task GenerateFrontendCode(PortalConfig portal)
        {
            string jsonString = File.ReadAllText(portal.SwaggerCachePath);
            JsonNode? jsonNode = JsonNode.Parse(jsonString);

            if (jsonNode == null)
            {
                Console.WriteLine("  Failed to parse swagger JSON.");
                return;
            }

            var info = jsonNode["info"];
            if (info != null)
            {
                Console.WriteLine($"  OpenAPI Version: {jsonNode["openapi"]?.GetValue<string>()}");
                Console.WriteLine($"  Title:           {info["title"]?.GetValue<string>()}");
                Console.WriteLine($"  Version:         {info["version"]?.GetValue<string>()}");
                Console.WriteLine();
            }

            var paths = jsonNode["paths"]?.AsObject();
            var schemas = jsonNode["components"]?["schemas"]?.AsObject();

            if (paths == null)
            {
                Console.WriteLine("  No paths found in swagger.");
                return;
            }

            var fieldLayout = JsonNode.Parse(File.ReadAllText(portal.FieldLayoutPath));

            string servicesOutputDir = Path.Combine(portal.OutputDir, "services");
            string typesOutputDir = Path.Combine(portal.OutputDir, "types");
            string contextsOutputDir = Path.Combine(portal.OutputDir, "contexts");
            string formsOutputDir = Path.Combine(portal.OutputDir, "forms");

            Directory.CreateDirectory(servicesOutputDir);
            Directory.CreateDirectory(typesOutputDir);
            Directory.CreateDirectory(contextsOutputDir);
            Directory.CreateDirectory(formsOutputDir);

            Console.WriteLine("  Fields Manifest");
            FieldsManifestGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), Path.Combine(portal.OutputDir, $"{portal.Name}-fields.json"));
            Console.WriteLine();

            Console.WriteLine("  Services");
            ApiServiceGenerator.Generate(paths, schemas, serviceTemplatePath, Path.Combine(servicesOutputDir, "api.service.ts"));
            Console.WriteLine();

            Console.WriteLine("  Types");
            ApiTypeGenerator.Generate(paths, schemas!, typeTemplatePath, Path.Combine(typesOutputDir, "api.types.ts"));
            Console.WriteLine();

            Console.WriteLine("  Enums");
            await EnumGenerator.Generate(dbConnectionString, enumTemplatePath, Path.Combine(typesOutputDir, "enums.ts"));
            Console.WriteLine();

            Console.WriteLine("  Contexts");
            ContextGenerator.Generate(paths, schemas, contextsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  Create Forms");
            CreateFormGenerator.Generate(paths, schemas, formsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  Update Forms");
            UpdateFormGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), formsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  View Forms");
            ViewFormGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), formsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  Delete Forms");
            DeleteFormGenerator.Generate(paths, schemas, formsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  Field Hooks");
            UseFieldsGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), formsOutputDir);
            Console.WriteLine();

            Console.WriteLine("  Layouts");
            UseLayoutGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), formsOutputDir);
            Console.WriteLine();

            Console.WriteLine($"  Completed generation:");
            Console.WriteLine($"    ✓ Services:  {servicesOutputDir}");
            Console.WriteLine($"    ✓ Types:     {typesOutputDir}");
            Console.WriteLine($"    ✓ Contexts:  {contextsOutputDir}");
            Console.WriteLine($"    ✓ Forms:     {formsOutputDir}");

            await Task.CompletedTask;
        }
    }

    record PortalConfig(
        string Name,
        string SwaggerUrl,
        string SwaggerCachePath,
        string FieldLayoutPath,
        string OutputDir
    );
}
