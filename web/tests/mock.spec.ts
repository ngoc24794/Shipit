import { describe, it, expect, vi } from 'vitest'
import { MockShipitApi } from '../src/api/mock'
import type { LogLine, RunStatus } from '../src/api/types'

describe('MockShipitApi', () => {
  it('credentials list strictly contains metadata and never reveals secret values', async () => {
    const api = new MockShipitApi()
    const creds = await api.getCredentials()

    expect(creds.length).toBeGreaterThan(0)
    for (const cred of creds) {
      expect(cred.id).toBeDefined()
      expect(cred.name).toBeDefined()
      expect(cred.kind).toBeDefined()
      expect(cred.hasValue).toBe(true)
      // Assert secret property does not exist on Credential object
      expect((cred as any).value).toBeUndefined()
    }
  })

  it('streamRunLogs streams log lines over time and invokes onEnd upon completion', async () => {
    vi.useFakeTimers()
    const api = new MockShipitApi()
    const receivedLines: LogLine[] = []
    let finalStatus: RunStatus | null = null

    const cancel = api.streamRunLogs(
      1,
      0,
      (line) => receivedLines.push(line),
      (status) => {
        finalStatus = status
      }
    )

    // Advance fake timers
    await vi.advanceTimersByTimeAsync(1500)

    expect(receivedLines.length).toBeGreaterThan(0)
    expect(finalStatus).toBe('success')

    cancel()
    vi.useRealTimers()
  })

  it('streamRunLogs can be cancelled before completion', async () => {
    vi.useFakeTimers()
    const api = new MockShipitApi()
    const receivedLines: LogLine[] = []
    let finalStatus: RunStatus | null = null

    const cancel = api.streamRunLogs(
      1,
      0,
      (line) => receivedLines.push(line),
      (status) => {
        finalStatus = status
      }
    )

    // Advance time slightly to get first few lines
    await vi.advanceTimersByTimeAsync(100)
    const countAtCancel = receivedLines.length
    expect(countAtCancel).toBeGreaterThan(0)

    // Cancel streaming
    cancel()

    // Advance time further
    await vi.advanceTimersByTimeAsync(2000)

    // Count should not increase and onEnd should not be called
    expect(receivedLines.length).toBe(countAtCancel)
    expect(finalStatus).toBeNull()

    vi.useRealTimers()
  })

  it('creates pipeline with null registryCredentialId and returns null', async () => {
    const api = new MockShipitApi()
    const pipeline = await api.createPipeline({
      name: 'pipeline-no-reg-cred',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'no-auth-app',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'no-auth-app',
      deployContainer: 'app'
    })

    expect(pipeline.registryCredentialId).toBeNull()
    const fetched = await api.getPipeline(pipeline.id)
    expect(fetched.registryCredentialId).toBeNull()
  })

  it('fails to create pipeline when registryCredentialId does not exist', async () => {
    const api = new MockShipitApi()
    await expect(
      api.createPipeline({
        name: 'pipeline-invalid-reg-cred',
        gitUrl: 'https://git.example.com/org/repo.git',
        gitBranch: 'main',
        gitCredentialId: 2,
        slaveId: 1,
        imageName: 'test-app',
        registryHost: '192.168.1.241',
        registryCredentialId: 99999,
        deployNamespace: 'default',
        deployDeployment: 'test-app',
        deployContainer: 'app'
      })
    ).rejects.toThrow()
  })

  it('saves and returns buildContext correctly, normalizes whitespace to null', async () => {
    const api = new MockShipitApi()

    // 1. Valid non-empty buildContext
    const p1 = await api.createPipeline({
      name: 'pipeline-with-context',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      buildContext: 'services/app',
      imageName: 'my-service',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'my-service',
      deployContainer: 'app'
    })
    expect(p1.buildContext).toBe('services/app')
    const fetched1 = await api.getPipeline(p1.id)
    expect(fetched1.buildContext).toBe('services/app')

    // 2. Dot as root repo
    const pDot = await api.createPipeline({
      name: 'pipeline-dot-context',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      buildContext: '.',
      imageName: 'dot-service',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'dot-service',
      deployContainer: 'app'
    })
    expect(pDot.buildContext).toBe('.')

    // 3. Whitespace string converts to null
    const p2 = await api.createPipeline({
      name: 'pipeline-whitespace-context',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      buildContext: '   ',
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(p2.buildContext).toBeNull()

    // 4. Update pipeline with valid context, then update to whitespace (null)
    const updated1 = await api.updatePipeline(p2.id, {
      name: 'pipeline-whitespace-context',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      buildContext: 'docker',
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(updated1.buildContext).toBe('docker')

    const updated2 = await api.updatePipeline(p2.id, {
      name: 'pipeline-whitespace-context',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      buildContext: '   ',
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(updated2.buildContext).toBeNull()
  })

  it('validates buildContext and returns 400 ApiError with Problem.errors', async () => {
    const api = new MockShipitApi()
    const baseInput = {
      name: 'invalid-context-pipeline',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'app',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'app',
      deployContainer: 'app'
    }

    await expect(api.createPipeline({ ...baseInput, buildContext: '../x' })).rejects.toThrow()
    await expect(api.createPipeline({ ...baseInput, buildContext: '/abs' })).rejects.toThrow()
    await expect(api.createPipeline({ ...baseInput, buildContext: 'a b' })).rejects.toThrow()
  })

  it('saves and returns deployRegistryHost correctly, normalizes whitespace to null', async () => {
    const api = new MockShipitApi()

    // 1. Valid deployRegistryHost
    const p1 = await api.createPipeline({
      name: 'pipeline-with-deploy-host',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'my-service',
      registryHost: '192.168.1.241',
      deployRegistryHost: 'registry.local:5000',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'my-service',
      deployContainer: 'app'
    })
    expect(p1.deployRegistryHost).toBe('registry.local:5000')
    const fetched1 = await api.getPipeline(p1.id)
    expect(fetched1.deployRegistryHost).toBe('registry.local:5000')

    // 2. Whitespace string converts to null
    const p2 = await api.createPipeline({
      name: 'pipeline-whitespace-deploy-host',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      deployRegistryHost: '   ',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(p2.deployRegistryHost).toBeNull()

    // 3. Null converts to null
    const p3 = await api.createPipeline({
      name: 'pipeline-null-deploy-host',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'null-service',
      registryHost: '192.168.1.241',
      deployRegistryHost: null,
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'null-service',
      deployContainer: 'app'
    })
    expect(p3.deployRegistryHost).toBeNull()

    // 4. Update pipeline with valid deployRegistryHost, then update to whitespace (null)
    const updated1 = await api.updatePipeline(p2.id, {
      name: 'pipeline-whitespace-deploy-host',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      deployRegistryHost: 'k8s-reg.internal:5000',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(updated1.deployRegistryHost).toBe('k8s-reg.internal:5000')

    const updated2 = await api.updatePipeline(p2.id, {
      name: 'pipeline-whitespace-deploy-host',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'ws-service',
      registryHost: '192.168.1.241',
      deployRegistryHost: '   ',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'ws-service',
      deployContainer: 'app'
    })
    expect(updated2.deployRegistryHost).toBeNull()
  })

  it('validates deployRegistryHost and returns 400 ApiError with Problem.errors', async () => {
    const api = new MockShipitApi()
    const baseInput = {
      name: 'invalid-deploy-host-pipeline',
      gitUrl: 'https://git.example.com/org/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'app',
      registryHost: '192.168.1.241',
      registryCredentialId: null,
      deployNamespace: 'default',
      deployDeployment: 'app',
      deployContainer: 'app'
    }

    await expect(api.createPipeline({ ...baseInput, deployRegistryHost: 'http://x' })).rejects.toThrow()
    await expect(api.createPipeline({ ...baseInput, deployRegistryHost: 'a b' })).rejects.toThrow()
    await expect(api.createPipeline({ ...baseInput, deployRegistryHost: 'x;y' })).rejects.toThrow()
  })

  describe('cancelRun', () => {
    it('cancels a queued run immediately with status cancelled, failureReason and skipped steps', async () => {
      const api = new MockShipitApi()
      // Run 4 is queued
      const cancelledRun = await api.cancelRun(4)
      expect(cancelledRun.id).toBe(4)
      expect(cancelledRun.status).toBe('cancelled')
      expect(cancelledRun.failureReason).toBe('cancelled by user')
      expect(cancelledRun.finishedAt).toBeTruthy()

      const detail = await api.getRun(4)
      expect(detail.status).toBe('cancelled')
      expect(detail.failureReason).toBe('cancelled by user')
      for (const step of detail.steps) {
        expect(step.status).toBe('skipped')
      }

      // Check idempotent repeated call
      const repeated = await api.cancelRun(4)
      expect(repeated.status).toBe('cancelled')
    })

    it('cancels a running run asynchronously: running step becomes cancelled, subsequent skipped', async () => {
      const api = new MockShipitApi()
      // Run 3 is running with checkout: success, build: running, push: pending, deploy: pending
      const initial = await api.cancelRun(3)
      expect(initial.id).toBe(3)

      // Wait for simulated async engine stop
      await new Promise((resolve) => setTimeout(resolve, 80))

      const detail = await api.getRun(3)
      expect(detail.status).toBe('cancelled')
      expect(detail.failureReason).toBe('cancelled by user')
      expect(detail.finishedAt).toBeTruthy()

      const checkoutStep = detail.steps.find((s) => s.name === 'checkout')
      const buildStep = detail.steps.find((s) => s.name === 'build')
      const pushStep = detail.steps.find((s) => s.name === 'push')
      const deployStep = detail.steps.find((s) => s.name === 'deploy')

      expect(checkoutStep?.status).toBe('success')
      expect(buildStep?.status).toBe('cancelled')
      expect(buildStep?.finishedAt).toBeTruthy()
      expect(pushStep?.status).toBe('skipped')
      expect(deployStep?.status).toBe('skipped')
    })

    it('returns 409 conflict when trying to cancel a finished run (success or failed)', async () => {
      const api = new MockShipitApi()
      // Run 1 is success
      await expect(api.cancelRun(1)).rejects.toMatchObject({
        status: 409,
        message: expect.stringContaining('Run đã kết thúc')
      })

      // Run 2 is failed
      await expect(api.cancelRun(2)).rejects.toMatchObject({
        status: 409,
        message: expect.stringContaining('Run đã kết thúc')
      })
    })

    it('returns 404 when run does not exist', async () => {
      const api = new MockShipitApi()
      await expect(api.cancelRun(99999)).rejects.toMatchObject({
        status: 404
      })
    })
  })
})
