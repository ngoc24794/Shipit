using System.Text;
using Shipit.Core;

namespace Shipit.Ssh.Tests;

public class RemoteCommandBuilderTests
{
    private static readonly string[] MaliciousPayloads =
    [
        "; rm -rf /",
        "&& echo pwned",
        "| cat /etc/passwd",
        "`whoami`",
        "$(whoami)",
        "test\nexit 1",
        "test' OR '1'='1",
        "test\"hello",
        "has space",
        "-malicious-flag",
        "../traversal",
        "..",
    ];

    [Fact]
    public void WorkDirFor_ValidRunId_ReturnsExpectedPath()
    {
        var path = RemoteCommandBuilder.WorkDirFor(42);
        Assert.Equal("/tmp/shipit/run-42", path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void WorkDirFor_ZeroOrNegativeRunId_ThrowsArgumentOutOfRangeException(long runId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RemoteCommandBuilder.WorkDirFor(runId));
    }

    [Fact]
    public void Quote_SafelyWrapsAndEscapes()
    {
        Assert.Equal("''", RemoteCommandBuilder.Quote(""));
        Assert.Equal("'simple'", RemoteCommandBuilder.Quote("simple"));
        Assert.Equal("'it'\\''s complicated'", RemoteCommandBuilder.Quote("it's complicated"));
        Assert.Equal("'/path/with space'", RemoteCommandBuilder.Quote("/path/with space"));
    }

    [Fact]
    public void Clone_ValidParameters_BuildsSecureScriptWithoutSecretInCommandText()
    {
        var token = "ghp_SECRET_TOKEN_XYZ123";
        var cmd = RemoteCommandBuilder.Clone(
            "https://github.com/org/repo.git",
            "feature/my-branch",
            "/tmp/shipit/run-1",
            token);

        // Secret MUST NOT appear in CommandText
        Assert.DoesNotContain(token, cmd.CommandText);
        Assert.Equal(token, cmd.StdinSecret);

        // Must contain git clone with oauth2 and askpass setup
        Assert.Contains("git clone --depth 1 --branch 'feature/my-branch' -- 'https://github.com/org/repo.git' '/tmp/shipit/run-1'", cmd.CommandText);
        Assert.Contains("GIT_ASKPASS", cmd.CommandText);
        Assert.Contains("GIT_TERMINAL_PROMPT=0", cmd.CommandText);
        Assert.Contains("chmod 700", cmd.CommandText);
        Assert.Contains("oauth2", cmd.CommandText);
        Assert.Contains("rm -f \"$ASKPASS\"", cmd.CommandText);
        Assert.Contains("unset SHIPIT_GIT_TOKEN", cmd.CommandText);
    }

    [Theory]
    [InlineData("ftp://github.com/org/repo.git")]
    [InlineData("ssh://git@github.com/org/repo.git")]
    [InlineData("git@github.com:org/repo.git")]
    [InlineData("file:///etc/passwd")]
    public void Clone_InvalidUrlScheme_ThrowsArgumentException(string invalidUrl)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
            invalidUrl, "main", "/tmp/shipit/run-1", "token"));
    }

    [Fact]
    public void Clone_MaliciousUrlPayloads_ThrowArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            var ex = Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
                payload, "main", "/tmp/shipit/run-1", "secret-token"));
            Assert.DoesNotContain("secret-token", ex.Message);

            if (payload != "-malicious-flag")
            {
                var ex2 = Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
                    $"https://example.com/{payload}", "main", "/tmp/shipit/run-1", "secret-token"));
                Assert.DoesNotContain("secret-token", ex2.Message);
            }
        }
    }

    [Fact]
    public void Clone_MaliciousBranchPayloads_ThrowArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            var ex = Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
                "https://example.com/repo.git", payload, "/tmp/shipit/run-1", "secret-token"));
            Assert.DoesNotContain("secret-token", ex.Message);
        }
    }

    [Fact]
    public void Clone_MaliciousWorkDirPayloads_ThrowArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            var ex = Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
                "https://example.com/repo.git", "main", $"/tmp/shipit/{payload}", "secret-token"));
            Assert.DoesNotContain("secret-token", ex.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("token\nnewline")]
    [InlineData("token\rnewline")]
    public void Clone_InvalidToken_ThrowsArgumentExceptionWithoutLeakingToken(string? invalidToken)
    {
        var ex = Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Clone(
            "https://example.com/repo.git", "main", "/tmp/shipit/run-1", invalidToken!));
        if (!string.IsNullOrEmpty(invalidToken))
        {
            Assert.DoesNotContain(invalidToken, ex.Message);
        }
    }

    [Fact]
    public void ShortSha_ValidWorkDir_BuildsCorrectCommand()
    {
        var cmd = RemoteCommandBuilder.ShortSha("/tmp/shipit/run-99");
        Assert.Equal("git -C '/tmp/shipit/run-99' rev-parse --short=7 HEAD", cmd.CommandText);
        Assert.Null(cmd.StdinSecret);
    }

    [Fact]
    public void ShortSha_MaliciousWorkDir_ThrowsArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.ShortSha($"/tmp/shipit/{payload}"));
        }
    }

    [Fact]
    public void DockerLogin_ValidArguments_BuildsCorrectCommandWithStdinSecret()
    {
        var password = "SuperSecretPassword123!";
        var cmd = RemoteCommandBuilder.DockerLogin("192.168.1.241:5000", "devops_user", password);

        Assert.Equal("docker login '192.168.1.241:5000' -u 'devops_user' --password-stdin", cmd.CommandText);
        Assert.Equal(password, cmd.StdinSecret);
        Assert.DoesNotContain(password, cmd.CommandText);
    }

    [Fact]
    public void DockerLogin_MaliciousHost_ThrowsArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                RemoteCommandBuilder.DockerLogin(payload, "validuser", "secretpass"));
            Assert.DoesNotContain("secretpass", ex.Message);
        }
    }

    [Fact]
    public void DockerLogin_MaliciousUser_ThrowsArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                RemoteCommandBuilder.DockerLogin("registry.example.com", payload, "secretpass"));
            Assert.DoesNotContain("secretpass", ex.Message);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("pass\nword")]
    public void DockerLogin_InvalidPassword_ThrowsArgumentExceptionWithoutLeakingPassword(string? invalidPass)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RemoteCommandBuilder.DockerLogin("registry.example.com", "user", invalidPass!));
        if (!string.IsNullOrEmpty(invalidPass))
        {
            Assert.DoesNotContain(invalidPass, ex.Message);
        }
    }

    [Fact]
    public void DockerBuild_ValidArguments_BuildsCorrectCommand()
    {
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "src/Dockerfile",
            "192.168.1.241/shipit-demo:abc1234");

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/src/Dockerfile' -t '192.168.1.241/shipit-demo:abc1234' '/tmp/shipit/run-1/src'",
            cmd.CommandText);
        Assert.Null(cmd.StdinSecret);

        // Verify shell tokens are properly delimited
        var tokens = ParseShellTokens(cmd.CommandText);
        Assert.Equal("docker", tokens[0]);
        Assert.Equal("build", tokens[1]);
        Assert.Equal("-f", tokens[2]);
        Assert.Equal("/tmp/shipit/run-1/src/Dockerfile", tokens[3]);
        Assert.Equal("-t", tokens[4]);
        Assert.Equal("192.168.1.241/shipit-demo:abc1234", tokens[5]);
        Assert.Equal("/tmp/shipit/run-1/src", tokens[6]);
    }

    [Fact]
    public void DockerBuild_RootDockerfile_UsesTheRepositoryRootAsContext()
    {
        var cmd = RemoteCommandBuilder.DockerBuild("/tmp/shipit/run-1", "Dockerfile", "192.168.1.241/app:abc1234");

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/Dockerfile' -t '192.168.1.241/app:abc1234' '/tmp/shipit/run-1'",
            cmd.CommandText);
    }

    [Fact]
    public void DockerBuild_BuildContextNull_SubdirectoryDockerfile_UsesDockerfileDirectory()
    {
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: null);

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/docker/Dockerfile.x' -t '192.168.1.241/app:abc1234' '/tmp/shipit/run-1/docker'",
            cmd.CommandText);
    }

    [Fact]
    public void DockerBuild_BuildContextDot_UsesWorkDirAsContext()
    {
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: ".");

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/docker/Dockerfile.x' -t '192.168.1.241/app:abc1234' '/tmp/shipit/run-1'",
            cmd.CommandText);
    }

    [Fact]
    public void DockerBuild_BuildContextCustomRelativePath_AppendsToWorkDir()
    {
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: "src/app");

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/docker/Dockerfile.x' -t '192.168.1.241/app:abc1234' '/tmp/shipit/run-1/src/app'",
            cmd.CommandText);
    }

    [Fact]
    public void DockerBuild_RootDockerfile_NullBuildContext_UsesWorkDir()
    {
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "Dockerfile",
            "192.168.1.241/app:abc1234",
            buildContext: null);

        Assert.Equal(
            "docker build -f '/tmp/shipit/run-1/Dockerfile' -t '192.168.1.241/app:abc1234' '/tmp/shipit/run-1'",
            cmd.CommandText);
    }

    [Theory]
    [InlineData("/abs")]
    [InlineData("../x")]
    [InlineData("a/../b")]
    [InlineData("-x")]
    [InlineData("a b")]
    [InlineData("a;b")]
    [InlineData("$(x)")]
    [InlineData("")]
    [InlineData("   ")]
    public void DockerBuild_MaliciousBuildContext_ThrowsArgumentException(string badContext)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: badContext));
    }

    [Fact]
    public void DockerBuild_TooLongBuildContext_ThrowsArgumentException()
    {
        var tooLong = new string('a', 201);
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: tooLong));

        // 200 characters is allowed
        var exactly200 = new string('a', 200);
        var cmd = RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "docker/Dockerfile.x",
            "192.168.1.241/app:abc1234",
            buildContext: exactly200);
        Assert.Contains(exactly200, cmd.CommandText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(".")]
    [InlineData("src/app")]
    [InlineData("docker/build-context")]
    [InlineData("a.b_c-d")]
    public void ValidateBuildContext_ValidInputs_DoNotThrow(string? validContext)
    {
        RemoteCommandBuilder.ValidateBuildContext(validContext);
    }

    [Theory]
    [InlineData("/absolute/Dockerfile")]
    [InlineData("-f")]
    [InlineData("../Dockerfile")]
    [InlineData("foo/../../bar")]
    public void DockerBuild_InvalidDockerfilePath_ThrowsArgumentException(string badPath)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            badPath,
            "192.168.1.241/app:v1"));
    }

    [Fact]
    public void DockerBuild_MaliciousDockerfilePathPayloads_ThrowArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerBuild(
                "/tmp/shipit/run-1",
                payload,
                "192.168.1.241/app:v1"));
        }
    }

    [Theory]
    [InlineData("UPPERCASE/repo:tag")]
    [InlineData("192.168.1.241/UPPER:tag")]
    [InlineData("repo:-leadingdash")]
    [InlineData("repo:tag;rm -rf /")]
    [InlineData("repo")]
    public void DockerBuild_InvalidImage_ThrowsArgumentException(string badImage)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerBuild(
            "/tmp/shipit/run-1",
            "Dockerfile",
            badImage));
    }

    [Fact]
    public void DockerPush_ValidImage_BuildsCorrectCommand()
    {
        var cmd = RemoteCommandBuilder.DockerPush("192.168.1.241:5000/my-app:v1.2.3");
        Assert.Equal("docker push '192.168.1.241:5000/my-app:v1.2.3'", cmd.CommandText);
        Assert.Null(cmd.StdinSecret);
    }

    [Fact]
    public void DockerPush_MaliciousImage_ThrowsArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerPush(payload));
        }
    }

    [Fact]
    public void DockerLogout_ValidHost_BuildsCorrectCommand()
    {
        var cmd = RemoteCommandBuilder.DockerLogout("192.168.1.241:5000");
        Assert.Equal("docker logout '192.168.1.241:5000'", cmd.CommandText);
        Assert.Null(cmd.StdinSecret);
    }

    [Fact]
    public void DockerLogout_MaliciousHost_ThrowsArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.DockerLogout(payload));
        }
    }

    [Fact]
    public void Cleanup_ValidWorkDir_BuildsCorrectCommand()
    {
        var cmd = RemoteCommandBuilder.Cleanup("/tmp/shipit/run-123");
        Assert.Equal("rm -rf -- '/tmp/shipit/run-123'", cmd.CommandText);
        Assert.Null(cmd.StdinSecret);
    }

    [Theory]
    [InlineData("/tmp/shipit")]
    [InlineData("/tmp/shipit/")]
    [InlineData("/tmp/other/run-1")]
    [InlineData("/tmp/shipit/../etc")]
    [InlineData("/etc/passwd")]
    public void Cleanup_DangerousWorkDir_ThrowsArgumentException(string badDir)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Cleanup(badDir));
    }

    [Fact]
    public void Cleanup_MaliciousWorkDirPayloads_ThrowArgumentException()
    {
        foreach (var payload in MaliciousPayloads)
        {
            Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.Cleanup($"/tmp/shipit/{payload}"));
        }
    }

    [Fact]
    public void BuildImageRef_ValidComponents_CombinesAndValidates()
    {
        var imageRef = RemoteCommandBuilder.BuildImageRef("192.168.1.241", "shipit-demo", "abc1234");
        Assert.Equal("192.168.1.241/shipit-demo:abc1234", imageRef);

        var complexRef = RemoteCommandBuilder.BuildImageRef("registry.local:5000", "my-org/sub-repo", "v1.0.0-rc.1");
        Assert.Equal("registry.local:5000/my-org/sub-repo:v1.0.0-rc.1", complexRef);
    }

    [Theory]
    [InlineData("192.168.1.241", "Invalid_Uppercase", "v1")]
    [InlineData("192.168.1.241", "validname", "-leading-dash-tag")]
    [InlineData("bad host", "validname", "v1")]
    [InlineData("host", "-leading-dash-repo", "v1")]
    public void BuildImageRef_InvalidComponents_ThrowsArgumentException(string host, string image, string tag)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommandBuilder.BuildImageRef(host, image, tag));
    }

    private static List<string> ParseShellTokens(string commandLine)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inSingleQuote = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '\'')
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (char.IsWhiteSpace(c) && !inSingleQuote)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            tokens.Add(sb.ToString());
        }

        return tokens;
    }
}
