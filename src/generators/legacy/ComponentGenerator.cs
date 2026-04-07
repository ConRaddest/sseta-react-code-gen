using System.Text;
using System.Text.Json.Nodes;

namespace ReactCodegen.Legacy
{
    public static class ComponentGenerator
    {
        public static void GenerateComponentsForResource(string resourceName, JsonObject? schemas, string outputDir)
        {
            if (schemas == null) return;

            var createModel = Utilities.FindSchema(schemas, $"{resourceName}CreateRequestModel");
            var updateModel = Utilities.FindSchema(schemas, $"{resourceName}UpdateRequestModel");

            // Check if it's a non-CRUD resource
            if (createModel == null && updateModel == null) return;

            // Create components directory structure
            string createComponentDir = Path.Combine(outputDir, "components", "legacy", "Forms", "Create");
            string updateComponentDir = Path.Combine(outputDir, "components", "legacy", "Forms", "Update");
            string viewComponentDir = Path.Combine(outputDir, "components", "legacy", "Forms", "View");
            string deleteComponentDir = Path.Combine(outputDir, "components", "legacy", "Forms", "Delete");

            if (!Directory.Exists(createComponentDir))
            {
                Console.WriteLine($"Component directory '{createComponentDir}' does not exist. Creating it...");
                Directory.CreateDirectory(createComponentDir);
            }

            if (!Directory.Exists(updateComponentDir))
            {
                Console.WriteLine($"Component directory '{updateComponentDir}' does not exist. Creating it...");
                Directory.CreateDirectory(updateComponentDir);
            }

            if (!Directory.Exists(viewComponentDir))
            {
                Console.WriteLine($"Component directory '{viewComponentDir}' does not exist. Creating it...");
                Directory.CreateDirectory(viewComponentDir);
            }

            if (!Directory.Exists(deleteComponentDir))
            {
                Console.WriteLine($"Component directory '{deleteComponentDir}' does not exist. Creating it...");
                Directory.CreateDirectory(deleteComponentDir);
            }

            // Generate Create component
            if (createModel != null)
            {
                GenerateCreateComponent(resourceName, createComponentDir, createModel);
            }

            // Generate Update component
            if (updateModel != null)
            {
                GenerateUpdateComponent(resourceName, updateComponentDir, updateModel, createModel);
            }

            // Generate View component (prefer response model to include display fields)
            var viewModel = Utilities.FindSchema(schemas, $"{resourceName}RetrieveResponseModel") ??
                            Utilities.FindSchema(schemas, $"{resourceName}ResponseModel") ??
                            Utilities.FindSchema(schemas, resourceName) ??
                            updateModel;
            if (viewModel != null)
            {
                GenerateViewComponent(resourceName, viewComponentDir, viewModel);
            }

            // Generate Delete component (using update model as it has all fields)
            if (updateModel != null)
            {
                GenerateDeleteComponent(resourceName, deleteComponentDir, updateModel);
            }
        }

        static void GenerateCreateComponent(string resourceName, string componentDir, JsonObject createModel)
        {
            var sb = new StringBuilder();
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string kebabResource = Utilities.ToKebabCase(resourceName);
            string mainResourceIdField = Utilities.GetIdFieldName(resourceName);
            var properties = createModel["properties"]?.AsObject();

            if (properties == null) return;

            // Collect foreign key fields for filter props (use field name to avoid conflicts)
            var foreignKeyProps = new List<(string propName, string parentTable)>();
            foreach (var prop in properties)
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                string? description = propSchema["description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                {
                    string parentTable = description.Substring("ParentTable:".Length).Trim();
                    // Use the actual field/property name minus trailing 'Id' to avoid collisions and verbosity
                    string fieldName = Utilities.RemoveIdSuffix(prop.Key);
                    string fieldCamel = Utilities.ToCamelCase(fieldName);
                    foreignKeyProps.Add(($"{fieldCamel}Filters", parentTable));
                }
            }

