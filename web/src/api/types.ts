export interface Problem {
  title: string
  detail?: string
  errors?: Record<string, string[]>
}

export interface Readiness {
  ready: boolean
  database: 'ok' | 'unreachable'
  pendingMigrations: number
}

export interface LoginRequest {
  username: string
  password: string
  rememberMe?: boolean
}

export interface Me {
  username: string
}

export interface Slave {
  id: number
  name: string
  host: string
  port?: number
  user: string
  sshCredentialId: number
}

export interface SlaveInput {
  name: string
  host: string
  port?: number
  user: string
  sshCredentialId: number
}

export interface ConnectionTestResult {
  ok: boolean
  message: string
}

export type CredentialKind = 'ssh_key' | 'git_token' | 'registry'

export interface Credential {
  id: number
  name: string
  kind: CredentialKind
  username?: string | null
  hasValue: boolean
  updatedAt?: string
}

export interface CredentialInput {
  name: string
  kind: CredentialKind
  username?: string | null
  value: string
}

export interface CredentialUpdate {
  name?: string
  username?: string | null
  value?: string
}

export type RunStatus = 'queued' | 'running' | 'success' | 'failed' | 'cancelled'
export type StepName = 'checkout' | 'build' | 'push' | 'deploy'
export type StepStatus = 'pending' | 'running' | 'success' | 'failed' | 'skipped' | 'cancelled'

export interface Run {
  id: number
  pipelineId: number
  status: RunStatus
  tag?: string | null
  image?: string | null
  failureReason?: string | null
  createdAt: string
  startedAt?: string | null
  finishedAt?: string | null
}

export interface RunStep {
  name: StepName
  status: StepStatus
  startedAt?: string | null
  finishedAt?: string | null
}

export interface RunDetail extends Run {
  steps: RunStep[]
}

export interface LogLine {
  seq: number
  step: StepName
  stream: 'stdout' | 'stderr' | 'system'
  text: string
  at: string
}

export interface Pipeline {
  id: number
  name: string
  gitUrl: string
  gitBranch?: string
  gitCredentialId: number
  slaveId: number
  dockerfilePath?: string
  buildContext: string | null
  imageName: string
  registryHost: string
  deployRegistryHost: string | null
  registryCredentialId: number | null
  deployNamespace: string
  deployDeployment: string
  deployContainer: string
  lastRun?: Run | null
}

export interface PipelineInput {
  name: string
  gitUrl: string
  gitBranch?: string
  gitCredentialId: number
  slaveId: number
  dockerfilePath?: string
  buildContext?: string | null
  imageName: string
  registryHost: string
  deployRegistryHost?: string | null
  registryCredentialId?: number | null
  deployNamespace: string
  deployDeployment: string
  deployContainer: string
}

export interface DeployTarget {
  id: number
  namespace: string
  deployment: string
  createdAt: string
}

export interface DeployTargetInput {
  namespace: string
  deployment: string
}
