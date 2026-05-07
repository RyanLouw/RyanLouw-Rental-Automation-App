var builder = DistributedApplication.CreateBuilder(args);


var postgres = builder.AddPostgres("rentalapp-postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("rentalapp-postgres");

var liveDb = postgres.AddDatabase("rentaldb_live");
var demoDb = postgres.AddDatabase("rentaldb_demo");

// Migration project (runs first). In Demo mode it migrates live + demo,
// resets demo from live, then applies demo-only seed migrations.
var migrations = builder.AddProject<Projects.RLRentalApp_Migrations>("database-migrations")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("DatabaseMode", "Demo")
    .WithReference(liveDb)
    .WithReference(demoDb)
    .WaitFor(liveDb)
    .WaitFor(demoDb);

// Web project. Switch DatabaseMode to Live when you want the app to use live data.
builder.AddProject<Projects.RLRentalApp_Web>("web")
    .WithExplicitStart()
    .WithEnvironment("DatabaseMode", "Demo")
    .WithReference(liveDb)
    .WithReference(demoDb)
    .WaitFor(migrations);

await builder.Build().RunAsync();
