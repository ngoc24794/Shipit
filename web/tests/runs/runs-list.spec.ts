import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import RunsView from '../../src/views/runs/RunsView.vue'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import type { Run } from '../../src/api/types'

describe('RunsView (Danh sách lượt chạy)', () => {
  let mockApi: MockShipitApi
  let router: any

  beforeEach(() => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'runs', component: RunsView },
        { path: '/runs/:id', name: 'run-detail', component: { template: '<div>Detail</div>' } }
      ]
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  async function waitMounted(wrapper: any) {
    // MockShipitApi has 20ms delay on async methods
    await new Promise((r) => setTimeout(r, 50))
    await flushPromises()
    await wrapper.vm.$nextTick()
  }

  async function waitMountedFake(wrapper: any) {
    await vi.advanceTimersByTimeAsync(50)
    await flushPromises()
    await wrapper.vm.$nextTick()
  }

  it('renders table with runs, pipeline names, status badges, and commit sha', async () => {
    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Lượt chạy')
    expect(wrapper.text()).toContain('#1')
    expect(wrapper.text()).toContain('shipit-demo-pipeline')
    expect(wrapper.text()).toContain('a1b2c3d')
    expect(wrapper.text()).toContain('Thành công')
    expect(wrapper.text()).toContain('Thất bại')
    expect(wrapper.text()).toContain('Đang chạy')
    expect(wrapper.text()).toContain('Đang chờ')
  })

  it('displays EmptyState when runs list is empty', async () => {
    vi.spyOn(mockApi, 'getRuns').mockResolvedValue([])

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Chưa có lượt chạy nào')
  })

  it('calculates duration for finished runs and updates running runs dynamically', async () => {
    vi.useFakeTimers()
    const now = new Date('2026-11-09T08:45:30Z').getTime()
    vi.setSystemTime(now)

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMountedFake(wrapper)

    // Run 1: started 07:30:05, finished 07:32:15 -> 2m 10s
    expect(wrapper.text()).toContain('2m 10s')

    // Run 3: started 08:45:03, now is 08:45:30 -> 27s
    expect(wrapper.text()).toContain('27s')

    // Advance 5 seconds
    await vi.advanceTimersByTimeAsync(5000)
    await wrapper.vm.$nextTick()

    // Should now be 32s
    expect(wrapper.text()).toContain('32s')
  })

  it('navigates to /runs/:id on row click and enter key', async () => {
    const pushSpy = vi.spyOn(router, 'push')

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    const row = wrapper.find('[data-run-id="1"]')
    expect(row.exists()).toBe(true)

    // Click row
    await row.trigger('click')
    expect(pushSpy).toHaveBeenCalledWith('/runs/1')

    // Press enter on row
    await row.trigger('keydown.enter')
    expect(pushSpy).toHaveBeenCalledWith('/runs/1')
  })

  it('opens trigger dialog, handles 409 conflict, and handles successful run creation', async () => {
    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    // Open dialog
    const openBtn = wrapper.find('button.w-full, button:has(.fill-current)')
    await openBtn.trigger('click')

    // Pipeline 1 has active run in mock -> triggerRun throws 409
    const triggerSpy = vi.spyOn(mockApi, 'triggerRun')

    const vm = wrapper.vm as any
    vm.triggerPipelineId = 1

    await vm.handleTriggerRun()
    await waitMounted(wrapper)

    expect(triggerSpy).toHaveBeenCalledWith(1)
    expect(vm.triggerError).toContain('Pipeline đang có lượt chạy')

    // Now simulate successful trigger
    const newMockRun: Run = {
      id: 99,
      pipelineId: 1,
      status: 'queued',
      createdAt: new Date().toISOString(),
      startedAt: null,
      finishedAt: null
    }
    triggerSpy.mockResolvedValueOnce(newMockRun)
    vi.spyOn(mockApi, 'getRuns').mockResolvedValueOnce([
      newMockRun,
      ...(await mockApi.getRuns())
    ])

    await vm.handleTriggerRun()
    await waitMounted(wrapper)

    expect(vm.showTriggerDialog).toBe(false)
    expect(wrapper.text()).toContain('#99')
  })

  it('auto-refreshes periodically when active runs exist, and stops when all runs complete', async () => {
    vi.useFakeTimers()
    const getRunsSpy = vi.spyOn(mockApi, 'getRuns')

    // Initial state: runs 3 & 4 are running/queued
    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMountedFake(wrapper)
    const initialCallCount = getRunsSpy.mock.calls.length

    // Advance 3 seconds -> poll should trigger
    await vi.advanceTimersByTimeAsync(3050)
    await flushPromises()
    expect(getRunsSpy.mock.calls.length).toBeGreaterThan(initialCallCount)

    // Now simulate runs completed: all success or failed
    const completedRuns: Run[] = [
      { id: 1, pipelineId: 1, status: 'success', createdAt: '2026-11-09T07:30:00Z' },
      { id: 2, pipelineId: 2, status: 'failed', createdAt: '2026-11-09T08:00:00Z' }
    ]
    getRunsSpy.mockResolvedValue(completedRuns)

    // Advance another 3 seconds to fetch completed runs
    await vi.advanceTimersByTimeAsync(3050)
    await flushPromises()

    const callCountAfterComplete = getRunsSpy.mock.calls.length

    // Advance another 6 seconds -> no more polling should occur
    await vi.advanceTimersByTimeAsync(6000)
    expect(getRunsSpy.mock.calls.length).toBe(callCountAfterComplete)

    wrapper.unmount()
  })

  it('filters runs by pipeline selection', async () => {
    const getRunsSpy = vi.spyOn(mockApi, 'getRuns')

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    const select = wrapper.findComponent({ name: 'AppSelect' })
    await select.vm.$emit('update:modelValue', 1)
    await waitMounted(wrapper)

    expect(getRunsSpy).toHaveBeenCalledWith({ pipelineId: 1 })
  })
})
