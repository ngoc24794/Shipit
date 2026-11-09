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
  Run,
  RunDetail,
  LogLine,
  RunStatus
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
      pipelineId: 1,
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
      ]
    },
    {
      id: 2,
      pipelineId: 2,
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
      ]
    },
    {
      id: 3,
      pipelineId: 1,
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
      ]
    },
    {
      id: 4,
      pipelineId: 2,
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
      pipelineId,
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
      ]
    }
    this.runs.unshift(newRun)
    pipeline.lastRun = newRun
    return newRun
  }

  async getRuns(params?: { pipelineId?: number; limit?: number }): Promise<Run[]> {
    await delay()
    this.checkAuth()
    let list = [...this.runs]
    if (params?.pipelineId !== undefined) {
      list = list.filter((r) => r.pipelineId === params.pipelineId)
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

      if (currentIndex < pendingLines.length) {
        onLine(pendingLines[currentIndex])
        currentIndex++
      } else {
        clearInterval(intervalId)
        const run = this.runs.find((r) => r.id === runId)
        onEnd(run?.status || 'success')
      }
    }, 40)

    return () => {
      cancelled = true
      if (intervalId) clearInterval(intervalId)
    }
  }
}
