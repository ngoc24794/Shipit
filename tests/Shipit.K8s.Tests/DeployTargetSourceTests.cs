using System.Net;
using k8s.Models;
using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class DeployTargetSourceTests
{
    private sealed class DynamicTargetStore : IDeployTargetStore
    {
        public HashSet<(string, string)> Allowed { get; } = new();

        public Task<bool> IsAllowedAsync(string ns, string deployment, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Allowed.Contains((ns, deployment)));
        }
    }

    [Fact]
    public async Task FakeK8sDeployer_DefenseInDepth_RejectsWhenTargetRemovedFromStore()
    {
        var store = new DynamicTargetStore();
        store.Allowed.Add(("devops", "shipit-demo"));

        var allowList = new DeployAllowList(store);
        var deployer = new FakeK8sDeployer(allowList);
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));

        // When allowed in store -> deploy succeeds
        var result1 = await deployer.SetImageAsync(request, log);
        Assert.True(result1.Success);

        // Target is removed from store (e.g. deleted from DB)
        store.Allowed.Clear();

        // Runtime defense: deploy fails with refusal message, no cluster patch performed
        var result2 = await deployer.SetImageAsync(request, log);
        Assert.False(result2.Success);
        Assert.Contains("not allowed", result2.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KubernetesDeployer_QueriesDynamicStore_RejectsAndAllowsAccordingly()
    {
        var store = new DynamicTargetStore();
        var allowList = new DeployAllowList(store);

        var handler = new MockHttpMessageHandler(req =>
        {
            // If it reaches HTTP, return a deployment
            var deployment = new V1Deployment
            {
                Metadata = new V1ObjectMeta { NamespaceProperty = "devops", Name = "my-app", Generation = 1 },
                Spec = new V1DeploymentSpec
                {
                    Replicas = 1,
                    Template = new V1PodTemplateSpec
                    {
                        Spec = new V1PodSpec
                        {
                            Containers = new List<V1Container>
                            {
                                new() { Name = "app", Image = "old:1.0" }
                            }
                        }
                    }
                },
                Status = new V1DeploymentStatus
                {
                    ObservedGeneration = 1,
                    UpdatedReplicas = 1,
                    AvailableReplicas = 1,
                    UnavailableReplicas = 0
                }
            };
            return MockHttpMessageHandler.JsonResponse(deployment, HttpStatusCode.OK);
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var deployer = new KubernetesDeployer(client, allowList);
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "my-app", "app", "new:1.0", TimeSpan.FromSeconds(10));

        // 1. Not in store -> rejected immediately, ZERO HTTP requests
        var rejectResult = await deployer.SetImageAsync(request, log);
        Assert.False(rejectResult.Success);
        Assert.Contains("not allowed", rejectResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);

        // 2. Added to store -> deploy proceeds and calls cluster
        store.Allowed.Add(("devops", "my-app"));
        var allowResult = await deployer.SetImageAsync(request, log);
        Assert.True(allowResult.Success);
        Assert.NotEmpty(handler.Requests);
    }
}
