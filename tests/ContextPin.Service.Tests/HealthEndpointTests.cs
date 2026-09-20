using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ContextPin.Service.Tests;

/// <summary>
/// Pins the one rule that must never regress as auth is added to this service:
/// health endpoints stay reachable without a token. aurelius-promptus shipped a
/// deny-by-default authorization policy without this exemption and it would have
/// failed every platform deploy — the probe is the one caller that can never
/// authenticate.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok_without_a_token()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Root_endpoint_identifies_the_service()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("context-pin");
    }
}
