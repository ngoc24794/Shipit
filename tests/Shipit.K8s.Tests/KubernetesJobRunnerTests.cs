using System.Net;
using System.Text;
using k8s.Models;
using Shipit.Core;
using Xunit;

namespace Shipit.K8s.Tests;

public class KubernetesJobRunnerTests
{
    private static JobAllowList CreateDefaultAllowList() =>
        new(
            allowedNamespaces: ["devops", "avina-workspace"],
            allowedConfigMaps: [("devops", "app-config"), ("avina-workspace", "app-config")],
            allowedSecrets: [("devops", "app-secret"), ("avina-workspace", "db-pass")],
            allowedPullSecrets: [("devops", "regcred")]);

    private static JobTaskRequest CreateRequest(
        string ns = "devops",
        string jobName = "test-job-1",
        string container = "runner",
        string image = "busybox:latest",
        IReadOnlyList<string>? args = null,
        IReadOnlyList<string>? cm = null,
        IReadOnlyList<string>? sec = null,
        IReadOnlyList<JobEnvVar>? env = null,
        IReadOnlyList<string>? pullSecrets = null,
        TimeSpan? timeout = null) =>
        new(
            Namespace: ns,
            JobName: jobName,
            ContainerName: container,
            Image: image,
            Args: args ?? ["echo", "hello"],
            EnvFromConfigMaps: cm ?? ["app-config"],
            EnvFromSecrets: sec ?? ["app-secret"],
            Env: env ?? [new JobEnvVar("FOO", "bar")],
            ImagePullSecrets: pullSecrets ?? ["regcred"],
            ServiceAccount: null,
            Labels: new Dictionary<string, string> { ["shipit/run-id"] = "99" },
            Timeout: timeout ?? TimeSpan.FromSeconds(10),
            BackoffLimit: 0,
            Ttl: TimeSpan.FromMinutes(10));

