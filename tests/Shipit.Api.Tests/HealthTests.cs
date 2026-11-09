using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Shipit.Api.Tests;

public class HealthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_returns_200_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }
}
