using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeMaps;
using Talynor.Infrastructure.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// Registers the MVC antiforgery filters; the API still exposes only JSON controllers.
builder.Services.AddControllersWithViews();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IKnowledgeMapReader, InMemoryKnowledgeMapReader>();
builder.Services.AddScoped<GetKnowledgeMap>();
builder.Services.AddDbContext<TalynorDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Talynor");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("ConnectionStrings:Talynor must be configured to use persistence.");
    }

    var sqlite = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrEmpty(sqlite.DataSource) && sqlite.DataSource != ":memory:" &&
        !Path.IsPathRooted(sqlite.DataSource))
    {
        sqlite.DataSource = Path.GetFullPath(sqlite.DataSource, builder.Environment.ContentRootPath);
    }

    options.UseSqlite(sqlite.ConnectionString);
});

builder.Services.AddIdentityCore<IdentityUser>()
    .AddEntityFrameworkStores<TalynorDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddApplicationCookie();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-Talynor.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    // Keep the OnValidatePrincipal callback installed by AddApplicationCookie.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-Talynor.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposes the entry point to WebApplicationFactory without changing host behavior.
public partial class Program { }
