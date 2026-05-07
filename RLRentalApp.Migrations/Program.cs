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
                RunMigrations(configSettings, profile.ConnectionStringName, TagNames.Demo);
            }
            else
            {
                RunMigrations(configSettings, profile.ConnectionStringName, TagNames.Rental);

                if (profile.Name.Equals("Demo", StringComparison.OrdinalIgnoreCase))
                {
                    RunMigrations(configSettings, profile.ConnectionStringName, TagNames.Demo);
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

    private static DatabaseProfile ResolveDatabaseProfile(IConfiguration config)
    {
        var mode = config["DatabaseMode"] ?? config["Database:Mode"] ?? DefaultMode;
        var section = config.GetSection($"DatabaseProfiles:{mode}");

        var connectionStringName = section["ConnectionStringName"];
        if (string.IsNullOrWhiteSpace(connectionStringName))
        {
            connectionStringName = mode.Equals("Live", StringComparison.OrdinalIgnoreCase) ? "rentaldb_live" : "rentaldb_demo";
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
                "In Aspire, this must match the database resource name (for example 'rentaldb_demo' or 'rentaldb_live').");
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

        foreach (var table in tables)
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
