using System.Text.RegularExpressions;
using Shipit.Core;

namespace Shipit.Ssh;

/// <summary>
/// Safe builder for remote shell commands executed on build slaves.
/// All user-supplied arguments are validated against strict allowlists and POSIX quoted.
/// Secrets are passed via <see cref="RemoteCommand.StdinSecret"/> and never appear in argv or command text.
/// </summary>
public static class RemoteCommandBuilder
{
    private static readonly Regex BranchRegex =
        new(@"^[A-Za-z0-9._/-]+$", RegexOptions.Compiled);

    private static readonly Regex RegistryHostRegex =
        new(@"^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*(:[0-9]{1,5})?$", RegexOptions.Compiled);

    private static readonly Regex UsernameRegex =
        new(@"^[A-Za-z0-9_@.][A-Za-z0-9._@-]{0,127}$", RegexOptions.Compiled);

    private static readonly Regex TagRegex =
        new(@"^[A-Za-z0-9_.-]{1,128}$", RegexOptions.Compiled);

    private static readonly Regex ImageRepoRegex =
        new(@"^[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*$", RegexOptions.Compiled);

    private static readonly Regex FullImageRegex =
        new(@"^(?:(?<host>(?:[A-Za-z0-9-]+\.)+[A-Za-z0-9-]+(?::[0-9]{1,5})?|localhost(?::[0-9]{1,5})?|[A-Za-z0-9.-]+:[0-9]{1,5})/)?(?<repo>[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*):(?<tag>[A-Za-z0-9_.-]{1,128})$", RegexOptions.Compiled);

    private static readonly Regex DockerfilePathRegex =
        new(@"^[A-Za-z0-9._/-]+$", RegexOptions.Compiled);

    private static readonly Regex BuildContextRegex =
        new(@"^[A-Za-z0-9._/-]+$", RegexOptions.Compiled);

    private static readonly Regex SafeWorkDirRegex =
        new(@"^/tmp/shipit/[A-Za-z0-9_][A-Za-z0-9._/-]*$", RegexOptions.Compiled);

