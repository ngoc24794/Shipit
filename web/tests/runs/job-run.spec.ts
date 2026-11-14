import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import RunDetailView from '../../src/views/runs/RunDetailView.vue'
import RunsView from '../../src/views/runs/RunsView.vue'
import StepTimeline from '../../src/components/StepTimeline.vue'
import TaskTable from '../../src/components/run/TaskTable.vue'
import LogViewer from '../../src/components/LogViewer.vue'
import StatusBadge from '../../src/components/StatusBadge.vue'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { useRunsStore } from '../../src/stores/runs'
import type { LogLine, RunDetail, RunTask, RunStatus } from '../../src/api/types'

describe('Job Run UI (Task J3: job-ui-run)', () => {
  let mockApi: MockShipitApi
  let router: any

  beforeEach(async () => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)

    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', name: 'runs', component: RunsView },
        { path: '/runs/:id', name: 'run-detail', component: RunDetailView },
        { path: '/pipelines', name: 'pipelines', component: { template: '<div>Pipelines</div>' } },
        { path: '/jobs/:id', name: 'job-detail', component: { template: '<div>Job</div>' } }
      ]
    })
    await router.push('/runs/5')
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

  it('Job run detail: renders 4 steps with "Chạy Job" instead of "Deploy", and Deploy is absent', async () => {
    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    expect(wrapper.text()).toContain('Checkout')
    expect(wrapper.text()).toContain('Build')
    expect(wrapper.text()).toContain('Push')
    expect(wrapper.text()).toContain('Chạy Job')
    expect(wrapper.text()).not.toContain('Deploy')
  })

  it('Pipeline run detail regression: renders 4 steps with "Deploy" and does NOT render "Chạy Job" or TaskTable', async () => {
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

    expect(wrapper.text()).toContain('Checkout')
    expect(wrapper.text()).toContain('Build')
    expect(wrapper.text()).toContain('Push')
    expect(wrapper.text()).toContain('Deploy')
    expect(wrapper.text()).not.toContain('Chạy Job')
    expect(wrapper.findComponent(TaskTable).exists()).toBe(false)
  })

  it('TaskTable: renders all tasks, exit codes (prominently when != 0; 124 shows "Quá thời gian"), status badges, and duration', async () => {
    const tasks: RunTask[] = [
      {
        position: 0,
        label: 'Tạo schema database',
        status: 'success',
        exitCode: 0,
        startedAt: '2026-11-09T09:01:20Z',
        finishedAt: '2026-11-09T09:01:40Z'
      },
      {
        position: 1,
        label: 'Chạy migration dữ liệu',
        status: 'failed',
        exitCode: 1,
        startedAt: '2026-11-09T09:01:40Z',
        finishedAt: '2026-11-09T09:02:10Z'
      },
      {
        position: 2,
        label: 'Khởi tạo dữ liệu mẫu',
        status: 'skipped',
        exitCode: null,
        startedAt: null,
        finishedAt: null
      },
      {
        position: 3,
        label: 'Xử lý tác vụ lâu',
        status: 'failed',
        exitCode: 124,
        startedAt: '2026-11-09T09:02:10Z',
        finishedAt: '2026-11-09T09:03:10Z'
      }
    ]

    const wrapper = mount(TaskTable, {
      props: { tasks }
    })

    expect(wrapper.text()).toContain('#1')
    expect(wrapper.text()).toContain('Tạo schema database')
    expect(wrapper.text()).toContain('Thành công')
    expect(wrapper.text()).toContain('0')
    expect(wrapper.text()).toContain('20s')

    expect(wrapper.text()).toContain('#2')
    expect(wrapper.text()).toContain('Chạy migration dữ liệu')
    expect(wrapper.text()).toContain('Thất bại')
    expect(wrapper.text()).toContain('1')
    expect(wrapper.text()).toContain('30s')

    // Task following a failed task is "Bỏ qua"
    expect(wrapper.text()).toContain('#3')
    expect(wrapper.text()).toContain('Khởi tạo dữ liệu mẫu')
    expect(wrapper.text()).toContain('Bỏ qua')

    // Exit code 124 shows "Quá thời gian"
    expect(wrapper.text()).toContain('#4')
    expect(wrapper.text()).toContain('124')
    expect(wrapper.text()).toContain('Quá thời gian')

    // Emits viewLog with task position
    const buttons = wrapper.findAll('button')
    await buttons[1].trigger('click')
    expect(wrapper.emitted('viewLog')?.[0]).toEqual([1])
  })

  it('Failed task displays summary banner "Tác vụ N thất bại (exit code X)" at top with jump link', async () => {
    const wrapper = mount(RunDetailView, {
      global: {
        plugins: [router],
        stubs: {
          AppLayout: { template: '<div><slot /></div>' }
        }
      }
    })

    await waitMounted(wrapper)

    // Banner for failed task 2 (position 1, exit code 1)
    const alert = wrapper.find('[role="alert"]')
    expect(alert.exists()).toBe(true)
    expect(alert.text()).toContain('Tác vụ 2 thất bại (exit code 1)')
    expect(alert.text()).toContain('Xem log tác vụ')
  })

  it('LogViewer: groups logs by task, displays "Chuẩn bị" group, and filters by task', async () => {
    const tasks: RunTask[] = [
      { position: 0, label: 'Tạo schema database', status: 'success', exitCode: 0 },
      { position: 1, label: 'Chạy migration dữ liệu', status: 'failed', exitCode: 1 }
    ]

    const lines: LogLine[] = [
      { seq: 1, step: 'checkout', stream: 'system', text: 'Checkout mã nguồn', at: '2026-11-09T09:00:00Z', taskPosition: null },
      { seq: 2, step: 'runjob', stream: 'stdout', text: 'Schema created', at: '2026-11-09T09:01:00Z', taskPosition: 0 },
      { seq: 3, step: 'runjob', stream: 'stderr', text: 'Migration failed error', at: '2026-11-09T09:02:00Z', taskPosition: 1 }
    ]

    const wrapper = mount(LogViewer, {
      props: {
        lines,
        tasks,
        isJob: true
      }
    })

    // Contains "Chuẩn bị" group
    expect(wrapper.text()).toContain('Chuẩn bị')
    expect(wrapper.text()).toContain('Checkout mã nguồn')

    // Contains task groups
    expect(wrapper.text()).toContain('Tác vụ 1 — Tạo schema database')
    expect(wrapper.text()).toContain('Schema created')

    expect(wrapper.text()).toContain('Tác vụ 2 — Chạy migration dữ liệu')
    expect(wrapper.text()).toContain('Migration failed error')

    // Task filter dropdown
    const select = wrapper.find('select#task-filter')
    expect(select.exists()).toBe(true)

    // Filter to task 0 only
    await select.setValue('0')
    expect(wrapper.text()).toContain('Schema created')
    expect(wrapper.find('#task-group-prep').attributes('style')).toContain('display: none')
    expect(wrapper.find('#task-group-1').attributes('style')).toContain('display: none')
    expect(wrapper.find('#task-group-0').attributes('style') || '').not.toContain('display: none')

    // Filter back to all
    await select.setValue('all')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('#task-group-prep').attributes('style') || '').not.toContain('display: none')
    expect(wrapper.find('#task-group-1').attributes('style') || '').not.toContain('display: none')
    expect(wrapper.find('#task-group-0').attributes('style') || '').not.toContain('display: none')
  })

  it('SSE fake stream: lines with increasing taskPosition are placed in right groups in increasing order', async () => {
    let capturedOnLine: ((line: LogLine) => void) | null = null
    let capturedOnEnd: ((status: RunStatus) => void) | null = null

    vi.spyOn(mockApi, 'getRunLogs').mockResolvedValue([])
    vi.spyOn(mockApi, 'streamRunLogs').mockImplementation((_runId, _after, onLine, onEnd) => {
      capturedOnLine = onLine
      capturedOnEnd = onEnd
      return () => {}
    })

    const runningJobRun: RunDetail = {
      id: 10,
      kind: 'job',
      jobId: 1,
      pipelineId: null,
      status: 'running',
      createdAt: '2026-11-09T09:00:00Z',
      startedAt: '2026-11-09T09:00:05Z',
      finishedAt: null,
      steps: [
        { name: 'checkout', status: 'success', startedAt: '2026-11-09T09:00:05Z', finishedAt: '2026-11-09T09:00:15Z' },
        { name: 'build', status: 'success', startedAt: '2026-11-09T09:00:15Z', finishedAt: '2026-11-09T09:01:00Z' },
        { name: 'push', status: 'success', startedAt: '2026-11-09T09:01:00Z', finishedAt: '2026-11-09T09:01:20Z' },
        { name: 'runjob', status: 'running', startedAt: '2026-11-09T09:01:20Z', finishedAt: null }
      ],
      tasks: [
        { position: 0, label: 'Task A', status: 'running', exitCode: null },
        { position: 1, label: 'Task B', status: 'pending', exitCode: null }
      ]
    }
    vi.spyOn(mockApi, 'getRun').mockResolvedValue(runningJobRun)

    await router.push('/runs/10')
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

    expect(capturedOnLine).not.toBeNull()

    // Stream lines with taskPosition null, then 0, then 1
    capturedOnLine!({ seq: 1, step: 'checkout', stream: 'stdout', text: 'Prep log line 1', at: '2026-11-09T09:00:00Z', taskPosition: null })
    capturedOnLine!({ seq: 2, step: 'runjob', stream: 'stdout', text: 'Task 0 log line 2', at: '2026-11-09T09:01:00Z', taskPosition: 0 })
    capturedOnLine!({ seq: 3, step: 'runjob', stream: 'stdout', text: 'Task 1 log line 3', at: '2026-11-09T09:02:00Z', taskPosition: 1 })

    await flushPromises()
    await wrapper.vm.$nextTick()

    const prepGroup = wrapper.find('#task-group-prep')
    expect(prepGroup.text()).toContain('Prep log line 1')

    const task0Group = wrapper.find('#task-group-0')
    expect(task0Group.text()).toContain('Task 0 log line 2')

    const task1Group = wrapper.find('#task-group-1')
    expect(task1Group.text()).toContain('Task 1 log line 3')

    capturedOnEnd!('success')
  })

  it('runsStore.runJob triggers a new job run and adds it to store', async () => {
    const store = useRunsStore()
    const newRun = await store.runJob(1)

    expect(newRun.kind).toBe('job')
    expect(newRun.jobId).toBe(1)
    expect(newRun.status).toBe('queued')
    expect(store.runs[0].id).toBe(newRun.id)
  })
})
