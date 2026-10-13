using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Threading.RateLimiting;
using Talynor.Application.Accounts;
using Talynor.Infrastructure.Accounts;
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
builder.Services.AddScoped<RegisterUser>();
builder.Services.AddScoped<IIdentityAccountService, IdentityAccountService>();
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

builder.Services.AddIdentityCore<IdentityUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // The email contract accepts Unicode; do not impose the default ASCII username whitelist.
        options.User.AllowedUserNameCharacters = string.Empty;
        options.Password.RequiredLength = 15;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
    })
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
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("registration", context =>
    {
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true)
        {
            address = address.MapToIPv4();
        }
        return RateLimitPartition.GetFixedWindowLimiter(address?.ToString() ?? "missing-ip", _ => new()
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(10), QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de solicitações atingido. Tente novamente mais tarde.",
            Extensions = { ["code"] = "rate_limited" }
        }, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposes the entry point to WebApplicationFactory without changing host behavior.
public partial class Program { }
