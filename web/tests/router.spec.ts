import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { router } from '../src/router'
import { useAuthStore } from '../src/stores/auth'
import { setApi } from '../src/api'
import { MockShipitApi } from '../src/api/mock'

describe('Router Guards', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('redirects unauthenticated user to /login', async () => {
    const mockApi = new MockShipitApi()
    // simulate logged out on the api
    await mockApi.logout()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = null
    authStore.initialized = false

    await router.push('/pipelines')
    await router.isReady()

    expect(router.currentRoute.value.name).toBe('login')
  })

  it('redirects authenticated user from /login to /', async () => {
    const mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/login')
    await router.isReady()

    expect(router.currentRoute.value.path).toBe('/')
  })

  it('redirects /deploy-targets to /settings/allowlist?tab=deployment', async () => {
    const mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/deploy-targets')
    await router.isReady()

    expect(router.currentRoute.value.path).toBe('/settings/allowlist')
    expect(router.currentRoute.value.query.tab).toBe('deployment')
  })

  it('redirects /settings/job-allow and /job-allow to /settings/allowlist?tab=job', async () => {
    const mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/settings/job-allow')
    await router.isReady()

    expect(router.currentRoute.value.path).toBe('/settings/allowlist')
    expect(router.currentRoute.value.query.tab).toBe('job')

    await router.push('/job-allow')
    await router.isReady()

    expect(router.currentRoute.value.path).toBe('/settings/allowlist')
    expect(router.currentRoute.value.query.tab).toBe('job')
  })
})
