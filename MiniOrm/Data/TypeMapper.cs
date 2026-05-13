namespace MiniOrm.Data;

public static class TypeMapper
{
    // C# int (PrimaryKey) => Postgres SERIAL + PRIMARY KEY
    public const string PrimaryKeyIntPostgresType = "SERIAL";
    public const string PrimaryKeyConstraint = "PRIMARY KEY";

    // Non-nullable C# types => PostgreSQL types
    private static readonly Dictionary<Type, string> PgTypeMap = new()
    {
        [typeof(int)] = "INTEGER",
        [typeof(long)] = "BIGINT",
        [typeof(float)] = "REAL",
        [typeof(double)] = "DOUBLE PRECISION",
        [typeof(decimal)] = "NUMERIC",
        [typeof(bool)] = "BOOLEAN",
        [typeof(string)] = "TEXT",
        [typeof(DateTime)] = "TIMESTAMP",
        [typeof(Guid)] = "UUID"
    };

    // Uses Nullable.GetUnderlyingType(type) for nullable value types (T?).
    public static string ToPostgresType(Type clrType)
    {
        var actualType = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (PgTypeMap.TryGetValue(actualType, out var pgType))
        {
            return pgType;
        }

        throw new NotSupportedException($"No PostgreSQL mapping exists for CLR type '{clrType.FullName}'.");
    }

    public static string ToColumnDefinition(ColumnMetadata column)
    {
        if (column.IsPrimaryKey && column.EffectiveType == typeof(int))
        {
            return $"{column.ColumnName} {PrimaryKeyIntPostgresType} {PrimaryKeyConstraint}";
        }

        var pgType = ToPostgresType(column.Property.PropertyType);
        var isNull = column.IsNullable ? "NULL" : "NOT NULL";
        return $"{column.ColumnName} {pgType} {isNull}";
    }
}
