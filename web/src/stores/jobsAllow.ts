import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { JobAllowEntry, JobAllowEntryInput } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'

export const useJobsAllowStore = defineStore('jobsAllow', () => {
  const entries = ref<JobAllowEntry[]>([])
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)

  const namespaces = computed(() => {
    const nsSet = new Set<string>()
    entries.value.forEach((e) => {
      if (e.kind === 'namespace') {
        nsSet.add(e.name)
      } else if (e.namespace) {
        nsSet.add(e.namespace)
      }
    })
    return Array.from(nsSet).sort()
  })

  function configMapsFor(namespace: string): string[] {
    return entries.value
      .filter((e) => e.kind === 'configmap' && e.namespace === namespace)
      .map((e) => e.name)
      .sort()
  }

  function secretsFor(namespace: string): string[] {
    return entries.value
      .filter((e) => e.kind === 'secret' && e.namespace === namespace)
      .map((e) => e.name)
      .sort()
  }

  function pullSecretsFor(namespace: string): string[] {
    return entries.value
      .filter((e) => e.kind === 'pullsecret' && e.namespace === namespace)
      .map((e) => e.name)
      .sort()
  }

  async function fetchEntries(): Promise<JobAllowEntry[]> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const data = await api.getJobAllowEntries()
      entries.value = data
      return data
    } catch (err: any) {
      error.value = err.message || 'Không thể tải danh sách allowlist'
      throw err
    } finally {
      loading.value = false
    }
  }

  async function createEntry(input: JobAllowEntryInput): Promise<JobAllowEntry> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createJobAllowEntry(input)
      await fetchEntries()
      return created
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = 'Mục này đã tồn tại trong allowlist.'
      } else {
        error.value = err.problem?.detail || err.message || 'Không thể thêm mục vào allowlist'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  async function deleteEntry(id: number): Promise<void> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteJobAllowEntry(id)
      await fetchEntries()
    } catch (err: any) {
      if (err instanceof ApiError && err.status === 409) {
        error.value = err.problem?.detail || 'Không thể xoá mục allowlist đang được Job sử dụng'
      } else {
        error.value = err.message || 'Không thể xoá mục allowlist'
      }
      throw err
    } finally {
      loading.value = false
    }
  }

  return {
    entries,
    loading,
    error,
    namespaces,
    configMapsFor,
    secretsFor,
    pullSecretsFor,
    fetchEntries,
    createEntry,
    deleteEntry
  }
})
