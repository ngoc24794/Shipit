import { describe, it, expect, beforeEach } from 'vitest'
import { useConsoleLog } from '../../src/composables/useConsoleLog'

describe('useConsoleLog', () => {
  beforeEach(() => {
    const api = useConsoleLog()
    api.clear()
    api.closeDrawer()
  })

  it('logs entries, tracks lastEntry, clears, and toggles drawer', () => {
    const api = useConsoleLog()
    expect(api.lastEntry.value).toBeNull()
    expect(api.drawerOpen.value).toBe(false)

    api.log('Chuyển tới /pipelines', 'info')
    api.log('Thành công', 'success')

    expect(api.entries.value).toHaveLength(2)
    expect(api.lastEntry.value?.message).toBe('Thành công')
    expect(api.lastEntry.value?.level).toBe('success')

    api.toggleDrawer()
    expect(api.drawerOpen.value).toBe(true)

    api.clear()
    expect(api.entries.value).toHaveLength(0)
    expect(api.lastEntry.value).toBeNull()
  })
})
