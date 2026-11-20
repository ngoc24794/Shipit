import { describe, it, expect, vi, beforeEach } from 'vitest'
import { HttpShipitApi } from '../src/api/client'
import { MockShipitApi } from '../src/api/mock'
import type { JobInput, RunStatus } from '../src/api/types'

describe('Web - API Contract Alignment (Task J6)', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
    mockApi = new MockShipitApi()
  })

  it('HttpShipitApi has runJob as an alias to triggerJobRun calling POST /api/jobs/{id}/runs', async () => {
    const api = new HttpShipitApi('http://localhost:5000')
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ id: 99, status: 'queued', kind: 'job', createdAt: new Date().toISOString() }), {
        status: 202,
        headers: { 'Content-Type': 'application/json' }
      })
    )

    const res = await api.runJob(12)
    expect(res.id).toBe(99)
    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5000/api/jobs/12/runs',
      expect.objectContaining({
        method: 'POST'
      })
    )
    fetchSpy.mockRestore()
  })

  it('streamRunLogs parses SSE JSON end event {"status":"success"} into "success"', () => {
    const api = new HttpShipitApi('http://localhost:5000')
    let capturedEndListener: ((event: any) => void) | null = null

    // Mock EventSource
    const mockEventSource = {
      addEventListener: vi.fn((event: string, handler: any) => {
        if (event === 'end') {
          capturedEndListener = handler
        }
      }),
      close: vi.fn(),
      onerror: null
    }

    vi.stubGlobal('EventSource', vi.fn(() => mockEventSource))

    let finalStatus: RunStatus | null = null
    api.streamRunLogs(1, 0, () => {}, (status) => {
      finalStatus = status
    })

    expect(capturedEndListener).not.toBeNull()

    // When backend sends SSE with data: {"status":"success"}
    capturedEndListener!({
      data: JSON.stringify({ status: 'success' })
    })

    expect(finalStatus).toBe('success')
    expect(mockEventSource.close).toHaveBeenCalled()

    // Test with "failed"
    capturedEndListener!({
      data: JSON.stringify({ status: 'failed' })
    })
    expect(finalStatus).toBe('failed')

    // Test fallback plain string
    capturedEndListener!({
      data: 'cancelled'
    })
    expect(finalStatus).toBe('cancelled')

    vi.unstubAllGlobals()
  })

  it('Job tasks use 0-based positions in MockShipitApi (Task 0 and Task 1)', async () => {
    const input: JobInput = {
      name: 'test-job-0based',
      gitUrl: 'https://git.example.com/repo.git',
      gitBranch: 'main',
      gitCredentialId: 2,
      slaveId: 1,
      imageName: 'test-image',
      registryHost: '192.168.1.241',
      namespace: 'avina-workspace',
      containerName: 'app',
      tasks: [
        { label: 'first-task', timeoutSeconds: 1800 },
        { label: 'second-task', timeoutSeconds: 1800 }
      ]
    }

    const created = await mockApi.createJob(input)
    expect(created.tasks).toHaveLength(2)
    expect(created.tasks[0].position).toBe(0)
    expect(created.tasks[1].position).toBe(1)

    // Preview should also return 0-based positions
    const preview = await mockApi.previewJob(input, created.id)
    expect(preview.tasks).toHaveLength(2)
    expect(preview.tasks[0].position).toBe(0)
    expect(preview.tasks[1].position).toBe(1)
  })

  it('previewJob returns empty tasks array when validation errors exist', async () => {
    const invalidInput: JobInput = {
      name: 'INVALID NAME WITH UPPERCASE',
      gitUrl: '',
      gitCredentialId: 0,
      slaveId: 0,
      imageName: '',
      registryHost: '',
      namespace: '',
      containerName: '',
      tasks: []
    }

    const preview = await mockApi.previewJob(invalidInput)
    expect(Object.keys(preview.errors).length).toBeGreaterThan(0)
    expect(preview.tasks).toEqual([])
  })
})
