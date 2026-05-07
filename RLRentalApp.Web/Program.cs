using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using RLRentalApp.Web.Data;
using RLRentalApp.Web.DataAccess;
using RLRentalApp.Web.Managers;
using RLRentalApp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.Filters.Add(new AuthorizeFilter(policy));
});

// Identity DB (Postgres)
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(GetRentalConnectionString(builder.Configuration)));

builder.Services.AddScoped<IPropertyDashboardDataAccess, PropertyDashboardDataAccess>();
builder.Services.Configure<GmailSmtpOptions>(builder.Configuration.GetSection(GmailSmtpOptions.SectionName));
builder.Services.AddScoped<IEmailService, GmailEmailService>();
builder.Services.AddScoped<IPropertyDashboardManager, PropertyDashboardManager>();

// Identity services
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<AuthDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var authDb = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await authDb.Database.MigrateAsync();
}

await IdentitySeeder.SeedAsync(app.Services);

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}");

app.Run();


static string GetRentalConnectionString(IConfiguration configuration)
{
    var mode = configuration["DatabaseMode"] ?? configuration["Database:Mode"] ?? "Demo";
    var connectionStringName = configuration[$"DatabaseProfiles:{mode}:ConnectionStringName"];

    if (string.IsNullOrWhiteSpace(connectionStringName))
    {
        connectionStringName = mode.Equals("Live", StringComparison.OrdinalIgnoreCase) ? "rentaldb_live" : "rentaldb_demo";
    }

    var connectionString = configuration.GetConnectionString(connectionStringName);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        connectionString = configuration.GetConnectionString("rentaldb");
    }

    return string.IsNullOrWhiteSpace(connectionString)
        ? throw new InvalidOperationException($"No rental database connection string found for mode '{mode}'.")
        : connectionString;
}