            bool hasForeignKeys = foreignKeyProps.Count > 0;
            bool hasStatusDefault = properties.Any(kvp => string.Equals(Utilities.ToCamelCase(kvp.Key), "statusId", StringComparison.OrdinalIgnoreCase));
            bool hasIdentityNumber = properties.Any(kvp => string.Equals(Utilities.ToCamelCase(kvp.Key), "identityNumber", StringComparison.OrdinalIgnoreCase));

            // Generate file header and imports
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { FormTemplate } from \"@/components/legacy/FormTemplate/FormTemplate\"");
            sb.AppendLine($"import use{resourceName}Fields from \"@/field-hooks/legacy/use{resourceName}Fields\"");
            sb.AppendLine("import { useResourceForm } from \"@/hooks/legacy/useResourceForm\"");
            sb.AppendLine($"import {{ {resourceName}Create }} from \"@/types/legacy/core/{resourceName}\"");
            sb.AppendLine($"import {{ create{resourceName} }} from \"@/services/legacy/core/{resourceName}Service\"");
            if (hasForeignKeys)
            {
                sb.AppendLine("import { Filter } from \"@/types/legacy/custom/Setting\"");
            }
            if (hasStatusDefault)
            {
                sb.AppendLine("import { Status } from \"@sseta/components\"");
            }
            if (hasIdentityNumber)
            {
                sb.AppendLine("import { useSaIdDefaults } from \"@/hooks/legacy/useSaIdDefaults\"");
            }
            sb.AppendLine("import { ReactNode } from \"react\"");
            sb.AppendLine();

