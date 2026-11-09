import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import RunDetailView from '../../src/views/runs/RunDetailView.vue'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { ApiError } from '../../src/api/client'
import type { LogLine, RunDetail, RunStatus } from '../../src/api/types'
import ConfirmDialog from '../../src/components/ConfirmDialog.vue'
import StatusBadge from '../../src/components/StatusBadge.vue'
import { useToast } from '../../src/composables/useToast'

describe('RunDetailView (Chi tiết lượt chạy)', () => {
  let mockApi: MockShipitApi
  let router: any

  beforeEach(async () => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'runs', component: { template: '<div>Runs</div>' } },
        { path: '/runs/:id', name: 'run-detail', component: RunDetailView }
      ]
    })
    await router.push('/runs/1')
    await router.isReady()
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  async function waitMounted(wrapper: any) {
    for (let i = 0; i < 30; i++) {
      await new Promise((r) => setTimeout(r, 20))
      await flushPromises()
      if (!wrapper.text().includes('Đang tải')) break
    }
    await wrapper.vm.$nextTick()
  }

  async function waitMountedFake(wrapper: any) {
    await vi.advanceTimersByTimeAsync(60)
    await flushPromises()
    await wrapper.vm.$nextTick()
  }

  it('renders run details with pipeline name, #id, status, tag, image, and timeline', async () => {
    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('shipit-demo-pipeline')
    expect(wrapper.text()).toContain('#1')
    expect(wrapper.text()).toContain('Thành công')
    expect(wrapper.text()).toContain('a1b2c3d')
    expect(wrapper.text()).toContain('192.168.1.241/shipit-demo:a1b2c3d')
    expect(wrapper.text()).toContain('2m 10s')

    // Timeline steps
    expect(wrapper.text()).toContain('Checkout')
    expect(wrapper.text()).toContain('Build')
    expect(wrapper.text()).toContain('Push')
    expect(wrapper.text()).toContain('Deploy')
  })

  it('displays friendly 404 state when run does not exist', async () => {
    await router.push('/runs/999')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Không tìm thấy lượt chạy')
    expect(wrapper.text()).toContain('Lượt chạy #999 không tồn tại hoặc đã bị xoá.')
    expect(wrapper.text()).toContain('Quay lại danh sách')
  })

  it('formats failureReason for interrupted runs with a friendly explanation', async () => {
    const failedRun: RunDetail = {
      id: 2,
      pipelineId: 2,
      status: 'failed',
      failureReason: 'interrupted',
      createdAt: '2026-11-09T08:00:00Z',
      startedAt: '2026-11-09T08:00:05Z',
      finishedAt: '2026-11-09T08:01:20Z',
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T08:00:05Z', finishedAt: '2026-11-09T08:00:20Z' },
        { name: 'build', status: 'failed', startedAt: '2026-11-09T08:00:20Z', finishedAt: '2026-11-09T08:01:20Z' },
        { name: 'push', status: 'skipped', startedAt: null, finishedAt: null },
        { name: 'deploy', status: 'skipped', startedAt: null, finishedAt: null }
      ]
    }
    vi.spyOn(mockApi, 'getRun').mockResolvedValue(failedRun)

    await router.push('/runs/2')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Lý do thất bại')
    expect(wrapper.text()).toContain('Lượt chạy bị gián đoạn đột ngột (tiến trình dừng hoặc hệ thống khởi động lại)')
  })

  it('streams logs in ascending sequence order and avoids duplicates when resuming with after', async () => {
    let capturedOnLine: ((line: LogLine) => void) | null = null
    let capturedOnEnd: ((status: RunStatus) => void) | null = null
    let capturedAfter: number | null = null

    // Mock initial logs
    const initialLogs: LogLine[] = [
      { seq: 1, step: 'checkout', stream: 'system', text: 'Step 1: checkout', at: '2026-11-09T08:00:00Z' },
      { seq: 2, step: 'checkout', stream: 'stdout', text: 'Git clone started', at: '2026-11-09T08:00:01Z' }
    ]
    vi.spyOn(mockApi, 'getRunLogs').mockResolvedValue(initialLogs)

    // Run 3 is running
    await router.push('/runs/3')
    await router.isReady()

    vi.spyOn(mockApi, 'streamRunLogs').mockImplementation(
      (runId, after, onLine, onEnd) => {
        capturedAfter = after
        capturedOnLine = onLine
        capturedOnEnd = onEnd
        return () => {}
      }
    )

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    // Check after parameter equals maximum sequence of initial logs
    expect(capturedAfter).toBe(2)

    // Emit duplicate line seq: 2
    capturedOnLine!({ seq: 2, step: 'checkout', stream: 'stdout', text: 'Duplicate line', at: '2026-11-09T08:00:01Z' })
    // Emit out of order lines: 4 then 3
    capturedOnLine!({ seq: 4, step: 'build', stream: 'stdout', text: 'Compiling project...', at: '2026-11-09T08:00:05Z' })
    capturedOnLine!({ seq: 3, step: 'build', stream: 'system', text: 'Step 2: build', at: '2026-11-09T08:00:02Z' })

    await wrapper.vm.$nextTick()

    const vm = wrapper.vm as any
    const seqs = vm.lines.map((l: LogLine) => l.seq)
    // Must be in ascending order without duplicate 2
    expect(seqs).toEqual([1, 2, 3, 4])
  })

  it('updates run status, steps, and aria-status when end event is received', async () => {
    let capturedOnEnd: ((status: RunStatus) => void) | null = null

    // Run 3 starts as running
    await router.push('/runs/3')
    await router.isReady()

    vi.spyOn(mockApi, 'streamRunLogs').mockImplementation(
      (_runId, _after, _onLine, onEnd) => {
        capturedOnEnd = onEnd
        return () => {}
      }
    )

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    const statusEl = wrapper.find('[role="status"]')
    expect(statusEl.text()).toContain('Lượt chạy đang chạy')

    // Prepare updated run detail upon completion
    const completedRun: RunDetail = {
      ...(await mockApi.getRun(3)),
      status: 'success',
      finishedAt: '2026-11-09T08:46:00Z',
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T08:45:03Z', finishedAt: '2026-11-09T08:45:15Z' },
        { name: 'build', status: 'success', startedAt: '2026-11-09T08:45:15Z', finishedAt: '2026-11-09T08:45:45Z' },
        { name: 'push', status: 'success', startedAt: '2026-11-09T08:45:45Z', finishedAt: '2026-11-09T08:45:55Z' },
        { name: 'deploy', status: 'success', startedAt: '2026-11-09T08:45:55Z', finishedAt: '2026-11-09T08:46:00Z' }
      ]
    }
    vi.spyOn(mockApi, 'getRun').mockResolvedValue(completedRun)

    // Trigger end event
    capturedOnEnd!('success')
    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Thành công')
    expect(statusEl.text()).toContain('Run đã kết thúc: thành công')
  })

  it('periodically refreshes active run to update timeline steps', async () => {
    vi.useFakeTimers()
    await router.push('/runs/3')
    await router.isReady()

    const getRunSpy = vi.spyOn(mockApi, 'getRun')

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMountedFake(wrapper)
    const initialCallCount = getRunSpy.mock.calls.length

    // Advance 3.5 seconds
    await vi.advanceTimersByTimeAsync(3500)
    await flushPromises()

    expect(getRunSpy.mock.calls.length).toBeGreaterThan(initialCallCount)
    wrapper.unmount()
  })

  it('cleans up stream and timers on unmount and route id change', async () => {
    const cancelStream = vi.fn()
    vi.spyOn(mockApi, 'streamRunLogs').mockReturnValue(cancelStream)

    await router.push('/runs/3')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    // Change route :id to 1
    await router.push('/runs/1')
    await waitMounted(wrapper)

    expect(cancelStream).toHaveBeenCalled()

    // Unmount wrapper
    wrapper.unmount()
  })

  it('triggers rerun, navigates to new run on success, and handles 409 conflict', async () => {
    const pushSpy = vi.spyOn(router, 'push')
    const triggerSpy = vi.spyOn(mockApi, 'triggerRun')

    await router.push('/runs/1')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    // In mockApi, pipeline 1 has run 3 active -> throws 409
    const rerunBtn = wrapper.find('button[aria-label="Chạy lại pipeline này"]')
    expect(rerunBtn.exists()).toBe(true)

    await rerunBtn.trigger('click')
    await waitMounted(wrapper)

    expect(triggerSpy).toHaveBeenCalledWith(1)
    // No navigation occurred due to 409
    expect(pushSpy).not.toHaveBeenCalledWith('/runs/888')

    // Now mock triggerRun success
    const newRun = { id: 888, pipelineId: 1, status: 'queued' as const, createdAt: new Date().toISOString() }
    triggerSpy.mockResolvedValueOnce(newRun)

    await rerunBtn.trigger('click')
    await waitMounted(wrapper)

    expect(pushSpy).toHaveBeenCalledWith('/runs/888')
  })

  it('shows Stop button only for queued or running runs, and hides it for success, failed, or cancelled runs', async () => {
    // 1. Running run (run 3)
    await router.push('/runs/3')
    await router.isReady()
    const runningWrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(runningWrapper)
    const runningStopBtn = runningWrapper.find('button[aria-label="Dừng lượt chạy"]')
    expect(runningStopBtn.exists()).toBe(true)
    expect(runningStopBtn.text()).toContain('Dừng')

    // 2. Queued run (run 4)
    await router.push('/runs/4')
    await router.isReady()
    const queuedWrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(queuedWrapper)
    const queuedStopBtn = queuedWrapper.find('button[aria-label="Dừng lượt chạy"]')
    expect(queuedStopBtn.exists()).toBe(true)

    // 3. Success run (run 1)
    await router.push('/runs/1')
    await router.isReady()
    const successWrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(successWrapper)
    expect(successWrapper.find('button[aria-label="Dừng lượt chạy"]').exists()).toBe(false)

    // 4. Failed run (run 2)
    await router.push('/runs/2')
    await router.isReady()
    const failedWrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(failedWrapper)
    expect(failedWrapper.find('button[aria-label="Dừng lượt chạy"]').exists()).toBe(false)

    // 5. Cancelled run
    const cancelledRun: RunDetail = {
      ...(await mockApi.getRun(1)),
      id: 55,
      status: 'cancelled',
      failureReason: 'cancelled by user',
      finishedAt: '2026-11-09T08:50:00Z'
    }
    vi.spyOn(mockApi, 'getRun').mockResolvedValue(cancelledRun)
    await router.push('/runs/55')
    await router.isReady()
    const cancelledWrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(cancelledWrapper)
    expect(cancelledWrapper.find('button[aria-label="Dừng lượt chạy"]').exists()).toBe(false)
  })

  it('opens confirmation dialog on click, cancels dialog without API call, and confirms calling cancelRun with right id', async () => {
    const cancelSpy = vi.spyOn(mockApi, 'cancelRun')
    await router.push('/runs/3')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    const stopBtn = wrapper.find('button[aria-label="Dừng lượt chạy"]')
    expect(stopBtn.exists()).toBe(true)

    // Click Stop button -> opens ConfirmDialog
    await stopBtn.trigger('click')
    await wrapper.vm.$nextTick()

    const dialog = wrapper.findComponent(ConfirmDialog)
    expect(dialog.exists()).toBe(true)
    expect(dialog.props('modelValue')).toBe(true)
    expect(dialog.props('title')).toContain('Dừng lần chạy #3?')
    expect(dialog.props('message')).toContain('Các bước đang chạy sẽ bị ngắt và Shipit sẽ dọn dẹp máy build.')

    // Cancel dialog
    dialog.vm.$emit('cancel')
    await wrapper.vm.$nextTick()
    expect(cancelSpy).not.toHaveBeenCalled()
    expect(dialog.props('modelValue')).toBe(false)

    // Reopen and confirm
    await stopBtn.trigger('click')
    await wrapper.vm.$nextTick()
    dialog.vm.$emit('confirm')
    await wrapper.vm.$nextTick()

    expect(cancelSpy).toHaveBeenCalledWith(3)
  })

  it('transitions button to "Đang dừng…" with spinner after confirm until cancelled, then button disappears and "Đã dừng" badge is shown', async () => {
    let resolveCancel: ((val: any) => void) | null = null
    const cancelPromise = new Promise((resolve) => {
      resolveCancel = resolve
    })
    vi.spyOn(mockApi, 'cancelRun').mockReturnValue(cancelPromise as any)

    await router.push('/runs/3')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    const stopBtn = wrapper.find('button[aria-label="Dừng lượt chạy"]')
    await stopBtn.trigger('click')
    await wrapper.vm.$nextTick()

    const dialog = wrapper.findComponent(ConfirmDialog)
    dialog.vm.$emit('confirm')
    await wrapper.vm.$nextTick()

    // During stopping: button shows "Đang dừng…", is disabled, has spinner
    const stoppingBtn = wrapper.find('button[aria-label="Đang dừng lượt chạy"]')
    expect(stoppingBtn.exists()).toBe(true)
    expect(stoppingBtn.text()).toContain('Đang dừng…')
    expect(stoppingBtn.attributes('disabled')).toBeDefined()
    expect(stoppingBtn.find('.animate-spin').exists()).toBe(true)

    // Complete cancellation
    const cancelledDetail: RunDetail = {
      ...(await mockApi.getRun(3)),
      status: 'cancelled',
      failureReason: 'cancelled by user',
      finishedAt: new Date().toISOString()
    }
    vi.spyOn(mockApi, 'getRun').mockResolvedValue(cancelledDetail)
    resolveCancel!({ id: 3, status: 'cancelled' })

    await new Promise((r) => setTimeout(r, 40))
    await flushPromises()
    await wrapper.vm.$nextTick()

    // Stop button is gone
    expect(wrapper.find('button[aria-label="Dừng lượt chạy"]').exists()).toBe(false)
    expect(wrapper.find('button[aria-label="Đang dừng lượt chạy"]').exists()).toBe(false)

    // StatusBadge displays "Đã dừng"
    const badge = wrapper.findComponent(StatusBadge)
    expect(badge.text()).toContain('Đã dừng')
    expect(badge.attributes('data-status')).toBe('cancelled')
  })

  it('handles 409 conflict: displays error toast, refreshes status, and does not get stuck in "Đang dừng…"', async () => {
    vi.spyOn(mockApi, 'cancelRun').mockRejectedValue(new ApiError(409, 'Run đã kết thúc'))
    const getRunSpy = vi.spyOn(mockApi, 'getRun')

    await router.push('/runs/3')
    await router.isReady()

    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    const stopBtn = wrapper.find('button[aria-label="Dừng lượt chạy"]')
    await stopBtn.trigger('click')
    await wrapper.vm.$nextTick()

    const dialog = wrapper.findComponent(ConfirmDialog)
    dialog.vm.$emit('confirm')
    await flushPromises()
    await wrapper.vm.$nextTick()

    // Toast error shown
    const toast = useToast()
    expect(toast.toasts.value.some((t) => t.message.includes('Run đã kết thúc'))).toBe(true)

    // Refreshed status
    expect(getRunSpy).toHaveBeenCalledWith(3)

    // Not stuck in "Đang dừng…"
    expect(wrapper.find('button[aria-label="Đang dừng lượt chạy"]').exists()).toBe(false)
    const normalStopBtn = wrapper.find('button[aria-label="Dừng lượt chạy"]')
    if (normalStopBtn.exists()) {
      expect(normalStopBtn.text()).toContain('Dừng')
      expect(normalStopBtn.attributes('disabled')).toBeUndefined()
    }
  })
})
