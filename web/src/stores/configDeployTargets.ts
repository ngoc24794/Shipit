import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { DeployTarget, DeployTargetInput } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useConfigDeployTargetsStore = defineStore('configDeployTargets', () => {
  const deployTargets = ref<DeployTarget[]>([])
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)

  async function fetchDeployTargets(): Promise<DeployTarget[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getDeployTargets()
      deployTargets.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách deployment được phép'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createDeployTarget(input: DeployTargetInput): Promise<DeployTarget> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createDeployTarget(input)
      await fetchDeployTargets()
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
      await fetchDeployTargets()
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
