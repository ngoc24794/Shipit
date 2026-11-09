using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class FakeK8sDeployerTests
{
    private static DeployAllowList CreateDefaultAllowList() =>
        new([new DeployAllowListEntry("devops", ["shipit-demo"])]);

    [Fact]
    public async Task FakeDeployer_RejectsOutsideAllowList()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList());
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "unauthorized-app", "app", "image:1.0", TimeSpan.FromSeconds(10));

        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Contains("not allowed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(deployer.Requests);
    }

    [Fact]
    public async Task FakeDeployer_RecordsReceivedRequests()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList());
        var log = new TestLogSink();
        var request1 = new DeployRequest("devops", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));
        var request2 = new DeployRequest("devops", "shipit-demo", "app", "image:2.0", TimeSpan.FromSeconds(10));

        await deployer.SetImageAsync(request1, log);
        await deployer.SetImageAsync(request2, log);

        Assert.Equal(2, deployer.Requests.Count);
        Assert.Equal("image:1.0", deployer.Requests[0].Image);
        Assert.Equal("image:2.0", deployer.Requests[1].Image);
    }

    [Fact]
    public async Task FakeDeployer_DefaultResult_ReturnsSuccess()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList());
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));

        var result = await deployer.SetImageAsync(request, log);

        Assert.True(result.Success);
        Assert.Equal("deployed image:1.0 to devops/shipit-demo", result.Message);
        Assert.NotEmpty(log.Messages);
    }

    [Fact]
    public async Task FakeDeployer_ConfiguredResult_CanBeOverridden()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList())
        {
            ConfiguredResult = new DeployResult(false, "simulated failure")
        };
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));

        var result = await deployer.SetImageAsync(request, log);

        Assert.False(result.Success);
        Assert.Equal("simulated failure", result.Message);
    }

    [Fact]
    public async Task FakeDeployer_ResultFactory_CanDynamicallyDetermineResult()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList())
        {
            ResultFactory = r => r.Image.Contains("fail")
                ? new DeployResult(false, "bad image")
                : new DeployResult(true, "ok")
        };
        var log = new TestLogSink();

        var res1 = await deployer.SetImageAsync(new DeployRequest("devops", "shipit-demo", "app", "fail-image", TimeSpan.FromSeconds(10)), log);
        var res2 = await deployer.SetImageAsync(new DeployRequest("devops", "shipit-demo", "app", "good-image", TimeSpan.FromSeconds(10)), log);

        Assert.False(res1.Success);
        Assert.True(res2.Success);
    }

    [Fact]
    public async Task FakeDeployer_WritesSimulatedLogs()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList());
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "shipit-demo", "app", "registry.local/demo:v1", TimeSpan.FromSeconds(10));

        await deployer.SetImageAsync(request, log);

        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Setting image"));
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Waiting for rollout"));
        Assert.Contains(log.Messages, m => m.Stream == LogStream.System && m.Line.Contains("Rollout complete"));
    }

    [Fact]
    public async Task FakeDeployer_HonorsDelay()
    {
        var deployer = new FakeK8sDeployer(CreateDefaultAllowList())
        {
            Delay = TimeSpan.FromMilliseconds(50)
        };
        var log = new TestLogSink();
        var request = new DeployRequest("devops", "shipit-demo", "app", "image:1.0", TimeSpan.FromSeconds(10));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await deployer.SetImageAsync(request, log);
        sw.Stop();

        Assert.True(result.Success);
        Assert.True(sw.ElapsedMilliseconds >= 30, $"Elapsed was {sw.ElapsedMilliseconds}ms");
    }
}
