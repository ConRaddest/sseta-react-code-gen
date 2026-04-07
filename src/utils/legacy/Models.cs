namespace ReactCodegen.Legacy
{
    public class ApiEndpoint
    {
        public string Path { get; set; } = "";
        public string Method { get; set; } = "";
        public string PathSuffix { get; set; } = "";
        public string ResourceName { get; set; } = "";
        public string Prefix { get; set; } = "";
    }

    public class ForeignKeyField
    {
        public string PropertyName { get; set; } = "";
        public string ParentTable { get; set; } = "";
        public string CamelCaseName { get; set; } = "";
        public string PluralName { get; set; } = "";
        public string ForeignKey { get; set; } = "";
    }

    public class ReportingEndpoint
    {
        public string Path { get; set; } = "";
        public string Method { get; set; } = "";
        public string Department { get; set; } = "";
        public string Category { get; set; } = "";
        public string ReportKey { get; set; } = "";
        public string? RequestSchemaName { get; set; }
        public string? ResponseSchemaName { get; set; }
        public string? ResponseDataSchemaName { get; set; }
        public bool ResponseDataIsArray { get; set; }
        public string? RequestTypeName { get; set; }
        public string? ResponseDataTypeName { get; set; }
    }

    public class SetmisEndpoint
    {
        public string Path { get; set; } = "";
        public string Method { get; set; } = "";
        public string EntityName { get; set; } = "";
        public string? RequestSchemaName { get; set; }
        public string? ResponseSchemaName { get; set; }
        public string? RecordSchemaName { get; set; }
        public string? RecordTypeName { get; set; }
        public bool HasDownloadRoute { get; set; } = false;
    }
}
