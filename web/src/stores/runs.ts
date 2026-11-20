import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { Run, RunDetail, Pipeline, GetRunsParams, PagedResult, TriggerRunOptions } from '../api/types'
import { getApi } from '../api'
import { runJob as apiRunJob } from '../api/runs-job'
import { createSwrChannel, type SwrOptions } from '../utils/swr'

export const useRunsStore = defineStore('runs', () => {
  const runs = ref<Run[]>([])
  const totalRuns = ref(0)
  const pipelines = ref<Pipeline[]>([])
  const jobs = ref<Array<{ id: number; name: string }>>([])
  const currentRun = ref<RunDetail | null>(null)

  const hasLoadedRuns = ref(false)
  const hasLoadedPipelines = ref(false)
  const hasLoadedJobs = ref(false)
  const loading = ref(false)
  const loadingPipelines = ref(false)
  const error = ref<string | null>(null)

  const jobsSwr = createSwrChannel<Array<{ id: number; name: string }>>()
  const pipelinesSwr = createSwrChannel<Pipeline[]>()

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

  function getJobName(jobId?: number | null): string {
    if (!jobId) return 'Job'
    const j = jobs.value.find((item) => item.id === jobId)
    return j?.name || `Job #${jobId}`
  }

  async function fetchJobs(opts?: SwrOptions): Promise<Array<{ id: number; name: string }>> {
    const api = getApi()
    try {
      const result = await jobsSwr.run(
        opts,
        () => hasLoadedJobs.value,
        async () => {
          const list = await api.getJobs()
          const mapped = list.map((j) => ({ id: j.id, name: j.name }))
          jobs.value = mapped
          hasLoadedJobs.value = true
          return mapped
        },
        () => {}
      )
      return result ?? jobs.value
    } catch {
      return jobs.value
    }
  }

  async function fetchPipelines(opts?: SwrOptions): Promise<Pipeline[]> {
    const api = getApi()
    try {
      const result = await pipelinesSwr.run(
        opts,
        () => hasLoadedPipelines.value,
        async () => {
          const list = await api.getPipelines()
          pipelines.value = list
          hasLoadedPipelines.value = true
          return list
        },
        (v) => {
          loadingPipelines.value = v
        }
      )
      return result ?? pipelines.value
    } catch {
      return pipelines.value
    }
  }

  async function fetchRuns(params?: GetRunsParams, opts?: SwrOptions): Promise<Run[]> {
    const api = getApi()
    const showLoading = !opts?.silent && !hasLoadedRuns.value
    if (showLoading) loading.value = true
    error.value = null
    try {
      const list = await api.getRuns(params)
      runs.value = list
      totalRuns.value = list.length
      hasLoadedRuns.value = true
      return list
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách lượt chạy'
      throw err
    } finally {
      if (showLoading) loading.value = false
    }
  }

  async function fetchRunsPaged(params?: GetRunsParams, opts?: SwrOptions): Promise<PagedResult<Run>> {
    const api = getApi()
    const showLoading = !opts?.silent && !hasLoadedRuns.value
    if (showLoading) loading.value = true
    error.value = null
    try {
      if (api.getRunsPaged) {
        const res = await api.getRunsPaged(params)
        runs.value = res.items
        totalRuns.value = res.total
        hasLoadedRuns.value = true
        return res
      }
      const list = await api.getRuns(params)
      runs.value = list
      totalRuns.value = list.length
      hasLoadedRuns.value = true
      return { items: list, total: list.length }
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách lượt chạy'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function refreshRuns(params?: GetRunsParams): Promise<Run[]> {
    const api = getApi()
    try {
      const list = await api.getRuns(params)
      runs.value = list
      totalRuns.value = list.length
      return list
    } catch {
      return runs.value
    }
  }

  async function refreshRunsPaged(params?: GetRunsParams): Promise<PagedResult<Run>> {
    const api = getApi()
    try {
      if (api.getRunsPaged) {
        const res = await api.getRunsPaged(params)
        runs.value = res.items
        totalRuns.value = res.total
        return res
      }
      const list = await api.getRuns(params)
      runs.value = list
      totalRuns.value = list.length
      return { items: list, total: list.length }
    } catch {
      return { items: runs.value, total: totalRuns.value }
    }
  }

  async function fetchRun(id: number): Promise<RunDetail> {
    const api = getApi()
    const detail = await api.getRun(id)
    currentRun.value = detail
    return detail
  }

  async function triggerRun(pipelineId: number, options?: TriggerRunOptions): Promise<Run> {
    const api = getApi()
    const newRun = options?.skipSteps?.length
      ? await api.triggerRun(pipelineId, options)
      : await api.triggerRun(pipelineId)
    const exists = runs.value.some((r) => r.id === newRun.id)
    if (!exists) {
      runs.value.unshift(newRun)
    }
    return newRun
  }

  async function runJob(jobId: number, options?: TriggerRunOptions): Promise<Run> {
    const newRun = options?.skipSteps?.length
      ? await apiRunJob(jobId, options)
      : await apiRunJob(jobId)
    const exists = runs.value.some((r) => r.id === newRun.id)
    if (!exists) {
      runs.value.unshift(newRun)
    }
    return newRun
  }

  async function cancelRun(id: number): Promise<Run> {
    const api = getApi()
    const updated = await api.cancelRun(id)
    if (currentRun.value && currentRun.value.id === id) {
      currentRun.value.status = updated.status
      if (updated.failureReason) {
        currentRun.value.failureReason = updated.failureReason
      }
      if (updated.finishedAt) {
        currentRun.value.finishedAt = updated.finishedAt
      }
    }
    const idx = runs.value.findIndex((r) => r.id === id)
    if (idx !== -1) {
      runs.value[idx] = { ...runs.value[idx], ...updated }
    }
    const pipeline = pipelines.value.find((p) => p.id === updated.pipelineId)
    if (pipeline && pipeline.lastRun?.id === updated.id) {
      pipeline.lastRun = { ...pipeline.lastRun, ...updated }
    }
    return updated
  }

  return {
    runs,
    totalRuns,
    pipelines,
    jobs,
    currentRun,
    loading,
    loadingPipelines,
    error,
    pipelinesMap,
    getPipelineName,
    getJobName,
    fetchJobs,
    fetchPipelines,
    fetchRuns,
    fetchRunsPaged,
    refreshRuns,
    refreshRunsPaged,
    fetchRun,
    triggerRun,
    runJob,
    cancelRun
  }
})
