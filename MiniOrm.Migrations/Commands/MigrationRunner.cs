using MiniOrm.Attributes;
using MiniOrm.Data;
using Npgsql;

namespace MiniOrm.Migrations.Commands;

public sealed class MigrationRunner
{
    private const string MigrationTable = "__migrations";
    private readonly string _connectionString;
    private readonly string _migrationDirectory;

    public MigrationRunner()
    {
        _connectionString = Environment.GetEnvironmentVariable("MINIORM_CONN")
            ?? throw new InvalidOperationException("Environment variable MINIORM_CONN is not set.");

        _migrationDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Migrations");
        Directory.CreateDirectory(_migrationDirectory);
    }

    public int Add(string migrationName)
    {
        var entities = GetEntityMetadatas();
        var schema = ReadSchema();
        var (upStatements, downStatements) = BuildDiff(entities, schema);

        var safeName = migrationName.Trim().Replace(" ", "_");
        var id = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{safeName}";
        var filePath = Path.Combine(_migrationDirectory, $"{id}.sql");
        if (File.Exists(filePath))
        {
            throw new InvalidOperationException($"Migration file already exists: {filePath}");
        }

        var upBody = upStatements.Count == 0 ? "-- no changes" : string.Join(Environment.NewLine, upStatements);
        var downBody = downStatements.Count == 0 ? "-- no changes" : string.Join(Environment.NewLine, downStatements);
        var template = $"-- up{Environment.NewLine}{upBody}{Environment.NewLine}{Environment.NewLine}-- down{Environment.NewLine}{downBody}{Environment.NewLine}";

        File.WriteAllText(filePath, template);
        Console.WriteLine($"Created migration: {filePath}");
        return 0;
    }

    public int Apply()
    {
        EnsureMigrationsTable();
        var pending = GetPendingMigrations();
        if (pending.Count == 0)
        {
            Console.WriteLine("No pending migrations.");
            return 0;
        }

        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        foreach (var migrationFile in pending)
        {
            var migrationId = Path.GetFileNameWithoutExtension(migrationFile);
            var (upSql, _) = ParseMigrationFile(migrationFile);

            using var tx = conn.BeginTransaction();
            try
            {
                if (!string.IsNullOrWhiteSpace(upSql) && !upSql.Contains("-- no changes", StringComparison.OrdinalIgnoreCase))
                {
                    using var upCommand = new NpgsqlCommand(upSql, conn, tx);
                    upCommand.ExecuteNonQuery();
                }

                using var insertCommand = new NpgsqlCommand(
                    $"INSERT INTO {MigrationTable} (migration_id, applied_on_utc) VALUES (@id, @appliedOn);",
                    conn,
                    tx);
                insertCommand.Parameters.AddWithValue("@id", migrationId);
                insertCommand.Parameters.AddWithValue("@appliedOn", DateTime.UtcNow);
                insertCommand.ExecuteNonQuery();

                tx.Commit();
                Console.WriteLine($"Applied {migrationId}");
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        return 0;
    }

    public int List()
    {
        EnsureMigrationsTable();
        var allFiles = Directory.GetFiles(_migrationDirectory, "*.sql").OrderBy(f => f).ToList();
        var applied = GetAppliedMigrationIds();
        if (allFiles.Count == 0)
        {
            Console.WriteLine("No migrations found.");
            return 0;
        }

        foreach (var file in allFiles)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            Console.WriteLine(applied.Contains(id) ? $"[applied] {id}" : $"[pending] {id}");
        }

        return 0;
    }

    public int Rollback()
    {
        EnsureMigrationsTable();
        var lastApplied = GetLastAppliedMigrationId();
        if (lastApplied is null)
        {
            Console.WriteLine("No applied migrations to rollback.");
            return 0;
        }

        var filePath = Path.Combine(_migrationDirectory, $"{lastApplied}.sql");
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Migration file not found for rollback: {filePath}");
        }

        var (_, downSql) = ParseMigrationFile(filePath);
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            if (!string.IsNullOrWhiteSpace(downSql) && !downSql.Contains("-- no changes", StringComparison.OrdinalIgnoreCase))
            {
                using var downCommand = new NpgsqlCommand(downSql, conn, tx);
                downCommand.ExecuteNonQuery();
            }

            using var deleteCommand = new NpgsqlCommand(
                $"DELETE FROM {MigrationTable} WHERE migration_id = @id;",
                conn,
                tx);
            deleteCommand.Parameters.AddWithValue("@id", lastApplied);
            deleteCommand.ExecuteNonQuery();

            tx.Commit();
            Console.WriteLine($"Rolled back {lastApplied}");
            return 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private List<EntityMetadata> GetEntityMetadatas()
    {
        var assembly = typeof(TypeMapper).Assembly;
        return assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttributes(typeof(TableAttribute), false).Length > 0)
            .Select(EntityMetadata.For)
            .ToList();
    }

