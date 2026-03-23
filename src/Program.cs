using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen
{
    // Reads a Swagger/OpenAPI spec from the running backend and generates
    // TypeScript services, types, contexts, and form components for the React frontend.
    class Program
    {
        // Input paths
        const string swaggerInputPath = "io/input/swagger/swagger.json";
        const string fieldLayoutInputPath = "io/input/form-layout/field-layout.json";

        // Output paths — one folder per generator
        const string servicesOutputDir = "io/output/services";
        const string typesOutputDir = "io/output/types";
        const string contextsOutputDir = "io/output/contexts";
        const string formsOutputDir = "io/output/forms";

        // Template paths
        const string serviceTemplatePath = "src/templates/services/api.service.ts";
        const string typeTemplatePath = "src/templates/types/api.type.ts";

        // Log path
        const string logFilePath = "io/output/codegen-log.txt";

        // Backend API
        const string swaggerApiUrl = "https://localhost:7222/swagger/v1/swagger.json";

        private static StreamWriter? _logWriter;
        private static TextWriter? _originalConsoleOut;

        static async Task Main()
        {
            // Set up dual logging — output goes to both the console and a log file
            Directory.CreateDirectory("io/output");
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
                // Fetch the latest swagger spec from the running backend and cache it to io/input/swagger/swagger.json
                Console.WriteLine($"Fetching swagger: {swaggerApiUrl}");
                string swaggerJson = await FetchSwaggerFromApi();

                Directory.CreateDirectory("io/input/swagger");
                await File.WriteAllTextAsync(swaggerInputPath, swaggerJson);
                Console.WriteLine();

                await GenerateFrontendCode(swaggerInputPath, fieldLayoutInputPath);
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

        // Fetches the Swagger JSON from the backend API.
        // Accepts self-signed certs so it works against localhost dev servers.
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
                throw new Exception($"Request timed out while fetching swagger: {ex.Message}", ex);
            }
        }

        // Parses the swagger spec and field layout, then orchestrates all code generators.
        // Each generator writes into its own folder under io/output/.
        static async Task GenerateFrontendCode(string swaggerPath, string fieldLayoutPath)
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
            }

            // paths = all API endpoints; schemas = all request/response models
            var paths = jsonNode["paths"]?.AsObject();
            var schemas = jsonNode["components"]?["schemas"]?.AsObject();

            if (paths == null)
            {
                Console.WriteLine("No paths found in swagger.");
                return;
            }

            // field layout drives form field ordering and visibility per resource
            var fieldLayout = JsonNode.Parse(File.ReadAllText(fieldLayoutPath));

            // Ensure all output directories exist
            Directory.CreateDirectory(servicesOutputDir);
            Directory.CreateDirectory(typesOutputDir);
            Directory.CreateDirectory(contextsOutputDir);
            Directory.CreateDirectory(formsOutputDir);

            Console.WriteLine("Generating services...");
            ApiServiceGenerator.Generate(paths, schemas, serviceTemplatePath, Path.Combine(servicesOutputDir, "management-api.service.ts"));

            Console.WriteLine("Generating types...");
            ApiTypeGenerator.Generate(paths, schemas!, typeTemplatePath, Path.Combine(typesOutputDir, "management-api.types.ts"));

            // TODO: ContextGenerator.Generate(schemas, contextsOutputDir);
            // TODO: FormGenerator.Generate(schemas, fieldLayout, formsOutputDir);

            Console.WriteLine();
            Console.WriteLine("Code generation completed successfully!");
            Console.WriteLine($"  Services:  {servicesOutputDir}");
            Console.WriteLine($"  Types:     {typesOutputDir}");
            Console.WriteLine($"  Contexts:  {contextsOutputDir}");
            Console.WriteLine($"  Forms:     {formsOutputDir}");

            await Task.CompletedTask;
        }
    }

}
