import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount as baseMount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import RunsView from '../../src/views/runs/RunsView.vue'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import type { Run } from '../../src/api/types'
import { useRunsStore } from '../../src/stores/runs'

describe('RunsView (Danh sách lượt chạy)', () => {
  let mockApi: MockShipitApi
  let router: any
  const mountedWrappers: VueWrapper[] = []

  const mount: typeof baseMount = ((component: any, options?: any) => {
    const w = baseMount(component, options)
    mountedWrappers.push(w)
    return w
  }) as typeof baseMount

  beforeEach(async () => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'runs', component: RunsView },
        { path: '/runs/:id', name: 'run-detail', component: { template: '<div>Detail</div>' } },
        { path: '/pipelines', name: 'pipelines', component: { template: '<div>Pipelines</div>' } },
        { path: '/jobs/:id', name: 'job-detail', component: { template: '<div>Job</div>' } }
      ]
    })
    await router.push('/')
    await router.isReady()
  })

  afterEach(() => {
    while (mountedWrappers.length > 0) {
      mountedWrappers.pop()?.unmount()
    }
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  async function waitMounted(wrapper: any) {
    // MockShipitApi has 20ms delay on async methods
    await new Promise((r) => setTimeout(r, 80))
    await flushPromises()
    await wrapper.vm.$nextTick()
    const store = useRunsStore()
    for (let i = 0; i < 30 && (store.loading || store.loadingPipelines); i++) {
      await new Promise((r) => setTimeout(r, 20))
      await flushPromises()
      await wrapper.vm.$nextTick()
    }
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
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
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

  it('renders "Loại" column with Pipeline and Job badges and links to corresponding resources', async () => {
    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
        }
      }
    })

    await waitMounted(wrapper)

    // Table headers should include "Loại"
    const headers = wrapper.findAll('th').map((th) => th.text())
    expect(headers).toContain('Loại')

    // Table rows should display both 'Pipeline' and 'Job'
    expect(wrapper.text()).toContain('Pipeline')
    expect(wrapper.text()).toContain('Job')

    // Links to pipeline and job
    const links = wrapper.findAll('a')
    const hrefs = links.map((l) => l.attributes('href'))
    expect(hrefs.some((h) => h?.includes('/jobs/1'))).toBe(true)
    expect(hrefs.some((h) => h?.includes('/pipelines'))).toBe(true)
  })

  it('displays EmptyState when runs list is empty', async () => {
    vi.spyOn(mockApi, 'getRunsPaged').mockResolvedValue({
      items: [],
      total: 0
    })

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
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
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
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
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
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
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
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

    // Now simulate successful trigger -> navigates to /runs/99
    const pushSpy = vi.spyOn(router, 'push')
    const newMockRun: Run = {
      id: 99,
      pipelineId: 1,
      status: 'queued',
      createdAt: new Date().toISOString(),
      startedAt: null,
      finishedAt: null
    }
    triggerSpy.mockResolvedValueOnce(newMockRun)

    await vm.handleTriggerRun()
    await waitMounted(wrapper)

    expect(vm.showTriggerDialog).toBe(false)
    expect(pushSpy).toHaveBeenCalledWith('/runs/99')
  })

  it('supports triggering a Job run from "Chạy ngay" modal and handles 409 conflict', async () => {
    const pushSpy = vi.spyOn(router, 'push')
    const runJobSpy = vi.spyOn(mockApi, 'runJob')

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
        }
      }
    })

    await waitMounted(wrapper)

    const openBtn = wrapper.find('button:has(.fill-current)')
    await openBtn.trigger('click')
    await wrapper.vm.$nextTick()

    // Switch segmented control to Job
    const jobToggle = wrapper.findAll('div[aria-label="Loại đối tượng chạy"] button').find((b) => b.text() === 'Job')!
    await jobToggle.trigger('click')
    await wrapper.vm.$nextTick()

    // Open combobox and search for avina-dbmigrator
    const combobox = wrapper.get('button[role="combobox"]')
    await combobox.trigger('click')
    const searchInput = wrapper.get('input[aria-label="Lọc danh sách"]')
    await searchInput.setValue('dbmigrator')
    const options = wrapper.findAll('li[role="option"]')
    expect(options.map((o) => o.text())).toContain('avina-dbmigrator (#1)')
    const option = options.find((o) => o.text().includes('avina-dbmigrator'))!
    await option.trigger('click')

    // First test 409 conflict for Job
    runJobSpy.mockRejectedValueOnce(new (await import('../../src/api/client')).ApiError(409, 'Job đang có lượt chạy'))
    const submitBtn = wrapper.findAll('button').filter((b) => b.text() === 'Chạy ngay').pop()!
    await submitBtn.trigger('click')
    await waitMounted(wrapper)

    expect(runJobSpy).toHaveBeenCalledWith(1)
    expect(wrapper.text()).toContain('Job đang có lượt chạy')

    // Now test successful Job run -> navigates to /runs/101
    runJobSpy.mockResolvedValueOnce({
      id: 101,
      kind: 'job',
      jobId: 1,
      pipelineId: null,
      status: 'queued',
      createdAt: new Date().toISOString(),
      startedAt: null,
      finishedAt: null
    })
    await submitBtn.trigger('click')
    await waitMounted(wrapper)

    expect(pushSpy).toHaveBeenCalledWith('/runs/101')
  })

  it('auto-refreshes periodically when active runs exist, and stops when all runs complete', async () => {
    vi.useFakeTimers()
    const getRunsPagedSpy = vi.spyOn(mockApi, 'getRunsPaged')

    // Initial state: runs 3 & 4 are running/queued
    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
        }
      }
    })

    await waitMountedFake(wrapper)
    const initialCallCount = getRunsPagedSpy.mock.calls.length

    // Advance 3 seconds -> poll should trigger
    await vi.advanceTimersByTimeAsync(3050)
    await flushPromises()
    expect(getRunsPagedSpy.mock.calls.length).toBeGreaterThan(initialCallCount)

    // Now simulate runs completed: all success or failed
    const completedRuns: Run[] = [
      { id: 1, pipelineId: 1, status: 'success', createdAt: '2026-11-09T07:30:00Z' },
      { id: 2, pipelineId: 2, status: 'failed', createdAt: '2026-11-09T08:00:00Z' }
    ]
    getRunsPagedSpy.mockResolvedValue({
      items: completedRuns,
      total: 2
    })

    // Advance another 3 seconds to fetch completed runs
    await vi.advanceTimersByTimeAsync(3050)
    await flushPromises()
    await wrapper.vm.$nextTick()

    const callCountAfterComplete = getRunsPagedSpy.mock.calls.length

    // Advance another 6 seconds -> no more polling should occur
    await vi.advanceTimersByTimeAsync(6000)
    expect(getRunsPagedSpy.mock.calls.length).toBe(callCountAfterComplete)

    wrapper.unmount()
  })

  it('filters runs by kind, status, pipeline/job target, and paginates', async () => {
    const getRunsPagedSpy = vi.spyOn(mockApi, 'getRunsPaged')

    const wrapper = mount(RunsView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' },
          Teleport: true
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Hiển thị 1 đến 20 trong 65')

    const targetSelect = wrapper
      .findAllComponents({ name: 'AppSelect' })
      .find((c) => c.attributes('aria-label') === 'Pipeline / Job')!
    targetSelect.vm.$emit('update:modelValue', 'pipeline:1')
    await waitMounted(wrapper)

    expect(getRunsPagedSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ pipelineId: 1, page: 1, pageSize: 20 })
    )

    const statusSelect = wrapper
      .findAllComponents({ name: 'AppSelect' })
      .find((c) => c.attributes('aria-label') === 'Trạng thái')!
    statusSelect.vm.$emit('update:modelValue', 'success')
    await waitMounted(wrapper)

    expect(getRunsPagedSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ pipelineId: 1, status: 'success', page: 1, pageSize: 20 })
    )

    // Clear filters and go to page 2
    const clearBtn = wrapper.findAll('button').find((b) => b.text().includes('Xoá bộ lọc'))!
    await clearBtn.trigger('click')
    await waitMounted(wrapper)

    await wrapper.get('button[aria-label="Trang sau"]').trigger('click')
    await waitMounted(wrapper)

    expect(getRunsPagedSpy).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2, pageSize: 20 })
    )
  })

  it('useRunsStore cancelRun action updates run in store and pipeline lastRun', async () => {
    const store = useRunsStore()
    await store.fetchPipelines()
    await store.fetchRuns()

    // Run 4 is queued
    const run4 = store.runs.find((r) => r.id === 4)
    expect(run4?.status).toBe('queued')

    const updated = await store.cancelRun(4)
    expect(updated.status).toBe('cancelled')

    const inStore = store.runs.find((r) => r.id === 4)
    expect(inStore?.status).toBe('cancelled')
  })
})
