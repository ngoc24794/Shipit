import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Slave, SlaveInput, ConnectionTestResult } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'
import { createSwrChannel, type SwrOptions } from '../utils/swr'

export const useConfigSlavesStore = defineStore('configSlaves', () => {
  const slaves = ref<Slave[]>([])
  const hasLoaded = ref<boolean>(false)
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const testingSlaveId = ref<number | null>(null)
  const testResults = ref<Record<number, ConnectionTestResult>>({})
  const slavesSwr = createSwrChannel<Slave[]>()

  async function fetchSlaves(opts?: SwrOptions): Promise<Slave[]> {
    const api = getApi()
    const result = await slavesSwr.run(
      opts,
      () => hasLoaded.value,
      async () => {
        error.value = null
        try {
          const data = await api.getSlaves()
          slaves.value = data
          hasLoaded.value = true
          return data
        } catch (err: any) {
          error.value = err.message || 'Không thể tải danh sách máy build'
          throw err
        }
      },
      (v) => {
        loading.value = v
      }
    )
    return result ?? slaves.value
  }

  async function createSlave(input: SlaveInput): Promise<Slave> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createSlave(input)
      slavesSwr.invalidate()
      await fetchSlaves({ force: true, silent: true })
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
      slavesSwr.invalidate()
      await fetchSlaves({ force: true, silent: true })
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
      slavesSwr.invalidate()
      await fetchSlaves({ force: true, silent: true })
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
