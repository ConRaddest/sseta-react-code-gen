using System.Text.Json;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class FieldTemplateGenerator
    {
        public static void GenerateFieldTemplatesFile(List<string> resourceNames, JsonObject? schemas, string outputDir, bool generateFieldTemplates)
        {
            if (!generateFieldTemplates || schemas == null || resourceNames.Count == 0) return;

            var allFieldTemplates = new Dictionary<string, List<string>>();

            // Process all resources
            foreach (var resourceName in resourceNames)
            {
                var createModel = Utilities.FindSchema(schemas, $"{resourceName}CreateRequestModel");
                if (createModel == null) continue; // Skip if no create model

                var fieldNames = ExtractFieldNames(createModel);
                if (fieldNames.Count > 0)
                {
                    allFieldTemplates[resourceName] = fieldNames;
                }
            }

            // Only create file if we have templates
            if (allFieldTemplates.Count > 0)
            {
                string fileName = "fields.json";
                string filePath = Path.Combine(outputDir, fileName);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string jsonContent = JsonSerializer.Serialize(allFieldTemplates, options);
                File.WriteAllText(filePath, jsonContent);
            }
        }

        static List<string> ExtractFieldNames(JsonObject createModel)
        {
            var fieldNames = new List<string>();
            var properties = createModel["properties"]?.AsObject();

            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    string fieldName = Utilities.ToCamelCase(prop.Key);
                    fieldNames.Add(fieldName);
                }
            }

            return fieldNames;
        }
    }
}