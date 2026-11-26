import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import type { JobAllowEntry, JobAllowEntryInput, RbacMutationResult } from '../api/types'
import { ApiError } from '../api/client'
import { getApi } from '../api'
import { createSwrChannel, type SwrOptions } from '../utils/swr'

export const useJobsAllowStore = defineStore('jobsAllow', () => {
  const entries = ref<JobAllowEntry[]>([])
  const hasLoaded = ref<boolean>(false)
  const loading = ref<boolean>(false)
  const error = ref<string | null>(null)
  const entriesSwr = createSwrChannel<JobAllowEntry[]>()

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

  async function fetchEntries(opts?: SwrOptions): Promise<JobAllowEntry[]> {
    const api = getApi()
    const result = await entriesSwr.run(
      opts,
      () => hasLoaded.value,
      async () => {
        error.value = null
        try {
          const data = await api.getJobAllowEntries()
          entries.value = data
          hasLoaded.value = true
          return data
        } catch (err: any) {
          error.value = err.message || 'Không thể tải danh sách allowlist'
          throw err
        }
      },
      (v) => {
        loading.value = v
      }
    )
    return result ?? entries.value
  }

  async function createEntry(input: JobAllowEntryInput): Promise<RbacMutationResult<JobAllowEntry>> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      const created = await api.createJobAllowEntry(input)
      const rbacWarning = api.consumeLastRbacWarning?.() ?? null
      entriesSwr.invalidate()
      await fetchEntries({ force: true, silent: true })
      return { data: created, rbacWarning }
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

  async function deleteEntry(id: number): Promise<RbacMutationResult<void>> {
    const api = getApi()
    loading.value = true
    error.value = null
    try {
      await api.deleteJobAllowEntry(id)
      const rbacWarning = api.consumeLastRbacWarning?.() ?? null
      entriesSwr.invalidate()
      await fetchEntries({ force: true, silent: true })
      return { data: undefined, rbacWarning }
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
