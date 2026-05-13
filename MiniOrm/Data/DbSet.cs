using System.Globalization;
using System.Reflection;
using Npgsql;

namespace MiniOrm.Data;

// Generic DbSet<T>:
// If T = Product, this class works with products table.
// If T = Order, this class works with orders table.
public class DbSet<T> where T : class, new()
{
    private readonly NpgsqlConnection _connection;
    private readonly EntityMetadata _meta;

    public DbSet(NpgsqlConnection connection)
    {
        _connection = connection;
        _meta = EntityMetadata.For<T>(); // Table/column info from reflection
    }

    // INSERT row and return new id.
    public int Insert(T entity)
    {
        var nonPkColumns = _meta.NonPrimaryColumns;

        // name, price, discount, in_stock
        var columnPart = string.Join(", ", nonPkColumns.Select(c => c.ColumnName));
        // @p0, @p1, @p2, @p3
        var valuePart = string.Join(", ", nonPkColumns.Select((_, i) => $"@p{i}"));

        var sql =
            $"INSERT INTO {_meta.TableName} ({columnPart}) VALUES ({valuePart}) RETURNING {_meta.PrimaryKey.ColumnName};";

        using var cmd = new NpgsqlCommand(sql, _connection);
        AddSqlParameters(cmd, nonPkColumns, entity);

        var result = cmd.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException("Insert failed: database did not return an id.");
        }

        var newId = Convert.ToInt32(result, CultureInfo.InvariantCulture);
        _meta.PrimaryKey.Property.SetValue(entity, newId); // put returned id back into object
        return newId;
    }

    // Find one row by primary key.
    public T? FindById(int id)
    {
        var selectedColumns = string.Join(", ", _meta.Columns.Select(c => c.ColumnName));
        var sql =
            $"SELECT {selectedColumns} FROM {_meta.TableName} WHERE {_meta.PrimaryKey.ColumnName} = @id;";

        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@id", id);

        using var resultRow = cmd.ExecuteReader();
        if (!resultRow.Read())
        {
            return null;
        }

        return RowToObject(resultRow); // converted row to Product/Order object
    }

    // Get all rows.
    public IEnumerable<T> GetAll()
    {
        var selectedColumns = string.Join(", ", _meta.Columns.Select(c => c.ColumnName));
        var sql = $"SELECT {selectedColumns} FROM {_meta.TableName};";

        using var cmd = new NpgsqlCommand(sql, _connection);
        using var resultRow = cmd.ExecuteReader();

        var rows = new List<T>();
        while (resultRow.Read())
        {
            rows.Add(RowToObject(resultRow));
        }

        return rows;
    }

    // Update one row by primary key
    public int Update(T entity)
    {
        var nonPkColumns = _meta.NonPrimaryColumns;

        // name=@p0, price=@p1, discount=@p2, ..
        var setPart = string.Join(", ", nonPkColumns.Select((c, i) => $"{c.ColumnName} = @p{i}"));
        var sql = $"UPDATE {_meta.TableName} SET {setPart} WHERE {_meta.PrimaryKey.ColumnName} = @id;";

        using var cmd = new NpgsqlCommand(sql, _connection);
        AddSqlParameters(cmd, nonPkColumns, entity);

        var pkValue = _meta.PrimaryKey.Property.GetValue(entity);
        if (pkValue is null)
        {
            throw new InvalidOperationException("Update failed: primary key value is null.");
        }

        cmd.Parameters.AddWithValue("@id", pkValue);
        return cmd.ExecuteNonQuery();
    }

    // Delete one row by primary key
    public int Delete(int id)
    {
        var sql = $"DELETE FROM {_meta.TableName} WHERE {_meta.PrimaryKey.ColumnName} = @id;";
        using var cmd = new NpgsqlCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery();
    }

    // object values -> SQL parameters.
    private static void AddSqlParameters(
        NpgsqlCommand cmd,
        IReadOnlyList<ColumnMetadata> columns,
        T entity)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            // If value is null, DB needs DBNull.Value.
            var value = columns[i].Property.GetValue(entity) ?? DBNull.Value;
            cmd.Parameters.AddWithValue($"@p{i}", value);
        }
    }

    // one DB row -> one object (T).
    private T RowToObject(NpgsqlDataReader reader)
    {
        var entity = new T();

        for (var i = 0; i < _meta.Columns.Count; i++)
        {
            var column = _meta.Columns[i];
            var property = column.Property;

            if (reader.IsDBNull(i))
            {
                property.SetValue(entity, null);
                continue;
            }

            var dbValue = reader.GetValue(i);
            SetValueInObject(entity, property, dbValue);
        }

        return entity;
    }

    // convert DB value type and set property.
    private static void SetValueInObject(T entity, PropertyInfo property, object value)
    {
        var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        var converted = targetType.IsEnum
            ? Enum.ToObject(targetType, value)
            : Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);

        property.SetValue(entity, converted);
    }
}
