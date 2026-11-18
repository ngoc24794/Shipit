import type {
  Problem,
  Readiness,
  LoginRequest,
  Me,
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
  LogLine,
  RunStatus,
  Job,
  JobInput,
  JobValidation,
  JobPreview,
  JobAllowEntry,
  JobAllowEntryInput,
  SearchResponse
} from './types'

export class ApiError extends Error {
  status: number
  problem?: Problem

  constructor(status: number, message: string, problem?: Problem) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

type UnauthorizedHandler = () => void
let unauthorizedHandler: UnauthorizedHandler | null = null

export function onUnauthorized(handler: UnauthorizedHandler): void {
  unauthorizedHandler = handler
}

export function notifyUnauthorized(): void {
  if (unauthorizedHandler) {
    unauthorizedHandler()
  }
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('shipit:unauthorized'))
  }
}

export interface ShipitApi {
  // Auth
  login(req: LoginRequest): Promise<void>
  logout(): Promise<void>
  getMe(): Promise<Me>

  // Health
  getHealth(): Promise<string>
  getReadiness(): Promise<Readiness>

  // Slaves
  getSlaves(): Promise<Slave[]>
  createSlave(input: SlaveInput): Promise<Slave>
  getSlave(id: number): Promise<Slave>
  updateSlave(id: number, input: SlaveInput): Promise<Slave>
  deleteSlave(id: number): Promise<void>
  testSlaveConnection(id: number): Promise<ConnectionTestResult>

  // Credentials
  getCredentials(): Promise<Credential[]>
  createCredential(input: CredentialInput): Promise<Credential>
  updateCredential(id: number, input: CredentialUpdate): Promise<Credential>
  deleteCredential(id: number): Promise<void>

  // Pipelines
  getPipelines(): Promise<Pipeline[]>
  createPipeline(input: PipelineInput): Promise<Pipeline>
  getPipeline(id: number): Promise<Pipeline>
  updatePipeline(id: number, input: PipelineInput): Promise<Pipeline>
  deletePipeline(id: number): Promise<void>

  // Deploy Targets
  getDeployTargets(): Promise<DeployTarget[]>
  createDeployTarget(input: DeployTargetInput): Promise<DeployTarget>
  deleteDeployTarget(id: number): Promise<void>

  // Jobs
  getJobs(): Promise<Job[]>
  createJob(input: JobInput): Promise<Job>
  getJob(id: number): Promise<Job>
  updateJob(id: number, input: JobInput): Promise<Job>
  deleteJob(id: number): Promise<void>
  validateJob(input: JobInput): Promise<JobValidation>
  previewJob(input: JobInput): Promise<JobPreview>
  triggerJobRun(id: number): Promise<Run>
  runJob?(id: number): Promise<Run>

  // Job Allow Entries
  getJobAllowEntries(): Promise<JobAllowEntry[]>
  createJobAllowEntry(input: JobAllowEntryInput): Promise<JobAllowEntry>
  deleteJobAllowEntry(id: number): Promise<void>

  // Runs
  triggerRun(pipelineId: number): Promise<Run>
  cancelRun(id: number): Promise<Run>
  getRuns(params?: { pipelineId?: number; jobId?: number; limit?: number }): Promise<Run[]>
  getRun(id: number): Promise<RunDetail>
  getRunLogs(id: number, after?: number): Promise<LogLine[]>
  streamRunLogs(
    runId: number,
    after: number,
    onLine: (line: LogLine) => void,
    onEnd: (status: RunStatus) => void,
    onError?: (err: Error) => void
  ): () => void

  // Search
  search(q: string, limit?: number, signal?: AbortSignal): Promise<SearchResponse>
}

export class HttpShipitApi implements ShipitApi {
  private baseUrl: string

  constructor(baseUrl: string = '') {
    this.baseUrl = baseUrl
  }

