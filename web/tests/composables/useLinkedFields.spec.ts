import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'
import { useLinkedFields } from '../../src/composables/useLinkedFields'

describe('useLinkedFields composable', () => {
  it('syncs followers from whichever field is edited first when all fields start empty', () => {
    const form = ref({ name: '', imageName: '', deployDeployment: '' })
    const { armed, leader } = useLinkedFields(form, ['name', 'imageName', 'deployDeployment'] as const)

    expect(armed.value).toBe(true)
    expect(leader.value).toBeNull()

    form.value.imageName = 'my-svc'
    expect(leader.value).toBe('imageName')
    expect(form.value.name).toBe('my-svc')
    expect(form.value.deployDeployment).toBe('my-svc')

    form.value.imageName = 'my-svc-v2'
    expect(form.value.name).toBe('my-svc-v2')
    expect(form.value.deployDeployment).toBe('my-svc-v2')
  })

  it('unlinks immediately when a non-leader field is edited and does not overwrite the others', () => {
    const form = ref({ name: '', imageName: '', containerName: '' })
    const { armed, leader } = useLinkedFields(form, ['name', 'imageName', 'containerName'] as const)

    form.value.name = 'dbmigrator'
    expect(form.value.imageName).toBe('dbmigrator')
    expect(form.value.containerName).toBe('dbmigrator')

    // Diverge on containerName
    form.value.containerName = 'migrator-app'
    expect(armed.value).toBe(false)
    expect(leader.value).toBeNull()
    expect(form.value.name).toBe('dbmigrator')
    expect(form.value.imageName).toBe('dbmigrator')

    // Further edits to name do not overwrite the other two
    form.value.name = 'dbmigrator-2'
    expect(form.value.imageName).toBe('dbmigrator')
    expect(form.value.containerName).toBe('migrator-app')
  })

  it('re-arms when all 3 fields are cleared to empty after diverging', () => {
    const form = ref({ name: '', imageName: '', deployDeployment: '' })
    const { armed, leader } = useLinkedFields(form, ['name', 'imageName', 'deployDeployment'] as const)

    form.value.name = 'alpha'
    form.value.imageName = 'beta'
    expect(armed.value).toBe(false)

    form.value.name = ''
    expect(armed.value).toBe(false)
    form.value.imageName = ''
    expect(armed.value).toBe(false)
    form.value.deployDeployment = ''
    expect(armed.value).toBe(true)
    expect(leader.value).toBeNull()

    form.value.deployDeployment = 'gamma'
    expect(leader.value).toBe('deployDeployment')
    expect(form.value.name).toBe('gamma')
    expect(form.value.imageName).toBe('gamma')
  })

  it('clears leader and stays armed when the leader itself is cleared back to empty', () => {
    const form = ref({ name: '', imageName: '', deployDeployment: '' })
    const { armed, leader } = useLinkedFields(form, ['name', 'imageName', 'deployDeployment'] as const)

    form.value.name = 'alpha'
    expect(leader.value).toBe('name')

    form.value.name = ''
    expect(form.value.imageName).toBe('')
    expect(form.value.deployDeployment).toBe('')
    expect(armed.value).toBe(true)
    expect(leader.value).toBeNull()

    // Now a different field can become the leader
    form.value.deployDeployment = 'beta'
    expect(leader.value).toBe('deployDeployment')
    expect(form.value.name).toBe('beta')
    expect(form.value.imageName).toBe('beta')
  })

  it('does not auto-sync during suspended hydration or bulk form replacement', () => {
    const form = ref({ name: '', imageName: '', deployDeployment: '' })
    const suspended = ref(true)
    const onSync = vi.fn()

    const { armed, reset } = useLinkedFields(form, ['name', 'imageName', 'deployDeployment'] as const, {
      suspended: () => suspended.value,
      onSync
    })

    // Simulate hydration
    form.value = { name: 'svc-a', imageName: 'img-a', deployDeployment: 'dep-a' }
    reset()
    suspended.value = false

    expect(armed.value).toBe(false)
    expect(onSync).not.toHaveBeenCalled()

    form.value.name = 'svc-a-edited'
    expect(form.value.imageName).toBe('img-a')
    expect(form.value.deployDeployment).toBe('dep-a')
    expect(onSync).not.toHaveBeenCalled()
  })

  it('invokes onSync callback with synced keys and leader when propagating', () => {
    const form = ref({ name: '', imageName: '', containerName: '' })
    const onSync = vi.fn()
    useLinkedFields(form, ['name', 'imageName', 'containerName'] as const, { onSync })

    form.value.name = 'worker'
    expect(onSync).toHaveBeenCalledTimes(1)
    expect(onSync).toHaveBeenCalledWith(['imageName', 'containerName', 'name'], 'worker', 'name')
  })
})
