using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Reads the exported Settings.json file and emits a TypeScript enum file
// with one enum per settings collection.
//
// Expected Settings.json shape:
// {
//   "SettingName": [
//     { "Id": "1", "Name": "Example", "Description": "Optional" }
//   ]
// }
//
// Duplicate key resolution (applied per enum):
//   1. Same normalised key from different names → append Description value
//   2. Still duplicated after step 1 → append _{id} to every occurrence
//
// Output format:
//   export enum EnumName {
//     KeyOne = 1,
//     KeyTwo = 2,
//   }
static class EnumGenerator
{
    const string Template = "Template";

    public static Task Generate(string settingsJsonPath, string templatePath, string outputPath)
    {
        if (!File.Exists(settingsJsonPath))
            throw new FileNotFoundException($"Settings JSON file not found: {settingsJsonPath}", settingsJsonPath);

        var settingsNode = JsonNode.Parse(File.ReadAllText(settingsJsonPath))?.AsObject()
            ?? throw new Exception($"Failed to parse settings JSON file: {settingsJsonPath}");

        var sb = new StringBuilder();
        int enumCount = 0;

        foreach (var (table, value) in settingsNode.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (value is not JsonArray rowsArray)
            {
                Console.WriteLine($"    ⚠ Skipping {table} (expected an array)");
                continue;
            }

            var rows = ReadEnumRows(table, rowsArray);
            if (rows.Count == 0) continue;

            AppendEnum(sb, table, rows);
            enumCount++;
        }

        string template = File.ReadAllText(templatePath);
        string output = template.Replace("// [[ENUMS]]", sb.ToString().TrimEnd());

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"    ✓ {Path.GetFileName(outputPath)}  ({enumCount} enums)");
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------

    static void AppendEnum(StringBuilder sb, string enumName, List<(string Key, int Id)> rows)
    {
        sb.AppendLine($"export enum {enumName} {{");
        foreach (var (key, id) in rows)
            sb.AppendLine($"  {key} = {id},");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // ---------------------------------------------------------------
    // Row reading
    // ---------------------------------------------------------------

    static List<(string Key, int Id)> ReadEnumRows(string table, JsonArray rowsArray)
    {
        var rows = new List<(string Key, int Id)>();
        var raw = new List<(string Name, string? Description, int Id)>();

        foreach (var rowNode in rowsArray)
        {
            if (rowNode is not JsonObject row) continue;

            int? id = ReadInt(row, "Id") ?? ReadInt(row, $"{table}Id");
            string? name = RemapEnumName(ReadString(row, "Name"));
            string? description = ReadString(row, "Description");

            if (id == null || string.IsNullOrWhiteSpace(name)) continue;

            raw.Add((name, description, id.Value));
        }

        if (raw.Count == 0)
        {
            Console.WriteLine($"    ⚠ Skipping {table} (no id/name rows)");
            return rows;
        }

        raw = raw.OrderBy(r => r.Id).ToList();

        // Pass 1: keys from Name only
        var keys = raw.Select(r => Formatters.NormalizeEnumKey(r.Name, r.Id.ToString())).ToList();

        // Pass 2: duplicates → append Description
        var duplicates = DuplicateSet(keys);
        if (duplicates.Count > 0)
        {
            for (int i = 0; i < raw.Count; i++)
            {
                if (!duplicates.Contains(keys[i])) continue;
                var (name, description, id) = raw[i];
                string combined = string.IsNullOrWhiteSpace(description) ? name : $"{name} {description}";
                keys[i] = Formatters.NormalizeEnumKey(combined, id.ToString());
            }
        }

        // Pass 3: still duplicated → keep the first occurrence and append _{id} to later occurrences
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < raw.Count; i++)
        {
            string key = seen.Add(keys[i])
                ? keys[i]
                : $"{keys[i]}_{raw[i].Id}";
            rows.Add((key, raw[i].Id));
        }

        return rows;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    static readonly HashSet<string> TemplateAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        $"Email{nameof(Template)}",
        $"SMS{nameof(Template)}",
        $"Notification{nameof(Template)}",
        $"Document{nameof(Template)}",
    };

    static string? RemapEnumName(string? name) =>
        name != null && TemplateAliases.Contains(name) ? nameof(Template) : name;

    static string? ReadString(JsonObject row, string propertyName)
    {
        var property = row.FirstOrDefault(p => p.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        if (property.Value == null) return null;

        try
        {
            return property.Value.GetValue<string>();
        }
        catch
        {
            return property.Value.ToString();
        }
    }

    static int? ReadInt(JsonObject row, string propertyName)
    {
        string? value = ReadString(row, propertyName);
        return int.TryParse(value, out int parsed) ? parsed : null;
    }

    static HashSet<string> DuplicateSet(List<string> keys) =>
        keys.GroupBy(k => k)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
}
