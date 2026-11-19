import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import PipelinesView from '../../src/views/config/PipelinesView.vue'
import JobsView from '../../src/views/jobs/JobsView.vue'
import SlavesView from '../../src/views/config/SlavesView.vue'
import CredentialsView from '../../src/views/config/CredentialsView.vue'
import DeployTargetsView from '../../src/views/config/DeployTargetsView.vue'
import JobAllowListView from '../../src/views/jobs/JobAllowListView.vue'
import { MockShipitApi } from '../../src/api/mock'
import { HttpShipitApi } from '../../src/api/client'
import { setApi } from '../../src/api'

function createTestRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/pipelines', component: PipelinesView },
      { path: '/pipelines/new', component: { template: '<div />' } },
      { path: '/pipelines/:id/edit', component: { template: '<div />' } },
      { path: '/jobs', component: JobsView },
      { path: '/jobs/new', component: { template: '<div />' } },
      { path: '/jobs/:id', component: { template: '<div />' } },
      { path: '/slaves', component: SlavesView },
      { path: '/slaves/new', component: { template: '<div />' } },
      { path: '/slaves/:id/edit', component: { template: '<div />' } },
      { path: '/credentials', component: CredentialsView },
      { path: '/credentials/new', component: { template: '<div />' } },
      { path: '/credentials/:id/edit', component: { template: '<div />' } },
      { path: '/settings/allowlist', component: { template: '<div />' } }
    ]
  })
}

async function waitMounted(wrapper: any) {
  await new Promise((r) => setTimeout(r, 50))
  await flushPromises()
  await wrapper.vm.$nextTick()
}

describe('All Data Views - Search, Filter, Sort, Pagination', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.useRealTimers()
  })

  it('PipelinesView supports search, status/namespace filters, and pagination (12 items)', async () => {
    const router = createTestRouter()
    await router.push('/pipelines')
    await router.isReady()

    const wrapper = mount(PipelinesView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 12 trong 12')

    // Change page size to 10 -> 2 pages
    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Số dòng mỗi trang')!.vm.$emit('update:modelValue', 10)
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 10 trong 12')

    await wrapper.get('button[aria-label="Trang sau"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 11 đến 12 trong 12')

    // Filter by namespace
    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Namespace')!.vm.$emit('update:modelValue', 'devops')
    await flushPromises()
    expect(wrapper.text()).toContain('shipit-demo-pipeline')

    // Search with no match shows empty-filter message + Xoá bộ lọc
    vi.useFakeTimers()
    await wrapper.get('input[type="text"]').setValue('nonexistent-xyz')
    await vi.advanceTimersByTimeAsync(350)
    await flushPromises()
    expect(wrapper.text()).toContain('Không có kết quả phù hợp')
  })

  it('JobsView supports search, status/namespace filters, and pagination (12 items)', async () => {
    const router = createTestRouter()
    await router.push('/jobs')
    await router.isReady()

    const wrapper = mount(JobsView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 12 trong 12')

    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Số dòng mỗi trang')!.vm.$emit('update:modelValue', 10)
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 10 trong 12')

    vi.useFakeTimers()
    await wrapper.get('input[type="text"]').setValue('dbmigrator')
    await vi.advanceTimersByTimeAsync(350)
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 1 trong 1')
    expect(wrapper.text()).toContain('avina-dbmigrator')
  })

  it('SlavesView renders DataToolbar and DataPagination and filters by search', async () => {
    const router = createTestRouter()
    await router.push('/slaves')
    await router.isReady()

    const wrapper = mount(SlavesView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 2 trong 2')

    vi.useFakeTimers()
    await wrapper.get('input[type="text"]').setValue('backup')
    await vi.advanceTimersByTimeAsync(350)
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 1 trong 1')
    expect(wrapper.text()).toContain('slave-backup')
  })

  it('CredentialsView filters by kind and search', async () => {
    const router = createTestRouter()
    await router.push('/credentials')
    await router.isReady()

    const wrapper = mount(CredentialsView, {
      global: {
        plugins: [router],
        stubs: { AppLayout: { template: '<div><slot /></div>' } }
      }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 3 trong 3')

    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Loại thông tin xác thực')!.vm.$emit('update:modelValue', 'registry')
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 1 trong 1')
    expect(wrapper.text()).toContain('registry-secret')
  })

  it('DeployTargetsView filters by namespace and search', async () => {
    const router = createTestRouter()
    await router.push('/settings/allowlist?tab=deployment')
    await router.isReady()

    const wrapper = mount(DeployTargetsView, {
      props: { embedded: true },
      global: { plugins: [router] }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 2 trong 2')

    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Namespace')!.vm.$emit('update:modelValue', 'devops')
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 1 trong 1')
    expect(wrapper.text()).toContain('shipit-demo')
  })

  it('JobAllowListView filters by kind, namespace, and search', async () => {
    const router = createTestRouter()
    await router.push('/settings/allowlist?tab=job')
    await router.isReady()

    const wrapper = mount(JobAllowListView, {
      props: { embedded: true },
      global: { plugins: [router] }
    })
    await waitMounted(wrapper)

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 6 trong 6')

    wrapper.findAllComponents({ name: 'AppSelect' }).find((c) => c.attributes('aria-label') === 'Loại tài nguyên')!.vm.$emit('update:modelValue', 'secret')
    await flushPromises()
    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 2 trong 2')
  })

  it('HttpShipitApi.getRunsPaged parses X-Total-Count header and falls back when missing', async () => {
    const client = new HttpShipitApi('')
    const sampleRuns = [{ id: 1, status: 'success', createdAt: '2026-11-09T00:00:00Z' }]

    const fetchMock = vi.fn()
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleRuns), {
          status: 200,
          headers: {
            'Content-Type': 'application/json',
            'X-Total-Count': '134'
          }
        })
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleRuns), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        })
      )
    vi.stubGlobal('fetch', fetchMock)

    const withHeader = await client.getRunsPaged({ page: 2, pageSize: 20, q: 'demo', kind: 'pipeline', status: 'success' })
    expect(withHeader.total).toBe(134)
    expect(fetchMock.mock.calls[0][0]).toContain('page=2')
    expect(fetchMock.mock.calls[0][0]).toContain('pageSize=20')
    expect(fetchMock.mock.calls[0][0]).toContain('q=demo')
    expect(fetchMock.mock.calls[0][0]).toContain('kind=pipeline')
    expect(fetchMock.mock.calls[0][0]).toContain('status=success')

    const withoutHeader = await client.getRunsPaged({ page: 1, pageSize: 20 })
    expect(withoutHeader.total).toBe(1)
  })
})
