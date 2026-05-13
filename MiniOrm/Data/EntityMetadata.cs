using System.Reflection;
using MiniOrm.Attributes;

namespace MiniOrm.Data;

// Stores one mapped property's metadata.
public class ColumnMetadata
{
    public required PropertyInfo Property { get; set; }
    public required string ColumnName { get; set; }
    public required bool IsPrimaryKey { get; set; }
    public required bool IsNullable { get; set; }
    public Type EffectiveType => Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
}

// Stores one entity's full metadata (table + columns).
public class EntityMetadata
{
    public required Type EntityType { get; set; }
    public required string TableName { get; set; }
    public required ColumnMetadata PrimaryKey { get; set; }
    public required List<ColumnMetadata> Columns { get; set; }

    public List<ColumnMetadata> NonPrimaryColumns => Columns.Where(c => !c.IsPrimaryKey).ToList();

    public static EntityMetadata For<T>() where T : class, new()
    {
        return For(typeof(T));
    }

    public static EntityMetadata For(Type entityType)
    {
        return Build(entityType);
    }

    private static EntityMetadata Build(Type entityType)
    {
        // Must have [Table("...")]
        var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
        if (tableAttr is null)
        {
            throw new InvalidOperationException($"{entityType.Name} must have [Table] attribute.");
        }

        // Read properties and keep only [Column] or [PrimaryKey]
        var mappedColumns = new List<ColumnMetadata>();
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
            var pkAttr = property.GetCustomAttribute<PrimaryKeyAttribute>();
            var isPrimaryKey = pkAttr is not null;

            // Ignore properties without [Column] and without [PrimaryKey].
            if (columnAttr is null && !isPrimaryKey)
            {
                continue;
            }

            // [PrimaryKey] can exist without [Column] according to assignment ss.
            var columnName = columnAttr?.Name ?? property.Name.ToLowerInvariant();

            mappedColumns.Add(new ColumnMetadata
            {
                Property = property,
                ColumnName = columnName,
                IsPrimaryKey = isPrimaryKey,
                IsNullable = IsNullableProperty(property)
            });
        }

        if (mappedColumns.Count == 0)
        {
            throw new InvalidOperationException($"{entityType.Name} has no mapped columns.");
        }

        // Must have exactly one primary key
        var primaryKeys = mappedColumns.Where(c => c.IsPrimaryKey).ToList();
        if (primaryKeys.Count != 1)
        {
            throw new InvalidOperationException($"{entityType.Name} must have exactly one [PrimaryKey].");
        }

        return new EntityMetadata
        {
            EntityType = entityType,
            TableName = tableAttr.Name,
            PrimaryKey = primaryKeys[0],
            Columns = mappedColumns
        };
    }

    private static bool IsNullableProperty(PropertyInfo property)
    {
        var type = property.PropertyType;

        // Nullable value types: int?, decimal?, DateTime? etc.
        if (Nullable.GetUnderlyingType(type) is not null)
        {
            return true;
        }

        // Non-nullable value types: int, bool, decimal ...
        if (type.IsValueType)
        {
            return false;
        }

        // Reference types: string vs string?
        var nullability = new NullabilityInfoContext().Create(property);
        return nullability.WriteState == NullabilityState.Nullable;
    }
}
