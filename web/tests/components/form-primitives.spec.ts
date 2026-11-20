import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import AppButton from '../../src/components/AppButton.vue'
import ButtonGroup from '../../src/components/ButtonGroup.vue'
import SegmentedControl from '../../src/components/SegmentedControl.vue'
import AppInput from '../../src/components/AppInput.vue'
import AppTextarea from '../../src/components/AppTextarea.vue'
import AppSecretInput from '../../src/components/AppSecretInput.vue'
import AppSelect from '../../src/components/AppSelect.vue'
import AppCombobox from '../../src/components/AppCombobox.vue'
import AppCheckbox from '../../src/components/AppCheckbox.vue'
import AppRadioGroup from '../../src/components/AppRadioGroup.vue'
import AppRadio from '../../src/components/AppRadio.vue'
import AppSwitch from '../../src/components/AppSwitch.vue'
import AppField from '../../src/components/AppField.vue'
import AppTag from '../../src/components/AppTag.vue'
import AppKbd from '../../src/components/AppKbd.vue'
import AppSpinner from '../../src/components/AppSpinner.vue'

describe('Form Primitives (D1 Terminal-native)', () => {
  describe('AppButton & ButtonGroup & SegmentedControl', () => {
    it('renders variants, loading state, kbd hint, and icon-only aria-label', () => {
      const btn = mount(AppButton, {
        props: { variant: 'danger', size: 'sm', kbd: 'x', loading: true },
        slots: { default: 'Huỷ' }
      })
      expect(btn.attributes('aria-busy')).toBe('true')
      expect(btn.attributes('disabled')).toBeDefined()
      expect(btn.findComponent(AppSpinner).exists()).toBe(true)
      expect(btn.findComponent(AppKbd).text()).toBe('x')

      const iconBtn = mount(AppButton, {
        props: { iconOnly: true, ariaLabel: 'Xoá mục' },
        slots: { default: 'X' }
      })
      expect(iconBtn.attributes('aria-label')).toBe('Xoá mục')
    })

    it('renders ButtonGroup with role=group', () => {
      const grp = mount(ButtonGroup, {
        props: { ariaLabel: 'Nhóm nút' },
        slots: { default: '<button>A</button><button>B</button>' }
      })
      expect(grp.attributes('role')).toBe('group')
      expect(grp.attributes('aria-label')).toBe('Nhóm nút')
    })

    it('SegmentedControl supports v-model and arrow key navigation skipping disabled options', async () => {
      const val = ref('all')
      const wrapper = mount({
        components: { SegmentedControl },
        setup() {
          const options = [
            { label: 'Tất cả', value: 'all' },
            { label: 'Pipeline', value: 'pipeline', disabled: true },
            { label: 'Job', value: 'job' }
          ]
          return { val, options }
        },
        template: '<SegmentedControl v-model="val" :options="options" aria-label="Loại" />'
      })

      const radios = wrapper.findAll('[role="radio"]')
      expect(radios).toHaveLength(3)
      expect(radios[0].attributes('aria-checked')).toBe('true')

      await radios[0].trigger('keydown', { key: 'ArrowRight' })
      expect(val.value).toBe('job')
      expect(radios[2].attributes('aria-checked')).toBe('true')

      await radios[2].trigger('keydown', { key: 'Home' })
      expect(val.value).toBe('all')
    })
  })

  describe('AppInput, AppTextarea & AppSecretInput', () => {
    it('AppInput supports v-model, clearable, maxlength counter, and number stepper keys', async () => {
      const text = ref('hello')
      const wrapper = mount({
        components: { AppInput },
        setup: () => ({ text }),
        template: '<AppInput v-model="text" clearable :maxlength="10" show-count />'
      })

      expect(wrapper.text()).toContain('5/10')
      await wrapper.get('button[aria-label="Xoá nội dung"]').trigger('click')
      expect(text.value).toBe('')

      const num = ref('5')
      const numWrap = mount({
        components: { AppInput },
        setup: () => ({ num }),
        template: '<AppInput v-model="num" type="number" :min="0" :max="10" :step="2" />'
      })
      await numWrap.get('input').trigger('keydown', { key: 'ArrowUp' })
      expect(num.value).toBe('7')
      await numWrap.get('input').trigger('keydown', { key: 'ArrowDown' })
      expect(num.value).toBe('5')
    })

    it('AppTextarea supports v-model and character counter', async () => {
      const val = ref('abc')
      const wrapper = mount({
        components: { AppTextarea },
        setup: () => ({ val }),
        template: '<AppTextarea v-model="val" :maxlength="50" show-count />'
      })
      expect(wrapper.text()).toContain('3/50')
      await wrapper.get('textarea').setValue('abcdef')
      expect(val.value).toBe('abcdef')
    })

    it('AppSecretInput toggles password visibility', async () => {
      const wrapper = mount(AppSecretInput, { props: { modelValue: 'secret' } })
      const input = wrapper.get('input')
      expect(input.attributes('type')).toBe('password')
      await wrapper.get('button').trigger('click')
      expect(input.attributes('type')).toBe('text')
      expect(wrapper.get('button').text()).toContain('[Ẩn]')
    })
  })

  describe('AppSelect & AppCombobox', () => {
    it('AppSelect works as custom combobox with ARIA and keyboard navigation', async () => {
      const val = ref('a')
      const wrapper = mount({
        components: { AppSelect },
        setup: () => ({
          val,
          options: [
            { label: 'Alpha', value: 'a' },
            { label: 'Beta', value: 'b', disabled: true },
            { label: 'Gamma', value: 'c' }
          ]
        }),
        template: '<AppSelect v-model="val" :options="options" />'
      })

      const combo = wrapper.get('[role="combobox"]')
      expect(combo.attributes('aria-expanded')).toBe('false')

      await combo.trigger('keydown', { key: 'ArrowDown' })
      expect(combo.attributes('aria-expanded')).toBe('true')
      expect(combo.attributes('aria-activedescendant')).toBeDefined()

      // Move from Alpha (0) -> skips disabled Beta (1) -> Gamma (2)
      await combo.trigger('keydown', { key: 'ArrowDown' })
      await combo.trigger('keydown', { key: 'Enter' })
      expect(val.value).toBe('c')
      expect(combo.attributes('aria-expanded')).toBe('false')
    })

    it('AppCombobox supports filtering, multiple chips, creatable, and 500 options', async () => {
      const selected = ref<string[]>(['tag1'])
      const bigOptions = Array.from({ length: 500 }, (_, i) => ({
        label: `Option ${i + 1}`,
        value: `opt-${i + 1}`
      }))
      bigOptions.unshift({ label: 'tag1', value: 'tag1' })

      const wrapper = mount({
        components: { AppCombobox },
        setup: () => ({ selected, bigOptions }),
        template: '<AppCombobox v-model="selected" :options="bigOptions" multiple searchable creatable />'
      })

      expect(wrapper.text()).toContain('tag1')
      await wrapper.get('[role="combobox"]').trigger('click')

      const searchInput = document.body.querySelector('input[aria-label="Lọc lựa chọn"]') as HTMLInputElement
      expect(searchInput).toBeTruthy()
      searchInput.value = 'new-branch'
      searchInput.dispatchEvent(new Event('input'))
      await wrapper.vm.$nextTick()

      const createOpt = Array.from(document.body.querySelectorAll('[role="option"]')).find((el) =>
        el.textContent?.includes('Tạo mới "new-branch"')
      ) as HTMLElement | undefined
      expect(createOpt).toBeTruthy()
      createOpt?.click()
      await wrapper.vm.$nextTick()
      expect(selected.value).toEqual(['tag1', 'new-branch'])

      // Escape closes
      await wrapper.get('[role="combobox"]').trigger('keydown', { key: 'Escape' })
    })

    it('displays matching option label for empty-string, 0, and false values, and placeholder only when no option matches', async () => {
      const emptyWrap = mount(AppSelect, {
        props: {
          modelValue: '',
          options: [
            { label: 'Tất cả trạng thái', value: '' },
            { label: 'Thành công', value: 'success' }
          ]
        }
      })
      expect(emptyWrap.get('[role="combobox"]').text()).toContain('Tất cả trạng thái')
      expect(emptyWrap.get('[role="combobox"]').text()).not.toContain('Chọn...')

      const zeroWrap = mount(AppCombobox, {
        props: {
          modelValue: 0,
          options: [
            { label: 'Không giới hạn', value: 0 },
            { label: '10', value: 10 }
          ]
        }
      })
      expect(zeroWrap.get('[role="combobox"]').text()).toContain('Không giới hạn')

      const falseWrap = mount(AppCombobox, {
        props: {
          modelValue: false,
          options: [
            { label: 'Tắt', value: false },
            { label: 'Bật', value: true }
          ]
        }
      })
      expect(falseWrap.get('[role="combobox"]').text()).toContain('Tắt')

      const noMatchWrap = mount(AppSelect, {
        props: {
          modelValue: '',
          options: [
            { label: 'Alpha', value: 'a' },
            { label: 'Beta', value: 'b' }
          ]
        }
      })
      expect(noMatchWrap.get('[role="combobox"]').text()).toContain('Chọn...')

      await noMatchWrap.setProps({ modelValue: 'nonexistent' })
      expect(noMatchWrap.get('[role="combobox"]').text()).toContain('Chọn...')

      const clearableWrap = mount(AppCombobox, {
        props: {
          modelValue: 'a',
          clearable: true,
          options: [
            { label: 'Tất cả', value: '' },
            { label: 'Alpha', value: 'a' }
          ]
        }
      })
      expect(clearableWrap.find('button[aria-label="Xoá lựa chọn"]').exists()).toBe(true)
      await clearableWrap.get('button[aria-label="Xoá lựa chọn"]').trigger('click')
      expect(clearableWrap.emitted('update:modelValue')?.[0]).toEqual([''])

      // Popup uses width: max-content so long option labels are not clipped to trigger width
      await clearableWrap.get('[role="combobox"]').trigger('click')
      const listbox = document.body.querySelector('[role="listbox"]') as HTMLElement
      const popup = listbox?.parentElement as HTMLElement
      expect(popup.style.width).toBe('max-content')
      clearableWrap.unmount()
    })
  })

  describe('AppCheckbox, AppRadioGroup/AppRadio & AppSwitch', () => {
    it('AppCheckbox supports boolean, indeterminate, array v-model, and Space toggle', async () => {
      const checked = ref(false)
      const wrapper = mount({
        components: { AppCheckbox },
        setup: () => ({ checked }),
        template: '<AppCheckbox v-model="checked" label="Đồng ý" />'
      })
      const box = wrapper.get('[role="checkbox"]')
      expect(box.text()).toBe('[ ]')
      expect(box.attributes('aria-checked')).toBe('false')

      await box.trigger('keydown', { key: ' ' })
      expect(checked.value).toBe(true)
      expect(box.text()).toBe('[x]')

      const indet = mount(AppCheckbox, { props: { indeterminate: true, label: 'Một phần' } })
      expect(indet.get('[role="checkbox"]').text()).toBe('[-]')
      expect(indet.get('[role="checkbox"]').attributes('aria-checked')).toBe('mixed')

      const arr = ref<string[]>([])
      const arrWrap = mount({
        components: { AppCheckbox },
        setup: () => ({ arr }),
        template: '<AppCheckbox v-model="arr" value="a" label="A" />'
      })
      await arrWrap.get('label').trigger('click')
      expect(arr.value).toEqual(['a'])
    })

    it('AppRadioGroup + AppRadio support roving tabindex and arrow keys', async () => {
      const picked = ref('r1')
      const wrapper = mount({
        components: { AppRadioGroup, AppRadio },
        setup: () => ({ picked }),
        template: `
          <AppRadioGroup v-model="picked" aria-label="Chọn">
            <AppRadio value="r1" label="Một" />
            <AppRadio value="r2" label="Hai" />
          </AppRadioGroup>
        `
      })

      const radios = wrapper.findAll('[role="radio"]')
      expect(radios[0].text()).toBe('(o)')
      expect(radios[1].text()).toBe('( )')
      expect(radios[0].attributes('tabindex')).toBe('0')
      expect(radios[1].attributes('tabindex')).toBe('-1')

      await radios[0].trigger('keydown', { key: 'ArrowDown' })
      expect(picked.value).toBe('r2')
      expect(radios[1].attributes('aria-checked')).toBe('true')
    })

    it('AppSwitch renders [ON ]/[OFF] and toggles with Space', async () => {
      const enabled = ref(false)
      const wrapper = mount({
        components: { AppSwitch },
        setup: () => ({ enabled }),
        template: '<AppSwitch v-model="enabled" label="Tự động cuộn" />'
      })

      const sw = wrapper.get('[role="switch"]')
      expect(sw.text()).toBe('[OFF]')
      expect(sw.attributes('aria-checked')).toBe('false')

      await sw.trigger('keydown', { key: ' ' })
      expect(enabled.value).toBe(true)
      expect(sw.text()).toBe('[ON ]')
      expect(sw.attributes('aria-checked')).toBe('true')
    })
  })

  describe('AppField & AppTag', () => {
    it('AppField wires aria-describedby and horizontal layout', () => {
      const wrapper = mount(AppField, {
        props: { label: 'Tên', required: true, error: 'Bắt buộc', layout: 'horizontal' },
        slots: {
          default: '<template #default="{ id, ariaDescribedby }"><input :id="id" :aria-describedby="ariaDescribedby" /></template>'
        }
      })
      expect(wrapper.text()).toContain('*')
      expect(wrapper.get('[role="alert"]').text()).toBe('Bắt buộc')
      const input = wrapper.get('input')
      expect(input.attributes('aria-describedby')).toContain('-error')
    })

    it('AppTag emits remove when removable', async () => {
      const tag = mount(AppTag, {
        props: { removable: true, variant: 'accent' },
        slots: { default: 'dev' }
      })
      await tag.get('button[aria-label="Xoá thẻ"]').trigger('click')
      expect(tag.emitted('remove')).toHaveLength(1)
    })
  })
})
