import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ChipInput from '../../src/components/job/ChipInput.vue'
import YamlPreview from '../../src/components/job/YamlPreview.vue'
import AllowlistMultiSelect from '../../src/components/job/AllowlistMultiSelect.vue'

describe('Job UI Components', () => {
  describe('ChipInput', () => {
    it('renders chips and adds new chip on Enter key', async () => {
      const wrapper = mount(ChipInput, {
        props: {
          modelValue: ['arg1', 'arg2']
        }
      })

      expect(wrapper.text()).toContain('arg1')
      expect(wrapper.text()).toContain('arg2')

      const input = wrapper.find('input')
      await input.setValue('arg3')
      await input.trigger('keydown', { key: 'Enter' })

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted).toBeDefined()
      expect(emitted![0][0]).toEqual(['arg1', 'arg2', 'arg3'])
    })

    it('adds new chip on comma key', async () => {
      const wrapper = mount(ChipInput, {
        props: {
          modelValue: ['start']
        }
      })

      const input = wrapper.find('input')
      await input.setValue('next')
      await input.trigger('keydown', { key: ',' })

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted![0][0]).toEqual(['start', 'next'])
    })

    it('removes last chip on Backspace when input is empty', async () => {
      const wrapper = mount(ChipInput, {
        props: {
          modelValue: ['first', 'second']
        }
      })

      const input = wrapper.find('input')
      await input.setValue('')
      await input.trigger('keydown', { key: 'Backspace' })

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted![0][0]).toEqual(['first'])
    })

    it('removes specific chip when clicking its remove button', async () => {
      const wrapper = mount(ChipInput, {
        props: {
          modelValue: ['alpha', 'beta', 'gamma']
        }
      })

      const removeBtn = wrapper.find('button[aria-label="Xoá đối số beta"]')
      expect(removeBtn.exists()).toBe(true)
      await removeBtn.trigger('click')

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted![0][0]).toEqual(['alpha', 'gamma'])
    })

    it('splits pasted text with commas or newlines into multiple chips', async () => {
      const wrapper = mount(ChipInput, {
        props: {
          modelValue: []
        }
      })

      const input = wrapper.find('input')
      const pasteEvent = new Event('paste')
      Object.defineProperty(pasteEvent, 'clipboardData', {
        value: {
          getData: () => 'migrate, --module, files\n--verbose'
        }
      })
      input.element.dispatchEvent(pasteEvent)

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted).toBeDefined()
      expect(emitted![0][0]).toEqual(['migrate', '--module', 'files', '--verbose'])
    })
  })

  describe('AllowlistMultiSelect', () => {
    it('only lists unselected allowed options in dropdown and adds to modelValue', async () => {
      const wrapper = mount(AllowlistMultiSelect, {
        attachTo: document.body,
        props: {
          modelValue: ['cm-1'],
          options: ['cm-1', 'cm-2', 'cm-3']
        }
      })

      expect(wrapper.text()).toContain('cm-1')

      // Open custom AppSelect combobox
      const trigger = wrapper.find('[role="combobox"]')
      await trigger.trigger('click')

      const options = Array.from(document.body.querySelectorAll('[role="option"]'))
      const optionTexts = options.map((o) => o.textContent?.trim() || '')
      expect(optionTexts.some((t) => t.includes('cm-2'))).toBe(true)
      expect(optionTexts.some((t) => t.includes('cm-3'))).toBe(true)
      expect(optionTexts.some((t) => t.includes('cm-1'))).toBe(false)

      const cm2Option = options.find((o) => o.textContent?.includes('cm-2')) as HTMLElement
      cm2Option.click()
      await wrapper.vm.$nextTick()

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted![0][0]).toEqual(['cm-1', 'cm-2'])
      wrapper.unmount()
    })

    it('removes item when clicking remove button', async () => {
      const wrapper = mount(AllowlistMultiSelect, {
        props: {
          modelValue: ['sec-a', 'sec-b'],
          options: ['sec-a', 'sec-b']
        }
      })

      const removeBtn = wrapper.find('button[aria-label="Bỏ chọn sec-a"]')
      expect(removeBtn.exists()).toBe(true)
      await removeBtn.trigger('click')

      const emitted = wrapper.emitted('update:modelValue')
      expect(emitted![0][0]).toEqual(['sec-b'])
    })
  })

  describe('YamlPreview', () => {
    it('renders tasks tabs, switches active YAML, and copies YAML to clipboard', async () => {
      const writeTextMock = vi.fn().mockResolvedValue(undefined)
      Object.assign(navigator, {
        clipboard: {
          writeText: writeTextMock
        }
      })

      const tasks = [
        { position: 1, label: 'files', yaml: 'apiVersion: batch/v1\nkind: Job\n# files task' },
        { position: 2, label: 'users', yaml: 'apiVersion: batch/v1\nkind: Job\n# users task' }
      ]

      const wrapper = mount(YamlPreview, {
        props: {
          tasks
        }
      })

      // Initial tab is files
      expect(wrapper.text()).toContain('files')
      expect(wrapper.text()).toContain('users')
      expect(wrapper.text()).toContain('# files task')

      // Switch to tab 2
      const tabButtons = wrapper.findAll('button').filter((b) => b.text().includes('users'))
      expect(tabButtons.length).toBeGreaterThan(0)
      await tabButtons[0].trigger('click')

      expect(wrapper.text()).toContain('# users task')

      // Click copy button
      const copyBtn = wrapper.find('button[aria-label*="Sao chép"]')
      expect(copyBtn.exists()).toBe(true)
      await copyBtn.trigger('click')

      expect(writeTextMock).toHaveBeenCalledWith('apiVersion: batch/v1\nkind: Job\n# users task')
    })
  })
})