    /// <summary>
    /// Encloses a string in POSIX single-quotes and safely escapes inner single quotes.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    /// <summary>
    /// Returns the absolute path on the slave for a given run ID.
    /// </summary>
    public static string WorkDirFor(long runId)
    {
        if (runId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runId), "Run ID must be greater than zero.");
        }
        return $"/tmp/shipit/run-{runId}";
    }

    /// <summary>
    /// Builds a command to clone a Git repository using an askpass script and credentials passed via stdin.
    /// The Git token is passed via <see cref="RemoteCommand.StdinSecret"/> and never appears in argv or .git/config.
    /// </summary>
    public static RemoteCommand Clone(string repoUrl, string branch, string workDir, string gitToken)
    {
        ValidateRepoUrl(repoUrl);
        ValidateBranch(branch);
        ValidateWorkDir(workDir);

        if (string.IsNullOrEmpty(gitToken))
        {
            throw new ArgumentException("Git token cannot be empty.", nameof(gitToken));
        }

        if (gitToken.Contains('\n') || gitToken.Contains('\r'))
        {
            throw new ArgumentException("Git token cannot contain newline characters.", nameof(gitToken));
        }

        // Script reads the token from stdin into SHIPIT_GIT_TOKEN, creates a temporary mode 700 askpass script,
        // exports GIT_ASKPASS and GIT_TERMINAL_PROMPT=0, runs git clone, cleans up the askpass script and token,
        // and returns the git clone exit status.
        var script = string.Join("\n",
            "IFS= read -r SHIPIT_GIT_TOKEN || true",
            "ASKPASS=$(mktemp 2>/dev/null || mktemp -t shipit.XXXXXX)",
            "chmod 700 \"$ASKPASS\"",
            "cat << 'EOF' > \"$ASKPASS\"",
            "#!/bin/sh",
            "case \"$1\" in",
            "  *Username*|*username*) echo 'oauth2' ;;",
            "  *) echo \"$SHIPIT_GIT_TOKEN\" ;;",
            "esac",
            "EOF",
            "export SHIPIT_GIT_TOKEN",
            "export GIT_ASKPASS=\"$ASKPASS\"",
            "export GIT_TERMINAL_PROMPT=0",
            $"git clone --depth 1 --branch {Quote(branch)} -- {Quote(repoUrl)} {Quote(workDir)}",
            "STATUS=$?",
            "rm -f \"$ASKPASS\"",
            "unset SHIPIT_GIT_TOKEN",
            "exit $STATUS");

        return new RemoteCommand(script, StdinSecret: gitToken);
    }

    /// <summary>
    /// Returns the short 7-character commit SHA of HEAD.
    /// </summary>
    public static RemoteCommand ShortSha(string workDir)
    {
        ValidateWorkDir(workDir);
        return new RemoteCommand($"git -C {Quote(workDir)} rev-parse --short=7 HEAD");
    }

    /// <summary>
    /// Authenticates with a Docker registry using credentials passed via stdin.
    /// </summary>
    public static RemoteCommand DockerLogin(string registryHost, string username, string password)
    {
        ValidateRegistryHost(registryHost);
        ValidateUsername(username);

        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be empty.", nameof(password));
        }

        if (password.Contains('\n') || password.Contains('\r'))
        {
            throw new ArgumentException("Password cannot contain newline characters.", nameof(password));
        }

        return new RemoteCommand(
            $"docker login {Quote(registryHost)} -u {Quote(username)} --password-stdin",
            StdinSecret: password);
    }

    /// <summary>
    /// Builds a Docker image.
    /// </summary>
    public static RemoteCommand DockerBuild(string workDir, string dockerfilePath, string image, string? buildContext = null)
    {
        ValidateWorkDir(workDir);
        ValidateDockerfilePath(dockerfilePath);
        ValidateImage(image);
        ValidateBuildContext(buildContext);

        var fullDockerfilePath = $"{workDir}/{dockerfilePath}";
        string contextDir;
        if (buildContext is null)
        {
            var lastSlash = dockerfilePath.LastIndexOf('/');
            contextDir = lastSlash > 0 ? $"{workDir}/{dockerfilePath[..lastSlash]}" : workDir;
        }
        else if (buildContext == ".")
        {
            contextDir = workDir;
        }
        else
        {
            contextDir = $"{workDir}/{buildContext}";
        }

        return new RemoteCommand($"docker build -f {Quote(fullDockerfilePath)} -t {Quote(image)} {Quote(contextDir)}");
    }

    /// <summary>
    /// Builds a Docker image (backward-compatible overload without buildContext).
    /// </summary>
    public static RemoteCommand DockerBuild(string workDir, string dockerfilePath, string image)
        => DockerBuild(workDir, dockerfilePath, image, null);

    /// <summary>
    /// Pushes a Docker image to the registry.
    /// </summary>
    public static RemoteCommand DockerPush(string image)
    {
        ValidateImage(image);
        return new RemoteCommand($"docker push {Quote(image)}");
    }

    /// <summary>
    /// Logs out from a Docker registry.
    /// </summary>
    public static RemoteCommand DockerLogout(string registryHost)
    {
        ValidateRegistryHost(registryHost);
        return new RemoteCommand($"docker logout {Quote(registryHost)}");
    }

    /// <summary>
    /// Removes the temporary working directory on the build slave.
    /// </summary>
    public static RemoteCommand Cleanup(string workDir)
    {
        ValidateWorkDir(workDir);
        return new RemoteCommand($"rm -rf -- {Quote(workDir)}");
    }

    /// <summary>
    /// Validates components and returns a canonical image reference: host/imageName:tag.
    /// </summary>
    public static string BuildImageRef(string registryHost, string imageName, string tag)
    {
        ValidateRegistryHost(registryHost);
        ValidateImageName(imageName);
        ValidateTag(tag);

        var imageRef = $"{registryHost}/{imageName}:{tag}";
        ValidateImage(imageRef);
        return imageRef;
    }

    /// <summary>
    /// Validates the optional build context relative path.
    /// </summary>
    public static void ValidateBuildContext(string? buildContext)
    {
        if (buildContext is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(buildContext) ||
            buildContext.Length > 200 ||
            buildContext.StartsWith('/') ||
            buildContext.StartsWith('-') ||
            buildContext.Contains("..") ||
            !BuildContextRegex.IsMatch(buildContext))
        {
            throw new ArgumentException($"Invalid build context: '{buildContext}'.", nameof(buildContext));
        }
    }

    private static void ValidateRepoUrl(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            throw new ArgumentException("Repo URL cannot be empty.", nameof(repoUrl));
        }

        if (!repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !repoUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Repo URL must start with https:// or http://", nameof(repoUrl));
        }

        if (!Uri.TryCreate(repoUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Repo URL is not a valid absolute URI.", nameof(repoUrl));
        }

        if (repoUrl.Contains("..") || repoUrl.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ';' or '&' or '|' or '`' or '$' or '\'' or '"' or '<' or '>'))
        {
            throw new ArgumentException("Repo URL contains forbidden characters.", nameof(repoUrl));
        }
    }

    private static void ValidateBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch) ||
            !BranchRegex.IsMatch(branch) ||
            branch.StartsWith("-") ||
            branch.Contains(".."))
        {
            throw new ArgumentException($"Invalid branch name: '{branch}'", nameof(branch));
        }
    }

    private static void ValidateWorkDir(string workDir)
    {
        if (string.IsNullOrWhiteSpace(workDir) ||
            !workDir.StartsWith("/tmp/shipit/", StringComparison.Ordinal) ||
            workDir.Length <= "/tmp/shipit/".Length ||
            workDir.Contains("..") ||
            !SafeWorkDirRegex.IsMatch(workDir) ||
            workDir.Split('/').Any(part => part.StartsWith("-") || part == ".." || part == "."))
        {
            throw new ArgumentException($"Invalid workDir: '{workDir}'. Must be a direct path under /tmp/shipit/.", nameof(workDir));
        }
    }

    /// <summary>
    /// Validates a registry host string (hostname with optional port).
    /// </summary>
    public static void ValidateRegistryHost(string registryHost)
    {
        if (string.IsNullOrWhiteSpace(registryHost) ||
            !RegistryHostRegex.IsMatch(registryHost) ||
            registryHost.StartsWith("-") ||
            registryHost.Contains(".."))
        {
            throw new ArgumentException($"Invalid registry host: '{registryHost}'", nameof(registryHost));
        }
    }

    private static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            !UsernameRegex.IsMatch(username) ||
            username.StartsWith("-") ||
            username.Contains(".."))
        {
            throw new ArgumentException($"Invalid username: '{username}'", nameof(username));
        }
    }

    private static void ValidateDockerfilePath(string dockerfilePath)
    {
        if (string.IsNullOrWhiteSpace(dockerfilePath) ||
            dockerfilePath.StartsWith("/") ||
            dockerfilePath.StartsWith("-") ||
            dockerfilePath.Contains("..") ||
            !DockerfilePathRegex.IsMatch(dockerfilePath))
        {
            throw new ArgumentException($"Invalid dockerfilePath: '{dockerfilePath}'. Must be a relative path.", nameof(dockerfilePath));
        }
    }

    private static void ValidateImageName(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName) ||
            imageName.StartsWith("-") ||
            imageName.StartsWith("/") ||
            imageName.Contains("..") ||
            !ImageRepoRegex.IsMatch(imageName))
        {
            throw new ArgumentException($"Invalid image name: '{imageName}'", nameof(imageName));
        }
    }

    private static void ValidateTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) ||
            tag.StartsWith("-") ||
            !TagRegex.IsMatch(tag))
        {
            throw new ArgumentException($"Invalid image tag: '{tag}'", nameof(tag));
        }
    }

    private static void ValidateImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image) ||
            image.StartsWith("-") ||
            !FullImageRegex.IsMatch(image))
        {
            throw new ArgumentException($"Invalid Docker image reference: '{image}'", nameof(image));
        }

        var match = FullImageRegex.Match(image);
        var tag = match.Groups["tag"].Value;
        if (tag.StartsWith("-"))
        {
            throw new ArgumentException($"Invalid Docker image tag starting with '-': '{image}'", nameof(image));
        }
    }
}