    private Dictionary<string, HashSet<string>> ReadSchema()
    {
        var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();

        const string sql = """
            SELECT table_name, column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
            ORDER BY table_name, ordinal_position;
            """;
        using var cmd = new NpgsqlCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var table = reader.GetString(0);
            var col = reader.GetString(1);
            if (!schema.TryGetValue(table, out var cols))
            {
                cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                schema[table] = cols;
            }

            cols.Add(col);
        }

        return schema;
    }

    private static (List<string> up, List<string> down) BuildDiff(
        IReadOnlyList<EntityMetadata> entities,
        IReadOnlyDictionary<string, HashSet<string>> schema)
    {
        var up = new List<string>();
        var down = new List<string>();

        foreach (var entity in entities)
        {
            if (!schema.ContainsKey(entity.TableName))
            {
                var definitions = entity.Columns.Select(TypeMapper.ToColumnDefinition);
                up.Add($"CREATE TABLE IF NOT EXISTS {entity.TableName} ({string.Join(", ", definitions)});");
                down.Insert(0, $"DROP TABLE IF EXISTS {entity.TableName};");
                continue;
            }

            var existingColumns = schema[entity.TableName];
            var missingColumns = entity.Columns.Where(c => !existingColumns.Contains(c.ColumnName)).ToList();
            foreach (var column in missingColumns)
            {
                var definition = TypeMapper.ToColumnDefinition(column);
                up.Add($"ALTER TABLE {entity.TableName} ADD COLUMN IF NOT EXISTS {definition};");
                down.Insert(0, $"ALTER TABLE {entity.TableName} DROP COLUMN IF EXISTS {column.ColumnName};");
            }
        }

        return (up, down);
    }

    private void EnsureMigrationsTable()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        var sql = $"""
            CREATE TABLE IF NOT EXISTS {MigrationTable} (
                migration_id TEXT PRIMARY KEY,
                applied_on_utc TIMESTAMP NOT NULL
            );
            """;
        using var command = new NpgsqlCommand(sql, conn);
        command.ExecuteNonQuery();
    }

    private List<string> GetPendingMigrations()
    {
        var all = Directory.GetFiles(_migrationDirectory, "*.sql").OrderBy(f => f).ToList();
        var applied = GetAppliedMigrationIds();
        return all.Where(f => !applied.Contains(Path.GetFileNameWithoutExtension(f))).ToList();
    }

    private HashSet<string> GetAppliedMigrationIds()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var command = new NpgsqlCommand($"SELECT migration_id FROM {MigrationTable};", conn);
        using var reader = command.ExecuteReader();

        var result = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private string? GetLastAppliedMigrationId()
    {
        using var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        using var command = new NpgsqlCommand(
            $"SELECT migration_id FROM {MigrationTable} ORDER BY applied_on_utc DESC LIMIT 1;",
            conn);
        return command.ExecuteScalar() as string;
    }

    private static (string upSql, string downSql) ParseMigrationFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var upMarker = "-- up";
        var downMarker = "-- down";

        var upIndex = content.IndexOf(upMarker, StringComparison.OrdinalIgnoreCase);
        var downIndex = content.IndexOf(downMarker, StringComparison.OrdinalIgnoreCase);
        if (upIndex < 0 || downIndex < 0 || downIndex <= upIndex)
        {
            throw new InvalidOperationException($"Invalid migration format in {filePath}. Expected '-- up' and '-- down'.");
        }

        var upSql = content[(upIndex + upMarker.Length)..downIndex].Trim();
        var downSql = content[(downIndex + downMarker.Length)..].Trim();
        return (upSql, downSql);
    }
}
