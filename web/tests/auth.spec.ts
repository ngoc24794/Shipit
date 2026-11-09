import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAuthStore } from '../src/stores/auth'
import { MockShipitApi } from '../src/api/mock'
import { setApi } from '../src/api'

describe('Auth Store', () => {
  let mockApi: MockShipitApi

  beforeEach(() => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('initializes and checks auth status', async () => {
    const auth = useAuthStore()
    expect(auth.isAuthenticated).toBe(false)

    const ok = await auth.checkAuth()
    expect(ok).toBe(true)
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.user?.username).toBe('admin')
  })

  it('handles successful login', async () => {
    const auth = useAuthStore()
    await mockApi.logout()
    auth.user = null

    await auth.login({ username: 'admin', password: 'admin123', rememberMe: true })
    expect(auth.isAuthenticated).toBe(true)
    expect((auth.user as any)?.username).toBe('admin')
    expect(auth.error).toBeNull()
  })

  it('handles failed login with 401 error message', async () => {
    const auth = useAuthStore()

    await expect(
      auth.login({ username: 'admin', password: 'wrongpassword' })
    ).rejects.toThrow()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.error).toBe('Sai tên đăng nhập hoặc mật khẩu')
  })

  it('handles logout', async () => {
    const auth = useAuthStore()
    await auth.checkAuth()
    expect(auth.isAuthenticated).toBe(true)

    await auth.logout()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.user).toBeNull()
  })
})
