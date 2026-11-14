import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Job, JobInput, JobValidation, JobPreview, Run } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useJobsStore = defineStore('jobs', () => {
  const jobs = ref<Job[]>([])
  const currentJob = ref<Job | null>(null)
  const loading = ref<boolean>(false)
  const saving = ref<boolean>(false)
  const validating = ref<boolean>(false)
  const previewing = ref<boolean>(false)
  const error = ref<string | null>(null)
  const triggeringId = ref<number | null>(null)

  async function fetchJobs(): Promise<Job[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getJobs()
      jobs.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách Job'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function getJob(id: number): Promise<Job> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const job = await api.getJob(id)
      currentJob.value = job
      return job
    } catch (err: any) {
      error.value = err.message || 'Không thể tải thông tin Job'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createJob(input: JobInput): Promise<Job> {
    const api = getApi()
    saving.value = true
    error.value = null
    try {
      const created = await api.createJob(input)
      await fetchJobs()
      return created
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Job với tên này đã tồn tại'
      } else {
        error.value = err.message || 'Không thể tạo Job'
      }
      throw err
    } finally {
      saving.value = false
    }
  }

  async function updateJob(id: number, input: JobInput): Promise<Job> {
    const api = getApi()
    saving.value = true
    error.value = null
    try {
      const updated = await api.updateJob(id, input)
      await fetchJobs()
      return updated
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Job với tên này đã tồn tại'
      } else {
        error.value = err.message || 'Không thể cập nhật Job'
      }
      throw err
    } finally {
      saving.value = false
    }
  }

  async function deleteJob(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteJob(id)
      await fetchJobs()
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Không thể xoá Job khi đang có lượt chạy hoạt động'
      } else {
        error.value = err.message || 'Không thể xoá Job'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function validateJob(input: JobInput): Promise<JobValidation> {
    const api = getApi()
    validating.value = true
    try {
      return await api.validateJob(input)
    } finally {
      validating.value = false
    }
  }

  async function previewJob(input: JobInput): Promise<JobPreview> {
    const api = getApi()
    previewing.value = true
    try {
      return await api.previewJob(input)
    } finally {
      previewing.value = false
    }
  }

  async function triggerJobRun(id: number): Promise<Run> {
    const api = getApi()
    triggeringId.value = id
    error.value = null
    try {
      const run = await api.triggerJobRun(id)
      await fetchJobs()
      return run
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Job đang có lượt chạy hoạt động'
      } else {
        error.value = err.message || 'Không thể kích hoạt lượt chạy'
      }
      throw err
    } finally {
      triggeringId.value = null
    }
  }

  return {
    jobs,
    currentJob,
    loading,
    saving,
    validating,
    previewing,
    error,
    triggeringId,
    fetchJobs,
    getJob,
    createJob,
    updateJob,
    deleteJob,
    validateJob,
    previewJob,
    triggerJobRun
  }
})
