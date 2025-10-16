using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkbookManagement.Data;
using WorkbookManagement.Models;
using WorkbookManagement.Areas.Identity.Data;

var builder = WebApplication.CreateBuilder(args);

// -------------------------
// Database (SQLite for dev)
// -------------------------
var conn = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=app.db";
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(conn));
builder.Services.AddDatabaseDeveloperPageExceptionFilter(); // helpful EF errors in dev

// -------------------------
// Identity (with Roles)
// -------------------------
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        // Optional but recommended:
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Ensure unauthenticated users are redirected to the Identity UI login
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Identity/Account/Login";
    o.AccessDeniedPath = "/Identity/Account/AccessDenied";
});

// -------------------------
// Authorization
// -------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SuperAdminOnly", p => p.RequireRole("SuperAdmin"));
});

// -------------------------
// MVC + Razor Pages
// -------------------------
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(options =>
{
    // Gate the built-in Register page to SuperAdmin only
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", "SuperAdminOnly");

    // (Optional) also gate RegisterConfirmation if desired
    // options.Conventions.AuthorizeAreaPage("Identity", "/Account/RegisterConfirmation", "SuperAdminOnly");
});

var app = builder.Build();

// -------------------------
// Seed roles/users on startup
// -------------------------
using (var scope = app.Services.CreateScope())
{
    var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    await SeedData.InitializeAsync(scope.ServiceProvider, cfg);
}

// -------------------------
// Pipeline
// -------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    // Shows detailed EF errors & pending migration UI if desired
    // app.UseMigrationsEndPoint();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// -------------------------
// Routes
// -------------------------

// 1) Areas (e.g., /Admin)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

// 2) Default MVC
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// 3) Identity Razor Pages (e.g., /Identity/Account/Login)
app.MapRazorPages();

app.Run();
