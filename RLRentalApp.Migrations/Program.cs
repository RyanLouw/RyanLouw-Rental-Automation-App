using Database.Migrations;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Serilog;

namespace RLRentalApp.DbMigration;

public static class Program
{
    private const string DefaultMode = "Demo";

    private sealed record DatabaseProfile(
        string Name,
        string ConnectionStringName,
        string? CloneFromConnectionStringName,
        bool ResetFromSourceOnMigration);

    public static int Main(string[] args)
    {
        try
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            Log.Information("Starting DB migration runner");

            var configSettings = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var profile = ResolveDatabaseProfile(configSettings);
            Log.Information("Database mode: {DatabaseMode}. Target connection: {ConnectionStringName}", profile.Name, profile.ConnectionStringName);

            if (profile.ResetFromSourceOnMigration && !string.IsNullOrWhiteSpace(profile.CloneFromConnectionStringName))
            {
                RunMigrations(configSettings, profile.CloneFromConnectionStringName, TagNames.Rental);
                RunMigrations(configSettings, profile.ConnectionStringName, TagNames.Rental);
                ResetDatabaseFromSource(configSettings, profile.CloneFromConnectionStringName, profile.ConnectionStringName);
                SeedDemoData(configSettings, profile.ConnectionStringName);
            }
            else
            {
                RunMigrations(configSettings, profile.ConnectionStringName, TagNames.Rental);

                if (profile.Name.Equals("Demo", StringComparison.OrdinalIgnoreCase))
                {
                    SeedDemoData(configSettings, profile.ConnectionStringName);
                }
            }

            Log.Information("✅ All migrations completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ Migration runner failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RunMigrations(IConfigurationRoot configSettings, string connectionStringName, string tag)
    {
        Log.Information("Running migrations for ConnectionStringName: {ConnectionStringName}, Tag: {Tag}", connectionStringName, tag);

        var serviceProvider = CreateServices(connectionStringName, tag, configSettings);

        using var scope = serviceProvider.CreateScope();
        MigrateUp(scope.ServiceProvider);

        Log.Information("Finished migrations for {ConnectionStringName} ({Tag})", connectionStringName, tag);
    }


    private static void SeedDemoData(IConfigurationRoot configSettings, string connectionStringName)
    {
        Log.Information("Seeding demo data for ConnectionStringName: {ConnectionStringName}", connectionStringName);

        ExecuteSqlScript(configSettings, connectionStringName, @"Migrations\Scripts\0003.sql");
        ExecuteSqlScript(configSettings, connectionStringName, @"Migrations\Scripts\0008.sql");

        Log.Information("Finished seeding demo data for {ConnectionStringName}", connectionStringName);
    }

    private static void ExecuteSqlScript(IConfiguration configSettings, string connectionStringName, string relativeScriptPath)
    {
        var connectionString = configSettings.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{connectionStringName}' not found while running demo seed script '{relativeScriptPath}'.");
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, relativeScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Demo seed script '{relativeScriptPath}' was not found.", scriptPath);
        }

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = File.ReadAllText(scriptPath);
        command.ExecuteNonQuery();
        transaction.Commit();

        Log.Information("Executed demo seed script {ScriptPath}", relativeScriptPath);
    }

    private static DatabaseProfile ResolveDatabaseProfile(IConfiguration config)
    {
        var mode = config["DatabaseMode"] ?? config["Database:Mode"] ?? DefaultMode;
        var section = config.GetSection($"DatabaseProfiles:{mode}");

        var connectionStringName = section["ConnectionStringName"];
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            connectionStringName = mode.Equals("Live", StringComparison.OrdinalIgnoreCase) ? "rentaldb-live" : "rentaldb-demo";
        }

        if (string.IsNullOrWhiteSpace(config.GetConnectionString(connectionStringName)) && !string.IsNullOrWhiteSpace(config.GetConnectionString("rentaldb")))
        {
            connectionStringName = "rentaldb";
        }

        var cloneFromConnectionStringName = section["CloneFromConnectionStringName"];
        var shouldResetFromSource = bool.TryParse(section["ResetFromSourceOnMigration"], out var resetFromSource) && resetFromSource;

        if (shouldResetFromSource && !string.IsNullOrWhiteSpace(cloneFromConnectionStringName) && string.IsNullOrWhiteSpace(config.GetConnectionString(cloneFromConnectionStringName)))
        {
            shouldResetFromSource = false;
            cloneFromConnectionStringName = null;
        }

        return new DatabaseProfile(
            mode,
            connectionStringName,
            cloneFromConnectionStringName,
            shouldResetFromSource);
    }