  private async request<T>(
    endpoint: string,
    options: RequestInit = {}
  ): Promise<T> {
    const url = `${this.baseUrl}${endpoint}`
    const headers: Record<string, string> = {
      Accept: 'application/json',
      ...((options.headers as Record<string, string>) || {})
    }

    if (options.body && typeof options.body === 'string') {
      headers['Content-Type'] = 'application/json'
    }

    const res = await fetch(url, {
      ...options,
      credentials: 'same-origin',
      headers
    })

    if (res.status === 401) {
      notifyUnauthorized()
      throw new ApiError(401, 'Chưa đăng nhập hoặc phiên làm việc đã hết hạn')
    }

    if (!res.ok) {
      let problem: Problem | undefined
      try {
        problem = await res.json()
      } catch {
        // Not JSON
      }
      const message = problem?.detail || problem?.title || `Yêu cầu thất bại với mã lỗi ${res.status}`
      throw new ApiError(res.status, message, problem)
    }

    if (res.status === 204 || res.headers.get('content-length') === '0') {
      return undefined as unknown as T
    }

    const contentType = res.headers.get('content-type') || ''
    if (contentType.includes('application/json')) {
      return (await res.json()) as T
    }

    return (await res.text()) as unknown as T
  }

  async login(req: LoginRequest): Promise<void> {
    await this.request<void>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify(req)
    })
  }

  async logout(): Promise<void> {
    await this.request<void>('/api/auth/logout', {
      method: 'POST'
    })
  }

  async getMe(): Promise<Me> {
    return this.request<Me>('/api/auth/me')
  }

  async getHealth(): Promise<string> {
    return this.request<string>('/healthz')
  }

  async getReadiness(): Promise<Readiness> {
    return this.request<Readiness>('/readyz')
  }

  async getSlaves(): Promise<Slave[]> {
    return this.request<Slave[]>('/api/slaves')
  }

  async createSlave(input: SlaveInput): Promise<Slave> {
    return this.request<Slave>('/api/slaves', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async getSlave(id: number): Promise<Slave> {
    return this.request<Slave>(`/api/slaves/${id}`)
  }

  async updateSlave(id: number, input: SlaveInput): Promise<Slave> {
    return this.request<Slave>(`/api/slaves/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input)
    })
  }

  async deleteSlave(id: number): Promise<void> {
    await this.request<void>(`/api/slaves/${id}`, {
      method: 'DELETE'
    })
  }

  async testSlaveConnection(id: number): Promise<ConnectionTestResult> {
    return this.request<ConnectionTestResult>(`/api/slaves/${id}/test`, {
      method: 'POST'
    })
  }

  async getCredentials(): Promise<Credential[]> {
    return this.request<Credential[]>('/api/credentials')
  }

  async createCredential(input: CredentialInput): Promise<Credential> {
    return this.request<Credential>('/api/credentials', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async updateCredential(id: number, input: CredentialUpdate): Promise<Credential> {
    return this.request<Credential>(`/api/credentials/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input)
    })
  }

  async deleteCredential(id: number): Promise<void> {
    await this.request<void>(`/api/credentials/${id}`, {
      method: 'DELETE'
    })
  }

  async getPipelines(): Promise<Pipeline[]> {
    return this.request<Pipeline[]>('/api/pipelines')
  }

  async createPipeline(input: PipelineInput): Promise<Pipeline> {
    return this.request<Pipeline>('/api/pipelines', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async getPipeline(id: number): Promise<Pipeline> {
    return this.request<Pipeline>(`/api/pipelines/${id}`)
  }

  async updatePipeline(id: number, input: PipelineInput): Promise<Pipeline> {
    return this.request<Pipeline>(`/api/pipelines/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input)
    })
  }

  async deletePipeline(id: number): Promise<void> {
    await this.request<void>(`/api/pipelines/${id}`, {
      method: 'DELETE'
    })
  }

  async getDeployTargets(): Promise<DeployTarget[]> {
    return this.request<DeployTarget[]>('/api/deploy-targets')
  }

  async createDeployTarget(input: DeployTargetInput): Promise<DeployTarget> {
    return this.request<DeployTarget>('/api/deploy-targets', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async deleteDeployTarget(id: number): Promise<void> {
    await this.request<void>(`/api/deploy-targets/${id}`, {
      method: 'DELETE'
    })
  }

  async triggerRun(pipelineId: number): Promise<Run> {
    return this.request<Run>(`/api/pipelines/${pipelineId}/runs`, {
      method: 'POST'
    })
  }

  async cancelRun(id: number): Promise<Run> {
    return this.request<Run>(`/api/runs/${id}/cancel`, {
      method: 'POST'
    })
  }

  async getRuns(params?: { pipelineId?: number; jobId?: number; limit?: number }): Promise<Run[]> {
    const search = new URLSearchParams()
    if (params?.pipelineId !== undefined) {
      search.set('pipelineId', params.pipelineId.toString())
    }
    if (params?.jobId !== undefined) {
      search.set('jobId', params.jobId.toString())
    }
    if (params?.limit !== undefined) {
      search.set('limit', params.limit.toString())
    }
    const query = search.toString() ? `?${search.toString()}` : ''
    return this.request<Run[]>(`/api/runs${query}`)
  }

  async getRun(id: number): Promise<RunDetail> {
    return this.request<RunDetail>(`/api/runs/${id}`)
  }

  async getRunLogs(id: number, after: number = 0): Promise<LogLine[]> {
    return this.request<LogLine[]>(`/api/runs/${id}/logs?after=${after}`)
  }

  streamRunLogs(
    runId: number,
    after: number,
    onLine: (line: LogLine) => void,
    onEnd: (status: RunStatus) => void,
    onError?: (err: Error) => void
  ): () => void {
    if (typeof EventSource === 'undefined') {
      // Fallback or test environment
      return () => {}
    }

    const url = `${this.baseUrl}/api/runs/${runId}/logs?after=${after}`
    const eventSource = new EventSource(url, { withCredentials: true })

    eventSource.addEventListener('log', (event: MessageEvent) => {
      try {
        const line = JSON.parse(event.data) as LogLine
        onLine(line)
      } catch (err) {
        if (onError) onError(err as Error)
      }
    })

    eventSource.addEventListener('end', (event: MessageEvent) => {
      let status: RunStatus = 'success'
      if (event.data) {
        try {
          const parsed = typeof event.data === 'string' ? JSON.parse(event.data) : event.data
          if (parsed && typeof parsed.status === 'string') {
            status = parsed.status as RunStatus
          } else if (typeof parsed === 'string') {
            status = parsed as RunStatus
          }
        } catch {
          status = event.data as RunStatus
        }
      }
      onEnd(status)
      eventSource.close()
    })

    eventSource.onerror = (e) => {
      if (onError) onError(new Error('EventSource failed'))
    }

    return () => {
      eventSource.close()
    }
  }

  async getJobs(): Promise<Job[]> {
    return this.request<Job[]>('/api/jobs')
  }

  async createJob(input: JobInput): Promise<Job> {
    return this.request<Job>('/api/jobs', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async getJob(id: number): Promise<Job> {
    return this.request<Job>(`/api/jobs/${id}`)
  }

  async updateJob(id: number, input: JobInput): Promise<Job> {
    return this.request<Job>(`/api/jobs/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input)
    })
  }

  async deleteJob(id: number): Promise<void> {
    await this.request<void>(`/api/jobs/${id}`, {
      method: 'DELETE'
    })
  }

  async validateJob(input: JobInput): Promise<JobValidation> {
    return this.request<JobValidation>('/api/jobs/validate', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async previewJob(input: JobInput): Promise<JobPreview> {
    return this.request<JobPreview>('/api/jobs/preview', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async triggerJobRun(id: number): Promise<Run> {
    return this.request<Run>(`/api/jobs/${id}/runs`, {
      method: 'POST'
    })
  }

  async runJob(id: number): Promise<Run> {
    return this.triggerJobRun(id)
  }

  async getJobAllowEntries(): Promise<JobAllowEntry[]> {
    return this.request<JobAllowEntry[]>('/api/job-allow-entries')
  }

  async createJobAllowEntry(input: JobAllowEntryInput): Promise<JobAllowEntry> {
    return this.request<JobAllowEntry>('/api/job-allow-entries', {
      method: 'POST',
      body: JSON.stringify(input)
    })
  }

  async deleteJobAllowEntry(id: number): Promise<void> {
    await this.request<void>(`/api/job-allow-entries/${id}`, {
      method: 'DELETE'
    })
  }

  async search(q: string, limit: number = 5, signal?: AbortSignal): Promise<SearchResponse> {
    const params = new URLSearchParams()
    params.set('q', q)
    params.set('limit', String(limit))
    return this.request<SearchResponse>(`/api/search?${params.toString()}`, { signal })
  }
}
