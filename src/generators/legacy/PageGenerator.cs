using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ReactCodegen.Legacy
{
    public static class PageGenerator
    {
        private static JsonObject? _fieldTemplates;

        public static void GeneratePagesForResource(string resourceName, JsonObject? schemas, string outputDir, List<string> coreResources, List<string> settingsResources)
        {
            if (schemas == null) return;

            // Load field templates if not already loaded
            if (_fieldTemplates == null)
            {
                _fieldTemplates = LoadFieldTemplates();
            }

            var searchModel = Utilities.FindSchema(schemas, $"{resourceName}RetrieveResponseModel");
            var createModel = Utilities.FindSchema(schemas, $"{resourceName}CreateRequestModel");
            var updateModel = Utilities.FindSchema(schemas, $"{resourceName}UpdateRequestModel");

            // Check if it's a non-CRUD resource
            if (createModel == null && updateModel == null) return;

            string resourceDir = Path.Combine(outputDir, resourceName);
            if (!Directory.Exists(resourceDir))
            {
                Console.WriteLine($"CRUD page directory '{resourceDir}' does not exist. Creating it...");
                Directory.CreateDirectory(resourceDir);
            }

            // Delete old create/update/view page folders if they exist (now using drawers)
            string createDir = Path.Combine(resourceDir, "create");
            string updateDir = Path.Combine(resourceDir, "update");
            string viewDir = Path.Combine(resourceDir, "view");

            if (Directory.Exists(createDir))
            {
                Directory.Delete(createDir, true);
                Console.WriteLine($"Deleted old create page folder: {createDir}");
            }

            if (Directory.Exists(updateDir))
            {
                Directory.Delete(updateDir, true);
                Console.WriteLine($"Deleted old update page folder: {updateDir}");
            }

            if (Directory.Exists(viewDir))
            {
                Directory.Delete(viewDir, true);
                Console.WriteLine($"Deleted old view page folder: {viewDir}");
            }

            // Generate List page
            if (searchModel != null)
            {
                GenerateListPage(resourceName, resourceDir, searchModel, coreResources, settingsResources);
            }
        }

        static void GenerateListPage(string resourceName, string resourceDir, JsonObject searchModel, List<string> coreResources, List<string> settingsResources)
        {
            var sb = new StringBuilder();
            string lowerResource = Utilities.ToCamelCase(resourceName);
            string kebabResource = Utilities.ToKebabCase(resourceName);
            string pluralResource = Utilities.GetPluralName(resourceName);
            var properties = searchModel["properties"]?.AsObject();

            if (properties == null) return;

            // Check if the resource has status fields
            bool hasStatusField = properties.Any(kvp => string.Equals(Utilities.ToCamelCase(kvp.Key), "statusId", StringComparison.OrdinalIgnoreCase));

            // Collect column information and foreign keys
            var priorityColumns = new List<string> { "name", "code", "purpose", "status", "description", "email" };
            var columns = new List<KeyValuePair<string, JsonNode?>>();
            var otherProps = new List<KeyValuePair<string, JsonNode?>>();
            var foreignKeyFields = new List<(string fieldName, string relatedResource)>();  // Track foreign keys for filtering
            var uniqueRelatedResources = new HashSet<string>();  // Track unique parent tables to avoid duplicates
            string mainResourceIdField = $"{lowerResource}Id";  // e.g., systemUserId for SystemUser
            var formattingRules = new List<string>();  // For formatData function
            bool needsDayjs = false;

            foreach (var prop in properties)
            {
                var propSchema = prop.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(prop.Key);
                string? description = propSchema["description"]?.GetValue<string>();

                // Check if this is a foreign key by looking for "ParentTable:" in description
                if (!string.IsNullOrEmpty(description) && description.StartsWith("ParentTable:"))
                {
                    // Skip audit foreign keys (createdBy, modifiedBy, deletedBy)
                    if (fieldName.ToLower().Contains("createdby") ||
                        fieldName.ToLower().Contains("modifiedby") ||
                        fieldName.ToLower().Contains("deletedby"))
                        continue;

                    // Skip the main resource ID (e.g., systemUserId for SystemUser resource)
                    if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Extract the parent table name (e.g., "ParentTable:AllowanceType" -> "AllowanceType")
                    string relatedResource = description.Substring("ParentTable:".Length).Trim();

                    foreignKeyFields.Add((fieldName, relatedResource));
                    uniqueRelatedResources.Add(relatedResource);  // Track unique parent tables
                    continue; // Skip foreign key IDs from columns
                }

                if (prop.Key.ToLower() == "id") continue; // Skip main ID

                // Skip the main resource ID field (e.g., interventionId, systemUserId, etc.)
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.Key.ToLower().Contains("created")) continue; // skip audit columns
                if (prop.Key.ToLower().Contains("modified")) continue; // skip audit columns
                if (prop.Key.ToLower().Contains("deleted")) continue; // skip audit columns

                if (prop.Key.ToLower().Contains("first")) continue; // skip first name columns
                if (prop.Key.ToLower().Contains("last")) continue;  // skip last name columns
                if (prop.Key.ToLower().Contains("middle")) continue;  // skip middle name columns

                string? type = propSchema["type"]?.GetValue<string>();
                string? format = propSchema["format"]?.GetValue<string>();

                // Check if we need dayjs
                if ((type == "string" && format == "date-time") || (type == "string" && format == "date"))
                {
                    needsDayjs = true;
                }

                // Determine formatting for this field
                string? formatting = null;
                if (type == "boolean")
                {
                    formatting = $"                {fieldName}: {lowerResource}.{fieldName} ? 'Yes' : 'No',";
                }
                else if (type == "string" && format == "date")
                {
                    formatting = $"                {fieldName}: {lowerResource}.{fieldName} ? dayjs({lowerResource}.{fieldName}).format('YYYY/MM/DD') : '',";
                }
                else if (type == "string" && format == "date-time")
                {
                    formatting = $"                {fieldName}: {lowerResource}.{fieldName} ? dayjs({lowerResource}.{fieldName}).format('YYYY/MM/DD HH:mm') : '',";
                }
                else if (!string.IsNullOrEmpty(description) && description.StartsWith("FieldType:Currency"))
                {
                    formatting = $"                {fieldName}: {lowerResource}.{fieldName} ? `R ${{Number({lowerResource}.{fieldName}).toLocaleString('en-ZA', {{ minimumFractionDigits: 2, maximumFractionDigits: 2 }})}}` : 'R 0.00',";
                }

                if (!string.IsNullOrEmpty(formatting))
                {
                    formattingRules.Add(formatting);
                }

                // Check if this property should be prioritized for column ordering
                bool isPriority = priorityColumns.Any(priority =>
                    prop.Key.ToLower().Contains(priority.ToLower()));

                if (isPriority)
                    columns.Add(prop);
                else
                    otherProps.Add(prop);
            }

            // Sort priority props by their order in the priorityColumns list
            columns = columns.OrderBy(prop =>
            {
                for (int i = 0; i < priorityColumns.Count; i++)
                {
                    if (prop.Key.ToLower().Contains(priorityColumns[i].ToLower()))
                        return i;
                }
                return int.MaxValue;
            }).ToList();

            // Generate file content
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine("// !!---------- AUTO-GENERATED: Do not edit manually! --------!!");
            sb.AppendLine("// !!---------------------------------------------------------!!");
            sb.AppendLine();
            sb.AppendLine("\"use client\"");
            sb.AppendLine();
            sb.AppendLine("// -------------------- ICONS ------------------------");
            sb.AppendLine("import { TbEye, TbPencil, TbPlus, TbTrash } from \"react-icons/tb\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- INTERNAL IMPORTS -------------------");
            sb.AppendLine("import { ActionConfig, TableColumn, FilterColumn } from \"@/components/legacy/DataTable/DataTable\"");
            sb.AppendLine($"import {{ delete{resourceName}, search{resourceName} }} from \"@/services/legacy/core/{resourceName}Service\"");
            sb.AppendLine("import { getIcon } from \"@/utils/legacy/icons\"");
            sb.AppendLine("import { TableObject } from \"@sseta/components\"");

            // Add service imports for unique foreign key relationships only if there are foreign keys
            if (uniqueRelatedResources.Count > 0)
            {
                foreach (var relatedResource in uniqueRelatedResources.OrderBy(r => r))
                {
                    // Determine subfolder for related resource
                    string subfolder = coreResources.Contains(relatedResource) ? "core" :
                                     settingsResources.Contains(relatedResource) ? "settings" : "";
                    string serviceImportPath = $"@/services/legacy/{subfolder}/{relatedResource}Service";
                    sb.AppendLine($"import {{ search{relatedResource} }} from \"{serviceImportPath}\"");
                }
            }

            sb.AppendLine($"import {{ {resourceName} }} from \"@/types/legacy/core/{resourceName}\"");
            sb.AppendLine("import { useDataTable } from \"@/hooks/legacy/useDataTable\"");
            sb.AppendLine("import { DataTable } from \"@/components/legacy/DataTable/DataTable\"");
            sb.AppendLine("import { useDrawer } from \"@/hooks/legacy/useDrawer\"");
            sb.AppendLine("import { Drawer } from \"@/components/legacy/Drawer/Drawer\"");
            sb.AppendLine($"import {{ Create{resourceName} }} from \"@/components/legacy/Forms/Create/Create{resourceName}\"");
            sb.AppendLine($"import {{ Update{resourceName} }} from \"@/components/legacy/Forms/Update/Update{resourceName}\"");
            sb.AppendLine($"import {{ View{resourceName} }} from \"@/components/legacy/Forms/View/View{resourceName}\"");
            sb.AppendLine($"import {{ Delete{resourceName} }} from \"@/components/legacy/Forms/Delete/Delete{resourceName}\"");
            sb.AppendLine();
            sb.AppendLine("// -------------- EXTERNAL IMPORTS -------------------");

            // Add dayjs import if needed
            if (needsDayjs)
            {
                sb.AppendLine("import dayjs from \"dayjs\"");
            }

            sb.AppendLine();
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine("// ----------------------- COMPONENT ---------------------------");
            sb.AppendLine("// -------------------------------------------------------------");
            sb.AppendLine($"export function {resourceName}ListPage() {{");

            // Add the rest of the component logic
            // Component logic
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------ CONTEXTS ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const { drawer, openDrawer, closeDrawer } = useDrawer()");
            sb.AppendLine();
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ------------------------- CONSTS ----------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const columns: TableColumn[] = [");

            int searchableColumns = 0;

            // Process priority props first, then others
            foreach (var column in columns.Concat(otherProps))
            {

                var propSchema = column.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(column.Key);
                string label = Utilities.GetFieldHeading(column.Key);
                bool sortable = true;
                bool searchable = propSchema["type"]?.GetValue<string>() == "string";
                if (searchable) searchableColumns++;

                if (searchable && searchableColumns > 5)
                {
                    searchable = false;
                }

                sb.AppendLine(
                    $"    {{ key: \"{fieldName}\", dbKey: \"{fieldName}\", label: \"{label}\", sortable: {sortable.ToString().ToLower()}{(searchable ? ", searchable: true" : "")} }},"
                );
            }

            sb.AppendLine("  ]");
            sb.AppendLine();

            // Generate useDataTable hook call
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ---------------------- USE DATA TABLE -----------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine($"  const table = useDataTable<{resourceName}>({{");
            sb.AppendLine($"    fetchService: search{resourceName},");
            sb.AppendLine($"    deleteService: delete{resourceName},");
            sb.AppendLine($"    idField: \"{lowerResource}Id\",");
            sb.AppendLine("    columns: columns,");
            sb.AppendLine($"    entityName: \"{resourceName}\",");

            // Add settingsConfig only if there are foreign keys
            if (uniqueRelatedResources.Count > 0)
            {
                sb.AppendLine("    settingsConfig: [");
                foreach (var fk in foreignKeyFields)
                {
                    sb.AppendLine("      {");
                    sb.AppendLine($"        service: search{fk.relatedResource},");
                    sb.AppendLine($"        idField: \"{fk.fieldName}\",");
                    sb.AppendLine("      },");
                }
                sb.AppendLine("    ],");
            }

            // Add formatData function if there are formatting rules
            if (formattingRules.Count > 0)
            {
                sb.AppendLine($"    formatData: ({lowerResource}: {resourceName}) => {{");
                sb.AppendLine("      return {");
                sb.AppendLine($"        ...{lowerResource},");
                foreach (var rule in formattingRules)
                {
                    // Fix indentation in formatting rules
                    string fixedRule = rule.Replace("                ", "        ");
                    sb.AppendLine(fixedRule);
                }
                sb.AppendLine("      }");
                sb.AppendLine("    },");
            }

            sb.AppendLine("  })");
            sb.AppendLine();

            // Generate filterColumns configuration
            sb.AppendLine("  const filterColumns: FilterColumn[] = [");

            // Add text filters for searchable string columns
            foreach (var column in columns.Concat(otherProps))
            {
                var propSchema = column.Value?.AsObject();
                if (propSchema == null) continue;

                string fieldName = Utilities.ToCamelCase(column.Key);

                // Skip main resource ID and deletedBy fields
                if (string.Equals(fieldName, mainResourceIdField, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fieldName.ToLower() == "deletedby")
                    continue;

                // Skip foreign key name/code fields (e.g., systemUserTypeName, mfaTypeName)
                // These are already handled by select filters using the ID fields
                bool isForeignKeyNameField = foreignKeyFields.Any(fk =>
                {
                    // Convert relatedResource (e.g., "SystemUserType") to camelCase (e.g., "systemUserType")
                    string relatedResourceCamel = Utilities.ToCamelCase(fk.relatedResource);
                    // Check if current field is the name or code field for this foreign key
                    return fieldName.Equals($"{relatedResourceCamel}Name", StringComparison.OrdinalIgnoreCase) ||
                           fieldName.Equals($"{relatedResourceCamel}Code", StringComparison.OrdinalIgnoreCase);
                });
                if (isForeignKeyNameField)
                    continue;

                string label = Utilities.GetFieldHeading(column.Key);
                string? type = propSchema["type"]?.GetValue<string>();
                string? format = propSchema["format"]?.GetValue<string>();

                // Determine filter type
                string filterType = "text";
                if (type == "string" && format == "date")
                    filterType = "date";
                else if (type == "string" && format == "date-time")
                    filterType = "date";
                else if (type == "number" || type == "integer")
                    filterType = "number";

                sb.AppendLine($"    {{ key: \"{fieldName}\", dbKey: \"{fieldName}\", label: \"{label}\", filterType: \"{filterType}\" }},");
            }

            // Add select filters for foreign keys
            foreach (var fk in foreignKeyFields)
            {
                string label = Utilities.GetFieldHeading(fk.relatedResource);
                sb.AppendLine($"    {{");
                sb.AppendLine($"      key: \"{fk.fieldName}\",");
                sb.AppendLine($"      dbKey: \"{fk.fieldName}\",");
                sb.AppendLine($"      label: \"{label}\",");
                sb.AppendLine($"      filterType: \"select\",");
                sb.AppendLine($"      selectItems: table.settings[\"{fk.fieldName}\"] || [],");
                sb.AppendLine($"    }},");
            }

            // Add date filters for audit columns
            sb.AppendLine("    { key: \"createdOn\", dbKey: \"createdOn\", label: \"Created Date\", filterType: \"date\" },");
            sb.AppendLine("    { key: \"modifiedOn\", dbKey: \"modifiedOn\", label: \"Modified Date\", filterType: \"date\" },");

            sb.AppendLine("  ]");
            sb.AppendLine();

            // Generate table actions and row actions
            sb.AppendLine("  const tableActions: ActionConfig[] = [");
            sb.AppendLine("    {");
            sb.AppendLine("      label: \"Create New\",");
            sb.AppendLine("      icon: TbPlus,");
            sb.AppendLine($"      requiredPermission: \"{resourceName}.Create\",");
            sb.AppendLine("      onClick: () => {");
            sb.AppendLine("        openDrawer(\"create\")");
            sb.AppendLine("      },");
            sb.AppendLine("    },");
            sb.AppendLine("  ]");
            sb.AppendLine();

            sb.AppendLine("  const rowActions: ActionConfig[] = [");
            sb.AppendLine("    {");
            sb.AppendLine("      label: \"View\",");
            sb.AppendLine("      icon: TbEye,");
            sb.AppendLine($"      requiredPermission: \"{resourceName}.Retrieve\",");
            sb.AppendLine("      onClick: (row) => {");
            sb.AppendLine("        if (row?.id) {");
            sb.AppendLine("          openDrawer(\"view\", Number(row.id))");
            sb.AppendLine("        }");
            sb.AppendLine("      },");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      label: \"Edit\",");
            sb.AppendLine("      icon: TbPencil,");
            sb.AppendLine($"      requiredPermission: \"{resourceName}.Update\",");
            sb.AppendLine("      onClick: (row) => {");
            sb.AppendLine("        if (row?.id) {");
            sb.AppendLine("          openDrawer(\"update\", Number(row.id))");
            sb.AppendLine("        }");
            sb.AppendLine("      },");
            sb.AppendLine("    },");
            sb.AppendLine("    {");
            sb.AppendLine("      label: \"Delete\",");
            sb.AppendLine("      icon: TbTrash,");
            sb.AppendLine($"      requiredPermission: \"{resourceName}.Delete\",");
            sb.AppendLine("      onClick: (row) => {");
            sb.AppendLine("        if (row?.id) {");
            sb.AppendLine("          openDrawer(\"delete\", Number(row.id))");
            sb.AppendLine("        }");
            sb.AppendLine("      },");
            sb.AppendLine("    },");
            sb.AppendLine("  ]");
            sb.AppendLine();

            // Add handleDrawerSuccess function
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // ----------------------- FUNCTIONS ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  const handleDrawerSuccess = () => {");
            sb.AppendLine("    closeDrawer()");
            sb.AppendLine("    table.refetch()");
            sb.AppendLine("  }");
            sb.AppendLine();

            // Generate render section
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  // --------------------- MAIN RENDER ---------------------------");
            sb.AppendLine("  // -------------------------------------------------------------");
            sb.AppendLine("  return (");
            sb.AppendLine("    <>");
            sb.AppendLine("      <DataTable");
            sb.AppendLine("        filterColumns={filterColumns}");
            sb.AppendLine("        onFilterApply={(filters) => {");
            sb.AppendLine("          table.setSearchFilter(\"\")");
            sb.AppendLine("          table.setFilterByList(filters)");
            sb.AppendLine("          table.setPageNumber(1)");
            sb.AppendLine("        }}");
            sb.AppendLine("        columns={columns}");
            sb.AppendLine("        data={table.formattedData}");
            sb.AppendLine("        rowActions={rowActions}");
            sb.AppendLine("        tableActions={tableActions}");
            sb.AppendLine("        loading={table.loading}");
            sb.AppendLine("        orderByList={table.orderByList}");
            sb.AppendLine("        searchFilter={table.searchFilter}");
            sb.AppendLine("        header={{");
            sb.AppendLine($"          title: \"{Utilities.GetFieldHeading(Utilities.GetPluralName(resourceName))}\",");
            sb.AppendLine($"          subtitle: \"Manage {Utilities.GetFieldHeading(lowerResource).ToLower()} records and details\",");
            sb.AppendLine($"          icon: getIcon(\"{lowerResource}\", \"main\"),");
            sb.AppendLine("          size: \"lg\",");
            sb.AppendLine("        }}");
            sb.AppendLine("        onSearch={table.handleSearch}");
            sb.AppendLine("        onSearchChange={table.setSearchFilter}");
            sb.AppendLine("        onOrderByChange={table.setOrderByList}");
            sb.AppendLine("        pagination={{");
            sb.AppendLine("          currentPage: table.pageNumber,");
            sb.AppendLine("          totalPages: table.totalPages,");
            sb.AppendLine("          pageSize: table.pageSize,");
            sb.AppendLine("          totalItems: table.totalItems,");
            sb.AppendLine("          onPageChange: table.setPageNumber,");
            sb.AppendLine("          onPageSizeChange: (size) => {");
            sb.AppendLine("            table.setPageSize(size)");
            sb.AppendLine("            table.setPageNumber(1)");
            sb.AppendLine("          },");
            sb.AppendLine("        }}");
            sb.AppendLine("      />");
            sb.AppendLine();

            // Add drawer components
            sb.AppendLine("      {/* Create Drawer */}");
            sb.AppendLine($"      <Drawer isOpen={{drawer[\"create\"]}} onClose={{closeDrawer}} size=\"lg\" title=\"Create {Utilities.GetFieldHeading(resourceName)}\">");
            if (hasStatusField)
            {
                sb.AppendLine($"        <Create{resourceName} onSuccess={{handleDrawerSuccess}} renderInDrawer statusEnabled />");
            }
            else
            {
                sb.AppendLine($"        <Create{resourceName} onSuccess={{handleDrawerSuccess}} renderInDrawer />");
            }
            sb.AppendLine("      </Drawer>");
            sb.AppendLine();

            sb.AppendLine("      {/* View Drawer */}");
            sb.AppendLine($"      <Drawer isOpen={{drawer[\"view\"]}} onClose={{closeDrawer}} size=\"lg\" title=\"View {Utilities.GetFieldHeading(resourceName)}\">");
            var tableObjectKey = Utilities.NormalizeEnumKey(resourceName);
            sb.AppendLine($"        <View{resourceName} {lowerResource}Id={{drawer[\"view\"]}} tabsConfig={{{{ objectId: TableObject.{tableObjectKey} }}}} />");
            sb.AppendLine("      </Drawer>");
            sb.AppendLine();

            sb.AppendLine("      {/* Update Drawer */}");
            sb.AppendLine($"      <Drawer isOpen={{drawer[\"update\"]}} onClose={{closeDrawer}} size=\"lg\" title=\"Update {Utilities.GetFieldHeading(resourceName)}\">");
            if (hasStatusField)
            {
                sb.AppendLine($"        <Update{resourceName} {lowerResource}Id={{drawer[\"update\"]}} onSuccess={{handleDrawerSuccess}} renderInDrawer statusEnabled />");
            }
            else
            {
                sb.AppendLine($"        <Update{resourceName} {lowerResource}Id={{drawer[\"update\"]}} onSuccess={{handleDrawerSuccess}} renderInDrawer />");
            }
            sb.AppendLine("      </Drawer>");
            sb.AppendLine();

            sb.AppendLine("      {/* Delete Drawer */}");
            sb.AppendLine($"      <Drawer isOpen={{drawer[\"delete\"]}} onClose={{closeDrawer}} size=\"lg\" title=\"Delete {Utilities.GetFieldHeading(resourceName)}\">");
            sb.AppendLine($"        <Delete{resourceName} {lowerResource}Id={{drawer[\"delete\"]}} onSuccess={{handleDrawerSuccess}} renderInDrawer />");
            sb.AppendLine("      </Drawer>");
            sb.AppendLine("    </>");
            sb.AppendLine("  )");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine($"export default {resourceName}ListPage");

            // Write file with resource name instead of "page.tsx"
            string filePath = Path.Combine(resourceDir, $"{resourceName}ListPage.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }

        public static void GenerateDatabaseAdminPage(string resourceName, string dbAdminAppPagesDir)
        {
            string kebabResource = Utilities.ToKebabCase(resourceName);
            string pageDir = Path.Combine(dbAdminAppPagesDir, kebabResource);

            if (!Directory.Exists(pageDir))
                Directory.CreateDirectory(pageDir);

            var sb = new StringBuilder();
            sb.AppendLine($"import {resourceName}ListPage from \"@/components/legacy/Pages/List/{resourceName}/{resourceName}ListPage\"");
            sb.AppendLine();
            sb.AppendLine($"export default async function {resourceName}Page() {{");
            sb.AppendLine($"  return <{resourceName}ListPage />");
            sb.AppendLine("}");

            string filePath = Path.Combine(pageDir, "page.tsx");
            File.WriteAllText(filePath, sb.ToString());
        }

        private static JsonObject? LoadFieldTemplates()
        {
            try
            {
                string templatesPath = Path.Combine("input", "fieldTemplates.json");
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
    }
}
