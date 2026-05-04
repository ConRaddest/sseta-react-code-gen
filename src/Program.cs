using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen
{
    // Reads Swagger/OpenAPI specs from running backends and generates
    // TypeScript services, types, contexts, and form components for each portal.
    // All configuration is loaded from input/codegen.config.json.
    class Program
    {
        const string configPath = "input/codegen.config.json";

        private static StreamWriter? _logWriter;
        private static TextWriter? _originalConsoleOut;

        // Usage:
        //   dotnet run                          full run (enums + all portals)
        //   dotnet run enums                    enums only
        //   dotnet run cached                   all portals using cached swagger (skip fetch)
        //   dotnet run <portal>                 one portal (management|partner|learner)
        //   dotnet run <portal> cached          one portal, use cached swagger
        //   dotnet run <portal> <layer>         one portal, one layer
        //   dotnet run <portal> <layer> cached  one portal, one layer, cached swagger
        //
        // Layers: services, types, contexts, forms, createforms, updateforms,
        //         viewforms, deleteforms, fieldhooks, fieldsmanifest, formlayout
        static async Task Main(string[] args)
        {
            // Dispatch to legacy mode if requested
            if (args.Length > 0 && args[0].Equals("legacy", StringComparison.OrdinalIgnoreCase))
            {
                await ReactCodegen.Legacy.LegacyProgram.RunAsync();
                return;
            }

            // Parse run mode flags
            bool enumsOnly    = args.Length > 0 && args[0].Equals("enums",  StringComparison.OrdinalIgnoreCase);
            bool useCached    = args.Any(a => a.Equals("cached", StringComparison.OrdinalIgnoreCase));
            string? portalArg = args.FirstOrDefault(a =>
                !a.Equals("cached", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals("enums",  StringComparison.OrdinalIgnoreCase) &&
                new[] { "management", "partner", "learner" }.Contains(a, StringComparer.OrdinalIgnoreCase));
            string? layerArg  = args.FirstOrDefault(a =>
                !a.Equals("cached", StringComparison.OrdinalIgnoreCase) &&
                !a.Equals("enums",  StringComparison.OrdinalIgnoreCase) &&
                a != portalArg &&
                new[] { "services", "types", "contexts", "forms", "createforms", "updateforms",
                         "viewforms", "deleteforms", "fieldhooks", "fieldsmanifest", "formlayout" }
                    .Contains(a, StringComparer.OrdinalIgnoreCase));


            // ---------------------------------------------------------------
            // Load config
            // ---------------------------------------------------------------
            if (!File.Exists(configPath))
            {
                Console.Error.WriteLine($"Config file not found: {configPath}");
                return;
            }

            var configNode = JsonNode.Parse(File.ReadAllText(configPath));
            if (configNode == null)
            {
                Console.Error.WriteLine("Failed to parse config file.");
                return;
            }

            var inputs  = configNode["inputs"]  ?? throw new Exception("Missing inputs in config.");
            var outputs = configNode["outputs"] ?? throw new Exception("Missing outputs in config.");

            Formatters.Modules = inputs["modules"]?.AsArray()
                .Select(m => m?.GetValue<string>() ?? "")
                .Where(m => m.Length > 0)
                .ToArray()
                ?? throw new Exception("Missing inputs.modules in config.");

            Formatters.KnownAcronyms = new HashSet<string>(
                inputs["acronyms"]?.AsArray()
                    .Select(a => a?.GetValue<string>() ?? "")
                    .Where(a => a.Length > 0)
                ?? [],
                StringComparer.OrdinalIgnoreCase
            );

            Formatters.KeepNameSuffixWords = new HashSet<string>(
                inputs["keepNameSuffix"]?.AsArray()
                    .Select(a => a?.GetValue<string>() ?? "")
                    .Where(a => a.Length > 0)
                ?? [],
                StringComparer.OrdinalIgnoreCase
            );

            string settingsJsonPath = inputs["settingsJsonPath"]?.GetValue<string>()
                ?? "/home/cdt/DEV/pmis/sql-server-db/Settings.json";

            var t = inputs["templates"] ?? throw new Exception("Missing inputs.templates in config.");
            string T(string key) => t[key]?.GetValue<string>() ?? throw new Exception($"Missing inputs.templates.{key} in config.");

            string serviceTemplatePath  = T("service");
            string typeTemplatePath     = T("type");
            string enumTemplatePath     = T("enum");
            string createFormTemplate   = T("createForm");
            string updateFormTemplate   = T("updateForm");
            string viewFormTemplate     = T("viewForm");
            string deleteFormTemplate   = T("deleteForm");
            string useFieldsTemplate    = T("useFields");
            string useLayoutTemplate = T("useLayout");
            string contextTemplate   = T("context");

            string logFilePath     = outputs["log"]?.GetValue<string>()   ?? "src/_output/codegen-log.txt";
            string enumsOutputPath = outputs["enums"]?.GetValue<string>() ?? throw new Exception("Missing outputs.enums in config.");

            var portalsArray = configNode["portals"]?.AsArray()
                ?? throw new Exception("Missing portals array in config.");

            var portals = portalsArray
                .Where(p => p != null)
                .Select(p =>
                {
                    var i = p!["input"]  ?? throw new Exception($"Portal '{p["name"]}' missing input config.");
                    var o = p["output"]  ?? throw new Exception($"Portal '{p["name"]}' missing output config.");
                    string baseDir = o["baseDir"]?.GetValue<string>() ?? throw new Exception($"Portal '{p["name"]}' missing output.baseDir.");
                    string Resolve(string key, string err)
                    {
                        var rel = o[key]?.GetValue<string>() ?? throw new Exception(err);
                        return Path.Combine(baseDir, rel);
                    }
                    string primaryPrefix = p["apiPrefix"]?.GetValue<string>() ?? "management";
                    var additionalPrefixes = (p["additionalPrefixes"]?.AsArray() ?? [])
                        .Select(x => x?.GetValue<string>() ?? "")
                        .Where(x => x.Length > 0)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var allPrefixes = new HashSet<string>(additionalPrefixes, StringComparer.OrdinalIgnoreCase) { primaryPrefix };

                    return new PortalConfig(
                        Name:                 p["name"]?.GetValue<string>()             ?? throw new Exception("Portal missing name."),
                        ApiPrefix:            primaryPrefix,
                        ApiPrefixes:          allPrefixes,
                        SwaggerUrl:           i["swaggerUrl"]?.GetValue<string>()       ?? throw new Exception("Portal missing input.swaggerUrl."),
                        SwaggerCachePath:     i["swaggerCachePath"]?.GetValue<string>() ?? throw new Exception("Portal missing input.swaggerCachePath."),
                        FieldLayoutPath:      i["fieldLayoutPath"]?.GetValue<string>()  ?? throw new Exception("Portal missing input.fieldLayoutPath."),
                        Output: new PortalOutput(
                            Services:       Resolve("services",       "Portal missing output.services."),
                            Types:          Resolve("types",          "Portal missing output.types."),
                            Contexts:       Resolve("contexts",       "Portal missing output.contexts."),
                            Forms:          Resolve("forms",          "Portal missing output.forms."),
                            FieldsManifest: Resolve("fieldsManifest", "Portal missing output.fieldsManifest.")
                        ),
                        Blacklist: (p["blacklist"]?.AsArray() ?? [])
                                        .Select(e => e?.GetValue<string>() ?? "")
                                        .Where(e => e.Length > 0)
                                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    );
                })
                .ToArray();

            // ---------------------------------------------------------------
            // Setup logging
            // ---------------------------------------------------------------
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
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
                // Enums — skip when targeting a specific portal or layer, run when full or enums-only
                bool runEnums = enumsOnly || (portalArg == null && layerArg == null && !useCached);

                if (runEnums)
                {
                    Console.WriteLine($"=================================================================");
                    Console.WriteLine($"Shared");
                    Console.WriteLine($"=================================================================");
                    Console.WriteLine("  Enums");
                    await EnumGenerator.Generate(settingsJsonPath, enumTemplatePath, enumsOutputPath);
                    Console.WriteLine($"    ✓ Enums: {enumsOutputPath}");
                    Console.WriteLine();
                }

                if (!enumsOnly)
                {
                    var selectedPortals = portalArg == null
                        ? portals
                        : portals.Where(p => p.Name.Equals(portalArg, StringComparison.OrdinalIgnoreCase)).ToArray();

                    foreach (var portal in selectedPortals)
                    {
                        Console.WriteLine($"=================================================================");
                        Console.WriteLine($"Portal: {portal.Name}");
                        Console.WriteLine($"=================================================================");

                        try
                        {
                            if (useCached)
                            {
                                Console.WriteLine($"  Using cached swagger: {portal.SwaggerCachePath}");
                            }
                            else
                            {
                                Console.WriteLine($"Fetching swagger: {portal.SwaggerUrl}");
                                string swaggerJson = await FetchSwaggerFromApi(portal.SwaggerUrl);
                                Directory.CreateDirectory(Path.GetDirectoryName(portal.SwaggerCachePath)!);
                                await File.WriteAllTextAsync(portal.SwaggerCachePath, swaggerJson);
                            }
                            Console.WriteLine();

                            await GenerateFrontendCode(portal, serviceTemplatePath, typeTemplatePath, createFormTemplate, updateFormTemplate, viewFormTemplate, deleteFormTemplate, useFieldsTemplate, useLayoutTemplate, contextTemplate, layerArg);
                        }
                        catch
                        {
                            Console.WriteLine($"  Skipping portal and continuing (ensure api is running and endpoints are correctly configured)");
                        }

                        Console.WriteLine();
                    }
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

        static async Task GenerateFrontendCode(
            PortalConfig portal,
            string serviceTemplatePath,
            string typeTemplatePath,
            string createFormTemplate,
            string updateFormTemplate,
            string viewFormTemplate,
            string deleteFormTemplate,
            string useFieldsTemplate,
            string useLayoutTemplate,
            string contextTemplate,
            string? layerArg = null)
        {
            bool Layer(params string[] names) =>
                layerArg == null || names.Any(n => n.Equals(layerArg, StringComparison.OrdinalIgnoreCase));

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

            // formlayout always runs when any form-related layer is requested
            bool needsLayout = Layer("formlayout", "forms", "createforms", "updateforms", "viewforms", "deleteforms", "fieldhooks", "fieldsmanifest");

            if (needsLayout)
            {
                Console.WriteLine("  Form Layout Seed");
                FormLayoutSeedGenerator.Generate(paths, schemas, portal.FieldLayoutPath, portal.Blacklist, portal.ApiPrefixes);
                Console.WriteLine();
            }

            var fieldLayout = needsLayout ? JsonNode.Parse(File.ReadAllText(portal.FieldLayoutPath)) : null;

            Directory.CreateDirectory(portal.Output.Services);
            Directory.CreateDirectory(portal.Output.Types);
            Directory.CreateDirectory(portal.Output.Contexts);
            Directory.CreateDirectory(portal.Output.Forms);
            Directory.CreateDirectory(Path.GetDirectoryName(portal.Output.FieldsManifest)!);

            if (Layer("fieldsmanifest"))
            {
                Console.WriteLine("  Fields Manifest");
                FieldsManifestGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), portal.Output.FieldsManifest, portal.Blacklist, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("services"))
            {
                Console.WriteLine("  Services");
                ApiServiceGenerator.Generate(paths, schemas, serviceTemplatePath, Path.Combine(portal.Output.Services, "api.service.ts"), portal.ApiPrefixes, portal.Blacklist);
                Console.WriteLine();
            }

            if (Layer("types"))
            {
                Console.WriteLine("  Types");
                ApiTypeGenerator.Generate(paths, schemas!, typeTemplatePath, Path.Combine(portal.Output.Types, "api.types.ts"));
                Console.WriteLine();
            }

            if (Layer("contexts"))
            {
                Console.WriteLine("  Contexts");
                ContextGenerator.Generate(paths, schemas, portal.Output.Contexts, portal.Blacklist, contextTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("forms", "createforms"))
            {
                Console.WriteLine("  Create Forms");
                CreateFormGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), portal.Output.Forms, portal.Blacklist, createFormTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("forms", "updateforms"))
            {
                Console.WriteLine("  Update Forms");
                UpdateFormGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), portal.Output.Forms, portal.Blacklist, updateFormTemplate, useFieldsTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("forms", "viewforms"))
            {
                Console.WriteLine("  View Forms");
                ViewFormGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), portal.Output.Forms, portal.Blacklist, viewFormTemplate, useLayoutTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("forms", "deleteforms"))
            {
                Console.WriteLine("  Delete Forms");
                DeleteFormGenerator.Generate(paths, schemas, portal.Output.Forms, portal.Blacklist, deleteFormTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            if (Layer("forms", "fieldhooks"))
            {
                Console.WriteLine("  Field Hooks");
                UseFieldsGenerator.Generate(paths, schemas, fieldLayout?.AsObject(), portal.Output.Forms, portal.Blacklist, useFieldsTemplate, portal.ApiPrefixes);
                Console.WriteLine();
            }

            Console.WriteLine($"  Completed generation:");
            Console.WriteLine($"    ✓ Services:       {portal.Output.Services}");
            Console.WriteLine($"    ✓ Types:          {portal.Output.Types}");
            Console.WriteLine($"    ✓ Contexts:       {portal.Output.Contexts}");
            Console.WriteLine($"    ✓ Forms:          {portal.Output.Forms}");
            Console.WriteLine($"    ✓ Fields Manifest: {portal.Output.FieldsManifest}");

            await Task.CompletedTask;
        }
    }

    record PortalOutput(
        string Services,
        string Types,
        string Contexts,
        string Forms,
        string FieldsManifest
    );

    record PortalConfig(
        string Name,
        string ApiPrefix,
        HashSet<string> ApiPrefixes,
        string SwaggerUrl,
        string SwaggerCachePath,
        string FieldLayoutPath,
        PortalOutput Output,
        HashSet<string> Blacklist
    );
}
