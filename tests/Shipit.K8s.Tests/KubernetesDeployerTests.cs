using System.Net;
using k8s.Models;
using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class KubernetesDeployerTests
{
    private static DeployAllowList CreateDefaultAllowList() =>
        new([new DeployAllowListEntry("devops", ["shipit-demo", "web-app"])]);

    private static V1Deployment CreateDeployment(
        string ns,
        string name,
        string containerName,
        string currentImage,
        long generation = 1,
        long observedGeneration = 1,
        int replicas = 1,
        int updatedReplicas = 1,
        int availableReplicas = 1,
        int? unavailableReplicas = 0,
        IEnumerable<string>? otherContainers = null)
    {
        var containers = new List<V1Container>
        {
            new() { Name = containerName, Image = currentImage }
        };

        if (otherContainers != null)
        {
            foreach (var other in otherContainers)
            {
                containers.Add(new V1Container { Name = other, Image = "other:v1" });
            }
        }

        return new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                NamespaceProperty = ns,
                Name = name,
                Generation = generation
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = replicas,
                Template = new V1PodTemplateSpec
                {
                    Spec = new V1PodSpec
                    {
                        Containers = containers
                    }
                }
            },
            Status = new V1DeploymentStatus
            {
                ObservedGeneration = observedGeneration,
                UpdatedReplicas = updatedReplicas,
                AvailableReplicas = availableReplicas,
                UnavailableReplicas = unavailableReplicas
            }
        };
    }

    [Fact]
    public async Task OutsideAllowList_RejectsImmediately_ZeroRequestsToCluster()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed deployment."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var request = new DeployRequest("production", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("Invalid_Namespace", "shipit-demo", "app", "image:1.0")]
    [InlineData("-invalid-start", "shipit-demo", "app", "image:1.0")]
    [InlineData("invalid-end-", "shipit-demo", "app", "image:1.0")]
    [InlineData("devops", "Invalid_Deployment", "app", "image:1.0")]
    [InlineData("devops", "-bad-dep", "app", "image:1.0")]
    [InlineData("devops", "shipit-demo", "App_Upper", "image:1.0")]
    [InlineData("devops", "shipit-demo", "app with spaces", "image:1.0")]
    [InlineData("devops", "shipit-demo", "app", "image with space:1.0")]
    [InlineData("devops", "shipit-demo", "app", "image\twithcontrol:1.0")]
    [InlineData("devops", "shipit-demo", "app", "")]
    public async Task ValidateNames_RejectsInvalidIdentifiers_WithoutCallingApi(
        string ns, string deployment, string container, string image)
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for invalid inputs."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var request = new DeployRequest(ns, deployment, container, image, TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("Invalid", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task WrongContainer_ReturnsErrorListingAvailableContainers()
    {
        var deployment = CreateDeployment(
            "devops",
            "shipit-demo",
            "web",
            "old:1.0",
            otherContainers: ["sidecar", "metrics"]);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(deployment);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "database", "db:1.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("Container 'database' not found in deployment 'shipit-demo'", result.Message);
        Assert.Contains("'web'", result.Message);
        Assert.Contains("'sidecar'", result.Message);
        Assert.Contains("'metrics'", result.Message);
        Assert.Single(handler.Requests); // Only the GET call was made
    }

    [Fact]
    public async Task PatchBody_MatchesExactJsonAndContentType()
    {
        var initialDeployment = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1);
        var patchedDeployment = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2);

        var getCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCount++;
                return MockHttpMessageHandler.JsonResponse(getCount == 1 ? initialDeployment : patchedDeployment);
            }

            if (req.Method == HttpMethod.Patch)
            {
                return MockHttpMessageHandler.JsonResponse(patchedDeployment);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.True(result.Success);

        var patchReq = handler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Patch);
        Assert.NotNull(patchReq);
        Assert.NotNull(patchReq.ContentType);
        Assert.Contains("application/strategic-merge-patch+json", patchReq.ContentType);

        const string expectedJson = "{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"app\",\"image\":\"new:2.0\"}]}}}}";
        Assert.Equal(expectedJson, patchReq.ContentBody);
    }

    [Fact]
    public async Task Rollout_SucceedsAfterMultiplePolls()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2, updatedReplicas: 2, availableReplicas: 2);
        var patched = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 1, replicas: 2, updatedReplicas: 0, availableReplicas: 2);
        var poll1 = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 1, availableReplicas: 1);
        var poll2Ready = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 2, availableReplicas: 2, unavailableReplicas: 0);

        var getCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCount++;
                return getCount switch
                {
                    1 => MockHttpMessageHandler.JsonResponse(initial),
                    2 => MockHttpMessageHandler.JsonResponse(patched),
                    3 => MockHttpMessageHandler.JsonResponse(poll1),
                    _ => MockHttpMessageHandler.JsonResponse(poll2Ready)
                };
            }

            if (req.Method == HttpMethod.Patch)
            {
                return MockHttpMessageHandler.JsonResponse(patched);
            }

            throw new InvalidOperationException($"Unexpected: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.True(result.Success);
        Assert.Equal("deployed new:2.0 to devops/shipit-demo", result.Message);
        Assert.True(getCount >= 4, $"Expected at least 4 GET calls, got {getCount}");

        // Verify logs emitted on status changes
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("1/2 replicas updated"));
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("2/2 replicas updated"));
    }

    [Fact]
    public async Task Rollout_TimesOut_WhenReplicasNeverReady()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2);
        // Deployment remains unready: updatedReplicas = 0
        var unready = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(unready);
            }

            if (req.Method == HttpMethod.Patch)
            {
                return MockHttpMessageHandler.JsonResponse(unready);
            }

            throw new InvalidOperationException();
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        // Timeout 50ms, poll interval 5ms
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.FromMilliseconds(5));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMilliseconds(50));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.StartsWith("rollout timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shipit-demo", result.Message);
    }

    [Fact]
    public async Task ApiError_403Forbidden_ReturnsFailureWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.ErrorResponse(HttpStatusCode.Forbidden, "Forbidden", "User forbidden to read deployment"));

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("403", result.Message);
    }

    [Fact]
    public async Task ApiError_404NotFound_ReturnsFailureWithoutThrowing()
    {
        var handler = new MockHttpMessageHandler(_ =>
            MockHttpMessageHandler.ErrorResponse(HttpStatusCode.NotFound, "NotFound", "Deployment not found"));

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("404", result.Message);
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0");
        var handler = new MockHttpMessageHandler(req => MockHttpMessageHandler.JsonResponse(initial));
        var client = MockHttpMessageHandler.CreateClient(handler);

        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.FromSeconds(10));
        var log = new TestLogSink();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deployer.SetImageAsync(request, log, cts.Token));
    }

    [Fact]
    public async Task Idempotency_WhenImageAlreadyMatches_SkipsPatchAndWaitsRollout()
    {
        // Initial deployment already has "my-image:1.0"
        var ready = CreateDeployment("devops", "shipit-demo", "app", "my-image:1.0", generation: 1, observedGeneration: 1, replicas: 1, updatedReplicas: 1, availableReplicas: 1, unavailableReplicas: 0);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(ready);
            }

            throw new InvalidOperationException($"PATCH should not be called: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "my-image:1.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.True(result.Success);
        Assert.Equal("deployed my-image:1.0 to devops/shipit-demo", result.Message);
        // Verify no PATCH request was made
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Patch);
        // Verify system log noted existing image
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("already uses image"));
    }
}
