using YamlDotNet.RepresentationModel;

namespace Shipit.Api.Tests;

public class ContractTests
{
    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Shipit.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Shipit.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Returns (METHOD, path) for every operation declared in docs/api-contract.yaml.</summary>
    internal static IReadOnlyList<(string Method, string Path)> LoadOperations()
    {
        var path = System.IO.Path.Combine(FindRepoRoot(), "docs", "api-contract.yaml");
        var yaml = new YamlStream();
        using (var reader = File.OpenText(path))
        {
            yaml.Load(reader);
        }

        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var paths = (YamlMappingNode)root.Children[new YamlScalarNode("paths")];
        string[] methods = ["get", "post", "put", "delete", "patch"];
        var result = new List<(string, string)>();
        foreach (var (key, value) in paths.Children)
        {
            foreach (var (opKey, _) in ((YamlMappingNode)value).Children)
            {
                var name = ((YamlScalarNode)opKey).Value!;
                if (methods.Contains(name))
                {
                    result.Add((name.ToUpperInvariant(), ((YamlScalarNode)key).Value!));
                }
            }
        }

        return result;
    }

    [Fact]
    public void Contract_is_valid_yaml_and_declares_operations()
    {
        var ops = LoadOperations();
        Assert.Contains(("GET", "/healthz"), ops);
        Assert.Contains(("POST", "/api/pipelines/{id}/runs"), ops);
        Assert.Contains(("GET", "/api/runs/{id}/logs"), ops);
        Assert.True(ops.Count >= 20, $"expected at least 20 operations, found {ops.Count}");
    }
}