    private static V1Pod CreatePod(
        string ns,
        string podName,
        string jobName,
        string containerName,
        string phase = "Running",
        int? exitCode = null,
        string? waitingReason = null,
        string? waitingMessage = null,
        bool unschedulable = false)
    {
        var containerStatus = new V1ContainerStatus
        {
            Name = containerName,
            State = new V1ContainerState()
        };

        if (exitCode.HasValue)
        {
            containerStatus.State.Terminated = new V1ContainerStateTerminated
            {
                ExitCode = exitCode.Value
            };
        }
        else if (waitingReason != null)
        {
            containerStatus.State.Waiting = new V1ContainerStateWaiting
            {
                Reason = waitingReason,
                Message = waitingMessage
            };
        }
        else if (phase == "Running")
        {
            containerStatus.State.Running = new V1ContainerStateRunning();
        }

        var conditions = new List<V1PodCondition>();
        if (unschedulable)
        {
            conditions.Add(new V1PodCondition
            {
                Type = "PodScheduled",
                Status = "False",
                Reason = "Unschedulable",
                Message = "0/3 nodes are available: insufficient memory"
            });
        }

        return new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = podName,
                NamespaceProperty = ns,
                Labels = new Dictionary<string, string>
                {
                    ["job-name"] = jobName
                }
            },
            Status = new V1PodStatus
            {
                Phase = phase,
                Conditions = conditions,
                ContainerStatuses = new List<V1ContainerStatus> { containerStatus }
            }
        };
    }

    [Fact]
    public async Task OutsideAllowList_Namespace_RejectsWithoutClusterCall()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed namespace."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(ns: "forbidden-ns");
        var result = await runner.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("Namespace 'forbidden-ns' is not allowed", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OutsideAllowList_ConfigMap_RejectsWithoutClusterCall()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed configmap."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(cm: ["disallowed-cm"]);
        var result = await runner.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("ConfigMap 'disallowed-cm'", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OutsideAllowList_Secret_RejectsWithoutClusterCall()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed secret."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(sec: ["disallowed-sec"]);
        var result = await runner.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("Secret 'disallowed-sec'", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OutsideAllowList_EnvSecretKeyRef_RejectsWithoutClusterCall()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed secret ref."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(env: [new JobEnvVar("FOO", SecretName: "forbidden-sec", SecretKey: "pass")]);
        var result = await runner.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("Secret 'forbidden-sec'", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OutsideAllowList_PullSecret_RejectsWithoutClusterCall()
    {
        var handler = new MockHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be made for disallowed pull secret."));
        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());
        var log = new TestLogSink();

        var req = CreateRequest(pullSecrets: ["disallowed-pull"]);
        var result = await runner.RunAsync(req, log);

        Assert.False(result.Success);
        Assert.Contains("Image pull secret 'disallowed-pull'", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Success_ExitCode0_StreamsLogsInOrder_AndReturnsSuccess()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        var pod = CreatePod("devops", "test-job-1-pod1", "test-job-1", "runner", phase: "Succeeded", exitCode: 0);
        var podList = new V1PodList { Items = new List<V1Pod> { pod } };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            // 1. Create Job
            if (req.Method == HttpMethod.Post && path == "/apis/batch/v1/namespaces/devops/jobs")
            {
                return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            }

            // 2. List Pods by label
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods")
            {
                return MockHttpMessageHandler.JsonResponse(podList);
            }

            // 3. Read Pod
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1")
            {
                return MockHttpMessageHandler.JsonResponse(pod);
            }

            // 4. Read Pod Log
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1/log")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("log line 1\nlog line 2\n", Encoding.UTF8, "text/plain")
                };
            }

            // 5. Read Job (if polled)
            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1")
            {
                return MockHttpMessageHandler.JsonResponse(job);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var result = await runner.RunAsync(CreateRequest(), log);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Job completed successfully", result.Message);

        // Verify logs streamed in order
        var stdoutLines = log.Messages.Where(m => m.Stream == LogStream.Stdout).Select(m => m.Line).ToList();
        Assert.Equal(2, stdoutLines.Count);
        Assert.Equal("log line 1", stdoutLines[0]);
        Assert.Equal("log line 2", stdoutLines[1]);
    }

    [Fact]
    public async Task ExitCodeNonZero_ReturnsFailedWithMessage()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        var pod = CreatePod("devops", "test-job-1-pod1", "test-job-1", "runner", phase: "Failed", exitCode: 2);
        var podList = new V1PodList { Items = new List<V1Pod> { pod } };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Post && path == "/apis/batch/v1/namespaces/devops/jobs")
            {
                return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            }
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods")
            {
                return MockHttpMessageHandler.JsonResponse(podList);
            }
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1")
            {
                return MockHttpMessageHandler.JsonResponse(pod);
            }
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1/log")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("error occurred\n", Encoding.UTF8, "text/plain")
                };
            }
            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1")
            {
                return MockHttpMessageHandler.JsonResponse(job);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var result = await runner.RunAsync(CreateRequest(), log);

        Assert.False(result.Success);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("job exited with code 2", result.Message);
    }

    [Fact]
    public async Task Timeout_JobDeadlineExceeded_ReturnsTimedOut124()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        job.Status = new V1JobStatus
        {
            Conditions = new List<V1JobCondition>
            {
                new()
                {
                    Type = "Failed",
                    Status = "True",
                    Reason = "DeadlineExceeded",
                    Message = "Job was active longer than specified deadline"
                }
            }
        };

        // Pod is not found (or deleted due to deadline)
        var podList = new V1PodList { Items = new List<V1Pod>() };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Post && path == "/apis/batch/v1/namespaces/devops/jobs")
            {
                return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            }
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods")
            {
                return MockHttpMessageHandler.JsonResponse(podList);
            }
            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1")
            {
                return MockHttpMessageHandler.JsonResponse(job);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var result = await runner.RunAsync(CreateRequest(), log);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Equal(124, result.ExitCode);
        Assert.Contains("DeadlineExceeded", result.Message);
    }

    [Fact]
    public async Task Cancellation_SendsDeleteWithForeground_AndThrowsOperationCanceledException()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        using var cts = new CancellationTokenSource();

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Post && path == "/apis/batch/v1/namespaces/devops/jobs")
            {
                // Cancel right after Job creation
                cts.Cancel();
                return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            }

            if (req.Method == HttpMethod.Delete && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1")
            {
                return MockHttpMessageHandler.JsonResponse(new V1Status { Status = "Success" });
            }

            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods")
            {
                // If it reaches list pods before cancellation triggers
                cts.Cancel();
                return MockHttpMessageHandler.JsonResponse(new V1PodList { Items = new List<V1Pod>() });
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await runner.RunAsync(CreateRequest(), log, cts.Token);
        });

        // Verify DELETE was issued with Foreground propagation policy
        var deleteReq = handler.Requests.FirstOrDefault(r => r.Method == HttpMethod.Delete);
        Assert.NotNull(deleteReq);
        Assert.Equal("/apis/batch/v1/namespaces/devops/jobs/test-job-1", deleteReq.RequestUri.AbsolutePath);
        Assert.Contains("propagationPolicy=Foreground", deleteReq.RequestUri.Query);
    }

    [Fact]
    public async Task DeleteByRunAsync_SendsCorrectLabelSelector_AndDeletesForeground()
    {
        var job1 = new V1Job { Metadata = new V1ObjectMeta { Name = "job-run-42-1" } };
        var job2 = new V1Job { Metadata = new V1ObjectMeta { Name = "job-run-42-2" } };
        var jobList = new V1JobList { Items = new List<V1Job> { job1, job2 } };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs")
            {
                return MockHttpMessageHandler.JsonResponse(jobList);
            }

            if (req.Method == HttpMethod.Delete)
            {
                return MockHttpMessageHandler.JsonResponse(new V1Status { Status = "Success" });
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList());

        await runner.DeleteByRunAsync("devops", 42);

        // Verify GET had labelSelector
        var getReq = handler.Requests.First(r => r.Method == HttpMethod.Get);
        Assert.Contains("shipit/run-id=42", Uri.UnescapeDataString(getReq.RequestUri.Query));

        // Verify DELETE sent for both jobs with Foreground
        var deleteReqs = handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        Assert.Equal(2, deleteReqs.Count);
        Assert.Equal("/apis/batch/v1/namespaces/devops/jobs/job-run-42-1", deleteReqs[0].RequestUri.AbsolutePath);
        Assert.Contains("propagationPolicy=Foreground", deleteReqs[0].RequestUri.Query);
        Assert.Equal("/apis/batch/v1/namespaces/devops/jobs/job-run-42-2", deleteReqs[1].RequestUri.AbsolutePath);
        Assert.Contains("propagationPolicy=Foreground", deleteReqs[1].RequestUri.Query);
    }

    [Fact]
    public async Task PodUnschedulable_ReturnsFailedWithShortReason()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        var pod = CreatePod("devops", "test-job-1-pod1", "test-job-1", "runner", phase: "Pending", unschedulable: true);
        var podList = new V1PodList { Items = new List<V1Pod> { pod } };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post) return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods") return MockHttpMessageHandler.JsonResponse(podList);
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1") return MockHttpMessageHandler.JsonResponse(pod);
            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1") return MockHttpMessageHandler.JsonResponse(job);
            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var result = await runner.RunAsync(CreateRequest(), log);

        Assert.False(result.Success);
        Assert.Contains("pod scheduling failed", result.Message);
        Assert.Contains("insufficient memory", result.Message);
    }

    [Fact]
    public async Task ImagePullBackOff_ReturnsFailedWithShortReason()
    {
        var job = JobSpecBuilder.Build(CreateRequest());
        var pod = CreatePod(
            "devops",
            "test-job-1-pod1",
            "test-job-1",
            "runner",
            phase: "Pending",
            waitingReason: "ImagePullBackOff",
            waitingMessage: "Back-off pulling image 'busybox:latest'");
        var podList = new V1PodList { Items = new List<V1Pod> { pod } };

        var handler = new MockHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post) return MockHttpMessageHandler.JsonResponse(job, HttpStatusCode.Created);
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods") return MockHttpMessageHandler.JsonResponse(podList);
            if (req.Method == HttpMethod.Get && path == "/api/v1/namespaces/devops/pods/test-job-1-pod1") return MockHttpMessageHandler.JsonResponse(pod);
            if (req.Method == HttpMethod.Get && path == "/apis/batch/v1/namespaces/devops/jobs/test-job-1") return MockHttpMessageHandler.JsonResponse(job);
            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RequestUri}");
        });

        var client = MockHttpMessageHandler.CreateClient(handler);
        var runner = new KubernetesJobRunner(client, CreateDefaultAllowList(), pollInterval: TimeSpan.Zero);
        var log = new TestLogSink();

        var result = await runner.RunAsync(CreateRequest(), log);

        Assert.False(result.Success);
        Assert.Contains("ImagePullBackOff", result.Message);
    }
}
