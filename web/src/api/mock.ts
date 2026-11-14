import type { ShipitApi } from './client'
import type {
  LoginRequest,
  Me,
  Readiness,
  Slave,
  SlaveInput,
  ConnectionTestResult,
  Credential,
  CredentialInput,
  CredentialUpdate,
  Pipeline,
  PipelineInput,
  DeployTarget,
  DeployTargetInput,
  Run,
  RunDetail,
  RunTask,
  LogLine,
  RunStatus,
  Job,
  JobInput,
  JobValidation,
  JobPreview,
  JobPreviewTask,
  JobAllowEntry,
  JobAllowEntryInput
} from './types'
import { ApiError } from './client'

function delay(ms: number = 20): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function normalizeBuildContext(value?: string | null): string | null {
  if (value === undefined || value === null) return null
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

function validateBuildContext(val: string): string | null {
  if (val.length > 200) {
    return 'Thư mục context build tối đa 200 ký tự'
  }
  if (val.startsWith('/') || val.startsWith('-')) {
    return 'Thư mục context build không được bắt đầu bằng / hoặc -'
  }
  if (val.includes('..')) {
    return 'Thư mục context build không được chứa ..'
  }
  if (!/^[A-Za-z0-9._/-]+$/.test(val)) {
    return 'Thư mục context build chỉ chứa chữ cái, số và ký tự . _ / -'
  }
  return null
}

function normalizeDeployRegistryHost(value?: string | null): string | null {
  if (value === undefined || value === null) return null
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

function validateDeployRegistryHost(val: string): string | null {
  if (/^https?:\/\//i.test(val)) {
    return 'Host registry khi deploy không được chứa http:// hoặc https://'
  }
  if (/\s/.test(val)) {
    return 'Host registry khi deploy không được chứa khoảng trắng'
  }
  if (!/^[a-zA-Z0-9.-]+(?::\d+)?$/.test(val)) {
    return 'Host registry khi deploy chỉ chứa chữ cái, số, dấu chấm, dấu gạch ngang và cổng tuỳ chọn'
  }
  return null
}

export class MockShipitApi implements ShipitApi {
  private loggedInUser: Me | null = { username: 'admin' }

  private slaves: Slave[] = [
    {
      id: 1,
      name: 'slave-staging-01',
      host: '192.168.1.49',
      port: 22,
      user: 'devops',
      sshCredentialId: 1
    },
    {
      id: 2,
      name: 'slave-backup',
      host: '192.168.1.50',
      port: 22,
      user: 'deployer',
      sshCredentialId: 1
    }
  ]

  private deployTargets: DeployTarget[] = [
    {
      id: 1,
      namespace: 'devops',
      deployment: 'shipit-demo',
      createdAt: '2026-11-09T08:00:00Z'
    },
    {
      id: 2,
      namespace: 'avina-workspace',
      deployment: 'avina-catalog',
      createdAt: '2026-11-09T08:00:00Z'
    }
  ]

  // IMPORTANT: Credentials strictly do NOT store or expose secret values in mock or production.
  private credentials: Credential[] = [
    {
      id: 1,
      name: 'devops-ssh-key',
      kind: 'ssh_key',
      hasValue: true,
      updatedAt: '2026-11-09T08:00:00Z'
    },
    {
      id: 2,
      name: 'gitlab-token',
      kind: 'git_token',
      hasValue: true,
      updatedAt: '2026-11-09T08:15:00Z'
    },
    {
      id: 3,
      name: 'registry-secret',
      kind: 'registry',
      username: 'devops',
      hasValue: true,
      updatedAt: '2026-11-09T08:30:00Z'
    }
  ]

  private runs: RunDetail[] = [
    {
      id: 1,
      kind: 'pipeline',
      pipelineId: 1,
      jobId: null,
      status: 'success',
      tag: 'a1b2c3d',
      image: '192.168.1.241/shipit-demo:a1b2c3d',
      failureReason: null,
      createdAt: '2026-11-09T07:30:00Z',
      startedAt: '2026-11-09T07:30:05Z',
      finishedAt: '2026-11-09T07:32:15Z',
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T07:30:05Z', finishedAt: '2026-11-09T07:30:15Z' },
        { name: 'build', status: 'success', startedAt: '2026-11-09T07:30:15Z', finishedAt: '2026-11-09T07:31:30Z' },
        { name: 'push', status: 'success', startedAt: '2026-11-09T07:31:30Z', finishedAt: '2026-11-09T07:31:55Z' },
        { name: 'deploy', status: 'success', startedAt: '2026-11-09T07:31:55Z', finishedAt: '2026-11-09T07:32:15Z' }
      ],
      tasks: []
    },
    {
      id: 2,
      kind: 'pipeline',
      pipelineId: 2,
      jobId: null,
      status: 'failed',
      tag: 'e4f5a6b',
      image: null,
      failureReason: 'Build step failed: compilation error in Dockerfile layer 4',
      createdAt: '2026-11-09T08:00:00Z',
      startedAt: '2026-11-09T08:00:05Z',
      finishedAt: '2026-11-09T08:01:20Z',
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T08:00:05Z', finishedAt: '2026-11-09T08:00:20Z' },
        { name: 'build', status: 'failed', startedAt: '2026-11-09T08:00:20Z', finishedAt: '2026-11-09T08:01:20Z' },
        { name: 'push', status: 'skipped', startedAt: null, finishedAt: null },
        { name: 'deploy', status: 'skipped', startedAt: null, finishedAt: null }
      ],
      tasks: []
    },
    {
      id: 3,
      kind: 'pipeline',
      pipelineId: 1,
      jobId: null,
      status: 'running',
      tag: '7c8d9e0',
      image: null,
      failureReason: null,
      createdAt: '2026-11-09T08:45:00Z',
      startedAt: '2026-11-09T08:45:03Z',
      finishedAt: null,
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T08:45:03Z', finishedAt: '2026-11-09T08:45:15Z' },
        { name: 'build', status: 'running', startedAt: '2026-11-09T08:45:15Z', finishedAt: null },
        { name: 'push', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'deploy', status: 'pending', startedAt: null, finishedAt: null }
      ],
      tasks: []
    },
    {
      id: 4,
      kind: 'pipeline',
      pipelineId: 2,
      jobId: null,
      status: 'queued',
      tag: null,
      image: null,
      failureReason: null,
      createdAt: '2026-11-09T08:50:00Z',
      startedAt: null,
      finishedAt: null,
      steps: [
        { name: 'checkout', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'build', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'push', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'deploy', status: 'pending', startedAt: null, finishedAt: null }
      ],
      tasks: []
    },
    {
      id: 5,
      kind: 'job',
      jobId: 1,
      pipelineId: null,
      status: 'failed',
      tag: 'c3d4e5f',
      image: '192.168.1.241/avina-dbmigrator:c3d4e5f',
      failureReason: 'Tác vụ 2 thất bại (exit code 1)',
      createdAt: '2026-11-09T09:00:00Z',
      startedAt: '2026-11-09T09:00:05Z',
      finishedAt: '2026-11-09T09:02:10Z',
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T09:00:05Z', finishedAt: '2026-11-09T09:00:15Z' },
        { name: 'build', status: 'success', startedAt: '2026-11-09T09:00:15Z', finishedAt: '2026-11-09T09:01:00Z' },
        { name: 'push', status: 'success', startedAt: '2026-11-09T09:01:00Z', finishedAt: '2026-11-09T09:01:20Z' },
        { name: 'runjob', status: 'failed', startedAt: '2026-11-09T09:01:20Z', finishedAt: '2026-11-09T09:02:10Z' }
      ],
      tasks: [
        {
          position: 0,
          label: 'Tạo schema database',
          status: 'success',
          exitCode: 0,
          startedAt: '2026-11-09T09:01:20Z',
          finishedAt: '2026-11-09T09:01:40Z'
        },
        {
          position: 1,
          label: 'Chạy migration dữ liệu',
          status: 'failed',
          exitCode: 1,
          startedAt: '2026-11-09T09:01:40Z',
          finishedAt: '2026-11-09T09:02:10Z'
        },
        {
          position: 2,
          label: 'Khởi tạo dữ liệu mẫu',
          status: 'skipped',
          exitCode: null,
          startedAt: null,
          finishedAt: null
        }
      ]
    }
  ]

  private pipelines: Pipeline[] = [
    {
      id: 1,
      name: 'shipit-demo-pipeline',
      gitUrl: 'https://git.example.com/devops/shipit-demo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      buildContext: null,
      imageName: 'shipit-demo',
      registryHost: '192.168.1.241',
      deployRegistryHost: null,
      registryCredentialId: 3,
      deployNamespace: 'devops',
      deployDeployment: 'shipit-demo',
      deployContainer: 'app',
      lastRun: {
        id: 3,
        pipelineId: 1,
        status: 'running',
        tag: '7c8d9e0',
        image: null,
        failureReason: null,
        createdAt: '2026-11-09T08:45:00Z',
        startedAt: '2026-11-09T08:45:03Z',
        finishedAt: null
      }
    },
    {
      id: 2,
      name: 'avina-catalog-pipeline',
      gitUrl: 'https://git.example.com/avina/avina-catalog.git',
      gitBranch: 'master',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'Dockerfile',
      buildContext: null,
      imageName: 'avina-catalog',
      registryHost: '192.168.1.241',
      deployRegistryHost: null,
      registryCredentialId: 3,
      deployNamespace: 'avina-workspace',
      deployDeployment: 'avina-catalog',
      deployContainer: 'app',
      lastRun: {
        id: 2,
        pipelineId: 2,
        status: 'failed',
        tag: 'e4f5a6b',
        image: null,
        failureReason: 'Build step failed',
        createdAt: '2026-11-09T08:00:00Z',
        startedAt: '2026-11-09T08:00:05Z',
        finishedAt: '2026-11-09T08:01:20Z'
      }
    }
  ]

  private jobAllowEntries: JobAllowEntry[] = [
    {
      id: 1,
      namespace: 'avina-workspace',
      kind: 'namespace',
      name: 'avina-workspace',
      createdAt: '2026-11-14T08:00:00Z'
    },
    {
      id: 2,
      namespace: 'avina-workspace',
      kind: 'configmap',
      name: 'avina-files-config',
      createdAt: '2026-11-14T08:00:00Z'
    },
    {
      id: 3,
      namespace: 'avina-workspace',
      kind: 'configmap',
      name: 'avina-users-config',
      createdAt: '2026-11-14T08:00:00Z'
    },
    {
      id: 4,
      namespace: 'avina-workspace',
      kind: 'secret',
      name: 'avina-files-secret',
      createdAt: '2026-11-14T08:00:00Z'
    },
    {
      id: 5,
      namespace: 'avina-workspace',
      kind: 'secret',
      name: 'avina-users-secret',
      createdAt: '2026-11-14T08:00:00Z'
    },
    {
      id: 6,
      namespace: 'avina-workspace',
      kind: 'pullsecret',
      name: 'registry-secret',
      createdAt: '2026-11-14T08:00:00Z'
    }
  ]

  private jobs: Job[] = [
    {
      id: 1,
      name: 'avina-dbmigrator',
      gitUrl: 'http://git.example/AvinaWorkspace',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      dockerfilePath: 'docker/Dockerfile.dbmigrator',
      buildContext: '.',
      imageName: 'avina-dbmigrator',
      registryHost: '192.168.1.241',
      deployRegistryHost: null,
      registryCredentialId: null,
      namespace: 'avina-workspace',
      containerName: 'dbmigrator',
      serviceAccount: null,
      imagePullSecrets: ['registry-secret'],
      tasks: [
        {
          position: 1,
          label: 'files',
          args: ['migrate', '--module', 'files'],
          envFromConfigMaps: ['avina-files-config'],
          envFromSecrets: ['avina-files-secret'],
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }
          ],
          timeoutSeconds: 1800,
          backoffLimit: 0,
          ttlSeconds: 3600
        },
        {
          position: 2,
          label: 'users',
          args: ['migrate', '--module', 'users'],
          envFromConfigMaps: ['avina-users-config'],
          envFromSecrets: ['avina-users-secret'],
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }
          ],
          timeoutSeconds: 1800,
          backoffLimit: 0,
          ttlSeconds: 3600
        }
      ],
      lastRun: null
    }
  ]

  private sampleLogs: Record<number, LogLine[]> = {
    1: [
      { seq: 1, step: 'checkout', stream: 'system', text: 'Bắt đầu bước checkout mã nguồn...', at: '2026-11-09T07:30:05Z' },
      { seq: 2, step: 'checkout', stream: 'stdout', text: 'Cloning into \'shipit-demo\'...', at: '2026-11-09T07:30:08Z' },
      { seq: 3, step: 'checkout', stream: 'stdout', text: 'HEAD is now at a1b2c3d feat: initial commit', at: '2026-11-09T07:30:14Z' },
      { seq: 4, step: 'checkout', stream: 'system', text: 'Checkout hoàn tất thành công.', at: '2026-11-09T07:30:15Z' },
      { seq: 5, step: 'build', stream: 'system', text: 'Bắt đầu build Docker image...', at: '2026-11-09T07:30:15Z' },
      { seq: 6, step: 'build', stream: 'stdout', text: 'Step 1/6 : FROM mcr.microsoft.com/dotnet/aspnet:10.0', at: '2026-11-09T07:30:20Z' },
      { seq: 7, step: 'build', stream: 'stdout', text: ' ---> 5e2390f14d8a', at: '2026-11-09T07:30:35Z' },
      { seq: 8, step: 'build', stream: 'stdout', text: 'Step 2/6 : WORKDIR /app', at: '2026-11-09T07:30:40Z' },
      { seq: 9, step: 'build', stream: 'stdout', text: 'Step 6/6 : ENTRYPOINT ["dotnet", "ShipitDemo.dll"]', at: '2026-11-09T07:31:25Z' },
      { seq: 10, step: 'build', stream: 'stdout', text: 'Successfully built 5e2390f14d8a', at: '2026-11-09T07:31:29Z' },
      { seq: 11, step: 'build', stream: 'system', text: 'Build image 192.168.1.241/shipit-demo:a1b2c3d thành công.', at: '2026-11-09T07:31:30Z' },
      { seq: 12, step: 'push', stream: 'system', text: 'Đăng nhập registry 192.168.1.241...', at: '2026-11-09T07:31:30Z' },
      { seq: 13, step: 'push', stream: 'stdout', text: 'Login Succeeded', at: '2026-11-09T07:31:32Z' },
      { seq: 14, step: 'push', stream: 'stdout', text: 'Pushing 192.168.1.241/shipit-demo:a1b2c3d...', at: '2026-11-09T07:31:40Z' },
      { seq: 15, step: 'push', stream: 'stdout', text: 'a1b2c3d: Pushed', at: '2026-11-09T07:31:54Z' },
      { seq: 16, step: 'push', stream: 'system', text: 'Đẩy image lên registry thành công.', at: '2026-11-09T07:31:55Z' },
      { seq: 17, step: 'deploy', stream: 'system', text: 'Cập nhật image deployment/shipit-demo namespace devops...', at: '2026-11-09T07:31:55Z' },
      { seq: 18, step: 'deploy', stream: 'stdout', text: 'deployment.apps/shipit-demo image updated', at: '2026-11-09T07:31:58Z' },
      { seq: 19, step: 'deploy', stream: 'stdout', text: 'Waiting for rollout to finish: 0 of 1 updated replicas are available...', at: '2026-11-09T07:32:05Z' },
      { seq: 20, step: 'deploy', stream: 'stdout', text: 'deployment "shipit-demo" successfully rolled out', at: '2026-11-09T07:32:14Z' },
      { seq: 21, step: 'deploy', stream: 'system', text: 'Lượt chạy hoàn tất thành công.', at: '2026-11-09T07:32:15Z' }
    ],
    5: [
      { seq: 1, step: 'checkout', stream: 'system', text: 'Bắt đầu bước checkout mã nguồn...', at: '2026-11-09T09:00:05Z', taskPosition: null },
      { seq: 2, step: 'checkout', stream: 'stdout', text: 'Cloning into \'avina-dbmigrator\'...', at: '2026-11-09T09:00:08Z', taskPosition: null },
      { seq: 3, step: 'checkout', stream: 'stdout', text: 'HEAD is now at c3d4e5f feat: add migration scripts', at: '2026-11-09T09:00:14Z', taskPosition: null },
      { seq: 4, step: 'checkout', stream: 'system', text: 'Checkout hoàn tất thành công.', at: '2026-11-09T09:00:15Z', taskPosition: null },
      { seq: 5, step: 'build', stream: 'system', text: 'Bắt đầu build Docker image...', at: '2026-11-09T09:00:15Z', taskPosition: null },
      { seq: 6, step: 'build', stream: 'stdout', text: 'Successfully built 7f8a9b0c', at: '2026-11-09T09:00:59Z', taskPosition: null },
      { seq: 7, step: 'build', stream: 'system', text: 'Build image 192.168.1.241/avina-dbmigrator:c3d4e5f thành công.', at: '2026-11-09T09:01:00Z', taskPosition: null },
      { seq: 8, step: 'push', stream: 'system', text: 'Đăng nhập và đẩy image lên registry...', at: '2026-11-09T09:01:00Z', taskPosition: null },
      { seq: 9, step: 'push', stream: 'stdout', text: 'c3d4e5f: Pushed', at: '2026-11-09T09:01:19Z', taskPosition: null },
      { seq: 10, step: 'push', stream: 'system', text: 'Đẩy image lên registry thành công.', at: '2026-11-09T09:01:20Z', taskPosition: null },
      { seq: 11, step: 'runjob', stream: 'system', text: 'Khởi tạo cụm và chuẩn bị chạy tác vụ Job...', at: '2026-11-09T09:01:20Z', taskPosition: null },
      { seq: 12, step: 'runjob', stream: 'system', text: 'Bắt đầu tác vụ 1: Tạo schema database', at: '2026-11-09T09:01:20Z', taskPosition: 0 },
      { seq: 13, step: 'runjob', stream: 'stdout', text: 'Applying schema migrations...', at: '2026-11-09T09:01:25Z', taskPosition: 0 },
      { seq: 14, step: 'runjob', stream: 'stdout', text: 'Schema migrations applied successfully (exit code 0)', at: '2026-11-09T09:01:39Z', taskPosition: 0 },
      { seq: 15, step: 'runjob', stream: 'system', text: 'Tác vụ 1 hoàn tất thành công.', at: '2026-11-09T09:01:40Z', taskPosition: 0 },
      { seq: 16, step: 'runjob', stream: 'system', text: 'Bắt đầu tác vụ 2: Chạy migration dữ liệu', at: '2026-11-09T09:01:40Z', taskPosition: 1 },
      { seq: 17, step: 'runjob', stream: 'stdout', text: 'Running data migration 002_user_tenants...', at: '2026-11-09T09:01:50Z', taskPosition: 1 },
      { seq: 18, step: 'runjob', stream: 'stderr', text: 'Error: column "tenant_id" does not exist in table "organizations" (exit code 1)', at: '2026-11-09T09:02:08Z', taskPosition: 1 },
      { seq: 19, step: 'runjob', stream: 'system', text: 'Tác vụ 2 thất bại với exit code 1', at: '2026-11-09T09:02:10Z', taskPosition: 1 }
    ]
  }

  private checkAuth(): void {
    if (!this.loggedInUser) {
      throw new ApiError(401, 'Chưa đăng nhập hoặc phiên làm việc đã hết hạn')
    }
  }

  async login(req: LoginRequest): Promise<void> {
    await delay()
    if (req.username === 'admin' && req.password === 'admin123') {
      this.loggedInUser = { username: 'admin' }
      return
    }
    throw new ApiError(401, 'Sai tên đăng nhập hoặc mật khẩu')
  }

  async logout(): Promise<void> {
    await delay()
    this.loggedInUser = null
  }

  async getMe(): Promise<Me> {
    await delay()
    this.checkAuth()
    return this.loggedInUser!
  }

  async getHealth(): Promise<string> {
    await delay()
    return 'ok'
  }

  async getReadiness(): Promise<Readiness> {
    await delay()
    return {
      ready: true,
      database: 'ok',
      pendingMigrations: 0
    }
  }

  async getSlaves(): Promise<Slave[]> {
    await delay()
    this.checkAuth()
    return [...this.slaves]
  }

  async createSlave(input: SlaveInput): Promise<Slave> {
    await delay()
    this.checkAuth()
    const newSlave: Slave = {
      id: Date.now(),
      name: input.name,
      host: input.host,
      port: input.port ?? 22,
      user: input.user,
      sshCredentialId: input.sshCredentialId
    }
    this.slaves.push(newSlave)
    return newSlave
  }

  async getSlave(id: number): Promise<Slave> {
    await delay()
    this.checkAuth()
    const slave = this.slaves.find((s) => s.id === id)
    if (!slave) throw new ApiError(404, 'Không tìm thấy máy build')
    return slave
  }

  async updateSlave(id: number, input: SlaveInput): Promise<Slave> {
    await delay()
    this.checkAuth()
    const idx = this.slaves.findIndex((s) => s.id === id)
    if (idx === -1) throw new ApiError(404, 'Không tìm thấy máy build')
    const updated: Slave = {
      ...this.slaves[idx],
      name: input.name,
      host: input.host,
      port: input.port ?? 22,
      user: input.user,
      sshCredentialId: input.sshCredentialId
    }
    this.slaves[idx] = updated
    return updated
  }

  async deleteSlave(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const inUse = this.pipelines.some((p) => p.slaveId === id)
    if (inUse) {
      throw new ApiError(409, 'Không thể xoá máy build đang được pipeline sử dụng')
    }
    this.slaves = this.slaves.filter((s) => s.id !== id)
  }

  async testSlaveConnection(id: number): Promise<ConnectionTestResult> {
    await delay(100)
    this.checkAuth()
    const slave = this.slaves.find((s) => s.id === id)
    if (!slave) throw new ApiError(404, 'Không tìm thấy máy build')
    return {
      ok: true,
      message: `Kết nối SSH tới ${slave.user}@${slave.host}:${slave.port ?? 22} thành công!`
    }
  }

  async getCredentials(): Promise<Credential[]> {
    await delay()
    this.checkAuth()
    return [...this.credentials]
  }

  async createCredential(input: CredentialInput): Promise<Credential> {
    await delay()
    this.checkAuth()
    const newCred: Credential = {
      id: Date.now(),
      name: input.name,
      kind: input.kind,
      username: input.username ?? null,
      hasValue: true,
      updatedAt: new Date().toISOString()
    }
    this.credentials.push(newCred)
    return newCred
  }

  async updateCredential(id: number, input: CredentialUpdate): Promise<Credential> {
    await delay()
    this.checkAuth()
    const idx = this.credentials.findIndex((c) => c.id === id)
    if (idx === -1) throw new ApiError(404, 'Không tìm thấy thông tin xác thực')
    const current = this.credentials[idx]
    const updated: Credential = {
      ...current,
      name: input.name ?? current.name,
      username: input.username !== undefined ? input.username : current.username,
      hasValue: true,
      updatedAt: new Date().toISOString()
    }
    this.credentials[idx] = updated
    return updated
  }

  async deleteCredential(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const inUseSlave = this.slaves.some((s) => s.sshCredentialId === id)
    const inUsePipeline = this.pipelines.some(
      (p) => p.gitCredentialId === id || p.registryCredentialId === id
    )
    if (inUseSlave || inUsePipeline) {
      throw new ApiError(409, 'Không thể xoá thông tin xác thực đang được sử dụng')
    }
    this.credentials = this.credentials.filter((c) => c.id !== id)
  }

  async getPipelines(): Promise<Pipeline[]> {
    await delay()
    this.checkAuth()
    return [...this.pipelines]
  }

  async createPipeline(input: PipelineInput): Promise<Pipeline> {
    await delay()
    this.checkAuth()

    const registryCredentialId = input.registryCredentialId ?? null
    if (registryCredentialId !== null) {
      const cred = this.credentials.find((c) => c.id === registryCredentialId)
      if (!cred) {
        throw new ApiError(400, 'Thông tin xác thực Registry không tồn tại', {
          title: 'Validation failed',
          errors: {
            registryCredentialId: ['Thông tin xác thực Registry không tồn tại']
          }
        })
      }
    }

    const buildContext = normalizeBuildContext(input.buildContext)
    if (buildContext !== null) {
      const err = validateBuildContext(buildContext)
      if (err) {
        throw new ApiError(400, err, {
          title: 'Validation failed',
          errors: {
            buildContext: [err]
          }
        })
      }
    }

    const deployRegistryHost = normalizeDeployRegistryHost(input.deployRegistryHost)
    if (deployRegistryHost !== null) {
      const err = validateDeployRegistryHost(deployRegistryHost)
      if (err) {
        throw new ApiError(400, err, {
          title: 'Validation failed',
          errors: {
            deployRegistryHost: [err]
          }
        })
      }
    }

    const newPipeline: Pipeline = {
      id: Date.now(),
      name: input.name,
      gitUrl: input.gitUrl,
      gitBranch: input.gitBranch || 'main',
      gitCredentialId: input.gitCredentialId,
      slaveId: input.slaveId,
      dockerfilePath: input.dockerfilePath || 'Dockerfile',
      buildContext,
      imageName: input.imageName,
      registryHost: input.registryHost,
      deployRegistryHost,
      registryCredentialId,
      deployNamespace: input.deployNamespace,
      deployDeployment: input.deployDeployment,
      deployContainer: input.deployContainer,
      lastRun: null
    }
    this.pipelines.push(newPipeline)
    return newPipeline
  }

  async getPipeline(id: number): Promise<Pipeline> {
    await delay()
    this.checkAuth()
    const pipeline = this.pipelines.find((p) => p.id === id)
    if (!pipeline) throw new ApiError(404, 'Không tìm thấy pipeline')
    return pipeline
  }

  async updatePipeline(id: number, input: PipelineInput): Promise<Pipeline> {
    await delay()
    this.checkAuth()
    const idx = this.pipelines.findIndex((p) => p.id === id)
    if (idx === -1) throw new ApiError(404, 'Không tìm thấy pipeline')

    const registryCredentialId = input.registryCredentialId ?? null
    if (registryCredentialId !== null) {
      const cred = this.credentials.find((c) => c.id === registryCredentialId)
      if (!cred) {
        throw new ApiError(400, 'Thông tin xác thực Registry không tồn tại', {
          title: 'Validation failed',
          errors: {
            registryCredentialId: ['Thông tin xác thực Registry không tồn tại']
          }
        })
      }
    }

    const current = this.pipelines[idx]
    const buildContext =
      input.buildContext !== undefined
        ? normalizeBuildContext(input.buildContext)
        : current.buildContext

    if (buildContext !== null) {
      const err = validateBuildContext(buildContext)
      if (err) {
        throw new ApiError(400, err, {
          title: 'Validation failed',
          errors: {
            buildContext: [err]
          }
        })
      }
    }

    const deployRegistryHost =
      input.deployRegistryHost !== undefined
        ? normalizeDeployRegistryHost(input.deployRegistryHost)
        : current.deployRegistryHost

    if (deployRegistryHost !== null) {
      const err = validateDeployRegistryHost(deployRegistryHost)
      if (err) {
        throw new ApiError(400, err, {
          title: 'Validation failed',
          errors: {
            deployRegistryHost: [err]
          }
        })
      }
    }

    const updated: Pipeline = {
      ...current,
      name: input.name,
      gitUrl: input.gitUrl,
      gitBranch: input.gitBranch || 'main',
      gitCredentialId: input.gitCredentialId,
      slaveId: input.slaveId,
      dockerfilePath: input.dockerfilePath || 'Dockerfile',
      buildContext,
      imageName: input.imageName,
      registryHost: input.registryHost,
      deployRegistryHost,
      registryCredentialId,
      deployNamespace: input.deployNamespace,
      deployDeployment: input.deployDeployment,
      deployContainer: input.deployContainer
    }
    this.pipelines[idx] = updated
    return updated
  }

  async deletePipeline(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const activeRun = this.runs.find(
      (r) => r.pipelineId === id && (r.status === 'running' || r.status === 'queued')
    )
    if (activeRun) {
      throw new ApiError(409, 'Không thể xoá pipeline khi đang có lượt chạy hoạt động')
    }
    this.pipelines = this.pipelines.filter((p) => p.id !== id)
  }

  async getDeployTargets(): Promise<DeployTarget[]> {
    await delay()
    this.checkAuth()
    return [...this.deployTargets].sort((a, b) => {
      const cmp = a.namespace.localeCompare(b.namespace)
      return cmp !== 0 ? cmp : a.deployment.localeCompare(b.deployment)
    })
  }

  async createDeployTarget(input: DeployTargetInput): Promise<DeployTarget> {
    await delay()
    this.checkAuth()
    const ns = (input.namespace || '').trim()
    const deployment = (input.deployment || '').trim()

    const errors: Record<string, string[]> = {}
    if (!ns || ns.length > 63 || !/^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/.test(ns)) {
      errors.namespace = ['Namespace phải tuân theo chuẩn DNS-1123 label (1-63 ký tự)']
    }
    if (!deployment || deployment.length > 253 || !/^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$/.test(deployment)) {
      errors.deployment = ['Deployment phải tuân theo chuẩn DNS-1123 subdomain (1-253 ký tự)']
    }

    if (Object.keys(errors).length > 0) {
      throw new ApiError(400, 'Dữ liệu không hợp lệ', {
        title: 'Validation failed',
        errors
      })
    }

    if (this.deployTargets.some((t) => t.namespace === ns && t.deployment === deployment)) {
      throw new ApiError(409, `Deployment '${ns}/${deployment}' đã tồn tại`, {
        title: 'Deploy target already exists',
        detail: `Deploy target '${ns}/${deployment}' already exists.`
      })
    }

    const newTarget: DeployTarget = {
      id: this.deployTargets.length > 0 ? Math.max(...this.deployTargets.map((t) => t.id)) + 1 : 1,
      namespace: ns,
      deployment,
      createdAt: new Date().toISOString()
    }
    this.deployTargets.push(newTarget)
    return newTarget
  }

  async deleteDeployTarget(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const target = this.deployTargets.find((t) => t.id === id)
    if (!target) throw new ApiError(404, 'Không tìm thấy deployment được phép')

    const usingPipelines = this.pipelines
      .filter((p) => p.deployNamespace === target.namespace && p.deployDeployment === target.deployment)
      .map((p) => p.name)

    if (usingPipelines.length > 0) {
      const names = usingPipelines.map((n) => `'${n}'`).join(', ')
      throw new ApiError(409, `Không thể xoá vì đang được dùng bởi pipeline: ${names}`, {
        title: 'Deploy target is in use',
        detail: `Cannot delete deploy target '${target.namespace}/${target.deployment}' because it is in use by pipeline(s): ${names}.`
      })
    }

    this.deployTargets = this.deployTargets.filter((t) => t.id !== id)
  }

  async triggerRun(pipelineId: number): Promise<Run> {
    await delay()
    this.checkAuth()
    const pipeline = this.pipelines.find((p) => p.id === pipelineId)
    if (!pipeline) throw new ApiError(404, 'Không tìm thấy pipeline')

    const activeRun = this.runs.find(
      (r) => r.pipelineId === pipelineId && (r.status === 'running' || r.status === 'queued')
    )
    if (activeRun) {
      throw new ApiError(409, 'Pipeline này đang có một lượt chạy chưa hoàn tất')
    }

    const newRun: RunDetail = {
      id: Date.now(),
      kind: 'pipeline',
      pipelineId,
      jobId: null,
      status: 'queued',
      tag: null,
      image: null,
      failureReason: null,
      createdAt: new Date().toISOString(),
      startedAt: null,
      finishedAt: null,
      steps: [
        { name: 'checkout', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'build', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'push', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'deploy', status: 'pending', startedAt: null, finishedAt: null }
      ],
      tasks: []
    }
    this.runs.unshift(newRun)
    pipeline.lastRun = newRun
    return newRun
  }

  async runJob(jobId: number): Promise<Run> {
    return this.triggerJobRun(jobId)
  }

  async cancelRun(id: number): Promise<Run> {
    await delay()
    this.checkAuth()
    const run = this.runs.find((r) => r.id === id)
    if (!run) {
      throw new ApiError(404, 'Không tìm thấy lượt chạy')
    }

    if (run.status === 'success' || run.status === 'failed') {
      throw new ApiError(409, 'Run đã kết thúc')
    }

    if (run.status === 'cancelled') {
      return { ...run }
    }

    const now = new Date().toISOString()

    if (run.status === 'queued') {
      run.status = 'cancelled'
      run.failureReason = 'cancelled by user'
      run.finishedAt = now
      for (const step of run.steps) {
        if (step.status === 'running') {
          step.status = 'cancelled'
          step.finishedAt = now
        } else if (step.status === 'pending') {
          step.status = 'skipped'
        }
      }
      if (run.tasks) {
        for (const task of run.tasks) {
          if (task.status === 'running') {
            task.status = 'cancelled'
            task.finishedAt = now
          } else if (task.status === 'pending') {
            task.status = 'skipped'
          }
        }
      }
      const pipeline = this.pipelines.find((p) => p.id === run.pipelineId)
      if (pipeline && pipeline.lastRun?.id === run.id) {
        pipeline.lastRun = { ...pipeline.lastRun, ...run }
      }
      return { ...run }
    }

    if (run.status === 'running') {
      setTimeout(() => {
        if (run.status === 'running') {
          const stopTime = new Date().toISOString()
          run.status = 'cancelled'
          run.failureReason = 'cancelled by user'
          run.finishedAt = stopTime
          for (const step of run.steps) {
            if (step.status === 'running') {
              step.status = 'cancelled'
              step.finishedAt = stopTime
            } else if (step.status === 'pending') {
              step.status = 'skipped'
            }
          }
          if (run.tasks) {
            for (const task of run.tasks) {
              if (task.status === 'running') {
                task.status = 'cancelled'
                task.finishedAt = stopTime
              } else if (task.status === 'pending') {
                task.status = 'skipped'
              }
            }
          }
          const pipeline = this.pipelines.find((p) => p.id === run.pipelineId)
          if (pipeline && pipeline.lastRun?.id === run.id) {
            pipeline.lastRun = { ...pipeline.lastRun, ...run }
          }
        }
      }, 50)
      return { ...run }
    }

    return { ...run }
  }

  async getRuns(params?: { pipelineId?: number; jobId?: number; limit?: number }): Promise<Run[]> {
    await delay()
    this.checkAuth()
    let list = [...this.runs]
    if (params?.pipelineId !== undefined) {
      list = list.filter((r) => r.pipelineId === params.pipelineId)
    }
    if (params?.jobId !== undefined) {
      list = list.filter((r) => r.jobId === params.jobId)
    }
    list.sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    if (params?.limit) {
      list = list.slice(0, params.limit)
    }
    return list
  }

  async getRun(id: number): Promise<RunDetail> {
    await delay()
    this.checkAuth()
    const run = this.runs.find((r) => r.id === id)
    if (!run) throw new ApiError(404, 'Không tìm thấy lượt chạy')
    return run
  }

  async getRunLogs(id: number, after: number = 0): Promise<LogLine[]> {
    await delay()
    this.checkAuth()
    const logs = this.sampleLogs[id] || this.sampleLogs[1] || []
    return logs.filter((l) => l.seq > after)
  }

  streamRunLogs(
    runId: number,
    after: number,
    onLine: (line: LogLine) => void,
    onEnd: (status: RunStatus) => void,
    _onError?: (err: Error) => void
  ): () => void {
    const logs = this.sampleLogs[runId] || this.sampleLogs[1] || []
    const pendingLines = logs.filter((l) => l.seq > after)
    let cancelled = false
    let currentIndex = 0
    let intervalId: any = null

    intervalId = setInterval(() => {
      if (cancelled) {
        clearInterval(intervalId)
        return
      }

      const run = this.runs.find((r) => r.id === runId)
      if (run && (run.status === 'cancelled' || run.status === 'failed')) {
        clearInterval(intervalId)
        onEnd(run.status)
        return
      }

      if (currentIndex < pendingLines.length) {
        onLine(pendingLines[currentIndex])
        currentIndex++
      } else {
        clearInterval(intervalId)
        onEnd(run?.status || 'success')
      }
    }, 40)

    return () => {
      cancelled = true
      if (intervalId) clearInterval(intervalId)
    }
  }

  // --- Jobs ---

  async getJobs(): Promise<Job[]> {
    await delay()
    this.checkAuth()
    return JSON.parse(JSON.stringify(this.jobs))
  }

  async getJob(id: number): Promise<Job> {
    await delay()
    this.checkAuth()
    const job = this.jobs.find((j) => j.id === id)
    if (!job) throw new ApiError(404, 'Không tìm thấy Job')
    return JSON.parse(JSON.stringify(job))
  }

  async validateJob(input: JobInput): Promise<JobValidation> {
    await delay()
    this.checkAuth()
    const errors: Record<string, string[]> = {}

    const name = (input.name || '').trim()
    if (!name) {
      errors.name = ['Vui lòng nhập tên Job']
    } else if (name.length > 253 || !/^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$/.test(name)) {
      errors.name = ['Tên Job phải tuân theo chuẩn DNS-1123 subdomain (1-253 ký tự, chữ thường, số, dấu gạch ngang hoặc chấm)']
    }

    if (!input.gitUrl || !input.gitUrl.trim()) {
      errors.gitUrl = ['Vui lòng nhập địa chỉ Git URL']
    }

    if (!input.gitCredentialId) {
      errors.gitCredentialId = ['Vui lòng chọn thông tin xác thực Git']
    } else {
      const cred = this.credentials.find((c) => c.id === input.gitCredentialId && c.kind === 'git_token')
      if (!cred) {
        errors.gitCredentialId = ['Thông tin xác thực Git không hợp lệ']
      }
    }

    if (!input.slaveId) {
      errors.slaveId = ['Vui lòng chọn máy build']
    } else {
      const slave = this.slaves.find((s) => s.id === input.slaveId)
      if (!slave) {
        errors.slaveId = ['Máy build không tồn tại']
      }
    }

    if (!input.imageName || !input.imageName.trim()) {
      errors.imageName = ['Vui lòng nhập tên Image']
    }

    if (!input.registryHost || !input.registryHost.trim()) {
      errors.registryHost = ['Vui lòng nhập Host registry']
    }

    const ns = (input.namespace || '').trim()
    if (!ns) {
      errors.namespace = ['Vui lòng chọn Namespace']
    } else {
      const allowedNs = this.jobAllowEntries.some((e) => e.kind === 'namespace' && e.name === ns)
      if (!allowedNs) {
        errors.namespace = [`Namespace '${ns}' không nằm trong danh sách được phép`]
      }
    }

    if (!input.containerName || !input.containerName.trim()) {
      errors.containerName = ['Vui lòng nhập tên container']
    }

    if (input.buildContext) {
      const buildContextErr = validateBuildContext(input.buildContext)
      if (buildContextErr) {
        errors.buildContext = [buildContextErr]
      }
    }

    if (input.deployRegistryHost) {
      const deployRegErr = validateDeployRegistryHost(input.deployRegistryHost)
      if (deployRegErr) {
        errors.deployRegistryHost = [deployRegErr]
      }
    }

    if (input.imagePullSecrets && input.imagePullSecrets.length > 0) {
      for (const secret of input.imagePullSecrets) {
        const allowed = this.jobAllowEntries.some(
          (e) => e.kind === 'pullsecret' && e.namespace === ns && e.name === secret
        )
        if (!allowed) {
          errors.imagePullSecrets = [`ImagePullSecret '${secret}' không nằm trong allowlist của namespace '${ns}'`]
        }
      }
    }

    if (!input.tasks || input.tasks.length === 0) {
      errors.tasks = ['Job phải có ít nhất một tác vụ']
    } else {
      input.tasks.forEach((t, i) => {
        if (!t.label || !t.label.trim()) {
          errors[`tasks[${i}].label`] = ['Vui lòng nhập nhãn tác vụ']
        }
        if (t.timeoutSeconds === undefined || t.timeoutSeconds === null || Number(t.timeoutSeconds) <= 0) {
          errors[`tasks[${i}].timeoutSeconds`] = ['Thời gian timeout bắt buộc và phải lớn hơn 0']
        }
        if (t.envFromConfigMaps && t.envFromConfigMaps.length > 0) {
          for (const cm of t.envFromConfigMaps) {
            const allowed = this.jobAllowEntries.some(
              (e) => e.kind === 'configmap' && e.namespace === ns && e.name === cm
            )
            if (!allowed) {
              errors[`tasks[${i}].envFromConfigMaps`] = [`ConfigMap '${cm}' không nằm trong allowlist của namespace '${ns}'`]
            }
          }
        }
        if (t.envFromSecrets && t.envFromSecrets.length > 0) {
          for (const sec of t.envFromSecrets) {
            const allowed = this.jobAllowEntries.some(
              (e) => e.kind === 'secret' && e.namespace === ns && e.name === sec
            )
            if (!allowed) {
              errors[`tasks[${i}].envFromSecrets`] = [`Secret '${sec}' không nằm trong allowlist của namespace '${ns}'`]
            }
          }
        }
        if (t.env && t.env.length > 0) {
          t.env.forEach((env, envIdx) => {
            if (!env.name || !env.name.trim()) {
              errors[`tasks[${i}].env[${envIdx}].name`] = ['Tên biến môi trường không được để trống']
            }
            if (env.secretName) {
              const allowed = this.jobAllowEntries.some(
                (e) => e.kind === 'secret' && e.namespace === ns && e.name === env.secretName
              )
              if (!allowed) {
                errors[`tasks[${i}].env[${envIdx}].secretName`] = [
                  `Secret '${env.secretName}' không nằm trong allowlist của namespace '${ns}'`
                ]
              }
              if (!env.secretKey || !env.secretKey.trim()) {
                errors[`tasks[${i}].env[${envIdx}].secretKey`] = ['Secret key không được để trống khi lấy từ Secret']
              }
            }
          })
        }
      })
    }

    return {
      valid: Object.keys(errors).length === 0,
      errors
    }
  }

  async previewJob(input: JobInput): Promise<JobPreview> {
    await delay()
    this.checkAuth()
    const validation = await this.validateJob(input)
    const registry = input.deployRegistryHost || input.registryHost
    const tasks: JobPreviewTask[] = (input.tasks || []).map((t, idx) => {
      const position = idx + 1
      const lines = [
        'apiVersion: batch/v1',
        'kind: Job',
        'metadata:',
        `  name: ${input.name || 'job'}-${t.label || `task-${position}`}`,
        `  namespace: ${input.namespace || 'default'}`,
        '  labels:',
        `    shipit/job-name: ${input.name || 'job'}`,
        `    shipit/task: ${t.label || `task-${position}`}`,
        'spec:',
        `  backoffLimit: ${t.backoffLimit ?? 0}`,
        `  activeDeadlineSeconds: ${t.timeoutSeconds ?? 1800}`,
        `  ttlSecondsAfterFinished: ${t.ttlSeconds ?? 3600}`,
        '  template:',
        '    metadata:',
        '      labels:',
        `        shipit/job-name: ${input.name || 'job'}`,
        `        shipit/task: ${t.label || `task-${position}`}`,
        '    spec:',
        '      restartPolicy: Never',
        '      securityContext:',
        '        runAsNonRoot: true',
        '        runAsUser: 1654',
        ...(input.serviceAccount ? [`      serviceAccountName: ${input.serviceAccount}`] : []),
        ...(input.imagePullSecrets && input.imagePullSecrets.length > 0
          ? [
              '      imagePullSecrets:',
              ...input.imagePullSecrets.map((s) => `        - name: ${s}`)
            ]
          : []),
        '      containers:',
        `        - name: ${input.containerName || 'container'}`,
        `          image: ${registry ? `${registry}/` : ''}${input.imageName || 'image'}:<tag>`,
        ...(t.args && t.args.length > 0
          ? [
              '          args:',
              ...t.args.map((a) => `            - ${JSON.stringify(a)}`)
            ]
          : []),
        ...((t.envFromConfigMaps && t.envFromConfigMaps.length > 0) || (t.envFromSecrets && t.envFromSecrets.length > 0)
          ? [
              '          envFrom:',
              ...(t.envFromConfigMaps || []).map((cm) => `            - configMapRef:\n                name: ${cm}`),
              ...(t.envFromSecrets || []).map((sec) => `            - secretRef:\n                name: ${sec}`)
            ]
          : []),
        ...(t.env && t.env.length > 0
          ? [
              '          env:',
              ...t.env.map((e) => {
                if (e.secretName && e.secretKey) {
                  return `            - name: ${e.name}\n              valueFrom:\n                secretKeyRef:\n                  name: ${e.secretName}\n                  key: ${e.secretKey}`
                }
                return `            - name: ${e.name}\n              value: ${JSON.stringify(e.value ?? '')}`
              })
            ]
          : [])
      ]
      return {
        position,
        label: t.label || `task-${position}`,
        yaml: lines.join('\n')
      }
    })
    return {
      errors: validation.errors,
      tasks
    }
  }

  async createJob(input: JobInput): Promise<Job> {
    await delay()
    this.checkAuth()

    const validation = await this.validateJob(input)
    if (!validation.valid) {
      throw new ApiError(400, 'Dữ liệu Job không hợp lệ', {
        title: 'Validation failed',
        errors: validation.errors
      })
    }

    if (this.jobs.some((j) => j.name === input.name)) {
      throw new ApiError(409, `Job '${input.name}' đã tồn tại`, {
        title: 'Job already exists',
        detail: `Job '${input.name}' already exists.`,
        errors: { name: ['Tên Job đã tồn tại'] }
      })
    }

    const newJob: Job = {
      id: this.jobs.length > 0 ? Math.max(...this.jobs.map((j) => j.id)) + 1 : 1,
      name: input.name,
      gitUrl: input.gitUrl,
      gitBranch: input.gitBranch || 'main',
      gitCredentialId: input.gitCredentialId,
      slaveId: input.slaveId,
      dockerfilePath: input.dockerfilePath || 'Dockerfile',
      buildContext: normalizeBuildContext(input.buildContext),
      imageName: input.imageName,
      registryHost: input.registryHost,
      deployRegistryHost: normalizeDeployRegistryHost(input.deployRegistryHost),
      registryCredentialId: input.registryCredentialId ?? null,
      namespace: input.namespace,
      containerName: input.containerName,
      serviceAccount: input.serviceAccount || null,
      imagePullSecrets: input.imagePullSecrets || [],
      tasks: (input.tasks || []).map((t, idx) => ({
        position: idx + 1,
        label: t.label,
        args: t.args || [],
        envFromConfigMaps: t.envFromConfigMaps || [],
        envFromSecrets: t.envFromSecrets || [],
        env: t.env || [],
        timeoutSeconds: t.timeoutSeconds ?? 1800,
        backoffLimit: t.backoffLimit ?? 0,
        ttlSeconds: t.ttlSeconds ?? 3600
      })),
      lastRun: null
    }

    this.jobs.push(newJob)
    return JSON.parse(JSON.stringify(newJob))
  }

  async updateJob(id: number, input: JobInput): Promise<Job> {
    await delay()
    this.checkAuth()
    const idx = this.jobs.findIndex((j) => j.id === id)
    if (idx === -1) throw new ApiError(404, 'Không tìm thấy Job')

    const validation = await this.validateJob(input)
    if (!validation.valid) {
      throw new ApiError(400, 'Dữ liệu Job không hợp lệ', {
        title: 'Validation failed',
        errors: validation.errors
      })
    }

    if (this.jobs.some((j) => j.id !== id && j.name === input.name)) {
      throw new ApiError(409, `Job '${input.name}' đã tồn tại`, {
        title: 'Job already exists',
        detail: `Job '${input.name}' already exists.`,
        errors: { name: ['Tên Job đã tồn tại'] }
      })
    }

    const current = this.jobs[idx]
    const updated: Job = {
      ...current,
      name: input.name,
      gitUrl: input.gitUrl,
      gitBranch: input.gitBranch || 'main',
      gitCredentialId: input.gitCredentialId,
      slaveId: input.slaveId,
      dockerfilePath: input.dockerfilePath || 'Dockerfile',
      buildContext: normalizeBuildContext(input.buildContext),
      imageName: input.imageName,
      registryHost: input.registryHost,
      deployRegistryHost: normalizeDeployRegistryHost(input.deployRegistryHost),
      registryCredentialId: input.registryCredentialId ?? null,
      namespace: input.namespace,
      containerName: input.containerName,
      serviceAccount: input.serviceAccount || null,
      imagePullSecrets: input.imagePullSecrets || [],
      tasks: (input.tasks || []).map((t, taskIdx) => ({
        position: taskIdx + 1,
        label: t.label,
        args: t.args || [],
        envFromConfigMaps: t.envFromConfigMaps || [],
        envFromSecrets: t.envFromSecrets || [],
        env: t.env || [],
        timeoutSeconds: t.timeoutSeconds ?? 1800,
        backoffLimit: t.backoffLimit ?? 0,
        ttlSeconds: t.ttlSeconds ?? 3600
      }))
    }

    this.jobs[idx] = updated
    return JSON.parse(JSON.stringify(updated))
  }

  async deleteJob(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const job = this.jobs.find((j) => j.id === id)
    if (!job) throw new ApiError(404, 'Không tìm thấy Job')

    const activeRun = this.runs.find(
      (r) => r.jobId === id && (r.status === 'running' || r.status === 'queued')
    )
    if (activeRun) {
      throw new ApiError(409, 'Không thể xoá Job khi đang có lượt chạy hoạt động', {
        title: 'Job has active run',
        detail: 'Cannot delete job while it has an active run.'
      })
    }

    this.jobs = this.jobs.filter((j) => j.id !== id)
  }

  async triggerJobRun(id: number): Promise<Run> {
    await delay()
    this.checkAuth()
    const job = this.jobs.find((j) => j.id === id)
    if (!job) throw new ApiError(404, 'Không tìm thấy Job')

    const activeRun = this.runs.find(
      (r) => r.jobId === id && (r.status === 'running' || r.status === 'queued')
    )
    if (activeRun) {
      throw new ApiError(409, 'Job đang có lượt chạy hoạt động', {
        title: 'Job already has active run',
        detail: 'Job already has an active run.'
      })
    }

    const nextId = this.runs.length > 0 ? Math.max(...this.runs.map((r) => r.id)) + 1 : 1
    const tasks: RunTask[] =
      job.tasks && job.tasks.length > 0
        ? job.tasks.map((t, idx) => ({
            position: t.position ?? idx,
            label: t.label,
            status: 'pending' as const,
            exitCode: null,
            startedAt: null,
            finishedAt: null
          }))
        : [
            {
              position: 0,
              label: 'Khởi tạo',
              status: 'pending' as const,
              exitCode: null,
              startedAt: null,
              finishedAt: null
            }
          ]

    const newRun: RunDetail = {
      id: nextId,
      pipelineId: null,
      jobId: id,
      kind: 'job',
      status: 'queued',
      tag: null,
      image: null,
      failureReason: null,
      createdAt: new Date().toISOString(),
      startedAt: null,
      finishedAt: null,
      steps: [
        { name: 'checkout', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'build', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'push', status: 'pending', startedAt: null, finishedAt: null },
        { name: 'runjob', status: 'pending', startedAt: null, finishedAt: null }
      ],
      tasks
    }

    this.runs.unshift(newRun)
    job.lastRun = {
      id: newRun.id,
      pipelineId: null,
      jobId: id,
      kind: 'job',
      status: 'queued',
      tag: null,
      image: null,
      failureReason: null,
      createdAt: newRun.createdAt,
      startedAt: null,
      finishedAt: null
    }

    return JSON.parse(JSON.stringify(newRun))
  }

  // --- Job Allow Entries ---

  async getJobAllowEntries(): Promise<JobAllowEntry[]> {
    await delay()
    this.checkAuth()
    return [...this.jobAllowEntries].sort((a, b) => {
      const cmpNs = a.namespace.localeCompare(b.namespace)
      if (cmpNs !== 0) return cmpNs
      const cmpKind = a.kind.localeCompare(b.kind)
      if (cmpKind !== 0) return cmpKind
      return a.name.localeCompare(b.name)
    })
  }

  async createJobAllowEntry(input: JobAllowEntryInput): Promise<JobAllowEntry> {
    await delay()
    this.checkAuth()
    const ns = (input.namespace || '').trim()
    const kind = input.kind
    const name = (input.name || '').trim()

    const errors: Record<string, string[]> = {}
    if (!ns) {
      errors.namespace = ['Vui lòng nhập Namespace']
    } else if (ns.length > 63 || !/^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/.test(ns)) {
      errors.namespace = ['Namespace phải tuân theo chuẩn DNS-1123 label (1-63 ký tự)']
    }

    if (!kind) {
      errors.kind = ['Vui lòng chọn loại cho phép']
    }

    if (!name) {
      errors.name = ['Vui lòng nhập tên']
    } else if (name.length > 253) {
      errors.name = ['Tên tối đa 253 ký tự']
    }

    if (Object.keys(errors).length > 0) {
      throw new ApiError(400, 'Dữ liệu allowlist không hợp lệ', {
        title: 'Validation failed',
        errors
      })
    }

    const duplicate = this.jobAllowEntries.some(
      (e) => e.namespace === ns && e.kind === kind && e.name === name
    )
    if (duplicate) {
      throw new ApiError(409, `Mục allowlist '${kind}:${name}' trong namespace '${ns}' đã tồn tại`, {
        title: 'Job allow entry already exists',
        detail: `Job allow entry '${ns}/${kind}/${name}' already exists.`
      })
    }

    const newEntry: JobAllowEntry = {
      id: this.jobAllowEntries.length > 0 ? Math.max(...this.jobAllowEntries.map((e) => e.id)) + 1 : 1,
      namespace: ns,
      kind,
      name,
      createdAt: new Date().toISOString()
    }

    this.jobAllowEntries.push(newEntry)
    return JSON.parse(JSON.stringify(newEntry))
  }

  async deleteJobAllowEntry(id: number): Promise<void> {
    await delay()
    this.checkAuth()
    const entry = this.jobAllowEntries.find((e) => e.id === id)
    if (!entry) throw new ApiError(404, 'Không tìm thấy mục allowlist')

    // Check if in use by any Job
    const usingJobs: string[] = []
    for (const job of this.jobs) {
      if (entry.kind === 'namespace' && job.namespace === entry.name) {
        usingJobs.push(job.name)
      } else if (entry.kind === 'pullsecret' && job.namespace === entry.namespace && job.imagePullSecrets.includes(entry.name)) {
        usingJobs.push(job.name)
      } else if (entry.kind === 'configmap' && job.namespace === entry.namespace && job.tasks.some((t) => t.envFromConfigMaps.includes(entry.name))) {
        usingJobs.push(job.name)
      } else if (entry.kind === 'secret' && job.namespace === entry.namespace) {
        const inEnvFrom = job.tasks.some((t) => t.envFromSecrets.includes(entry.name))
        const inEnv = job.tasks.some((t) => t.env.some((e) => e.secretName === entry.name))
        if (inEnvFrom || inEnv) {
          usingJobs.push(job.name)
        }
      }
    }

    if (usingJobs.length > 0) {
      const uniqueNames = [...new Set(usingJobs)].map((n) => `'${n}'`).join(', ')
      throw new ApiError(409, `Không thể xoá mục allowlist đang được Job sử dụng: ${uniqueNames}`, {
        title: 'Mục allowlist đang được sử dụng',
        detail: `Không thể xoá mục allowlist '${entry.name}' vì đang được Job sử dụng: ${uniqueNames}.`
      })
    }

    this.jobAllowEntries = this.jobAllowEntries.filter((e) => e.id !== id)
  }
}
