import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { HttpShipitApi } from '../../src/api/client'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { useToast } from '../../src/composables/useToast'
import AllowlistView from '../../src/views/config/AllowlistView.vue'
import DeployTargetsView from '../../src/views/config/DeployTargetsView.vue'
import JobAllowListView from '../../src/views/jobs/JobAllowListView.vue'

describe('RBAC Sync UI & Warning Feedback', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
    const toast = useToast()
    toast.toasts.value.forEach((t) => toast.remove(t.id))
    toast.clear()
    await router.push('/settings/allowlist')
    await router.isReady()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('HttpShipitApi.syncRbac calls POST /api/rbac/sync and captures X-Shipit-Rbac-Warning on mutations', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            results: [{ namespace: 'avina-workspace', succeeded: true, error: null }],
            allSucceeded: true
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            id: 10,
            namespace: 'new-ns',
            deployment: 'app',
            createdAt: '2026-11-26T00:00:00Z'
          }),
          {
            status: 201,
            headers: {
              'Content-Type': 'application/json',
              'X-Shipit-Rbac-Warning': 'new-ns: HTTP 404 Not Found'
            }
          }
        )
      )
      .mockResolvedValueOnce(
        new Response(null, {
          status: 204
        })
      )

    vi.stubGlobal('fetch', fetchMock)

    const api = new HttpShipitApi()
    const syncRes = await api.syncRbac()
    expect(syncRes.allSucceeded).toBe(true)
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/rbac/sync',
      expect.objectContaining({ method: 'POST', credentials: 'same-origin' })
    )

    const created = await api.createDeployTarget({ namespace: 'new-ns', deployment: 'app' })
    expect(created.namespace).toBe('new-ns')
    expect(api.consumeLastRbacWarning()).toBe('new-ns: HTTP 404 Not Found')
    expect(api.consumeLastRbacWarning()).toBeNull()

    await api.deleteDeployTarget(10)
    expect(api.consumeLastRbacWarning()).toBeNull()
  })

  it('AllowlistView Sync RBAC button shows success and warning toasts', async () => {
    const toast = useToast()
    const syncSpy = vi.spyOn(mockApi, 'syncRbac')

    const wrapper = mount(AllowlistView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    const syncBtn = wrapper.findAll('button').find((b) => b.text().includes('Đồng bộ RBAC'))
    expect(syncBtn).toBeDefined()

    syncSpy.mockResolvedValueOnce({
      results: [
        { namespace: 'devops', succeeded: true, error: null },
        { namespace: 'avina-workspace', succeeded: true, error: null }
      ],
      allSucceeded: true
    })
    await syncBtn!.trigger('click')
    await new Promise((r) => setTimeout(r, 20))
    expect(toast.toasts.value.some((t) => t.type === 'success' && t.message.includes('2 namespace'))).toBe(true)

    syncSpy.mockResolvedValueOnce({
      results: [
        { namespace: 'devops', succeeded: true, error: null },
        { namespace: 'missing-ns', succeeded: false, error: 'HTTP 404 Not Found' }
      ],
      allSucceeded: false
    })
    await syncBtn!.trigger('click')
    await new Promise((r) => setTimeout(r, 20))
    expect(
      toast.toasts.value.some(
        (t) =>
          t.type === 'warn' &&
          t.message.includes('missing-ns') &&
          t.message.includes('HTTP 404 Not Found')
      )
    ).toBe(true)
  })

  it('DeployTargetsView shows warn toast when X-Shipit-Rbac-Warning is present', async () => {
    const toast = useToast()
    vi.spyOn(mockApi, 'consumeLastRbacWarning').mockReturnValueOnce('missing-ns: namespace not found')

    const wrapper = mount(DeployTargetsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })
    await new Promise((r) => setTimeout(r, 50))

    const nsInput = wrapper.find('input[placeholder="devops"]')
    const depInput = wrapper.find('input[placeholder="shipit-demo"]')
    await nsInput.setValue('missing-ns')
    await depInput.setValue('my-app')
    await wrapper.find('form').trigger('submit.prevent')

    await vi.waitFor(() => {
      expect(
        toast.toasts.value.some(
          (t) => t.type === 'warn' && t.message.includes('missing-ns: namespace not found')
        )
      ).toBe(true)
    })
  })

  it('JobAllowListView shows warn toast when X-Shipit-Rbac-Warning is present', async () => {
    const toast = useToast()
    vi.spyOn(mockApi, 'consumeLastRbacWarning').mockReturnValueOnce('missing-ns: HTTP 403 Forbidden')

    const wrapper = mount(JobAllowListView, {
      global: {
        plugins: [createPinia(), router]
      }
    })
    await new Promise((r) => setTimeout(r, 50))

    const nsInput = wrapper.find('input[placeholder="VD: avina-workspace"]')
    const nameInput = wrapper.find('input[placeholder="VD: avina-files-config"]')
    await nsInput.setValue('missing-ns')
    await nameInput.setValue('missing-ns')
    await wrapper.find('form').trigger('submit.prevent')

    await vi.waitFor(() => {
      expect(
        toast.toasts.value.some(
          (t) => t.type === 'warn' && t.message.includes('missing-ns: HTTP 403 Forbidden')
        )
      ).toBe(true)
    })
  })
})
