import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import App from '../src/App.vue'
import { router } from '../src/router'
import { setApi } from '../src/api'
import { MockShipitApi } from '../src/api/mock'
import { useAuthStore } from '../src/stores/auth'

const ALL_ROUTES = [
  '/login',
  '/',
  '/runs/1',
  '/pipelines',
  '/pipelines/new',
  '/pipelines/1/edit',
  '/jobs',
  '/jobs/new',
  '/jobs/1',
  '/slaves',
  '/slaves/new',
  '/slaves/1/edit',
  '/credentials',
  '/credentials/new',
  '/credentials/1/edit',
  '/settings/allowlist',
  '/settings/allowlist?tab=job',
  '/dev/components',
  '/route-does-not-exist-404'
]

describe('Route Smoke Test (Mock Mode)', () => {
  let consoleErrorSpy: ReturnType<typeof vi.spyOn>

  beforeEach(async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    setApi(new MockShipitApi())

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  afterEach(() => {
    consoleErrorSpy.mockRestore()
    document.body.innerHTML = ''
  })

  it('renders every app route in mock mode without console errors', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/')
    await router.isReady()

    const wrapper = mount(App, {
      attachTo: document.body,
      global: {
        plugins: [pinia, router]
      }
    })

    for (const path of ALL_ROUTES) {
      await router.push(path)
      await flushPromises()
      expect(wrapper.text().trim().length).toBeGreaterThan(0)
    }

    expect(consoleErrorSpy).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
