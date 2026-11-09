import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Slave, SlaveInput, ConnectionTestResult } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useConfigSlavesStore = defineStore('configSlaves', () => {
  const slaves = ref<Slave[]>([])
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const testingSlaveId = ref<number | null>(null)
  const testResults = ref<Record<number, ConnectionTestResult>>({})

  async function fetchSlaves(): Promise<Slave[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getSlaves()
      slaves.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách máy build'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createSlave(input: SlaveInput): Promise<Slave> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createSlave(input)
      await fetchSlaves()
      return created
    } catch (err: any) {
      error.value = err.message || 'Không thể tạo máy build'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function updateSlave(id: number, input: SlaveInput): Promise<Slave> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const updated = await api.updateSlave(id, input)
      await fetchSlaves()
      return updated
    } catch (err: any) {
      error.value = err.message || 'Không thể cập nhật máy build'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deleteSlave(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteSlave(id)
      await fetchSlaves()
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Không thể xoá máy build đang được pipeline sử dụng'
      } else {
        error.value = err.message || 'Không thể xoá máy build'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function testSlave(id: number): Promise<ConnectionTestResult> {
    const api = getApi()
    testingSlaveId.value = id
    try {
      const result = await api.testSlaveConnection(id)
      testResults.value[id] = result
      return result
    } catch (err: any) {
      const failedResult: ConnectionTestResult = {
        ok: false,
        message: err.message || 'Kiểm tra kết nối thất bại'
      }
      testResults.value[id] = failedResult
      throw err
    } finally {
      testingSlaveId.value = null
    }
  }

  return {
    slaves,
    loading,
    error,
    testingSlaveId,
    testResults,
    fetchSlaves,
    createSlave,
    updateSlave,
    deleteSlave,
    testSlave
  }
})
