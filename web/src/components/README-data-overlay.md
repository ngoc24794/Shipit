# Data, Overlay & Feedback Components (D1 Terminal-native)

Component reference for the gallery (`/dev/components`) and view integrations.

## Overlays & Positioning

### `useFloating(anchorRef, floatingRef, isOpen, options?)`
Located at `src/composables/useFloating.ts`. Positions teleported floating elements (`AppTooltip`, `AppPopover`, `AppDropdownMenu`, `ContextMenu`) relative to an `HTMLElement` or `VirtualPoint { x, y }`, with viewport clamping and automatic flip.
- **Options**: `{ placement?: Ref<FloatingPlacement> | FloatingPlacement, offset?: number, flip?: boolean, padding?: number }`
- **Returns**: `{ x, y, placement, updatePosition }`

### `AppDialog`
Modal dialog with D1 frame (`--border-strong`, `--shadow-overlay`), focus trap, `Esc`/overlay close, focus restoration, body scroll lock without layout shift, and `110ms` transition.
- **Props**: `modelValue: boolean`, `title?: string`, `maxWidth?: 'sm' | 'md' | 'lg' | 'xl' | 'full'` (default `'md'`), `size?: DialogSize`, `closeOnOverlay?: boolean` (default `true`), `closeOnEsc?: boolean` (default `true`)
- **Slots**: `default`, `header`, `title`, `footer`
- **Events**: `update:modelValue(boolean)`, `close()`
```vue
<AppDialog v-model="open" title="Chi tiết" size="md">
  <p>Nội dung modal</p>
  <template #footer><AppButton @click="open = false">Đóng</AppButton></template>
</AppDialog>
```

### `ConfirmDialog`
Confirmation dialog built on `AppDialog` with optional typed-name confirmation for destructive actions.
- **Props**: `modelValue: boolean`, `title?: string`, `message?: string`, `confirmText?: string`, `cancelText?: string`, `loading?: boolean`, `danger?: boolean`, `confirmValue?: string`, `confirmInputLabel?: string`, `confirmInputPlaceholder?: string`
- **Slots**: `default`
- **Events**: `update:modelValue(boolean)`, `confirm()`, `cancel()`

### `AppDrawer`
Side panel (`left` or `right`) with the same focus trap, `Esc`/overlay close, focus restoration, and scroll lock as `AppDialog`.
- **Props**: `modelValue: boolean`, `title?: string`, `side?: 'left' | 'right'` (default `'right'`), `size?: 'sm' | 'md' | 'lg'` (default `'md'`), `closeOnOverlay?: boolean`, `closeOnEsc?: boolean`
- **Slots**: `default`, `header`, `title`, `footer`
- **Events**: `update:modelValue(boolean)`, `close()`

### `AppTooltip`
Teleported tooltip with `role="tooltip"` and `aria-describedby`.
- **Props**: `text?: string`, `placement?: FloatingPlacement` (default `'top'`), `delay?: number` (default `150`), `disabled?: boolean`
- **Slots**: `default` (trigger), `content`

### `AppPopover`
Floating panel anchored to a trigger slot.
- **Props**: `modelValue?: boolean`, `placement?: FloatingPlacement` (default `'bottom-start'`), `closeOnClickOutside?: boolean`, `closeOnEsc?: boolean`
- **Slots**: `trigger({ open, toggle, close })`, `default({ close, open })`
- **Events**: `update:modelValue(boolean)`, `open()`, `close()`

### `AppDropdownMenu` & `ContextMenu`
Menu with `role="menu"`, `menuitem`, `separator`, icons, shortcuts, disabled/danger items, arrow-key roving, and typeahead.
- **Props (`AppDropdownMenu`)**: `items: MenuItem[]`, `placement?: FloatingPlacement` (default `'bottom-end'`), `label?: string`
- **Props (`ContextMenu`)**: `items: MenuItem[]`, `disabled?: boolean`, `label?: string`
- **Events**: `select(item: MenuItem)`
- **MenuItem**: `{ id: string, label?: string, icon?: Component, shortcut?: string, disabled?: boolean, danger?: boolean, separator?: boolean }`

---

## Feedback & Status

