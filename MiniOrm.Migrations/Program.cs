using MiniOrm.Migrations.Commands;

namespace MiniOrm.Migrations;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Required format:
        // dotnet run -- migrations add <Name>
        // dotnet run -- migrations apply
        // dotnet run -- migrations list
        // dotnet run -- migrations rollback
        if (args.Length < 2 || args[0].ToLower() != "migrations")
        {
            ShowUsage();
            return 1;
        }

        var runner = new MigrationRunner();
        var command = args[1].ToLower();

        if (command == "add")
        {
            if (args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))
            {
                Console.WriteLine("Write: migrations add <Name>");
                return 1;
            }

            return runner.Add(args[2]);
        }

        if (command == "apply")
        {
            return runner.Apply();
        }

        if (command == "list")
        {
            return runner.List();
        }

        if (command == "rollback")
        {
            return runner.Rollback();
        }

        ShowUsage();
        return 1;
    }

    private static void ShowUsage()
    {
        Console.WriteLine("List of commands:");
        Console.WriteLine("  migrations add <Name>");
        Console.WriteLine("  migrations apply");
        Console.WriteLine("  migrations list");
        Console.WriteLine("  migrations rollback");
    }
}
