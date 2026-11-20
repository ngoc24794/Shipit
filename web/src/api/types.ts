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
export type StepName = 'checkout' | 'build' | 'push' | 'deploy' | 'runjob'
export type StepStatus = 'pending' | 'running' | 'success' | 'failed' | 'skipped' | 'cancelled'

export interface TriggerRunOptions {
  skipSteps?: StepName[]
}

export interface Run {
  id: number
  pipelineId?: number | null
  jobId?: number | null
  kind?: 'pipeline' | 'job'
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

export interface RunTask {
  position: number
  label: string
  status: StepStatus
  exitCode?: number | null
  startedAt?: string | null
  finishedAt?: string | null
}

export interface RunDetail extends Run {
  steps: RunStep[]
  tasks?: RunTask[]
}

export interface LogLine {
  seq: number
  step: StepName
  stream: 'stdout' | 'stderr' | 'system'
  text: string
  at: string
  taskPosition?: number | null
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

export type JobAllowKind = 'namespace' | 'configmap' | 'secret' | 'pullsecret'

export interface JobAllowEntry {
  id: number
  namespace: string
  kind: JobAllowKind
  name: string
  createdAt: string
}

export interface JobAllowEntryInput {
  namespace: string
  kind: JobAllowKind
  name: string
}

export interface JobEnvVar {
  name?: string | null
  value?: string | null
  secretName?: string | null
  secretKey?: string | null
}

export interface JobTask {
  position: number
  label: string
  args: string[]
  envFromConfigMaps: string[]
  envFromSecrets: string[]
  env: JobEnvVar[]
  timeoutSeconds: number
  backoffLimit: number
  ttlSeconds: number
}

export interface JobTaskInput {
  label: string
  args?: string[]
  envFromConfigMaps?: string[]
  envFromSecrets?: string[]
  env?: JobEnvVar[]
  timeoutSeconds?: number | null
  backoffLimit?: number | null
  ttlSeconds?: number | null
}

export interface Job {
  id: number
  name: string
  gitUrl: string
  gitBranch: string
  gitCredentialId: number
  slaveId: number
  dockerfilePath: string
  buildContext: string | null
  imageName: string
  registryHost: string
  deployRegistryHost: string | null
  registryCredentialId: number | null
  namespace: string
  containerName: string
  serviceAccount?: string | null
  imagePullSecrets: string[]
  tasks: JobTask[]
  lastRun?: Run | null
}

export interface JobInput {
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
  namespace: string
  containerName: string
  serviceAccount?: string | null
  imagePullSecrets?: string[]
  tasks: JobTaskInput[]
}

export interface JobValidation {
  valid: boolean
  errors: Record<string, string[]>
}

export interface JobPreviewTask {
  position: number
  label: string
  yaml: string
}

export interface JobPreview {
  errors: Record<string, string[]>
  tasks: JobPreviewTask[]
}

export interface GetRunsParams {
  pipelineId?: number
  jobId?: number
  kind?: 'pipeline' | 'job'
  status?: RunStatus
  q?: string
  page?: number
  pageSize?: number
  limit?: number
}

export interface PagedResult<T> {
  items: T[]
  total: number
}

export type SearchKind =
  | 'run'
  | 'pipeline'
  | 'job'
  | 'slave'
  | 'credential'
  | 'deploy-target'
  | 'job-allow'

export interface SearchItem {
  id: string
  title: string
  subtitle: string | null
  path: string
}

export interface SearchGroup {
  kind: SearchKind
  label: string
  items: SearchItem[]
}

export interface SearchResponse {
  query: string
  groups: SearchGroup[]
}
