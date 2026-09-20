using ContextPin.Service.Data;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("contextpindb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:contextpindb is not configured. Set it via appsettings, " +
        "an environment variable (ConnectionStrings__contextpindb), or run " +
        "`docker compose up -d` for local development — see docker-compose.yml.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<IRuleSetRepository, RuleSetRepository>();
builder.Services.AddSingleton<IRepoPinRepository, RepoPinRepository>();
builder.Services.AddSingleton<IFindingRepository, FindingRepository>();

builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres");

var app = builder.Build();

// Migrations run synchronously before the app starts serving traffic, not as a
// background hosted service: a request that reads a table before it exists is a
// worse failure mode than a slightly slower cold start. There is exactly one
// instance of this service today, so there is no rolling-migration coordination
// problem yet to justify anything more elaborate.
await using (var scope = app.Services.CreateAsyncScope())
{
    var dataSource = scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Migrations");
    var migrationsDirectory = Path.Combine(AppContext.BaseDirectory, "Migrations");
    await MigrationRunner.ApplyPendingAsync(dataSource, migrationsDirectory, logger);
}

// AllowAnonymous is deliberate and will stay deliberate as the service grows: a
// platform health probe presents no token, and a service that requires auth on its
// own health endpoint fails every deploy the moment authentication is added. See
// konradcinkusz/aurelius-promptus for exactly that mistake and its fix.
app.MapHealthChecks("/health").AllowAnonymous();

app.MapGet("/", () => Results.Ok(new { service = "context-pin", status = "ok" }))
    .AllowAnonymous();

app.Run();

// Exposes the top-level-statement Program class to WebApplicationFactory<Program>
// in the test project.
public partial class Program { }
