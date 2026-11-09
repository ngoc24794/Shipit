import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Me, LoginRequest } from '../api/types'
import { ApiError, onUnauthorized } from '../api/client'
import { getApi } from '../api'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<Me | null>(null)
  const loading = ref<boolean>(false)
  const initialized = ref<boolean>(false)
  const error = ref<string | null>(null)

  const isAuthenticated = computed(() => !!user.value)

  // Register unauthorized listener to reset state on 401
  onUnauthorized(() => {
    user.value = null
    initialized.value = true
  })

  async function checkAuth(): Promise<boolean> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const me = await api.getMe()
      user.value = me
      return true
    } catch {
      user.value = null
      return false
    } finally {
      loading.value = false
      initialized.value = true
    }
  }

  async function login(req: LoginRequest): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.login(req)
      const me = await api.getMe()
      user.value = me
    } catch (err: any) {
      user.value = null
      if (err instanceof ApiError && err.status === 401) {
        error.value = 'Sai tên đăng nhập hoặc mật khẩu'
      } else {
        error.value = err.message || 'Đăng nhập không thành công'
      }
      throw err
    } finally {
      loading.value = false
      initialized.value = true
    }
  }

  async function logout(): Promise<void> {
    const api = getApi()
    loading.value = true
    try {
      await api.logout()
    } catch {
      // Ignore logout errors
    } finally {
      user.value = null
      loading.value = false
    }
  }

  return {
    user,
    loading,
    initialized,
    error,
    isAuthenticated,
    checkAuth,
    login,
    logout
  }
})
