using Database.Migrations;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace RLRentalApp.DbMigration;

public static class Program
{
    private sealed record MigratorTag(string ConnectionKey, string Tag);

    private static readonly MigratorTag[] MigratorTags =
    [
        new("rentaldb", TagNames.Rental) 
    ];

    public static int Main(string[] args)
    {
        try
        {

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            Log.Information("Starting DB migration runner");

            var configSettings = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            foreach (var tag in MigratorTags)
            {
                Log.Information("Running migrations for ConnectionKey: {ConnectionKey}, Tag: {Tag}", tag.ConnectionKey, tag.Tag);

                var serviceProvider = CreateServices(tag, configSettings);

                using var scope = serviceProvider.CreateScope();
                MigrateUp(scope.ServiceProvider);

                Log.Information("Finished migrations for {ConnectionKey}", tag.ConnectionKey);
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

    private static IServiceProvider CreateServices(MigratorTag migratorTag, IConfigurationRoot config)
    {
        var builder = Host.CreateApplicationBuilder();


        builder.Configuration.AddConfiguration(config);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .CreateLogger();



        var connection = builder.Configuration.GetConnectionString(migratorTag.ConnectionKey);

        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException(
                $"Connection string '{migratorTag.ConnectionKey}' not found. " +
                $"In Aspire, this must match the database resource name (e.g. 'rentaldb').");

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

                opt.Tags = new[] { migratorTag.Tag };
            });

        return builder.Services.BuildServiceProvider(validateScopes: true);
    }

    private static void MigrateUp(IServiceProvider services)
    {
        var runner = services.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();
    }
}