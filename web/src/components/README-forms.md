# Shipit Terminal-native Form Primitives (`web/src/components/`)

All primitives use D1 Terminal-native tokens (`2px` radius, `1px` structural borders, monospace typography, `28px` `md` / `24px` `sm` dense height, `80ms` snap feedback, and `--border-focus` ring).

## 1. `AppButton.vue`
- **Props**: `variant` (`'primary' | 'secondary' | 'ghost' | 'danger' | 'link'`), `size` (`'sm' | 'md' | 'lg'`), `type` (`'button' | 'submit' | 'reset'`), `loading`, `disabled`, `ariaLabel`, `iconOnly`, `kbd`, `block`, `as` (`'button' | 'a' | 'router-link'`), `to`, `href`.
- **Slots**: `leading`, default, `trailing`.
- **Example**:
  ```vue
  <AppButton variant="primary" size="md" kbd="n" :loading="saving">Lưu</AppButton>
  <AppButton variant="ghost" icon-only aria-label="Xoá"><Trash2 class="w-3.5 h-3.5" /></AppButton>
  ```

## 2. `ButtonGroup.vue` & `SegmentedControl.vue`
- **`ButtonGroup`**: `ariaLabel`, `attached` (default `true`). Wraps `<AppButton>` children.
- **`SegmentedControl`**: `modelValue`, `options: { label, value, disabled? }[]`, `ariaLabel`, `size` (`'sm' | 'md'`), `disabled`. Emits `update:modelValue`, `change`. Supports `ArrowLeft`/`ArrowRight`/`Home`/`End`.
  ```vue
  <SegmentedControl v-model="kind" :options="[{ label: 'Tất cả', value: '' }, { label: 'Pipeline', value: 'pipeline' }]" aria-label="Loại" />
  ```

## 3. `AppInput.vue`, `AppTextarea.vue`, `AppSecretInput.vue`
- **`AppInput`**: `modelValue`, `type` (`text|email|url|search|number|password`), `size` (`'sm'|'md'`), `clearable`, `maxlength`, `showCount`, `selectOnFocus`, `help`, `error`, `min`, `max`, `step`, `disabled`, `readonly`, `ariaDescribedby`. Slots: `prefix`, `suffix`. Emits: `update:modelValue`, `clear`, `focus`, `blur`.
- **`AppTextarea`**: `modelValue`, `rows`, `autosize`, `maxlength`, `showCount`, `help`, `error`, `disabled`, `readonly`, `ariaDescribedby`.
- **`AppSecretInput`**: `modelValue`, `hasExistingValue`, `size`, `error`, `disabled`, `ariaDescribedby`. Custom `[Hiện]` / `[Ẩn]` toggle button.

## 4. `AppSelect.vue` & `AppCombobox.vue`
- **`AppSelect`** (backward-compatible custom combobox): `modelValue`, `options: SelectOption[]`, `id`, `name`, `disabled`, `error`, `ariaDescribedby`, `placeholder`, `size`, `searchable`. Emits: `update:modelValue`, `change`.
- **`AppCombobox`**: Full-featured combobox with `searchable`, `clearable`, `multiple` (renders `AppTag` chips), `creatable` (emits `create`), `groups: ComboboxGroup[]`, `loading`, `emptyText`, `size`. Teleported to `body` via `usePopover` (`src/composables/usePopover.ts`). Full ARIA (`combobox`, `listbox`, `option`, `aria-activedescendant`, `aria-expanded`) and keyboard (`ArrowUp/Down`, `PageUp/Down`, `Home/End`, `Enter`, `Escape`, `Tab`, type-ahead).

## 5. `AppCheckbox.vue`, `AppRadioGroup.vue`, `AppRadio.vue`, `AppSwitch.vue`
- **`AppCheckbox`**: `[x]` / `[ ]` / `[-]` glyph. Supports `v-model` boolean or array (with `value` prop), `indeterminate`, `label`, `description`, `disabled`, `error`. Space toggles.
- **`AppRadioGroup` + `AppRadio`**: `(o)` / `( )` glyph, roving tabindex, `orientation` (`'vertical' | 'horizontal'`), arrow-key navigation.
- **`AppSwitch`**: `[ON ]` / `[OFF]` glyph, `role="switch"`, `aria-checked`, Space/Enter toggles.

## 6. `AppField.vue`, `AppTag.vue`, `AppKbd.vue`, `AppSpinner.vue`
- **`AppField`**: `label`, `id`, `required`, `description` / `help`, `error`, `layout` (`'vertical' | 'horizontal'`). Scoped slot provides `{ id, error, ariaDescribedby }`.
- **`AppTag`**: `variant` (`'default' | 'accent' | 'phosphor' | 'warn' | 'danger'`), `removable`, `disabled`, `size`. Emits `remove`.
- **`AppKbd`**: `keys` prop or default slot.
- **`AppSpinner`**: Braille terminal spinner (`size`: `'sm' | 'md' | 'lg'`, `label`).