### `AppToast` & `useToast()`
D1 console-line toast stack (`●`, `▲`, `✕`, `ℹ`) with pause-on-hover, optional action button, `aria-live="polite"`, and a persistent console history drawer.
- **Composable (`useToast`)**:
  - Reactive state: `toasts`, `entries`, `unreadCount`, `latestEntry`, `drawerOpen`
  - Methods: `add(msg, type?, durationOrOpts?, opts?)`, `success`, `error`, `warn`, `info`, `remove(id)`, `pause(id)`, `resume(id)`, `clear()`, `markAllRead()`, `openDrawer()`, `closeDrawer()`, `toggleDrawer()`

### `StatusBadge`
Compact D1 glyph + label badge (`● Thành công`, `▲ Đang chạy` with `.live-pulse`, `✕ Thất bại`, `◌ Đang chờ`, `○ Đã huỷ`, `─ Bỏ qua`, `○ Chưa chạy`).
- **Props**: `status: RunStatus | StepStatus`, `size?: 'sm' | 'md'`, `label?: string`

### `EmptyState` & `ErrorState`
- `EmptyState` props: `title?: string`, `description?: string`, `icon?: Component`; slots: `icon`, `title`, `description`, `action`.
- `ErrorState` props: `title?: string`, `message?: string`, `retryLabel?: string`, `retrying?: boolean`, `showRetry?: boolean`; slots: `title`, `default`, `action`; events: `retry()`.

### `AppSkeleton`
Dim block-character skeleton (`████`) respecting `prefers-reduced-motion`.
- **Props**: `variant?: 'line' | 'rows' | 'table' | 'card'`, `count?: number`, `columns?: number`

### `AppProgressBar`
Determinate/indeterminate progress bar with ASCII `sr-only` representation.
- **Props**: `value?: number`, `max?: number`, `indeterminate?: boolean`, `label?: string`, `showPercent?: boolean`, `variant?: 'phosphor' | 'accent' | 'warn' | 'danger'`

### `AppBadge`
Compact count/label badge.
- **Props**: `variant?: 'default' | 'phosphor' | 'accent' | 'warn' | 'danger'`, `size?: 'sm' | 'md'`, `count?: number | string`
- **Slots**: `default`

---

## Navigation, Tables & Wizards

### `AppTabs`
D1 tab bar with `ArrowLeft`/`ArrowRight`/`Home`/`End`, optional counts, and optional URL query sync.
- **Props**: `tabs: TabItem[]` (`{ id, label, count?, disabled? }`), `modelValue: string`, `queryParam?: string`, `ariaLabel?: string`
- **Events**: `update:modelValue(string)`, `change(string)`

### `AppBreadcrumb`
- **Props**: `items: BreadcrumbItem[]` (`{ label, to?, icon? }`)

### `AppTable` & `SortableHeader`
Sticky-header D1 table with `nowrap` ellipsis cells, `density`, `loading` skeleton rows, `empty`/`error` states, optional `rows` + `selectable` glyph checkboxes (`[x]`/`[ ]`/`[-]`) + `keyboardNav` (`j`/`k`/`ArrowUp`/`ArrowDown`/`Enter` + blinking `▌` caret).
- **Props (`AppTable`)**: `bordered?: boolean`, `density?: 'compact' | 'normal'`, `loading?: boolean`, `skeletonRows?: number`, `skeletonCols?: number`, `empty?: boolean`, `emptyTitle?: string`, `emptyDescription?: string`, `error?: string | null`, `rows?: any[]`, `rowKey?: string | ((row, idx) => string | number)`, `selectable?: boolean`, `selectedKeys?: Array<string | number>`, `activeIndex?: number`, `keyboardNav?: boolean`
- **Slots**: `header`, `header-columns`, `row({ row, index, active })`, `default`, `empty`, `error`
- **Events**: `update:selectedKeys`, `update:activeIndex`, `row-activate(row, index)`, `retry()`
- **Props (`SortableHeader`)**: `label: string`, `sortKey: string`, `sortBy: string | null`, `sortDir: SortDirection`, `align?: 'left' | 'center' | 'right'`
- **Events (`SortableHeader`)**: `sort(key: string)`

### `DataToolbar` & `DataPagination`
D1-styled toolbar and pagination bar with backward-compatible props/emits.

### `FormStepper` & `FormWizardLayout`
D1 wizard step tabs with free step switching, per-step error marks (`!`), and keyboard shortcut hint (`[←/→]`).
