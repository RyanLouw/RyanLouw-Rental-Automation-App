var builder = DistributedApplication.CreateBuilder(args);

// Use local PostgreSQL connection string instead of Docker Postgres
var rentalDb = builder.AddConnectionString("rentaldb");

// Migration project
var migrations = builder.AddProject<Projects.RLRentalApp_Migrations>("database-migrations")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithReference(rentalDb);

// Web project
builder.AddProject<Projects.RLRentalApp_Web>("web")
    .WithExplicitStart()
    .WithReference(rentalDb);
    

await builder.Build().RunAsync();