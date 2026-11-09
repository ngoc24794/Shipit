using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shipit.Api.Tests;

public partial class ContractConformanceTests
{
    [GeneratedRegex(@"\{(\w+):[^}]+\}")]
    private static partial Regex Constraint();

    private static string Normalize(string pattern) => Constraint().Replace(pattern, "{$1}").TrimEnd('/');

    private static HashSet<(string Method, string Path)> ImplementedOperations(ApiFactory factory)
    {
        _ = factory.CreateClient(); // starts the host
        var result = new HashSet<(string, string)>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var path = Normalize(endpoint.RoutePattern.RawText ?? string.Empty);
            var isApi = path.StartsWith("/api/", StringComparison.Ordinal) || path is "/healthz" or "/readyz";
            if (!isApi || path.Contains("{**", StringComparison.Ordinal))
            {
                continue; // static files, the SPA fallback and the /api 404 catch-all are not part of the contract
            }

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                result.Add((method.ToUpperInvariant(), path));
            }
        }

        return result;
    }

    [Fact]
    public void Every_operation_in_the_contract_is_implemented()
    {
        using var factory = new ApiFactory();
        var implemented = ImplementedOperations(factory);
        var missing = ContractTests.LoadOperations().Where(op => !implemented.Contains(op)).ToList();
        Assert.True(missing.Count == 0, "Not implemented: " + string.Join(", ", missing.Select(m => $"{m.Method} {m.Path}")));
    }

    [Fact]
    public void Every_api_endpoint_is_documented_in_the_contract()
    {
        using var factory = new ApiFactory();
        var documented = ContractTests.LoadOperations().ToHashSet();
        var extra = ImplementedOperations(factory).Where(op => !documented.Contains(op)).ToList();
        Assert.True(extra.Count == 0, "Not in docs/api-contract.yaml: " + string.Join(", ", extra.Select(m => $"{m.Method} {m.Path}")));
    }
}
