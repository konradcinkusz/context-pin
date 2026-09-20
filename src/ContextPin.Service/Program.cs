var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

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
