using System.Text;
using Microsoft.Data.SqlClient;

namespace ReactCodegen;

// Connects to the PMVR SQL database, reads every table in the [Settings] schema,
// and emits a TypeScript enum file with one enum per table.
//
// Assumes each Settings table has at least two columns:
//   - An integer Id column  (first int column whose name ends with "Id")
//   - A string Name column
//
// Split-by-type detection:
//   If a table has a column named {TableName}TypeId (e.g. ObjectTypeId on Object),
//   AND a corresponding Settings.{TableName}Type lookup table exists,
//   the table is split into one enum per type value instead of one flat enum.
//   e.g. Settings.Object with ObjectTypeId → TableObject, ReportObject, FormObject
//
// Duplicate key resolution (applied per enum):
//   1. Same normalised key from different names → append Description column value
//   2. Still duplicated after step 1 → append _{id} to every occurrence
//
// Output format:
//   export enum EnumName {
//     KeyOne = 1,
//     KeyTwo = 2,
//   }
static class EnumGenerator
{
    public static async Task Generate(string connectionString, string templatePath, string outputPath)
    {
        // ---------------------------------------------------------------
        // 1. Discover all tables in the Settings schema
        // ---------------------------------------------------------------
        var tableNames = new List<string>();

        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();

            const string tableQuery = """
                SELECT TABLE_NAME
                FROM   INFORMATION_SCHEMA.TABLES
                WHERE  TABLE_SCHEMA = 'Settings'
                  AND  TABLE_TYPE   = 'BASE TABLE'
                ORDER BY TABLE_NAME
                """;

            await using var cmd = new SqlCommand(tableQuery, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tableNames.Add(reader.GetString(0));
        }

        if (tableNames.Count == 0)
        {
            Console.WriteLine("    ⚠ No tables found in Settings schema.");
            return;
        }

        var tableSet = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------
        // 2. For each table, render one or more enums
        // ---------------------------------------------------------------
        var sb = new StringBuilder();
        int enumCount = 0;

        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();

            foreach (var table in tableNames)
            {
                // Check if this table has a {TableName}TypeId split column and a matching type table
                string typeColName = $"{table}TypeId";
                string typeTableName = $"{table}Type";
                bool hasSplitCol = await ColumnExists(conn, table, typeColName);
                bool hasTypeTable = tableSet.Contains(typeTableName);

                if (hasSplitCol && hasTypeTable)
                {
                    // Emit one enum per type value
                    var typeMap = await ReadIdNameMap(conn, typeTableName);

                    foreach (var (typeId, typeName) in typeMap.OrderBy(t => t.Key))
                    {
                        string enumName = $"{typeName}{table}";
                        var rows = await ReadEnumRows(conn, table, typeColName, typeId);
                        if (rows.Count == 0) continue;

                        AppendEnum(sb, enumName, rows);
                        enumCount++;
                    }
                }
                else
                {
                    // Standard flat enum
                    var rows = await ReadEnumRows(conn, table, splitCol: null, splitId: -1);
                    if (rows.Count == 0) continue;

                    AppendEnum(sb, table, rows);
                    enumCount++;
                }
            }
        }

        // ---------------------------------------------------------------
        // 3. Write output
        // ---------------------------------------------------------------
        string template = File.ReadAllText(templatePath);
        string output = template
            .Replace("// [[ENUMS]]", sb.ToString().TrimEnd())
            .Replace("[[DATETIME]]", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, output);

        Console.WriteLine($"    ✓ {Path.GetFileName(outputPath)}  ({enumCount} enums)");
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

    // Reads a simple Id → Name lookup table into a dictionary.
    static async Task<Dictionary<int, string>> ReadIdNameMap(SqlConnection conn, string table)
    {
        var map = new Dictionary<int, string>();
        string sql = $"SELECT * FROM [Settings].[{table}] ORDER BY 1";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        int idCol = -1, nameCol = -1;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string colName = reader.GetName(i);
            string colType = reader.GetFieldType(i).Name;
            if (idCol == -1 && colName.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                            && IsIntType(colType)) idCol = i;
            if (nameCol == -1 && colName.Equals("Name", StringComparison.OrdinalIgnoreCase)) nameCol = i;
        }

