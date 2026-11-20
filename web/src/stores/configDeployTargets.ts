import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { DeployTarget, DeployTargetInput } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'
import { createSwrChannel, type SwrOptions } from '../utils/swr'

export const useConfigDeployTargetsStore = defineStore('configDeployTargets', () => {
  const deployTargets = ref<DeployTarget[]>([])
  const hasLoaded = ref<boolean>(false)
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const targetsSwr = createSwrChannel<DeployTarget[]>()

  async function fetchDeployTargets(opts?: SwrOptions): Promise<DeployTarget[]> {
    const api = getApi()
    const result = await targetsSwr.run(
      opts,
      () => hasLoaded.value,
      async () => {
        error.value = null
        try {
          const data = await api.getDeployTargets()
          deployTargets.value = data
          hasLoaded.value = true
          return data
        } catch (err: any) {
          error.value = err.message || 'Không thể tải danh sách deployment được phép'
          throw err
        }
      },
      (v) => {
        loading.value = v
      }
    )
    return result ?? deployTargets.value
  }

  async function createDeployTarget(input: DeployTargetInput): Promise<DeployTarget> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createDeployTarget(input)
      targetsSwr.invalidate()
      await fetchDeployTargets({ force: true, silent: true })
      return created
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Deployment này đã tồn tại trong danh sách.'
      } else {
        error.value = err.problem?.detail || err.message || 'Không thể thêm deployment được phép'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deleteDeployTarget(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteDeployTarget(id)
      targetsSwr.invalidate()
      await fetchDeployTargets({ force: true, silent: true })
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = err.problem?.detail || 'Không thể xoá deployment đang được sử dụng bởi pipeline'
      } else {
        error.value = err.message || 'Không thể xoá deployment được phép'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  return {
    deployTargets,
    loading,
    error,
    fetchDeployTargets,
    createDeployTarget,
    deleteDeployTarget
  }
})
