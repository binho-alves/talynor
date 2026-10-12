using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Talynor.Application.KnowledgeMaps;
using Talynor.Infrastructure.KnowledgeMaps;
using Talynor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddControllers();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposes the entry point to WebApplicationFactory without changing host behavior.
public partial class Program { }
