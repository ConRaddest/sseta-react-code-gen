using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class FieldHooksGenerator
    {
        private static JsonObject? _fieldTemplates;

        public static void GenerateFieldHooksFile(string resourceName, JsonObject? schemas, string outputDir, List<string> coreResources, List<string> settingsResources)
        {
            if (schemas == null) return;

            var createModel = Utilities.FindSchema(schemas, $"{resourceName}CreateRequestModel");
            if (createModel == null) return; // Skip if no create model

            // Load field templates if not already loaded
            if (_fieldTemplates == null)
            {
                _fieldTemplates = LoadFieldTemplates();
            }

            var fieldHookContent = GenerateFieldHook(resourceName, createModel, _fieldTemplates, coreResources, settingsResources);
            if (!string.IsNullOrEmpty(fieldHookContent))
            {
                string fileName = $"use{resourceName}Fields.tsx";
                string filePath = Path.Combine(outputDir, fileName);
                File.WriteAllText(filePath, fieldHookContent);
            }
        }

        private static JsonObject? LoadFieldTemplates()
        {
            try
            {
                string templatesPath = Path.Combine("input", "legacy", "fieldTemplates.json");
                if (File.Exists(templatesPath))
                {
                    string templatesContent = File.ReadAllText(templatesPath);
                    return JsonNode.Parse(templatesContent)?.AsObject();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load field templates: {ex.Message}");
            }
            return null;
        }

        static string GenerateFieldHook(string resourceName, JsonObject createModel, JsonObject? fieldTemplates, List<string> coreResources, List<string> settingsResources)
        {
            var sb = new StringBuilder();
            var properties = createModel["properties"]?.AsObject();
            var requiredFields = new HashSet<string>();

            if (createModel["required"] != null)
            {
                foreach (var req in createModel["required"]?.AsArray() ?? [])
                {
                    requiredFields.Add(req?.GetValue<string>() ?? "");
                }
            }

            if (properties == null) return "";

            // Get ordered field groups from field templates
            var fieldGroups = GetOrderedFieldGroupsFromTemplate(resourceName, fieldTemplates, properties);

            // Collect foreign key fields (per-field, no dedup by parent table)
            var foreignKeyFields = new List<ForeignKeyField>();

            foreach (var fieldGroup in fieldGroups)
            {
                foreach (var fieldConfig in fieldGroup.Fields)
                {
                    string fieldName = fieldConfig.Name;
                    if (!properties.ContainsKey(fieldName)) continue;
                    var prop = new KeyValuePair<string, JsonNode?>(fieldName, properties[fieldName]);
                    var propSchema = prop.Value?.AsObject();
                    if (propSchema == null) continue;

                    string? description = propSchema["description"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                    {
                        string parentTable = description.Substring("ParentTable:".Length);
                        foreignKeyFields.Add(new ForeignKeyField
                        {
                            PropertyName = prop.Key,
                            ParentTable = parentTable,
                            PluralName = Utilities.GetPluralName(parentTable),
                            CamelCaseName = Utilities.ToCamelCase(parentTable),
                            ForeignKey = Utilities.ToCamelCase(parentTable) + "Id",
                        });
                    }
                }
            }

            // Check if any field is an ID number field
            bool hasIdNumberField = false;
            foreach (var fieldGroup in fieldGroups)
            {
                foreach (var fieldConfig in fieldGroup.Fields)
                {
                    string fieldName = fieldConfig.Name;
                    if (properties.ContainsKey(fieldName))
                    {
                        var propSchema = properties[fieldName]?.AsObject();
                        if (propSchema != null)
                        {
                            string fieldType = GetFieldType(fieldName, propSchema);
                            if (fieldType == "idnumber")
                            {
                                hasIdNumberField = true;
                                break;
                            }
                        }
                    }
                }
                if (hasIdNumberField) break;
            }

            // Generate imports
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { FieldProps } from \"@/types/legacy/custom/FieldProps\"");

            if (foreignKeyFields.Count > 0)
            {
                sb.AppendLine("import { Filter } from \"@/types/legacy/custom/Setting\"");
            }

            if (hasIdNumberField)
            {
                sb.AppendLine("import { validateSouthAfricanId } from \"@/utils/legacy/saId\"");
            }

            sb.AppendLine();

            // Import services for foreign keys (dedupe by parent table)
            if (foreignKeyFields.Count > 0)
            {
                var importedParents = new HashSet<string>();
                foreach (var fk in foreignKeyFields)
                {
                    if (importedParents.Add(fk.ParentTable))
                    {
                        // Determine subfolder for parent service
                        string subfolder = coreResources.Contains(fk.ParentTable) ? "core" :
                                         settingsResources.Contains(fk.ParentTable) ? "settings" : "";
                        string serviceImportPath = !string.IsNullOrEmpty(subfolder)
                            ? $"@/services/legacy/{subfolder}/{fk.ParentTable}Service"
                            : $"@/services/legacy/{fk.ParentTable}Service";
                        sb.AppendLine($"import {{ search{fk.ParentTable} }} from \"{serviceImportPath}\"");
                    }
                }
                sb.AppendLine();
            }

            sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");

            // Check if both start and end date/datetime exist for date range validation
            bool hasStartDate = properties.ContainsKey("startDate") || properties.ContainsKey("StartDate");
            bool hasEndDate = properties.ContainsKey("endDate") || properties.ContainsKey("EndDate");
            bool hasStartDateTime = properties.ContainsKey("startDateTime") || properties.ContainsKey("StartDateTime");
            bool hasEndDateTime = properties.ContainsKey("endDateTime") || properties.ContainsKey("EndDateTime");
            bool hasDateRange = (hasStartDate && hasEndDate) || (hasStartDateTime && hasEndDateTime);

            if (hasDateRange)
            {
                sb.AppendLine("import { FieldErrors, useWatch } from \"react-hook-form\"");
            }
            else
            {
                sb.AppendLine("import { FieldErrors } from \"react-hook-form\"");
            }

            sb.AppendLine();

            // Generate params interface
            sb.AppendLine($"interface Use{resourceName}FieldsParams {{");
            sb.AppendLine("  control: any");
            sb.AppendLine("  errors: FieldErrors");
            sb.AppendLine("  disabledFields?: string[]");
            sb.AppendLine("  hiddenFields?: string[]");
            foreach (var fk in foreignKeyFields)
            {
                // Use field-specific filter prop (without trailing 'Id') to avoid collisions and verbosity
                var propBase = Utilities.RemoveIdSuffix(fk.PropertyName);
                var filterCamel = Utilities.ToCamelCase(propBase);
                sb.AppendLine($"  {filterCamel}Filters?: Filter[]");
            }
            sb.AppendLine("  onFieldChange?: (field: string, value: any) => void");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate return type interface
            sb.AppendLine($"interface Use{resourceName}FieldsReturn {{");
            sb.AppendLine("    fields: Record<string, FieldProps>");
            sb.AppendLine("    fieldGroups: Array<{");
            sb.AppendLine("        groupName: string");
            sb.AppendLine("        totalColumns: number");
            sb.AppendLine("        fields: Array<{ name: string; columns: number }>");
            sb.AppendLine("    }>");
            sb.AppendLine("    isLoadingSettings: boolean");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate hook signature with object destructuring
            string hookName = $"use{resourceName}Fields";
            sb.AppendLine($"export default function {hookName}({{");
            sb.AppendLine("  control,");
            sb.AppendLine("  errors,");
            sb.AppendLine("  disabledFields = [],");
            sb.AppendLine("  hiddenFields = [],");
            foreach (var fk in foreignKeyFields)
            {
                // Use field-specific filter prop (without trailing 'Id') to avoid collisions and verbosity
                var propBase = Utilities.RemoveIdSuffix(fk.PropertyName);
                var filterCamel = Utilities.ToCamelCase(propBase);
                sb.AppendLine($"  {filterCamel}Filters = [],");
            }
            sb.AppendLine("  onFieldChange,");
            sb.AppendLine($"}}: Use{resourceName}FieldsParams): Use{resourceName}FieldsReturn {{");

            sb.AppendLine("    const isLoadingSettings = false");
            sb.AppendLine();

            // Add useWatch for date range validation if both dates exist
            if (hasDateRange)
            {
                sb.AppendLine("    // Watch start and end dates to set min/max constraints");
                if (hasStartDate && hasEndDate)
                {
                    sb.AppendLine("    const startDate = useWatch({ control, name: \"startDate\" })");
                    sb.AppendLine("    const endDate = useWatch({ control, name: \"endDate\" })");
                }
                if (hasStartDateTime && hasEndDateTime)
                {
                    sb.AppendLine("    const startDateTime = useWatch({ control, name: \"startDateTime\" })");
                    sb.AppendLine("    const endDateTime = useWatch({ control, name: \"endDateTime\" })");
                }
                sb.AppendLine();
            }

            // Generate fields object
            sb.AppendLine("    const fields: Record<string, FieldProps> = {");

            for (int groupIndex = 0; groupIndex < fieldGroups.Count; groupIndex++)
            {
                var fieldGroup = fieldGroups[groupIndex];

                // Add group comment
                if (!string.IsNullOrEmpty(fieldGroup.GroupName))
                {
                    sb.AppendLine($"        // ------------ {fieldGroup.GroupName} ---------------");
                }

                foreach (var fieldConfig in fieldGroup.Fields)
                {
                    string fieldName = fieldConfig.Name;
                    if (!properties.ContainsKey(fieldName)) continue;
                    var prop = new KeyValuePair<string, JsonNode?>(fieldName, properties[fieldName]);
                    var propSchema = prop.Value?.AsObject();
                    if (propSchema == null) continue;

                    string camelCaseFieldName = Utilities.ToCamelCase(prop.Key);

                    string fieldType = prop.Key.ToLower().Contains("password") ? "password" : GetFieldType(prop.Key, propSchema);
                    bool isRequired = requiredFields.Contains(prop.Key);

                    // Map idnumber to text for the type field, but keep internal type for validation
                    string outputFieldType = fieldType == "idnumber" ? "text" : fieldType;

                    // For foreign keys, find the corresponding foreign key field by property name
                    ForeignKeyField? fkField = null;
                    string? description = propSchema["description"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                    {
                        fkField = foreignKeyFields.FirstOrDefault(f => f.PropertyName == prop.Key);
                    }

                    // Determine field type flags for date range validation
                    bool isStartDateField = prop.Key.Equals("StartDate", StringComparison.OrdinalIgnoreCase) ||
                                           prop.Key.Equals("startDate", StringComparison.Ordinal);
                    bool isEndDateField = prop.Key.Equals("EndDate", StringComparison.OrdinalIgnoreCase) ||
                                         prop.Key.Equals("endDate", StringComparison.Ordinal);
                    bool isStartDateTimeField = prop.Key.Equals("StartDateTime", StringComparison.OrdinalIgnoreCase) ||
                                               prop.Key.Equals("startDateTime", StringComparison.Ordinal);
                    bool isEndDateTimeField = prop.Key.Equals("EndDateTime", StringComparison.OrdinalIgnoreCase) ||
                                             prop.Key.Equals("endDateTime", StringComparison.Ordinal);
                    bool isDateOfBirthField = prop.Key.Equals("DateOfBirth", StringComparison.OrdinalIgnoreCase);

                    sb.AppendLine($"        {camelCaseFieldName}: {{");
                    sb.AppendLine($"            name: \"{camelCaseFieldName}\",");
                    sb.AppendLine($"            heading: \"{Utilities.GetFieldHeading(prop.Key)}{(!isRequired && outputFieldType != "checkbox" ? " (Optional)" : "")}\",");
                    sb.AppendLine($"            placeholder: \"{GetFieldPlaceholder(prop.Key, fieldType)}\",");
                    sb.AppendLine($"            type: \"{outputFieldType}\",");

                    // Add minDate/maxDate for date range validation
                    if (hasDateRange && (fieldType == "date" || fieldType == "datetime"))
                    {
                        if (isStartDateField && hasStartDate && hasEndDate)
                        {
                            sb.AppendLine("            maxDate: endDate ? new Date(endDate) : undefined,");
                        }
                        else if (isEndDateField && hasStartDate && hasEndDate)
                        {
                            sb.AppendLine("            minDate: startDate ? new Date(startDate) : undefined,");
                        }
                        else if (isStartDateTimeField && hasStartDateTime && hasEndDateTime)
                        {
                            sb.AppendLine("            maxDate: endDateTime ? new Date(endDateTime) : undefined,");
                        }
                        else if (isEndDateTimeField && hasStartDateTime && hasEndDateTime)
                        {
                            sb.AppendLine("            minDate: startDateTime ? new Date(startDateTime) : undefined,");
                        }
                    }

                    // Date of birth must not be in the future
                    if (isDateOfBirthField && (fieldType == "date" || fieldType == "datetime"))
                    {
                        sb.AppendLine("            maxDate: new Date(),");
                    }

                    if (fkField != null)
                    {
                        var fkPropBase = Utilities.RemoveIdSuffix(fkField.PropertyName);

                        var foreignKeyNameField = Utilities.RemoveIdSuffix(fkPropBase) + "Name";

                        sb.AppendLine($"            foreignKeyNameField: \"{foreignKeyNameField}\",");
                        sb.AppendLine("            selectSettingsConfig: {");
                        sb.AppendLine($"                service: search{fkField.ParentTable},");
                        sb.AppendLine($"                idField: \"{Utilities.ToCamelCase(fkField.ForeignKey)}\",");
                        sb.AppendLine($"                filterByList: {Utilities.ToCamelCase(fkPropBase)}Filters,");
                        sb.AppendLine("            },");
                    }

                    // Generate validation rules
                    sb.AppendLine("            rules: {");

                    // Add date range validation if applicable
                    bool needsDateRangeValidation = (hasDateRange && (fieldType == "date" || fieldType == "datetime") &&
                                                     ((isStartDateField && hasStartDate && hasEndDate) ||
                                                      (isEndDateField && hasStartDate && hasEndDate) ||
                                                      (isStartDateTimeField && hasStartDateTime && hasEndDateTime) ||
                                                      (isEndDateTimeField && hasStartDateTime && hasEndDateTime)));

                    if (needsDateRangeValidation)
                    {
                        bool isStartField = isStartDateField || isStartDateTimeField;
                        string heading = Utilities.GetFieldHeading(prop.Key).ToLower();
                        string watchVarStart = isStartDateTimeField || isEndDateTimeField ? "startDateTime" : "startDate";
                        string watchVarEnd = isStartDateTimeField || isEndDateTimeField ? "endDateTime" : "endDate";
                        string fieldLabel = fieldType == "datetime" ? "date time" : "date";

                        if (isRequired)
                        {
                            sb.AppendLine($"                required: \"Please fill in this field.\",");
                        }

                        sb.AppendLine("                validate: (value: any) => {");
                        sb.AppendLine($"                    if (!value) return \"Please select {(Utilities.StartsWithVowel(heading) ? "an" : "a")} {heading}.\";");

                        if (isStartField)
                        {
                            sb.AppendLine($"                    if ({watchVarEnd}) {{");
                            sb.AppendLine("                        const start = new Date(value);");
                            sb.AppendLine($"                        const end = new Date({watchVarEnd});");
                            sb.AppendLine("                        if (start > end) {");
                            sb.AppendLine($"                            return \"Start {fieldLabel} must be before end {fieldLabel}.\";");
                            sb.AppendLine("                        }");
                            sb.AppendLine("                    }");
                        }
                        else // isEndField
                        {
                            sb.AppendLine($"                    if ({watchVarStart}) {{");
                            sb.AppendLine($"                        const start = new Date({watchVarStart});");
                            sb.AppendLine("                        const end = new Date(value);");
                            sb.AppendLine("                        if (end < start) {");
                            sb.AppendLine($"                            return \"End {fieldLabel} must be after start {fieldLabel}.\";");
                            sb.AppendLine("                        }");
                            sb.AppendLine("                    }");
                        }

                        sb.AppendLine("                    return true;");
                        sb.AppendLine("                },");
                    }
                    else if (isDateOfBirthField && (fieldType == "date" || fieldType == "datetime"))
                    {
                        if (isRequired)
                        {
                            sb.AppendLine($"                required: \"{GetRequiredMessage(prop.Key, fieldType)}\",");
                        }

                        sb.AppendLine("                validate: (value: any) => {");
                        sb.AppendLine("                    if (!value) return true;");
                        sb.AppendLine("                    const selected = new Date(value);");
                        sb.AppendLine("                    const today = new Date();");
                        sb.AppendLine("                    if (selected > today) {");
                        sb.AppendLine("                        return \"Date of birth cannot be in the future.\";");
                        sb.AppendLine("                    }");
                        sb.AppendLine("                    return true;");
                        sb.AppendLine("                },");
                    }
                    else if (fieldType == "select")
                    {
                        string heading = Utilities.GetFieldHeading(prop.Key).ToLower();
                        string article = Utilities.StartsWithVowel(heading) ? "an" : "a";

                        sb.AppendLine("                validate: (value: any) => {");

                        if (isRequired)
                        {
                            sb.AppendLine("                    if (value === undefined || value === null || value === 0) {");
                            sb.AppendLine($"                        return \"Please select {article} {heading}.\"");
                            sb.AppendLine("                    }");
                        }
                        else
                        {
                            sb.AppendLine("                    // Optional field - allow empty values");
                            sb.AppendLine("                    if (value === undefined || value === null || value === 0) {");
                            sb.AppendLine("                        return true");
                            sb.AppendLine("                    }");
                        }
                        sb.AppendLine("                    return true");
                        sb.AppendLine("                },");
                    }
                    else if (isRequired)
                    {
                        if (fieldType == "checkbox")
                        {
                            sb.AppendLine("                validate: (value: any) => {");
                            sb.AppendLine("                    if (value === true || value === false) {");
                            sb.AppendLine("                        return true");
                            sb.AppendLine("                    }");
                            sb.AppendLine($"                    return \"Please check this field.\"");
                            sb.AppendLine("                },");
                        }
                        else
                        {
                            sb.AppendLine($"                required: \"{GetRequiredMessage(prop.Key, fieldType)}\",");
                        }
                    }

                    // Add field-specific validation
                    AddFieldValidation(sb, propSchema, fieldType);

                    sb.AppendLine("            },");
                    sb.AppendLine("            errors,");
                    sb.AppendLine("            control,");
                    sb.AppendLine($"            disabled: disabledFields.includes(\"{camelCaseFieldName}\"),");
                    sb.AppendLine($"            onValueChange: (val) => onFieldChange?.(\"{camelCaseFieldName}\", val),");
                    sb.AppendLine("        },");
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            // Generate allFieldGroups array (before filtering)
            sb.AppendLine("    const allFieldGroups = [");
            foreach (var group in fieldGroups)
            {
                sb.AppendLine("        {");
                sb.AppendLine($"            groupName: \"{group.GroupName}\",");
                sb.AppendLine($"            totalColumns: {group.TotalColumns},");
                sb.AppendLine("            fields: [");

                foreach (var fieldConfig in group.Fields)
                {
                    if (!properties.ContainsKey(fieldConfig.Name)) continue;
                    string camelCaseFieldName = Utilities.ToCamelCase(fieldConfig.Name);
                    sb.AppendLine($"                {{ name: \"{camelCaseFieldName}\", columns: {fieldConfig.Columns} }},");
                }

                sb.AppendLine("            ],");
                sb.AppendLine("        },");
            }
            sb.AppendLine("    ]");
            sb.AppendLine();

            // Filter out hidden fields from field groups
            sb.AppendLine("    // Filter out hidden fields from field groups");
            sb.AppendLine("    const fieldGroups = allFieldGroups");
            sb.AppendLine("        .map(group => ({");
            sb.AppendLine("            ...group,");
            sb.AppendLine("            fields: group.fields.filter(field => !hiddenFields.includes(field.name))");
            sb.AppendLine("        }))");
            sb.AppendLine("        .filter(group => group.fields.length > 0) // Remove empty groups");
            sb.AppendLine();

            sb.AppendLine("    return { fields, fieldGroups, isLoadingSettings: isLoadingSettings }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        static string GetFieldType(string fieldName, JsonObject propSchema)
        {
            string? type = propSchema["type"]?.GetValue<string>();
            int? maxLength = propSchema["maxLength"]?.GetValue<int>();

            string? format = propSchema["format"]?.GetValue<string>();
            string? description = propSchema["description"]?.GetValue<string>();

            // Foreign key fields are selects
            if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                return "select";

            if (!string.IsNullOrEmpty(description) && description.StartsWith("FieldType:Currency"))
                return "currency";

            // Mobile number fields are phone type
            if (fieldName.ToLower().Contains("mobilenumber") || fieldName.ToLower().Contains("phone"))
                return "phone";

            // ID number fields (South African ID)
            if (fieldName.ToLower().Contains("identitynumber") || fieldName.ToLower().Contains("idnumber"))
                return "idnumber";

            return type switch
            {
                "integer" => "number",
                "number" => "number",
                "string" when format == "date-time" => "datetime",
                "string" when format == "date" => "date",
                "string" => maxLength > 500 ? "textarea" : "text",
                "boolean" => "checkbox",
                _ => "text"
            };
        }

        static string GetFieldPlaceholder(string fieldName, string fieldType)
        {
            string heading = Utilities.GetFieldHeading(fieldName).ToLower();

            return fieldType switch
            {
                "select" => $"Select {heading}...",
                "currency" => "Enter amount...",
                "number" => $"Enter {heading}...",
                "date" => $"Select {heading}...",
                "datetime" => $"Select {heading}...",
                "idnumber" => "Enter 13-digit ID number...",
                "checkbox" => $"{char
                .ToUpper(Utilities.GetFieldHeading(fieldName)
                .ToLower()[0])}{Utilities.GetFieldHeading(fieldName)
                .ToLower().Substring(1)}?",
                _ => $"Enter {heading}..."
            };
        }

        static string GetRequiredMessage(string fieldName, string fieldType)
        {
            string heading = Utilities.GetFieldHeading(fieldName).ToLower();
            string article = Utilities.StartsWithVowel(heading) ? "an" : "a";

            return fieldType switch
            {
                "select" => $"Please select {article} {heading}.",
                "currency" => "Please enter an amount.",
                "number" => $"Please enter {heading}.",
                "datetime" => $"Please select {article} {heading}.",
                "checkbox" => $"Please check this field.",
                _ => "Please fill in this field."
            };
        }

        static void AddFieldValidation(StringBuilder sb, JsonObject propSchema, string fieldType)
        {
            // Add ID number validation
            if (fieldType == "idnumber")
            {
                sb.AppendLine("                pattern: {");
                sb.AppendLine("                    value: /^\\d{13}$/,");
                sb.AppendLine("                    message: \"ID number must be exactly 13 digits.\",");
                sb.AppendLine("                },");
                sb.AppendLine("                validate: (value: string) => {");
                sb.AppendLine("                    if (!validateSouthAfricanId(value)) {");
                sb.AppendLine("                        return \"Please enter a valid South African ID number.\"");
                sb.AppendLine("                    }");
                sb.AppendLine("                    return true");
                sb.AppendLine("                },");
                return; // ID number has its own validation, skip other validations
            }

            // Add minLength/maxLength for strings
            if (fieldType == "text" || fieldType == "textarea")
            {
                if (propSchema.TryGetPropertyValue("minLength", out var minLengthNode))
                {
                    int minLength = minLengthNode?.GetValue<int>() ?? 0;
                    if (minLength > 0)
                    {
                        sb.AppendLine($"                minLength: {{");
                        sb.AppendLine($"                    value: {minLength},");
                        sb.AppendLine($"                    message: \"Must be at least {minLength} characters.\",");
                        sb.AppendLine("                },");
                    }
                }

                if (propSchema.TryGetPropertyValue("maxLength", out var maxLengthNode))
                {
                    int maxLength = maxLengthNode?.GetValue<int>() ?? 0;
                    if (maxLength > 0)
                    {
                        sb.AppendLine($"                maxLength: {{");
                        sb.AppendLine($"                    value: {maxLength},");
                        sb.AppendLine($"                    message: \"Must be at less than {maxLength} characters.\",");
                        sb.AppendLine("                },");
                    }
                }
            }

            // Add min/max for numbers and currency
            if (fieldType == "number" || fieldType == "currency")
            {
                // Add default min for currency
                if (fieldType == "currency")
                {
                    sb.AppendLine("                min: {");
                    sb.AppendLine("                    value: 0.01,");
                    sb.AppendLine("                    message: \"Amount must be greater than 0.\",");
                    sb.AppendLine("                },");
                }
            }
        }

        private class FieldGroup
        {
            public string GroupName { get; set; } = "";
            public int TotalColumns { get; set; } = 2;
            public List<FieldConfig> Fields { get; set; } = new();
        }

        private class FieldConfig
        {
            public string Name { get; set; } = "";
            public int Columns { get; set; } = 1;
        }

        private static List<FieldGroup> GetOrderedFieldGroupsFromTemplate(string resourceName, JsonObject? fieldTemplates, JsonObject properties)
        {
            var fieldGroups = new List<FieldGroup>();
            var processedFields = new HashSet<string>();

            if (fieldTemplates != null && fieldTemplates.TryGetPropertyValue(resourceName, out var resourceTemplateNode))
            {
                var resourceTemplate = resourceTemplateNode?.AsArray();
                if (resourceTemplate != null)
                {
                    // Iterate through field groups in order
                    foreach (var groupNode in resourceTemplate)
                    {
                        var group = groupNode?.AsObject();
                        if (group == null) continue;

                        var groupName = group["groupName"]?.GetValue<string>() ?? "";
                        var totalColumns = group["totalColumns"]?.GetValue<int>() ?? 2;
                        var fieldsArray = group["fields"]?.AsArray();
                        if (fieldsArray == null) continue;

                        var groupFields = new List<FieldConfig>();

                        // Add fields from this group in order
                        foreach (var fieldNode in fieldsArray)
                        {
                            var field = fieldNode?.AsObject();
                            if (field == null) continue;

                            var fieldName = field["name"]?.GetValue<string>();
                            var columns = field["columns"]?.GetValue<int>() ?? 1;
                            if (!string.IsNullOrEmpty(fieldName) && properties.ContainsKey(fieldName))
                            {
                                groupFields.Add(new FieldConfig
                                {
                                    Name = fieldName,
                                    Columns = columns
                                });
                                processedFields.Add(fieldName);
                            }
                        }

                        if (groupFields.Count > 0)
                        {
                            fieldGroups.Add(new FieldGroup
                            {
                                GroupName = groupName,
                                TotalColumns = totalColumns,
                                Fields = groupFields
                            });
                        }
                    }
                }
            }

            // If no template found or template doesn't contain all properties, add remaining properties in a default group
            var remainingFields = properties.Where(kvp => !processedFields.Contains(kvp.Key)).ToList();
            if (remainingFields.Count > 0)
            {
                fieldGroups.Add(new FieldGroup
                {
                    GroupName = "Additional Fields",
                    TotalColumns = 2,
                    Fields = remainingFields.Select(kvp => new FieldConfig
                    {
                        Name = kvp.Key,
                        Columns = 1
                    }).ToList()
                });
            }

            return fieldGroups;
        }
    }
}