        if (idCol == -1 || nameCol == -1) return map;

        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(idCol) || reader.IsDBNull(nameCol)) continue;
            int id = Convert.ToInt32(reader.GetValue(idCol));
            string name = Formatters.NormalizeEnumKey(reader.GetString(nameCol));
            map[id] = name;
        }

        return map;
    }

    // Reads enum rows from a table, optionally filtered to a specific splitCol value.
    // Applies duplicate-key resolution before returning.
    static async Task<List<(string Key, int Id)>> ReadEnumRows(
        SqlConnection conn,
        string table,
        string? splitCol,
        int splitId)
    {
        var rows = new List<(string Key, int Id)>();

        string whereClause = splitCol != null ? $" WHERE [{splitCol}] = {splitId}" : "";
        string sql = $"SELECT * FROM [Settings].[{table}]{whereClause} ORDER BY 1";

        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        int idCol = -1, idColFallback = -1, nameCol = -1, descCol = -1;
        string preferredIdCol = $"{table}Id";

        for (int i = 0; i < reader.FieldCount; i++)
        {
            string colName = reader.GetName(i);
            string colType = reader.GetFieldType(i).Name;

            if (IsIntType(colType))
            {
                if (colName.Equals(preferredIdCol, StringComparison.OrdinalIgnoreCase)
                    || colName.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    idCol = i;
                else if (idColFallback == -1 && colName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    idColFallback = i;
            }

            if (nameCol == -1 && colName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                nameCol = i;

            if (descCol == -1 && colName.Equals("Description", StringComparison.OrdinalIgnoreCase))
                descCol = i;
        }

        if (idCol == -1) idCol = idColFallback;

        if (idCol == -1 || nameCol == -1)
        {
            Console.WriteLine($"    ⚠ Skipping Settings.{table} (no id/name columns)");
            return rows;
        }

        // Collect raw rows
        var raw = new List<(string Name, string? Description, int Id)>();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(idCol) || reader.IsDBNull(nameCol)) continue;

            int id = Convert.ToInt32(reader.GetValue(idCol));
            string name = reader.GetString(nameCol);
            string? description = (descCol != -1 && !reader.IsDBNull(descCol))
                ? reader.GetString(descCol)
                : null;

            raw.Add((name, description, id));
        }

        // Pass 1: keys from Name only
        var keys = raw.Select(r => Formatters.NormalizeEnumKey(r.Name, r.Id.ToString())).ToList();

        // Pass 2: duplicates → append Description
        var duplicates = DuplicateSet(keys);
        if (duplicates.Count > 0 && descCol != -1)
        {
            for (int i = 0; i < raw.Count; i++)
            {
                if (!duplicates.Contains(keys[i])) continue;
                var (name, description, id) = raw[i];
                string combined = string.IsNullOrWhiteSpace(description) ? name : $"{name} {description}";
                keys[i] = Formatters.NormalizeEnumKey(combined, id.ToString());
            }
        }

        // Pass 3: still duplicated → append _{id} on every occurrence
        var stillDuplicate = DuplicateSet(keys);
        for (int i = 0; i < raw.Count; i++)
        {
            string key = stillDuplicate.Contains(keys[i])
                ? $"{keys[i]}_{raw[i].Id}"
                : keys[i];
            rows.Add((key, raw[i].Id));
        }

        return rows;
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    static async Task<bool> ColumnExists(SqlConnection conn, string table, string column)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM   INFORMATION_SCHEMA.COLUMNS
            WHERE  TABLE_SCHEMA  = 'Settings'
              AND  TABLE_NAME    = @table
              AND  COLUMN_NAME   = @column
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    static HashSet<string> DuplicateSet(List<string> keys) =>
        keys.GroupBy(k => k)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

    static bool IsIntType(string typeName) =>
        typeName is "Int32" or "Int16" or "Int64";
}
