import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Credential, CredentialInput, CredentialUpdate } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'
import { createSwrChannel, type SwrOptions } from '../utils/swr'

export const useConfigCredentialsStore = defineStore('configCredentials', () => {
  const credentials = ref<Credential[]>([])
  const hasLoaded = ref<boolean>(false)
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const credsSwr = createSwrChannel<Credential[]>()

  async function fetchCredentials(opts?: SwrOptions): Promise<Credential[]> {
    const api = getApi()
    const result = await credsSwr.run(
      opts,
      () => hasLoaded.value,
      async () => {
        error.value = null
        try {
          const data = await api.getCredentials()
          credentials.value = data
          hasLoaded.value = true
          return data
        } catch (err: any) {
          error.value = err.message || 'Không thể tải danh sách thông tin xác thực'
          throw err
        }
      },
      (v) => {
        loading.value = v
      }
    )
    return result ?? credentials.value
  }

  async function createCredential(input: CredentialInput): Promise<Credential> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createCredential(input)
      credsSwr.invalidate()
      await fetchCredentials({ force: true, silent: true })
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
      credsSwr.invalidate()
      await fetchCredentials({ force: true, silent: true })
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
      credsSwr.invalidate()
      await fetchCredentials({ force: true, silent: true })
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
