using System.Security.Cryptography;
using System.Text;

namespace ContextPin.Service.Security;

/// <summary>
/// Minimal shared-secret gate for the write endpoints (publishing a rule set,
/// pinning a repo, reporting a finding) until per-repo service tokens exist.
/// </summary>
/// <remarks>
/// Every consuming repo's CI shares one key for now — a stopgap, not the final
/// model. A stopgap is not the same as leaving these endpoints open, though:
/// this session spent an entire PR elsewhere (aurelius-promptus) on exactly
/// that mistake, and this service has no other authentication pipeline
/// installed yet, so this filter is the only thing standing between a write
/// endpoint and the open internet once this is deployed. Fails closed, not
/// open: a missing configuration value refuses every request rather than
/// silently accepting them.
/// </remarks>
public sealed class RequireAdminKey(IConfiguration configuration) : IEndpointFilter
{
    private const string HeaderName = "X-Admin-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = configuration["AdminApiKey"];
        if (string.IsNullOrEmpty(expected))
        {
            return Results.Problem(
                "AdminApiKey is not configured on this service.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        // Hashing both sides to a fixed 32 bytes first means FixedTimeEquals always
        // compares equal-length buffers, so there is nothing to reason about
        // regarding its documented (and harmless) different-length fast path —
        // simpler to make the comparison itself always constant-time than to argue
        // about why a length check would have been fine here too.
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));

        if (string.IsNullOrEmpty(provided) || !CryptographicOperations.FixedTimeEquals(providedHash, expectedHash))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
