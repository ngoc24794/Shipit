# Shipit Staging Jenkins Pipeline

Self-contained Declarative Pipeline for building and deploying Shipit to the **staging** Kubernetes cluster (`staging-admin@staging`, namespace `devops`).

## Creating the Jenkins Job

1. Create a new **Pipeline** job in Jenkins (e.g. `shipit-staging`).
2. Under **Pipeline → Definition**, select **Pipeline script**.
3. Paste the full contents of `deploy/jenkins/shipit-staging.groovy` and save.

## Required Plugins

- Pipeline (Declarative)
- Credentials Binding (`withCredentials`)
- Docker Pipeline (`docker.withRegistry`)
- Kubernetes CLI (`withKubeConfig`)
- Build Name and Description Setter (`buildName`, optional — wrapped in `safeBuildName`)
- Workspace Cleanup (`deleteDir`)

## Required Jenkins Credentials

| Credential ID | Kind | Description |
|---|---|---|
| `github-token` | Secret text | GitHub personal access token with read access to `ngoc24794/Shipit` |
| `private-docker-registry-creds` | Username with password | Credentials for Docker registry `192.168.1.241` |
| `k8s-local` | Secret file | Kubeconfig containing context `staging-admin@staging` |

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `GIT_BRANCH` | `main` | Branch to clone and build |
| `IMAGE_TAG` | `""` | Existing git short SHA tag to deploy directly, skipping clone/test/build |
| `APPLY_MANIFESTS` | `false` | Apply `deploy/k8s/base/*.yaml` (namespace, RBAC, deployment, service) before rollout |
| `RUN_TESTS` | `false` | Run web (`npm run typecheck && npm test`) and backend (`dotnet test`) tests if tools are on the agent |

## Manual Prerequisites (One-Time Setup)

1. **Namespace & secrets**: Run `scripts/create-secrets.ps1` to create `shipit-secrets` in namespace `devops`. The pipeline verifies the secret exists and never creates, reads, or prints its values.
2. **Registry pull secret**: Ensure `registry-secret` exists in namespace `devops` (referenced by `deploy/k8s/base/20-deployment.yaml`).
3. **First-time install**: Run the job once with `APPLY_MANIFESTS=true` to apply `00-namespace.yaml`, `10-rbac-devops.yaml`, `11-rbac-avina-workspace.yaml`, `20-deployment.yaml`, and `30-service.yaml`.

## Common Workflows

- **Regular deploy**: Leave `IMAGE_TAG` empty and `APPLY_MANIFESTS=false`; the pipeline clones `GIT_BRANCH`, builds `192.168.1.241/shipit:<short-sha>` with BuildKit, pushes it, runs `kubectl set image`, waits for rollout (auto-rolling back on failure), and smoke-checks `/healthz` and `/readyz`.
- **Redeploy / rollback**: Set `IMAGE_TAG` to a previously pushed short SHA (e.g. `1b256f5`); clone, tests, and docker build are skipped and the deployment is updated directly to that tag.

> **Database migrations**: Shipit applies EF Core migrations automatically on startup (`ShipitHost.PrepareDatabaseAsync()`), so no separate migration stage is needed.
