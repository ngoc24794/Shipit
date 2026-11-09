import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Credential, CredentialInput, CredentialUpdate } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useConfigCredentialsStore = defineStore('configCredentials', () => {
  const credentials = ref<Credential[]>([])
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)

  async function fetchCredentials(): Promise<Credential[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getCredentials()
      credentials.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách thông tin xác thực'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createCredential(input: CredentialInput): Promise<Credential> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createCredential(input)
      await fetchCredentials()
      return created
    } catch (err: any) {
      error.value = err.message || 'Không thể tạo thông tin xác thực'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function updateCredential(id: number, input: CredentialUpdate): Promise<Credential> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const updated = await api.updateCredential(id, input)
      await fetchCredentials()
      return updated
    } catch (err: any) {
      error.value = err.message || 'Không thể cập nhật thông tin xác thực'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deleteCredential(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteCredential(id)
      await fetchCredentials()
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Thông tin xác thực đang được dùng bởi slave/pipeline'
      } else {
        error.value = err.message || 'Không thể xoá thông tin xác thực'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  return {
    credentials,
    loading,
    error,
    fetchCredentials,
    createCredential,
    updateCredential,
    deleteCredential
  }
})
