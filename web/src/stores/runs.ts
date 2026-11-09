import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Run, RunDetail, Pipeline } from '../api/types'
import { getApi } from '../api'

export const useRunsStore = defineStore('runs', () => {
  const runs = ref<Run[]>([])
  const pipelines = ref<Pipeline[]>([])
  const currentRun = ref<RunDetail | null>(null)

  const loading = ref(false)
  const loadingPipelines = ref(false)
  const error = ref<string | null>(null)

  const pipelinesMap = computed(() => {
    const map = new Map<number, Pipeline>()
    for (const p of pipelines.value) {
      map.set(p.id, p)
    }
    return map
  })

  function getPipelineName(pipelineId: number): string {
    return pipelinesMap.value.get(pipelineId)?.name || `Pipeline #${pipelineId}`
  }

  async function fetchPipelines(): Promise<Pipeline[]> {
    const api = getApi()
    loadingPipelines.value = true
    try {
      const list = await api.getPipelines()
      pipelines.value = list
      return list
    } catch {
      return pipelines.value
    } finally {
      loadingPipelines.value = false
    }
  }

  async function fetchRuns(params?: { pipelineId?: number; limit?: number }): Promise<Run[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const list = await api.getRuns(params)
      runs.value = list
      return list
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách lượt chạy'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function refreshRuns(params?: { pipelineId?: number; limit?: number }): Promise<Run[]> {
    const api = getApi()
    try {
      const list = await api.getRuns(params)
      runs.value = list
      return list
    } catch {
      return runs.value
    }
  }

  async function fetchRun(id: number): Promise<RunDetail> {
    const api = getApi()
    const detail = await api.getRun(id)
    currentRun.value = detail
    return detail
  }

  async function triggerRun(pipelineId: number): Promise<Run> {
    const api = getApi()
    const newRun = await api.triggerRun(pipelineId)
    const exists = runs.value.some((r) => r.id === newRun.id)
    if (!exists) {
      runs.value.unshift(newRun)
    }
    return newRun
  }

  return {
    runs,
    pipelines,
    currentRun,
    loading,
    loadingPipelines,
    error,
    pipelinesMap,
    getPipelineName,
    fetchPipelines,
    fetchRuns,
    refreshRuns,
    fetchRun,
    triggerRun
  }
})
