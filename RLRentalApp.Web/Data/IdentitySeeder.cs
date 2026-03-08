using Microsoft.AspNetCore.Identity;
namespace RLRentalApp.Web.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = "ryan@local";
        var password = "Password1!";

        var existing = await userManager.FindByNameAsync(email);
        if (existing != null) return;

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            throw new Exception("Failed to create seed user: " + string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}