            // Generate interface for props
            sb.AppendLine($"interface Create{resourceName}Props {{");
            sb.AppendLine("  onSuccess?: () => void");
            sb.AppendLine("  onCreated?: (resourceId: number) => void");
            sb.AppendLine("  redirectPath?: string");
            sb.AppendLine($"  defaultValues?: Partial<{resourceName}Create>");
            sb.AppendLine($"  setValues?: Partial<{resourceName}Create>");
            sb.AppendLine("  disabledFields?: string[]");
            sb.AppendLine("  hiddenFields?: string[]");
            sb.AppendLine("  renderInDrawer?: boolean");
            if (hasStatusDefault)
            {
                sb.AppendLine("  statusEnabled?: boolean");
            }
            sb.AppendLine("  showGroupHeadings?: boolean");
            sb.AppendLine("  headerContent?: ReactNode");
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"  {fk.propName}?: Filter[]");
            }
            sb.AppendLine("  onFieldChange?: (field: string, value: any) => void");
            sb.AppendLine($"  liveValues?: Partial<{resourceName}Create>");
            sb.AppendLine("  onBeforeSubmit?: () => boolean");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate component function
            sb.AppendLine($"export function Create{resourceName}({{");
            sb.AppendLine("  onSuccess,");
            sb.AppendLine("  onCreated,");
            sb.AppendLine("  redirectPath,");
            sb.AppendLine("  defaultValues,");
            sb.AppendLine("  setValues,");
            sb.AppendLine("  disabledFields: externalDisabledFields,");
            sb.AppendLine("  hiddenFields,");
            sb.AppendLine("  renderInDrawer = false,");
            if (hasStatusDefault)
            {
                sb.AppendLine("  statusEnabled = false,");
            }
            sb.AppendLine("  showGroupHeadings = true,");
            sb.AppendLine("  headerContent,");
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"  {fk.propName},");
            }
            sb.AppendLine("  onFieldChange,");
            sb.AppendLine("  liveValues,");
            sb.AppendLine("  onBeforeSubmit,");
            sb.AppendLine($"}}: Create{resourceName}Props) {{");
            if (hasStatusDefault)
            {
                sb.AppendLine("  // Auto-hide status fields if statusEnabled is false");
                sb.AppendLine("  const computedHiddenFields = statusEnabled");
                sb.AppendLine("    ? hiddenFields");
                sb.AppendLine("    : [...(hiddenFields || []), \"statusId\", \"statusReason\"]");
                sb.AppendLine();
            }
            sb.AppendLine("  const {");
            if (hasIdentityNumber)
            {
                sb.AppendLine("    form,");
            }
            else
            {
                sb.AppendLine("    form: {");
                sb.AppendLine("      handleSubmit,");
                sb.AppendLine("      control,");
                sb.AppendLine("      formState: { errors, isValid },");
                sb.AppendLine("    },");
            }
            sb.AppendLine("    disabledFields,");
            sb.AppendLine("    validationErrors,");
            sb.AppendLine("    isSubmitting,");
            sb.AppendLine("    onSubmit,");
            sb.AppendLine($"  }} = useResourceForm<{resourceName}Create>({{");
            sb.AppendLine("    baseDefaultValues: {");

            foreach (var prop in properties)
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(prop.Key);
                if (fieldName == "statusId")
                {
                    sb.AppendLine($"      {fieldName}: Status.Draft,");
                }
                else if (string.Equals(propSchema["type"]?.GetValue<string>(), "boolean", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"      {fieldName}: false,");
                }
                else
                {
                    sb.AppendLine($"      {fieldName}: undefined,");
                }
            }

            sb.AppendLine("    },");
            sb.AppendLine("    defaultValues,");
            sb.AppendLine("    setValues,");
            sb.AppendLine("    externalDisabledFields,");
            sb.AppendLine("    liveValues,");
            sb.AppendLine("    submitConfig: {");
            sb.AppendLine($"      submitAction: create{resourceName},");
            sb.AppendLine($"      successMessage: \"{Utilities.GetFieldHeading(resourceName)} successfully created.\",");
            sb.AppendLine("      onSuccess,");
            sb.AppendLine("      onCreated,");
            sb.AppendLine("      redirectPath,");
            sb.AppendLine($"      extractCreatedId: (response) => response.data?.{mainResourceIdField},");
            sb.AppendLine("    },");
            sb.AppendLine("  })");
            sb.AppendLine();
            if (hasIdentityNumber)
            {
                sb.AppendLine("  const {");
                sb.AppendLine("    handleSubmit,");
                sb.AppendLine("    control,");
                sb.AppendLine("    formState: { errors, isValid },");
                sb.AppendLine("  } = form");
                sb.AppendLine();
                sb.AppendLine("  useSaIdDefaults(form)");
                sb.AppendLine();
            }
            sb.AppendLine($"  const {{ fields, fieldGroups, isLoadingSettings }} = use{resourceName}Fields({{");
            sb.AppendLine("    control,");
            sb.AppendLine("    errors,");
            sb.AppendLine("    disabledFields,");
            if (hasStatusDefault)
            {
                sb.AppendLine("    hiddenFields: computedHiddenFields,");
            }
            else
            {
                sb.AppendLine("    hiddenFields,");
            }
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"    {fk.propName},");
            }
            sb.AppendLine("    onFieldChange: onFieldChange,");
            sb.AppendLine("  })");
            sb.AppendLine();

            // Generate render
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // --------------------- MAIN RENDER ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  return (");
            sb.AppendLine("    <FormTemplate");
            sb.AppendLine("      loading={isSubmitting || isLoadingSettings}");
            sb.AppendLine($"      header=\"Create {Utilities.GetFieldHeading(resourceName)}\"");
            sb.AppendLine("      validationErrors={validationErrors}");
            sb.AppendLine("      fields={fields}");
            sb.AppendLine("      fieldGroups={fieldGroups}");
            sb.AppendLine("      onSubmit={onSubmit}");
            sb.AppendLine("      handleSubmit={handleSubmit}");
            sb.AppendLine("      submitButtonText=\"Save\"");
            sb.AppendLine("      isValid={isValid}");
            sb.AppendLine("      showGroupHeadings={showGroupHeadings}");
            sb.AppendLine("      headerContent={headerContent}");
            sb.AppendLine("      renderActionsInFooter={renderInDrawer}");
            sb.AppendLine("      onBeforeSubmit={onBeforeSubmit}");
            sb.AppendLine("    />");
            sb.AppendLine("  )");
            sb.AppendLine("}");

            // Write file
            string filePath = Path.Combine(componentDir, $"Create{resourceName}.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }

        static void GenerateUpdateComponent(string resourceName, string componentDir, JsonObject updateModel, JsonObject? createModel = null)
        {
            var sb = new StringBuilder();
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string kebabResource = Utilities.ToKebabCase(resourceName);
            string mainResourceIdField = Utilities.GetIdFieldName(resourceName);
            var properties = updateModel["properties"]?.AsObject();
            var foreignKeyProperties = createModel?["properties"]?.AsObject() ?? properties;

            if (properties == null) return;

            // Collect foreign key fields for filter props (use field name to avoid conflicts)
            var foreignKeyProps = new List<(string propName, string parentTable)>();
            foreach (var prop in foreignKeyProperties ?? new JsonObject())
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                // Skip the primary id field - it's not a foreign key
                string camelName = Utilities.ToCamelCase(prop.Key);
                if (string.Equals(camelName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                string? description = propSchema["description"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                {
                    string parentTable = description.Substring("ParentTable:".Length).Trim();
                    // Use the actual field/property name minus trailing 'Id' to avoid collisions and verbosity
                    string fieldName = Utilities.RemoveIdSuffix(prop.Key);
                    string fieldCamel = Utilities.ToCamelCase(fieldName);
                    foreignKeyProps.Add(($"{fieldCamel}Filters", parentTable));
                }
            }

            bool hasForeignKeys = foreignKeyProps.Count > 0;
            bool hasStatusField = properties.Any(kvp => string.Equals(Utilities.ToCamelCase(kvp.Key), "statusId", StringComparison.OrdinalIgnoreCase));
            bool hasIdentityNumber = properties.Any(kvp => string.Equals(Utilities.ToCamelCase(kvp.Key), "identityNumber", StringComparison.OrdinalIgnoreCase));

            // Generate file header and imports
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { FormTemplate } from \"@/components/legacy/FormTemplate/FormTemplate\"");
            sb.AppendLine($"import use{resourceName}Fields from \"@/field-hooks/legacy/use{resourceName}Fields\"");
            sb.AppendLine("import { omitUndefinedValues, useResourceForm } from \"@/hooks/legacy/useResourceForm\"");
            sb.AppendLine($"import {{ {resourceName}Update }} from \"@/types/legacy/core/{resourceName}\"");
            sb.AppendLine($"import {{ get{resourceName}, update{resourceName} }} from \"@/services/legacy/core/{resourceName}Service\"");
            if (hasForeignKeys)
            {
                sb.AppendLine("import { Filter } from \"@/types/legacy/custom/Setting\"");
            }
            if (hasIdentityNumber)
            {
                sb.AppendLine("import { useSaIdDefaults } from \"@/hooks/legacy/useSaIdDefaults\"");
            }
            sb.AppendLine("import { ReactNode } from \"react\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");
            sb.AppendLine("import { useEffect, useState } from \"react\"");
            sb.AppendLine("import { AxiosError } from \"axios\"");
            sb.AppendLine();

            // Generate interface for props
            sb.AppendLine($"interface Update{resourceName}Props {{");
            sb.AppendLine($"  {lowerResource}Id: number");
            sb.AppendLine("  onSuccess?: () => void");
            sb.AppendLine("  redirectPath?: string");
            sb.AppendLine($"  defaultValues?: Partial<{resourceName}Update>");
            sb.AppendLine($"  setValues?: Partial<{resourceName}Update>");
            sb.AppendLine("  disabledFields?: string[]");
            sb.AppendLine("  hiddenFields?: string[]");
            sb.AppendLine("  renderInDrawer?: boolean");
            if (hasStatusField)
            {
                sb.AppendLine("  statusEnabled?: boolean");
            }
            sb.AppendLine("  showGroupHeadings?: boolean");
            sb.AppendLine("  headerContent?: ReactNode");
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"  {fk.propName}?: Filter[]");
            }
            sb.AppendLine("  onFieldChange?: (field: string, value: any) => void");
            sb.AppendLine($"  liveValues?: Partial<{resourceName}Update>");
            sb.AppendLine("  onBeforeSubmit?: () => boolean");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate component function
            sb.AppendLine($"export function Update{resourceName}({{");
            sb.AppendLine($"  {lowerResource}Id,");
            sb.AppendLine("  onSuccess,");
            sb.AppendLine("  redirectPath,");
            sb.AppendLine("  defaultValues,");
            sb.AppendLine("  setValues,");
            sb.AppendLine("  disabledFields: externalDisabledFields,");
            sb.AppendLine("  hiddenFields,");
            sb.AppendLine("  renderInDrawer = false,");
            if (hasStatusField)
            {
                sb.AppendLine("  statusEnabled = false,");
            }
            sb.AppendLine("  showGroupHeadings = true,");
            sb.AppendLine("  headerContent,");
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"  {fk.propName},");
            }
            sb.AppendLine("  onFieldChange,");
            sb.AppendLine("  liveValues,");
            sb.AppendLine("  onBeforeSubmit,");
            sb.AppendLine($"}}: Update{resourceName}Props) {{");
            if (hasStatusField)
            {
                sb.AppendLine("  // Auto-hide status fields if statusEnabled is false");
                sb.AppendLine("  const computedHiddenFields = statusEnabled");
                sb.AppendLine("    ? hiddenFields");
                sb.AppendLine("    : [...(hiddenFields || []), \"statusId\", \"statusReason\"]");
                sb.AppendLine();
            }
            sb.AppendLine("  const sanitizedDefaultValues = omitUndefinedValues(defaultValues)");
            sb.AppendLine("  const sanitizedSetValues = omitUndefinedValues(setValues)");
            sb.AppendLine();
            sb.AppendLine("  const {");
            if (hasIdentityNumber)
            {
                sb.AppendLine("    form,");
            }
            else
            {
                sb.AppendLine("    form: {");
                sb.AppendLine("      handleSubmit,");
                sb.AppendLine("      control,");
                sb.AppendLine("      formState: { errors, isValid },");
                sb.AppendLine("      reset,");
                sb.AppendLine("      trigger,");
                sb.AppendLine("    },");
            }
            sb.AppendLine("    disabledFields,");
            sb.AppendLine("    validationErrors,");
            sb.AppendLine("    isSubmitting,");
            sb.AppendLine("    onSubmit,");
            sb.AppendLine($"  }} = useResourceForm<{resourceName}Update>({{");
            sb.AppendLine("    baseDefaultValues: {");

            // Skip the main resource ID field in defaults
            foreach (var prop in properties)
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(prop.Key);
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if this is a foreign key field (check in foreignKeyProperties)
                bool isForeignKey = false;
                if (foreignKeyProperties != null)
                {
                    foreach (var fkProp in foreignKeyProperties)
                    {
                        if (string.Equals(Utilities.ToCamelCase(fkProp.Key), fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            var fkSchema = fkProp.Value?.AsObject();
                            string? fkDescription = fkSchema?["description"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(fkDescription) && fkDescription.StartsWith("ParentTable:"))
                            {
                                isForeignKey = true;
                                break;
                            }
                        }
                    }
                }

                if (string.Equals(propSchema["type"]?.GetValue<string>(), "boolean", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"      {fieldName}: false,");
                }
                else
                {
                    sb.AppendLine($"      {fieldName}: undefined,");
                }

                // Add corresponding name field for foreign keys
                if (isForeignKey && fieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                {
                    string nameField = Utilities.RemoveIdSuffix(fieldName) + "Name";
                    sb.AppendLine($"      {nameField}: undefined,");
                }
            }

            sb.AppendLine("    } as any,");
            sb.AppendLine("    defaultValues,");
            sb.AppendLine("    setValues,");
            sb.AppendLine("    externalDisabledFields,");
            sb.AppendLine("    liveValues,");
            sb.AppendLine("    sanitizeValues: true,");
            sb.AppendLine("    submitConfig: {");
            sb.AppendLine($"      submitAction: (data) => update{resourceName}({lowerResource}Id, data),");
            sb.AppendLine($"      successMessage: \"{Utilities.GetFieldHeading(resourceName)} successfully updated.\",");
            sb.AppendLine("      onSuccess,");
            sb.AppendLine("      redirectPath,");
            sb.AppendLine("    },");
            sb.AppendLine("  })");
            sb.AppendLine();
            if (hasIdentityNumber)
            {
                sb.AppendLine("  const {");
                sb.AppendLine("    handleSubmit,");
                sb.AppendLine("    control,");
                sb.AppendLine("    formState: { errors, isValid },");
                sb.AppendLine("    reset,");
                sb.AppendLine("    trigger,");
                sb.AppendLine("  } = form");
                sb.AppendLine();
                sb.AppendLine("  useSaIdDefaults(form)");
                sb.AppendLine();
            }
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------- STATES ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const [isLoadingData, setIsLoadingData] = useState<boolean>(false)");
            sb.AppendLine();
            sb.AppendLine($"  const {{ fields, fieldGroups, isLoadingSettings }} = use{resourceName}Fields({{");
            sb.AppendLine("    control,");
            sb.AppendLine("    errors,");
            sb.AppendLine("    disabledFields,");
            if (hasStatusField)
            {
                sb.AppendLine("    hiddenFields: computedHiddenFields,");
            }
            else
            {
                sb.AppendLine("    hiddenFields,");
            }
            foreach (var fk in foreignKeyProps)
            {
                sb.AppendLine($"    {fk.propName},");
            }
            sb.AppendLine("    onFieldChange: onFieldChange,");
            sb.AppendLine("  })");
            sb.AppendLine();

            // Generate functions
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ---------------------- FUNCTIONS ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine($"  async function load{resourceName}() {{");
            sb.AppendLine("    setIsLoadingData(true)");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await get{resourceName}({lowerResource}Id)");
            sb.AppendLine("      const data = response.data");
            sb.AppendLine();
            sb.AppendLine("      reset({");

            // Reset with loaded data, skip main resource ID
            foreach (var prop in properties)
            {
                string fieldName = Utilities.ToCamelCase(prop.Key);
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Check if this is a foreign key field (check in foreignKeyProperties)
                bool isForeignKey = false;
                if (foreignKeyProperties != null)
                {
                    foreach (var fkProp in foreignKeyProperties)
                    {
                        if (string.Equals(Utilities.ToCamelCase(fkProp.Key), fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            var fkSchema = fkProp.Value?.AsObject();
                            string? fkDescription = fkSchema?["description"]?.GetValue<string>();
                            if (!string.IsNullOrEmpty(fkDescription) && fkDescription.StartsWith("ParentTable:"))
                            {
                                isForeignKey = true;
                                break;
                            }
                        }
                    }
                }

                var propSchema = prop.Value?.AsObject();
                if (string.Equals(propSchema?["type"]?.GetValue<string>(), "boolean", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"        {fieldName}: data.{fieldName} ?? false,");
                }
                else
                {
                    sb.AppendLine($"        {fieldName}: data.{fieldName} || undefined,");
                }

                // Add corresponding name field for foreign keys
                if (isForeignKey && fieldName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                {
                    string nameField = Utilities.RemoveIdSuffix(fieldName) + "Name";
                    sb.AppendLine($"        {nameField}: data.{nameField} || undefined,");
                }
            }

            sb.AppendLine("        ...sanitizedDefaultValues,");
            sb.AppendLine("        ...sanitizedSetValues,");
            sb.AppendLine("      } as any)");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      if (error instanceof AxiosError) {");
            sb.AppendLine("        console.error(error)");
            sb.AppendLine("      }");
            sb.AppendLine("    } finally {");
            sb.AppendLine("      setIsLoadingData(false)");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();

            // Generate effects
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------ EFFECTS ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  useEffect(() => {");
            sb.AppendLine($"    if ({lowerResource}Id) {{");
            sb.AppendLine($"      load{resourceName}()");
            sb.AppendLine("    }");
            sb.AppendLine($"  }}, [{lowerResource}Id])");
            sb.AppendLine();
            sb.AppendLine("  // Trigger validation after both data and settings are loaded");
            sb.AppendLine("  useEffect(() => {");
            sb.AppendLine("    if (!isLoadingData && !isLoadingSettings) {");
            sb.AppendLine("      trigger()");
            sb.AppendLine("    }");
            sb.AppendLine("  }, [isLoadingData, isLoadingSettings, trigger])");
            sb.AppendLine();

            // Generate render
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // --------------------- MAIN RENDER ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  return (");
            sb.AppendLine("    <FormTemplate");
            sb.AppendLine("      loading={isSubmitting || isLoadingData || isLoadingSettings}");
            sb.AppendLine($"      header=\"Update {Utilities.GetFieldHeading(resourceName)}\"");
            sb.AppendLine("      validationErrors={validationErrors}");
            sb.AppendLine("      fields={fields}");
            sb.AppendLine("      fieldGroups={fieldGroups}");
            sb.AppendLine("      onSubmit={onSubmit}");
            sb.AppendLine("      handleSubmit={handleSubmit}");
            sb.AppendLine("      submitButtonText=\"Save\"");
            sb.AppendLine("      isValid={isValid}");
            sb.AppendLine("      showGroupHeadings={showGroupHeadings}");
            sb.AppendLine("      headerContent={headerContent}");
            sb.AppendLine("      renderActionsInFooter={renderInDrawer}");
            sb.AppendLine("      onBeforeSubmit={onBeforeSubmit}");
            sb.AppendLine("    />");
            sb.AppendLine("  )");
            sb.AppendLine("}");

            // Write file
            string filePath = Path.Combine(componentDir, $"Update{resourceName}.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }

        static void GenerateViewComponent(string resourceName, string componentDir, JsonObject viewModel)
        {
            var sb = new StringBuilder();
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string kebabResource = Utilities.ToKebabCase(resourceName);
            var properties = viewModel["properties"]?.AsObject();

            if (properties == null) return;

            // Generate file header and imports
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { ViewTemplate } from \"@/components/legacy/ViewTemplate/ViewTemplate\"");
            sb.AppendLine($"import use{resourceName}Fields from \"@/field-hooks/legacy/use{resourceName}Fields\"");
            sb.AppendLine($"import {{ {resourceName} }} from \"@/types/legacy/core/{resourceName}\"");
            sb.AppendLine($"import {{ get{resourceName} }} from \"@/services/legacy/core/{resourceName}Service\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");
            sb.AppendLine("import { useEffect, useState } from \"react\"");
            sb.AppendLine("import { useForm } from \"react-hook-form\"");
            sb.AppendLine("import { AxiosError } from \"axios\"");
            sb.AppendLine();

            // Generate interface for props
            sb.AppendLine($"interface View{resourceName}Props {{");
            sb.AppendLine($"  {lowerResource}Id: number");
            sb.AppendLine("  tabsConfig?: {");
            sb.AppendLine("    objectId: number");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate component function
            sb.AppendLine($"export function View{resourceName}({{ {lowerResource}Id, tabsConfig }}: View{resourceName}Props) {{");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------ CONTEXTS ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const {");
            sb.AppendLine("    control,");
            sb.AppendLine("    formState: { errors },");
            sb.AppendLine("    reset,");
            sb.AppendLine($"  }} = useForm<{resourceName}>({{");
            sb.AppendLine("    mode: \"onTouched\",");
            sb.AppendLine("    defaultValues: {");

            // Generate default values, skip main resource ID
            string mainResourceIdField = $"{lowerResource}Id";
            foreach (var prop in properties)
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(prop.Key);
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                string defaultValue = Utilities.GetDefaultValueForType(propSchema);
                sb.AppendLine($"      {fieldName}: undefined,");
            }

            sb.AppendLine("    },");
            sb.AppendLine("  })");
            sb.AppendLine();

            // Generate states
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------- STATES ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const [isLoadingData, setIsLoadingData] = useState<boolean>(false)");
            sb.AppendLine();

            // Generate consts
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------ CONSTS -----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine($"  const {{ fields, fieldGroups, isLoadingSettings }} = use{resourceName}Fields({{");
            sb.AppendLine("    control,");
            sb.AppendLine("    errors,");
            sb.AppendLine("    disabledFields: [],");
            sb.AppendLine("  })");
            sb.AppendLine();

            // Generate functions
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ---------------------- FUNCTIONS ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine($"  async function load{resourceName}() {{");
            sb.AppendLine("    setIsLoadingData(true)");
            sb.AppendLine("    try {");
            sb.AppendLine($"      const response = await get{resourceName}({lowerResource}Id)");
            sb.AppendLine("      const data = response.data");
            sb.AppendLine();
            sb.AppendLine("      reset({");

            // Reset with loaded data, skip main resource ID
            foreach (var prop in properties)
            {
                string fieldName = Utilities.ToCamelCase(prop.Key);
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                var propSchema = prop.Value?.AsObject();
                string defaultValue = Utilities.GetDefaultValueForType(propSchema);
                sb.AppendLine($"        {fieldName}: data.{fieldName} || undefined,");
            }

            sb.AppendLine("      })");
            sb.AppendLine("    } catch (error) {");
            sb.AppendLine("      if (error instanceof AxiosError) {");
            sb.AppendLine("        console.error(error)");
            sb.AppendLine("      }");
            sb.AppendLine("    } finally {");
            sb.AppendLine("      setIsLoadingData(false)");
            sb.AppendLine("    }");
            sb.AppendLine("  }");
            sb.AppendLine();

            // Generate effects
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------ EFFECTS ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  useEffect(() => {");
            sb.AppendLine($"    if ({lowerResource}Id) {{");
            sb.AppendLine($"      load{resourceName}()");
            sb.AppendLine("    }");
            sb.AppendLine($"  }}, [{lowerResource}Id])");
            sb.AppendLine();

            // Generate render
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // --------------------- MAIN RENDER ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  return (");
            sb.AppendLine("    <ViewTemplate");
            sb.AppendLine("      loading={isLoadingData || isLoadingSettings}");
            sb.AppendLine($"      header=\"View {Utilities.GetFieldHeading(resourceName)}\"");
            sb.AppendLine($"      id={{{lowerResource}Id}}");
            sb.AppendLine("      objectId={tabsConfig?.objectId}");
            sb.AppendLine("      fields={fields}");
            sb.AppendLine("      fieldGroups={fieldGroups}");
            sb.AppendLine("    />");
            sb.AppendLine("  )");
            sb.AppendLine("}");

            // Write file
            string filePath = Path.Combine(componentDir, $"View{resourceName}.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }

        static void GenerateDeleteComponent(string resourceName, string componentDir, JsonObject updateModel)
        {
            var sb = new StringBuilder();
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string kebabResource = Utilities.ToKebabCase(resourceName);
            var properties = updateModel["properties"]?.AsObject();

            if (properties == null) return;
            var idPropName = $"{lowerResource}Id";

            // Generate file header and imports
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine($"import {{ View{resourceName} }} from \"@/components/legacy/Forms/View/View{resourceName}\"");
            sb.AppendLine("import { DeleteResourcePanel } from \"@/components/legacy/DeleteResourcePanel/DeleteResourcePanel\"");
            sb.AppendLine($"import {{ delete{resourceName} }} from \"@/services/legacy/core/{resourceName}Service\"");
            sb.AppendLine();
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// --------------------- MAIN COMPONENT ------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine($"interface Delete{resourceName}Props {{");
            sb.AppendLine($"    {lowerResource}Id: number");
            sb.AppendLine("    onSuccess?: () => void");
            sb.AppendLine("    renderInDrawer?: boolean");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"export function Delete{resourceName}({{ {lowerResource}Id, onSuccess, renderInDrawer = false }}: Delete{resourceName}Props) {{");
            sb.AppendLine("    return (");
            sb.AppendLine("        <DeleteResourcePanel");
            sb.AppendLine($"            entityName=\"{Utilities.GetFieldHeading(resourceName)}\"");
            sb.AppendLine("            renderInDrawer={renderInDrawer}");
            sb.AppendLine($"            deleteAction={{() => delete{resourceName}({lowerResource}Id)}}");
            sb.AppendLine("            onSuccess={onSuccess}");
            sb.AppendLine("        >");
            sb.AppendLine($"            <View{resourceName} {idPropName}={{{idPropName}}} />");
            sb.AppendLine("        </DeleteResourcePanel>");
            sb.AppendLine("    )");
            sb.AppendLine("}");

            // Write file
            string filePath = Path.Combine(componentDir, $"Delete{resourceName}.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }
    }
}
