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
})