    private static IServiceProvider CreateServices(string connectionStringName, string tag, IConfigurationRoot config)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddConfiguration(config);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        var connection = builder.Configuration.GetConnectionString(connectionStringName);

        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' not found. " +
                "In Aspire, this must match the database resource name (for example 'rentaldb-demo' or 'rentaldb-live').");
        }

        builder.Services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb =>
            {
                rb.AddPostgres()
                  .WithGlobalConnectionString(connection)
                  .ScanIn(typeof(Program).Assembly).For.Migrations();
            })
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .Configure<RunnerOptions>(opt =>
            {
                opt.Tags = new[] { tag };
            });

        return builder.Services.BuildServiceProvider(validateScopes: true);
    }

    private static void MigrateUp(IServiceProvider services)
    {
        var runner = services.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }

    private static void ResetDatabaseFromSource(IConfiguration config, string sourceConnectionStringName, string targetConnectionStringName)
    {
        var sourceConnectionString = config.GetConnectionString(sourceConnectionStringName);
        var targetConnectionString = config.GetConnectionString(targetConnectionStringName);

        if (string.IsNullOrWhiteSpace(sourceConnectionString) || string.IsNullOrWhiteSpace(targetConnectionString))
        {
            throw new InvalidOperationException("Both source and target connection strings are required to reset the demo database.");
        }

        var sourceBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString);
        var targetBuilder = new NpgsqlConnectionStringBuilder(targetConnectionString);
        if (string.Equals(sourceBuilder.Host, targetBuilder.Host, StringComparison.OrdinalIgnoreCase)
            && sourceBuilder.Port == targetBuilder.Port
            && string.Equals(sourceBuilder.Database, targetBuilder.Database, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("Skipping demo reset because source '{Source}' and target '{Target}' resolve to the same database.", sourceConnectionStringName, targetConnectionStringName);
            return;
        }

        Log.Warning("Resetting demo database '{Target}' from live/source database '{Source}'. Demo data will be truncated first.", targetConnectionStringName, sourceConnectionStringName);

        using var source = new NpgsqlConnection(sourceConnectionString);
        using var target = new NpgsqlConnection(targetConnectionString);
        source.Open();
        target.Open();

        var tables = LoadCommonTables(source, target);
        if (tables.Count == 0)
        {
            Log.Warning("No common public tables found to copy from {Source} to {Target}.", sourceConnectionStringName, targetConnectionStringName);
            return;
        }

        using (var truncate = target.CreateCommand())
        {
            truncate.CommandText = $"TRUNCATE TABLE {string.Join(", ", tables.Select(QuoteIdentifier))} RESTART IDENTITY CASCADE;";
            truncate.ExecuteNonQuery();
        }

        foreach (var table in SortTablesForCopy(target, tables))
        {
            var columns = LoadCommonColumns(source, target, table);
            if (columns.Count == 0)
            {
                continue;
            }

            CopyTable(source, target, table, columns);
            ResetTableSequences(target, table, columns);
        }

        Log.Information("Finished resetting demo database '{Target}' from '{Source}'.", targetConnectionStringName, sourceConnectionStringName);
    }


    private static List<string> SortTablesForCopy(NpgsqlConnection target, List<string> tables)
    {
        var remaining = tables.ToHashSet(StringComparer.Ordinal);
        var dependencies = LoadTableDependencies(target, remaining);
        var ordered = new List<string>();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(table => dependencies[table].All(dependency => !remaining.Contains(dependency)))
                .OrderBy(table => table, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
            {
                // Circular foreign keys are not expected in this schema, but keep the
                // reset deterministic rather than looping forever if one is added later.
                ready = remaining.OrderBy(table => table, StringComparer.Ordinal).Take(1).ToList();
            }

            foreach (var table in ready)
            {
                ordered.Add(table);
                remaining.Remove(table);
            }
        }

        return ordered;
    }

    private static Dictionary<string, HashSet<string>> LoadTableDependencies(NpgsqlConnection connection, HashSet<string> tables)
    {
        var dependencies = tables.ToDictionary(
            table => table,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT child.relname AS child_table,
                   parent.relname AS parent_table
            FROM pg_constraint c
            JOIN pg_class child ON child.oid = c.conrelid
            JOIN pg_namespace child_ns ON child_ns.oid = child.relnamespace
            JOIN pg_class parent ON parent.oid = c.confrelid
            JOIN pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
            WHERE c.contype = 'f'
              AND child_ns.nspname = 'public'
              AND parent_ns.nspname = 'public';";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var child = reader.GetString(0);
            var parent = reader.GetString(1);
            if (dependencies.TryGetValue(child, out var childDependencies) && tables.Contains(parent))
            {
                childDependencies.Add(parent);
            }
        }

        return dependencies;
    }

    private static List<string> LoadCommonTables(NpgsqlConnection source, NpgsqlConnection target)
    {
        var sourceTables = LoadTables(source);
        var targetTables = LoadTables(target);
        return sourceTables.Intersect(targetTables, StringComparer.Ordinal).OrderBy(x => x).ToList();
    }

    private static HashSet<string> LoadTables(NpgsqlConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE';";

        using var reader = cmd.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static List<string> LoadCommonColumns(NpgsqlConnection source, NpgsqlConnection target, string table)
    {
        var sourceColumns = LoadColumns(source, table);
        var targetColumns = LoadColumns(target, table);
        return sourceColumns.Where(x => targetColumns.Contains(x)).ToList();
    }

    private static List<string> LoadColumns(NpgsqlConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table
            ORDER BY ordinal_position;";
        cmd.Parameters.AddWithValue("@table", table);

        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static void CopyTable(NpgsqlConnection source, NpgsqlConnection target, string table, List<string> columns)
    {
        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));
        var sourceSql = $"COPY (SELECT {quotedColumns} FROM {QuoteIdentifier(table)}) TO STDOUT (FORMAT BINARY)";
        var targetSql = $"COPY {QuoteIdentifier(table)} ({quotedColumns}) FROM STDIN (FORMAT BINARY)";

        using var reader = source.BeginBinaryExport(sourceSql);
        using var writer = target.BeginBinaryImport(targetSql);

        while (reader.StartRow() != -1)
        {
            writer.StartRow();
            for (var i = 0; i < columns.Count; i++)
            {
                writer.Write(reader.Read<object?>());
            }
        }

        writer.Complete();
        Log.Information("Copied table {Table} ({ColumnCount} columns).", table, columns.Count);
    }


    private static void ResetTableSequences(NpgsqlConnection connection, string table, List<string> columns)
    {
        foreach (var column in columns)
        {
            using var sequenceCommand = connection.CreateCommand();
            sequenceCommand.CommandText = "SELECT pg_get_serial_sequence(@tableName, @columnName);";
            sequenceCommand.Parameters.AddWithValue("@tableName", $"public.{QuoteIdentifier(table)}");
            sequenceCommand.Parameters.AddWithValue("@columnName", column);

            var sequence = sequenceCommand.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(sequence))
            {
                continue;
            }

            using var resetCommand = connection.CreateCommand();
            resetCommand.CommandText = $@"
                SELECT setval(
                    @sequenceName::regclass,
                    GREATEST((SELECT COALESCE(MAX({QuoteIdentifier(column)}), 0) FROM {QuoteIdentifier(table)}), 1),
                    (SELECT COALESCE(MAX({QuoteIdentifier(column)}), 0) > 0 FROM {QuoteIdentifier(table)}));";
            resetCommand.Parameters.AddWithValue("@sequenceName", sequence);
            resetCommand.ExecuteNonQuery();
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}
