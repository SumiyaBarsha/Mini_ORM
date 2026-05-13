using Npgsql;

namespace MiniOrm.Data;

// Base class for DB connection handling.
// AppDbContext or other context will inherit from this
public abstract class DbContext : IDisposable
{
    // Constructor gets connection string from outside
    protected DbContext(string connectionString)
    {
        // Check for empty connection string
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.");
        }

        // Create PostgreSQL connection object
        Connection = new NpgsqlConnection(connectionString);
        
        Connection.Open();
    }

    // Child context classes can use this connection=> inherited
    protected NpgsqlConnection Connection { get; }

    // Optional public getter if we need to pass connection to other classes.
    public NpgsqlConnection DatabaseConnection => Connection;
 
    // Ensures DB connection is closed 
    public void Dispose()
    {
        Connection.Dispose();
    }
}
