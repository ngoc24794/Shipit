# Shipit Shell & Business Components (D1 Terminal-native)

## Shell & Navigation

### `AppLayout` (`src/layouts/AppLayout.vue`)
Fixed `100vh` terminal shell with zero page-level scroll:
- **Top command bar (`40px`)**: brand prompt (`$ SHIPIT`), route breadcrumb, `:` command-line input (`run <name>`, `goto <route>`, `filter <status|all>`, `theme <dark|light>`, `clear`, `help`), `GlobalSearch` (`Ctrl+K`), `?` shortcuts trigger, theme toggle, user badge, logout.
- **Left rail (`196px` / `52px` collapsed)**: grouped navigation (`Vận hành`, `Thiết lập`) with `g r/p/j/s/c/a` hints and collapse toggle.
- **Workspace**: `min-w-[1024px]` inner container with `overflow-y-hidden` and `110ms` view-enter transition.
- **Bottom status bar (`26px`)**: live pulse dot, running-run count from `useRunsStore()`, current route context, clickable last console log line (toggles drawer via `useConsoleLog()`).

**Slots:** `default` (page content)

---

### `useConsoleLog` (`src/composables/useConsoleLog.ts`)
Shared interface for the bottom status bar and toast/console history drawer:
- `entries: Ref<ConsoleLogEntry[]>`
- `lastEntry: ComputedRef<ConsoleLogEntry | null>`
- `drawerOpen: Ref<boolean>`
- `log(message, level?)`, `clear()`, `openDrawer()`, `closeDrawer()`, `toggleDrawer()`

---

### `useShortcuts` (`src/composables/useShortcuts.ts`) & `ShortcutsOverlay` (`src/components/ShortcutsOverlay.vue`)
Central context-aware keyboard shortcut registry supporting single keys (`j`, `k`, `n`, `r`, `x`, `/`, `:`, `?`, `Esc`) and two-key chords (`g r`, `g p`, `g j`, `g s`, `g c`, `g a`). Automatically ignores inputs/textareas/selects/comboboxes, unregisters scoped bindings on unmount, and tracks conflicts in `conflicts`.

**`ShortcutsOverlay` Props:** `modelValue?: boolean`  
**`ShortcutsOverlay` Events:** `update:modelValue(boolean)`, `close()`

```vue
<ShortcutsOverlay />
```

---

## Layout & Utility Components

### `SplitPane` (`src/components/SplitPane.vue`)
Draggable two-column split container with keyboard resize (`ArrowLeft`/`ArrowRight`/`Home`/`End`, `Shift` for 48px step) and `localStorage` persistence.

| Prop | Type | Default | Description |
|---|---|---|---|
| `storageKey` | `string` | `''` | Optional `localStorage` key |
| `defaultSize` | `number` | `390` | Initial left pane width (px) |
| `minSize` | `number` | `280` | Minimum width (px) |
| `maxSize` | `number` | `580` | Maximum width (px) |
| `ariaLabel` | `string` | `'Điều chỉnh độ rộng bảng điều khiển'` | Separator label |

**Slots:** `left`, `right`  
**Events:** `resize(size: number)`

---

### `KeyValueGrid` (`src/components/KeyValueGrid.vue`) & `CopyButton` (`src/components/CopyButton.vue`)
Compact monospace key-value metadata grid with optional per-row copy buttons.

**`KeyValueGrid` Props:** `items: KeyValueItem[]`, `labelWidth?: string` (`'110px'`)  
**`KeyValueGrid` Slots:** `value-<key|index>`  
**`CopyButton` Props:** `value: string`, `label?: string`, `ariaLabel?: string`, `size?: 'xs' | 'sm'`, `silent?: boolean`  
**`CopyButton` Events:** `copied(value: string)`

---

### `RelativeTime` (`src/components/RelativeTime.vue`) & `DurationText` (`src/components/DurationText.vue`)
Monospace `tabular-nums` timestamp and duration formatters.

- `RelativeTime`: `value?: string | null`, `live?: boolean`
- `DurationText`: `startedAt?: string | null`, `finishedAt?: string | null`, `running?: boolean`

---

## Search & Run Components

### `GlobalSearch` (`src/components/search/GlobalSearch.vue`) & `SearchResults` (`src/components/search/SearchResults.vue`)
D1-styled command palette (`Ctrl+K` / `/`), preserving `useSearchStore()` behavior, arrow/Tab navigation, and match highlighting.

### `LogViewer` (`src/components/LogViewer.vue`)
D1-styled log viewer (`--bg-input`) with severity coloring (`log-err`, `log-warn`, `log-sys`), task/step filter, in-log search with match navigation, follow-tail toggle, line numbers, copy, and download.

### `StepTimeline` (`src/components/StepTimeline.vue`) & `TaskTable` (`src/components/run/TaskTable.vue`)
Vertical tmux-like step tree with `├─`/`└─` branch glyphs and status glyphs (`●`, `▲`, `✕`, `◌`, `○`).

---

## Job / Pipeline Editor Components (`src/components/job/`)
- `TaskCard.vue`: D1-styled task card with `#N` phosphor badge, reorder/duplicate/delete controls, and drag-and-drop support.
- `EnvEditor.vue`: Key/source/value or Secret-ref editor row list.
- `ChipInput.vue`: Multi-token argument input with comma/Enter/paste splitting.
- `AllowlistMultiSelect.vue`: Multi-select allowlist picker with selected chips.
- `YamlPreview.vue`: Tabbed Kubernetes Job YAML preview with copy button.
