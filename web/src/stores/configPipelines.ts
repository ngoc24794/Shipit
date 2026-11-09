import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Pipeline, PipelineInput, Run } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useConfigPipelinesStore = defineStore('configPipelines', () => {
  const pipelines = ref<Pipeline[]>([])
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const triggeringId = ref<number | null>(null)

  async function fetchPipelines(): Promise<Pipeline[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getPipelines()
      pipelines.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách pipeline'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createPipeline(input: PipelineInput): Promise<Pipeline> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createPipeline(input)
      await fetchPipelines()
      return created
    } catch (err: any) {
      error.value = err.message || 'Không thể tạo pipeline'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function updatePipeline(id: number, input: PipelineInput): Promise<Pipeline> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const updated = await api.updatePipeline(id, input)
      await fetchPipelines()
      return updated
    } catch (err: any) {
      error.value = err.message || 'Không thể cập nhật pipeline'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deletePipeline(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deletePipeline(id)
      await fetchPipelines()
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Không thể xoá pipeline khi đang có lượt chạy'
      } else {
        error.value = err.message || 'Không thể xoá pipeline'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function triggerRun(pipelineId: number): Promise<Run> {
    const api = getApi()
    triggeringId.value = pipelineId
    error.value = null
    try {
      const run = await api.triggerRun(pipelineId)
      await fetchPipelines()
      return run
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Pipeline đang có lượt chạy'
      } else {
        error.value = err.message || 'Không thể kích hoạt lượt chạy'
      }
      throw err
    } finally {
      triggeringId.value = null
    }
  }

  return {
    pipelines,
    loading,
    error,
    triggeringId,
    fetchPipelines,
    createPipeline,
    updatePipeline,
    deletePipeline,
    triggerRun
  }
})
