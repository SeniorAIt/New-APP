using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Models;

namespace WorkbookManagement.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;

            var db = sp.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();

            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

            // Roles
            string[] roles = new[] { "SuperAdmin", "CompanyAdmin" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // Admin (use user-secrets in a later step)
            var adminEmail = config["Seed:AdminEmail"] ?? "admin@example.com";
            var adminPassword = config["Seed:AdminPassword"] ?? "Admin@123";

            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    CompanyName = "Global"
                };
                var res = await userManager.CreateAsync(admin, adminPassword);
                if (res.Succeeded)
                    await userManager.AddToRoleAsync(admin, "SuperAdmin");
            }
        }
    }
}
