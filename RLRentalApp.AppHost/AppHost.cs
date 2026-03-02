var builder = DistributedApplication.CreateBuilder(args);


var postgres = builder.AddPostgres("rentalapp-postgres")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("rentalapp-postgres");

var rentalDb = postgres.AddDatabase("rentaldb");

// Migration project (runs first)
var migrations = builder.AddProject<Projects.RLRentalApp_Migrations>("database-migrations")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithReference(rentalDb)
    .WaitFor(rentalDb);

// Web project
builder.AddProject<Projects.RLRentalApp_Web>("web")
    .WithExplicitStart()
    .WithReference(rentalDb)
    .WaitFor(migrations);

await builder.Build().RunAsync();