import { ref, watch, type Ref } from 'vue'

export interface UseLinkedFieldsOptions<K extends string> {
  /**
   * When returning true, changes are treated as programmatic hydration and
   * will not trigger auto-sync. Sync state is recalculated from current values.
   */
  suspended?: () => boolean
  /**
   * Optional callback invoked after follower fields are synced from the leader.
   */
  onSync?: (syncedKeys: K[], value: string, leader: K) => void
}

/**
 * Links a group of string fields on a reactive form ref so that when all fields
 * are empty ("armed"), typing into any one of them ("leader") automatically fills
 * the remaining fields with the same value.
 *
 * Editing any field other than the leader immediately disengages auto-sync so the
 * fields can diverge independently. Clearing all linked fields back to empty
 * automatically re-arms auto-sync.
 */
export function useLinkedFields<T extends Record<string, any>, K extends keyof T & string>(
  source: Ref<T>,
  keys: readonly K[],
  options: UseLinkedFieldsOptions<K> = {}
) {
  const isEmpty = (val: unknown): boolean => val === '' || val === null || val === undefined
  const allEmpty = (): boolean => keys.every((k) => isEmpty(source.value[k]))

  const armed = ref(allEmpty())
  const leader = ref<K | null>(null) as Ref<K | null>
  let isSyncing = false

  function reset() {
    armed.value = allEmpty()
    leader.value = null
  }

  watch(
    () => keys.map((k) => source.value[k] as string),
    (newVals, oldVals) => {
      if (isSyncing) return

      if (options.suspended?.()) {
        reset()
        return
      }

      const nowAllEmpty = newVals.every((v) => isEmpty(v))

      const changedIndices: number[] = []
      for (let i = 0; i < keys.length; i++) {
        if (newVals[i] !== oldVals[i]) {
          changedIndices.push(i)
        }
      }

      if (changedIndices.length === 0) return

      // Bulk/programmatic replacement (e.g. form.value = { ... }): never auto-sync
      if (changedIndices.length > 1) {
        armed.value = nowAllEmpty
        leader.value = null
        return
      }

      const changedKey = keys[changedIndices[0]]
      const newValue = newVals[changedIndices[0]] ?? ''

      // If all fields are now empty, re-arm with no leader
      if (nowAllEmpty) {
        armed.value = true
        leader.value = null
        return
      }

      if (!armed.value) {
        return
      }

      if (leader.value === null) {
        leader.value = changedKey
      }

      // User manually edited a different field than the leader: unlink immediately
      if (changedKey !== leader.value) {
        armed.value = false
        leader.value = null
        return
      }

      const followers = keys.filter((k) => k !== changedKey)
      isSyncing = true
      try {
        for (const follower of followers) {
          source.value[follower] = newValue as T[K]
        }
      } finally {
        isSyncing = false
      }

      // If the leader was cleared to empty, all fields are now empty -> re-arm with no leader
      if (allEmpty()) {
        armed.value = true
        leader.value = null
      }

      options.onSync?.([...followers, changedKey], newValue, changedKey)
    },
    { flush: 'sync' }
  )

  return {
    armed,
    leader,
    reset
  }
}
