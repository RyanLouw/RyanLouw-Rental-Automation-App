using Microsoft.AspNetCore.Identity;
namespace RLRentalApp.Web.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var email = configuration["SeedAdmin:Email"];
        var password = configuration["SeedAdmin:Password"];

        // Existing production systems should leave these unset. This avoids a
        // shared default administrator password in source control.
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var existing = await userManager.FindByNameAsync(email);
        if (existing != null) return;

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            throw new Exception("Failed to create seed user: " + string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
