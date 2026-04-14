using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen;

// Generates use{Prefix}CreateFields.tsx for every resource that has a Create endpoint.
//
// Output path: {formsOutputDir}/{module-lower}-{kebab-resource}/create/use{Prefix}CreateFields.tsx
static class UseFieldsGenerator
{
    public static void Generate(
        JsonObject paths,
        JsonObject? schemas,
        JsonObject? fieldLayout,
        string formsOutputDir,
        HashSet<string>? blacklist = null,
        string? templatePath = null,
        HashSet<string>? apiPrefixes = null)
    {
        apiPrefixes ??= ["management"];
        var createEndpoints = new List<(string Module, string Resource, string RequestType)>();

        foreach (var (rawPath, pathNode) in paths)
        {
            if (pathNode == null) continue;
            var parts = rawPath.TrimStart('/').Split('/');
            if (parts.Length < 5) continue;
            if (parts[0] != "api" || !apiPrefixes.Contains(parts[1])) continue;
            if (blacklist != null && (blacklist.Contains($"{parts[2]}.{parts[3]}") || blacklist.Contains($"{parts[2]}.{parts[3]}.Create"))) continue;
            if (!string.Equals(parts[4], "Create", StringComparison.OrdinalIgnoreCase)) continue;

            string module = parts[2];
            string resource = parts[3];

            foreach (var (method, opNode) in pathNode.AsObject())
            {
                if (method != "post" || opNode == null) continue;
                string? requestRef = opNode["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
                string requestType = requestRef != null ? Formatters.FormatTypeName(requestRef.Split('/').Last()) : $"{module}_{resource}CreateRequest";
                createEndpoints.Add((module, resource, requestType));
            }
        }

        int count = 0;
        foreach (var (module, resource, requestType) in createEndpoints)
        {
            string modulePascal = Formatters.ToPascalCase(module.ToLower());
            string prefix = modulePascal + resource;
            string kebabResource = Formatters.ToKebabCase(resource);
            string dir = Path.Combine(formsOutputDir, module.ToLower(), kebabResource, "create");
            Directory.CreateDirectory(dir);

            var requestSchema = Formatters.FindSchema(schemas, $"{resource}{module}CreateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{resource}CreateRequestModel")
                             ?? Formatters.FindSchema(schemas, $"{module}_{resource}CreateRequestModel");

            var properties = requestSchema?["properties"]?.AsObject();
            var requiredArray = requestSchema?["required"]?.AsArray();
            var requiredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (requiredArray != null)
                foreach (var r in requiredArray)
                    if (r?.GetValue<string>() is string s) requiredFields.Add(s);

            var searchableResources = Formatters.BuildSearchableResources(paths, module, apiPrefixes);
            var orderedFields = GetOrderedFields(resource, fieldLayout, properties, searchableResources);
            var fkFields = CollectFkFields(module, modulePascal, orderedFields, properties, searchableResources);

            string content = ApplyTemplate(Render(prefix, modulePascal, resource, requestType, orderedFields, properties, requiredFields, fkFields, fieldLayout, searchableResources), templatePath);
            File.WriteAllText(Path.Combine(dir, $"useCreateFields.tsx"), content);

            Console.WriteLine($"    ✓ {module}/{resource}");
            count++;
        }

        Console.WriteLine($"    {count} field hook(s) generated.");
    }

    // Returns field names ordered by the field layout, with any extra schema fields appended.
    internal static List<string> GetOrderedFields(string resource, JsonObject? fieldLayout, JsonObject? properties, HashSet<string>? searchableResources = null)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string pkField = Formatters.GetIdFieldName(resource);

        if (fieldLayout != null && fieldLayout[resource] is JsonArray groups)
        {
            foreach (var groupNode in groups)
            {
                var fields = groupNode?["fields"]?.AsArray();
                if (fields == null) continue;
                foreach (var fieldNode in fields)
                {
                    string? name = fieldNode?["name"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (Formatters.ExcludedFormFields.Contains(name)) continue;
                    if (name.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                    if (properties != null && !properties.ContainsKey(name)) continue;
                    if (Formatters.IsUnsearchableFk(properties?[name]?.AsObject(), searchableResources))
                    {
                        Console.WriteLine($"    ⚠ Skipping {resource}.{name} (no search endpoint)");
                        continue;
                    }
                    if (seen.Add(name)) ordered.Add(name);
                }
            }
        }

        if (properties != null)
            foreach (var (key, val) in properties)
            {
                if (Formatters.ExcludedFormFields.Contains(key)) continue;
                if (key.Equals(pkField, StringComparison.OrdinalIgnoreCase)) continue;
                if (Formatters.IsUnsearchableFk(val?.AsObject(), searchableResources))
                {
                    Console.WriteLine($"    ⚠ Skipping {resource}.{key} (no search endpoint)");
                    continue;
                }
                if (seen.Add(key)) ordered.Add(key);
            }

        return ordered;
    }

    // Derives the internal field type from a property schema.
    internal static string GetFieldType(string fieldName, JsonObject prop, HashSet<string>? searchableResources = null)
    {
        string lower = fieldName.ToLower();
        string? desc = prop["description"]?.GetValue<string>();
        string? type = prop["type"]?.GetValue<string>();
        string? format = prop["format"]?.GetValue<string>();
        int? maxLength = prop["maxLength"]?.GetValue<int>();

        if (!string.IsNullOrEmpty(desc) && desc.StartsWith("ParentTable:"))
        {
            string parentTable = desc["ParentTable:".Length..];
            if (searchableResources == null || searchableResources.Contains(parentTable)) return "select";
        }
        if (lower.Contains("longitude") || lower.Contains("latitude")) return "number";
        if (!string.IsNullOrEmpty(desc) && desc.StartsWith("FieldType:Currency")) return "currency";
        if (lower.Contains("mobilenumber") || lower.Contains("phone")) return "phone";
        if (lower.Contains("identitynumber") || lower.Contains("idnumber")) return "idnumber";

        string[] textareaKeywords = ["reason", "comment", "note", "description", "remarks", "feedback", "summary", "detail", "message", "explanation"];
        if (type == "string" && textareaKeywords.Any(lower.Contains)) return "textarea";

        return type switch
        {
            "integer" or "number" => "number",
            "string" when format == "date-time" => "datetime",
            "string" when format == "date" => "date",
            "string" when maxLength >= 500 => "textarea",
            "string" => "text",
            "boolean" => "checkbox",
            _ => "text"
        };
    }

    static string GetPlaceholder(string fieldName, string fieldType)
    {
        string heading = Formatters.GetFieldHeading(fieldName).ToLower();
        return fieldType switch
        {
            "select" => $"Select {heading}...",
            "date" or "datetime" => $"Select {heading}...",
            "checkbox" => $"{char.ToUpper(heading[0])}{heading[1..]}?",
            _ => $"Enter {heading}..."
        };
    }

    internal static string Render(
        string prefix,
        string modulePascal,
        string resource,
        string requestType,
        List<string> orderedFields,
        JsonObject? properties,
        HashSet<string> requiredFields,
        List<FkField> fkFields,
        JsonObject? fieldLayout,
        HashSet<string>? searchableResources = null,
        bool isUpdate = false)
    {
        var sb = new StringBuilder();
        bool hasSelects = fkFields.Count > 0;
        string kebabResource = Formatters.ToKebabCase(resource);
        string hookSuffix = isUpdate ? "Update" : "Create";

        // Detect which custom validators are needed
        bool needsPhoneValidator = orderedFields.Any(f =>
            properties?.ContainsKey(f) == true && GetFieldType(f, properties[f]!.AsObject()!, searchableResources) == "phone");
        bool needsIdNumberValidator = orderedFields.Any(f =>
            properties?.ContainsKey(f) == true && GetFieldType(f, properties[f]!.AsObject()!, searchableResources) == "idnumber");

        // Detect date range fields
        bool hasStartDate = orderedFields.Any(f => f.Equals("startDate", StringComparison.OrdinalIgnoreCase));
        bool hasEndDate = orderedFields.Any(f => f.Equals("endDate", StringComparison.OrdinalIgnoreCase));
        bool hasStartDateTime = orderedFields.Any(f => f.Equals("startDateTime", StringComparison.OrdinalIgnoreCase));
        bool hasEndDateTime = orderedFields.Any(f => f.Equals("endDateTime", StringComparison.OrdinalIgnoreCase));
        bool hasDateRange = (hasStartDate && hasEndDate) || (hasStartDateTime && hasEndDateTime);

        var componentLibImports = new List<string> { "FilterBy", "OrderBy", "FormLayout" };
        if (hasSelects) componentLibImports.Add("useSelect");
        if (needsPhoneValidator) componentLibImports.Add("validatePhoneNumber");
        if (needsIdNumberValidator) componentLibImports.Add("validateSAIdNumber");

        // Imports
        var rhfImportList = new List<string> { "FieldErrors" };
        if (hasDateRange) rhfImportList.Add("useWatch");
        if (hasDateRange) rhfImportList.Add("Control");
        sb.AppendLine($"import {{ {string.Join(", ", rhfImportList)} }} from \"react-hook-form\"");
        sb.AppendLine($"import {{ {string.Join(", ", componentLibImports)} }} from \"@sseta/components\"");
        if (hasSelects)
        {
            var seenContexts = new HashSet<string>();
            foreach (var fk in fkFields)
                if (seenContexts.Add(fk.ContextName))
                    sb.AppendLine($"import {{ {fk.HookName} }} from \"{fk.ContextImportPath}\"");
        }
        sb.AppendLine($"import {{ {requestType} }} from \"@/types/api.types\"");
        sb.AppendLine();

        // Props interface — always includes selectFilterBys/selectOrderBys for a stable contract
        sb.AppendLine($"interface Use{prefix}{hookSuffix}Props {{");
        sb.AppendLine($"  errors: FieldErrors<{requestType}>");
        sb.AppendLine("  disabledFields?: string[]");
        sb.AppendLine("  selectFilterBys?: Record<string, FilterBy[]>");
        sb.AppendLine("  selectOrderBys?: Record<string, OrderBy[]>");
        if (isUpdate && hasSelects)
            sb.AppendLine("  selectedLabels?: Record<string, string | null | undefined>");
        if (hasDateRange)
            sb.AppendLine($"  control?: Control<{requestType}>");
        sb.AppendLine("}");
        sb.AppendLine();

        // Hook signature
        sb.AppendLine($"export default function use{prefix}{hookSuffix}(props: Use{prefix}{hookSuffix}Props) {{");
        var destructureProps = new List<string> { "errors", "disabledFields = []", "selectFilterBys = {}", "selectOrderBys = {}" };
        if (isUpdate && hasSelects) destructureProps.Add("selectedLabels = {}");
        if (hasDateRange) destructureProps.Add("control");
        sb.AppendLine($"  const {{ {string.Join(", ", destructureProps)} }} = props");
        sb.AppendLine();

        if (hasDateRange)
        {
            sb.AppendLine("  // Watch start/end dates to drive min/max constraints and cross-field validation");
            if (hasStartDate && hasEndDate)
            {
                sb.AppendLine("  const startDate = useWatch({ name: \"startDate\", control })");
                sb.AppendLine("  const endDate = useWatch({ name: \"endDate\", control })");
            }
            if (hasStartDateTime && hasEndDateTime)
            {
                sb.AppendLine("  const startDateTime = useWatch({ name: \"startDateTime\", control })");
                sb.AppendLine("  const endDateTime = useWatch({ name: \"endDateTime\", control })");
            }
            sb.AppendLine();
        }

        if (hasSelects)
        {
            var emittedContextDestructure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in fkFields)
            {
                if (emittedContextDestructure.Add(fk.ParentTable))
                {
                    string pluralPascal = Formatters.ToPascalCase(fk.Plural);
                    sb.AppendLine($"  const {{");
                    sb.AppendLine($"    items: {fk.Plural},");
                    sb.AppendLine($"    totalRows: {fk.Plural}TotalRows,");
                    sb.AppendLine($"    lastSearchTerm: {fk.Plural}LastSearchTerm,");
                    sb.AppendLine($"    fetchItems: fetch{pluralPascal},");
                    sb.AppendLine($"    loadMoreItems: loadMore{pluralPascal},");
                    sb.AppendLine($"  }} = {fk.HookName}()");
                    sb.AppendLine();
                }

                var firstFk = fkFields.First(f => string.Equals(f.ParentTable, fk.ParentTable, StringComparison.OrdinalIgnoreCase));
                string sharedPlural = firstFk.Plural;
                string sharedPluralPascal = Formatters.ToPascalCase(sharedPlural);

                sb.AppendLine($"  const {fk.SelectVar} = useSelect(");
                sb.AppendLine("    {");
                sb.AppendLine($"      items: {sharedPlural},");
                sb.AppendLine($"      totalRows: {sharedPlural}TotalRows,");
                sb.AppendLine($"      lastSearchTerm: {sharedPlural}LastSearchTerm,");
                sb.AppendLine($"      fetchItems: fetch{sharedPluralPascal},");
                sb.AppendLine($"      loadMoreItems: loadMore{sharedPluralPascal},");
                sb.AppendLine("    },");
                sb.AppendLine("    {");
                sb.AppendLine($"      idField: \"{Formatters.GetIdFieldName(fk.ParentTable)}\",");
                sb.AppendLine("      searchColumns: [\"name\"],");
                sb.AppendLine($"      filterBys: selectFilterBys.{fk.FieldName},");
                sb.AppendLine($"      orderBys: selectOrderBys.{fk.FieldName},");
                sb.AppendLine("    }");
                sb.AppendLine("  )");
                sb.AppendLine();
            }
        }

        var fkByField = fkFields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);

        sb.AppendLine("  const fields = {");

        foreach (var fieldName in orderedFields)
        {
            if (properties == null || !properties.ContainsKey(fieldName)) continue;
            var prop = properties[fieldName]?.AsObject();
            if (prop == null) continue;

            string camel = Formatters.ToCamelCase(fieldName);
            string fieldType = GetFieldType(fieldName, prop, searchableResources);
            string outputType = fieldType == "idnumber" ? "text" : fieldType;
            bool isRequired = requiredFields.Contains(fieldName);
            string heading = Formatters.GetFieldHeading(fieldName) + (isRequired ? "" : " (Optional)");
            string placeholder = GetPlaceholder(fieldName, fieldType);
            bool isSelect = fieldType == "select";
            bool isEmailField = fieldName.Equals("Email", StringComparison.OrdinalIgnoreCase);
            fkByField.TryGetValue(fieldName, out var fk);

            bool isDateOrDateTime = fieldType == "date" || fieldType == "datetime";
            bool isStartDateField = fieldName.Equals("startDate", StringComparison.OrdinalIgnoreCase);
            bool isEndDateField = fieldName.Equals("endDate", StringComparison.OrdinalIgnoreCase);
            bool isStartDateTimeField = fieldName.Equals("startDateTime", StringComparison.OrdinalIgnoreCase);
            bool isEndDateTimeField = fieldName.Equals("endDateTime", StringComparison.OrdinalIgnoreCase);
            bool isDateOfBirthField = fieldName.Equals("dateOfBirth", StringComparison.OrdinalIgnoreCase);

            bool inDateRange = hasDateRange && isDateOrDateTime &&
                ((isStartDateField && hasStartDate && hasEndDate) ||
                 (isEndDateField && hasStartDate && hasEndDate) ||
                 (isStartDateTimeField && hasStartDateTime && hasEndDateTime) ||
                 (isEndDateTimeField && hasStartDateTime && hasEndDateTime));

            sb.AppendLine($"    {camel}: {{");
            sb.AppendLine("      props: {");
            sb.AppendLine($"        id: \"{kebabResource}-{camel}\",");
            sb.AppendLine($"        name: \"{camel}\",");
            sb.AppendLine($"        heading: \"{heading}\",");
            sb.AppendLine($"        type: \"{outputType}\" as const,");
            sb.AppendLine($"        placeholder: \"{placeholder}\",");
            sb.AppendLine($"        disabled: disabledFields.includes(\"{camel}\"),");
            if (isSelect)
                sb.AppendLine($"        error: errors.{camel} as any,");
            else
                sb.AppendLine($"        error: errors.{camel},");
            if (fk != null)
            {
                if (isUpdate)
                    sb.AppendLine($"        selectedLabel: selectedLabels.{camel} ?? undefined,");
                sb.AppendLine($"        ...{fk.SelectVar},");
            }
            // Date range min/max props
            if (inDateRange)
            {
                string watchVarStart = (isStartDateTimeField || isEndDateTimeField) ? "startDateTime" : "startDate";
                string watchVarEnd = (isStartDateTimeField || isEndDateTimeField) ? "endDateTime" : "endDate";
                if (isStartDateField || isStartDateTimeField)
                    sb.AppendLine($"        maxDate: {watchVarEnd} ? new Date({watchVarEnd}) : undefined,");
                else
                    sb.AppendLine($"        minDate: {watchVarStart} ? new Date({watchVarStart}) : undefined,");
            }
            if (isDateOfBirthField && isDateOrDateTime)
                sb.AppendLine("        maxDate: new Date(),");
            sb.AppendLine("      },");

            // Rules
            var ruleParts = new List<string>();

            if (fieldType == "select")
            {
                if (isRequired)
                    ruleParts.Add("required: \"Please fill in this field.\"");
            }
            else if (fieldType == "checkbox" && isRequired)
            {
                ruleParts.Add("validate: (value: any) => value === true || value === false || \"Please check this field.\"");
            }
            else if (inDateRange)
            {
                string watchVarStart = (isStartDateTimeField || isEndDateTimeField) ? "startDateTime" : "startDate";
                string watchVarEnd = (isStartDateTimeField || isEndDateTimeField) ? "endDateTime" : "endDate";
                string fieldLabel = fieldType == "datetime" ? "date time" : "date";
                string headingLower = Formatters.GetFieldHeading(fieldName).ToLower();
                string article = Formatters.StartsWithVowel(headingLower) ? "an" : "a";
                bool isStart = isStartDateField || isStartDateTimeField;

                if (isRequired)
                    ruleParts.Add($"required: \"Please fill in this field.\"");

                string watchOther = isStart ? watchVarEnd : watchVarStart;
                string comparison = isStart
                    ? $"new Date(value) > new Date({watchOther}) ? \"Start {fieldLabel} must be before end {fieldLabel}.\" : true"
                    : $"new Date(value) < new Date({watchOther}) ? \"End {fieldLabel} must be after start {fieldLabel}.\" : true";
                string emptyCheck = isRequired ? $"\"Please select {article} {headingLower}.\"" : "true";
                ruleParts.Add($"validate: (value: any) => !value ? {emptyCheck} : !{watchOther} ? true : {comparison}");
            }
            else if (isDateOfBirthField && isDateOrDateTime)
            {
                if (isRequired)
                    ruleParts.Add("required: \"Please fill in this field.\"");
                ruleParts.Add("validate: (value: any) => !value || new Date(value) <= new Date() || \"Date of birth cannot be in the future.\"");
            }
            else if (isRequired)
            {
                ruleParts.Add("required: \"Please fill in this field.\"");
            }

            // Text length
            if (fieldType == "text" || fieldType == "textarea")
            {
                if (prop["minLength"]?.GetValue<int>() is int minLen && minLen > 0)
                    ruleParts.Add($"minLength: {{ value: {minLen}, message: \"Must be at least {minLen} characters.\" }}");
                if (prop["maxLength"]?.GetValue<int>() is int maxLen && maxLen > 0)
                    ruleParts.Add($"maxLength: {{ value: {maxLen}, message: \"Must be less than {maxLen} characters.\" }}");
            }

            // Currency min
            if (fieldType == "currency")
                ruleParts.Add("min: { value: 0.01, message: \"Amount must be greater than 0.\" }");

            // Phone / ID number / email
            if (fieldType == "phone")
                ruleParts.Add("validate: validatePhoneNumber");
            if (fieldType == "idnumber")
            {
                ruleParts.Add("validate: validateSAIdNumber");
            }
            if (isEmailField)
                ruleParts.Add("pattern: { value: /^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/, message: \"Please enter a valid email address.\" }");

            sb.Append("      rules: {");
            if (ruleParts.Count > 0)
            {
                sb.AppendLine();
                foreach (var part in ruleParts)
                    sb.AppendLine($"        {part},");
                sb.AppendLine("      },");
            }
            else
            {
                sb.AppendLine(" },");
            }

            sb.AppendLine("    },");
        }

        sb.AppendLine("  }");
        sb.AppendLine();

        // Inline layout
        var groups = Formatters.BuildLayoutGroups(resource, fieldLayout, properties, searchableResources: searchableResources);
        sb.AppendLine("  const layout: FormLayout[] = [");
        foreach (var group in groups)
        {
            sb.AppendLine("    {");
            sb.AppendLine($"      groupName: \"{group.GroupName}\",");
            sb.AppendLine($"      totalColumns: {group.TotalColumns},");
            sb.AppendLine("      fields: [");
            foreach (var field in group.Fields)
            {
                string typeFragment = field.Type != null ? $", type: \"{field.Type}\"" : "";
                sb.AppendLine($"        {{ name: \"{field.Name}\", columns: {field.Columns}, heading: \"{field.Heading}\"{typeFragment} }},");
            }
            sb.AppendLine("      ],");
            sb.AppendLine("    },");
        }
        sb.AppendLine("  ]");
        sb.AppendLine();

        sb.AppendLine("  return { fields, layout }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // Builds the list of FK fields for a given resource's ordered properties.
    public static List<FkField> CollectFkFields(
        string module,
        string modulePascal,
        List<string> orderedFields,
        JsonObject? properties,
        HashSet<string>? searchableResources = null)
    {
        // Pre-count parent table occurrences to detect ambiguous names.
        var parentTableCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (properties != null)
        {
            foreach (var fieldName in orderedFields)
            {
                var prop = properties[fieldName]?.AsObject();
                string? desc = prop?["description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(desc) && desc.StartsWith("ParentTable:"))
                {
                    string pt = desc["ParentTable:".Length..];
                    parentTableCounts[pt] = parentTableCounts.GetValueOrDefault(pt) + 1;
                }
            }
        }

        var fkFields = new List<FkField>();
        if (properties == null) return fkFields;

        foreach (var fieldName in orderedFields)
        {
            if (!properties.ContainsKey(fieldName)) continue;
            var prop = properties[fieldName]?.AsObject();
            if (prop == null) continue;
            string? desc = prop["description"]?.GetValue<string>();
            if (string.IsNullOrEmpty(desc) || !desc.StartsWith("ParentTable:")) continue;

            string parentTable = desc["ParentTable:".Length..];

            // Skip FK fields whose parent table has no Search endpoint.
            if (searchableResources != null && !searchableResources.Contains(parentTable)) continue;

            // When multiple fields share the same parent table, use the field name
            // (minus trailing Id) as the select var to avoid collisions.
            bool isAmbiguous = parentTableCounts.GetValueOrDefault(parentTable) > 1;
            string selectBase = isAmbiguous
                ? (fieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ? fieldName[..^2] : fieldName)
                : Formatters.ToCamelCase(parentTable);
            string selectVar = Formatters.ToCamelCase(selectBase);
            string plural = Formatters.GetPluralName(selectVar);
            string contextName = $"{modulePascal}{parentTable}Context";
            string hookName = $"use{modulePascal}{parentTable}";
            string contextImportPath = $"@/contexts/resources/{module.ToLower()}/{contextName}";

            fkFields.Add(new FkField(
                FieldName: fieldName,
                ParentTable: parentTable,
                SelectVar: selectVar,
                Plural: plural,
                ContextName: contextName,
                HookName: hookName,
                ContextImportPath: contextImportPath
            ));
        }

        return fkFields;
    }

    static string ApplyTemplate(string content, string? templatePath) =>
        templatePath != null && File.Exists(templatePath)
            ? File.ReadAllText(templatePath).Replace("// [[CONTENT]]", content)
            : content;
}

public record FkField(
    string FieldName,
    string ParentTable,
    string SelectVar,
    string Plural,
    string ContextName,
    string HookName,
    string ContextImportPath
);
