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

    [Fact]
    public async Task RolloutTimedOut_RollsBackToPreviousImage_SendsExactRollbackPatch_AndReturnsFailure()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2, updatedReplicas: 2, availableReplicas: 2, unavailableReplicas: 0);
        var unreadyNew = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);
        var rolledBackReady = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 3, observedGeneration: 3, replicas: 2, updatedReplicas: 2, availableReplicas: 2, unavailableReplicas: 0);

        var getCount = 0;
        var patchCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCount++;
                // 1: initial GET to read deployment and previousImage
                if (getCount == 1)
                {
                    return MockHttpMessageHandler.JsonResponse(initial);
                }

                // If rollback patch has occurred, return rolledBackReady
                if (patchCount >= 2)
                {
                    return MockHttpMessageHandler.JsonResponse(rolledBackReady);
                }

                // Polling during first rollout: unready
                return MockHttpMessageHandler.JsonResponse(unreadyNew);
            }

            if (req.Method == HttpMethod.Patch)
            {
                patchCount++;
                if (patchCount == 1)
                {
                    return MockHttpMessageHandler.JsonResponse(unreadyNew);
                }

                return MockHttpMessageHandler.JsonResponse(rolledBackReady);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        // Timeout 50ms, poll interval 5ms, rollbackTimeout 5s
        var deployer = new KubernetesDeployer(
            client,
            CreateDefaultAllowList(),
            pollInterval: TimeSpan.FromMilliseconds(5),
            rollbackTimeout: TimeSpan.FromSeconds(5));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMilliseconds(50));
        var result = await deployer.SetImageAsync(request, log);

        // 1. Success is false and message contains previousImage and rollback notice
        Assert.False(result.Success);
        Assert.Contains("old:1.0", result.Message);
        Assert.Contains("đã hoàn tác về old:1.0", result.Message);

        // 2. Exactly one patch back to previousImage after the new image patch (total 2 patches)
        var patches = handler.Requests.Where(r => r.Method == HttpMethod.Patch).ToList();
        Assert.Equal(2, patches.Count);

        // Patch 1: target container set to new:2.0
        const string expectedNewJson = "{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"app\",\"image\":\"new:2.0\"}]}}}}";
        Assert.Equal(expectedNewJson, patches[0].ContentBody);

        // Patch 2 (rollback): target container set to old:1.0 only
        const string expectedRollbackJson = "{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"app\",\"image\":\"old:1.0\"}]}}}}";
        Assert.Equal(expectedRollbackJson, patches[1].ContentBody);
        Assert.Contains("application/strategic-merge-patch+json", patches[1].ContentType);

        // 3. System log records rollback step
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Rollout failed, reverting to old:1.0"));
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Rollback completed to old:1.0"));
    }

    [Fact]
    public async Task RolloutSucceeds_NoRollbackPatchExecuted()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1);
        var readyNew = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 1, updatedReplicas: 1, availableReplicas: 1, unavailableReplicas: 0);

        var getCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                getCount++;
                return MockHttpMessageHandler.JsonResponse(getCount == 1 ? initial : readyNew);
            }

            if (req.Method == HttpMethod.Patch)
            {
                return MockHttpMessageHandler.JsonResponse(readyNew);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromSeconds(10));
        var result = await deployer.SetImageAsync(request, log);

        Assert.True(result.Success);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.DoesNotContain(log.Messages, m => m.Line.Contains("reverting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PreviousImageEqualsNewImage_RolloutFails_NoRollbackPatchExecuted()
    {
        var unready = CreateDeployment("devops", "shipit-demo", "app", "same:1.0", generation: 1, observedGeneration: 1, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(unready);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.FromMilliseconds(5));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "same:1.0", TimeSpan.FromMilliseconds(50));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.StartsWith("rollout timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        // Zero patches made
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.DoesNotContain("hoàn tác", result.Message);
        Assert.DoesNotContain(log.Messages, m => m.Line.Contains("reverting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnreadableDeployment_DoesNotAutoRollback_KeepsExistingError()
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
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.DoesNotContain("hoàn tác", result.Message);
    }

    [Fact]
    public async Task RollbackPatchFails_ReturnsFailureMessage_WithoutThrowing()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2);
        var unreadyNew = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);

        var patchCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(patchCount == 0 ? initial : unreadyNew);
            }

            if (req.Method == HttpMethod.Patch)
            {
                patchCount++;
                if (patchCount == 1)
                {
                    return MockHttpMessageHandler.JsonResponse(unreadyNew);
                }

                // Rollback patch fails with 500 Internal Server Error
                return MockHttpMessageHandler.ErrorResponse(HttpStatusCode.InternalServerError, "InternalServerError", "k8s apiserver unavailable");
            }

            throw new InvalidOperationException();
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.FromMilliseconds(5));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMilliseconds(50));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("hoàn tác thất bại", result.Message);
        Assert.Contains("500", result.Message);
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Rollback failed"));
    }

    [Fact]
    public async Task RollbackConfirmationTimesOut_ReturnsFailureMessage()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2);
        var unreadyNew = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);
        // During rollback, deployment stays unready
        var unreadyOld = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 3, observedGeneration: 3, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);

        var patchCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                if (patchCount == 0) return MockHttpMessageHandler.JsonResponse(initial);
                if (patchCount == 1) return MockHttpMessageHandler.JsonResponse(unreadyNew);
                return MockHttpMessageHandler.JsonResponse(unreadyOld);
            }

            if (req.Method == HttpMethod.Patch)
            {
                patchCount++;
                return MockHttpMessageHandler.JsonResponse(patchCount == 1 ? unreadyNew : unreadyOld);
            }

            throw new InvalidOperationException();
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(
            client,
            CreateDefaultAllowList(),
            pollInterval: TimeSpan.FromMilliseconds(5),
            rollbackTimeout: TimeSpan.FromMilliseconds(30));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMilliseconds(30));
        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("hoàn tác thất bại", result.Message);
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Rollback confirmation timed out"));
    }

    [Fact]
    public async Task CancellationDuringRollout_ThrowsOperationCanceledException_AndDoesNotRollback()
    {
        var initial = CreateDeployment("devops", "shipit-demo", "app", "old:1.0", generation: 1, observedGeneration: 1, replicas: 2);
        var unreadyNew = CreateDeployment("devops", "shipit-demo", "app", "new:2.0", generation: 2, observedGeneration: 2, replicas: 2, updatedReplicas: 0, availableReplicas: 0, unavailableReplicas: 2);

        using var cts = new CancellationTokenSource();
        var patchCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return MockHttpMessageHandler.JsonResponse(patchCount == 0 ? initial : unreadyNew);
            }

            if (req.Method == HttpMethod.Patch)
            {
                patchCount++;
                // Cancel right after first patch
                cts.Cancel();
                return MockHttpMessageHandler.JsonResponse(unreadyNew);
            }

            throw new InvalidOperationException();
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, CreateDefaultAllowList(), pollInterval: TimeSpan.FromMilliseconds(5));
        var log = new TestLogSink();

        var request = new DeployRequest("devops", "shipit-demo", "app", "new:2.0", TimeSpan.FromMinutes(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deployer.SetImageAsync(request, log, cts.Token));

        // Exactly 1 patch (the initial one), NO rollback patch
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch);
    }
}
