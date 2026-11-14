namespace Shipit.Data.Entities;

public enum CredentialKind
{
    SshKey,
    GitToken,
    Registry,
}

public enum RunStatus
{
    Queued,
    Running,
    Success,
    Failed,
    Cancelled,
}

public enum StepName
{
    Checkout,
    Build,
    Push,
    Deploy,
    RunJob,
}

public enum StepStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Skipped,
    Cancelled,
}

public enum JobAllowKind
{
    Namespace,
    ConfigMap,
    Secret,
    PullSecret,
}
