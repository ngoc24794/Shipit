# Shipit D1 Terminal-native Design Tokens

## Theme Switching
- Dark theme ("Phosphor Refined", default) is active when `<html class="dark">` is present (managed by `web/src/composables/useTheme.ts`).
- Light theme ("Paper Terminal") is active when `.dark` is absent on `<html>`.

## Color Tokens
| Token | Dark (`.dark`) | Light (`:root`) | Tailwind Class | Usage |
|---|---|---|---|---|
| `--bg` | `#070b09` | `#f4f5f0` | `bg-bg` | Primary canvas |
| `--bg-elevated` | `#0b110e` | `#ffffff` | `bg-bg-elevated` | Topbar, rail, panels, modals |
| `--bg-surface` | `#101915` | `#eaece5` | `bg-bg-surface` | Table headers, status bar, footers |
| `--bg-hover` | `#15221c` | `#e1e4db` | `bg-bg-hover` | Row/item hover |
| `--bg-selected` | `#162921` | `#d6e4dc` | `bg-bg-selected` | Selected row / active nav |
| `--bg-input` | `#050807` | `#fbfcf9` | `bg-bg-input` | Form inputs, log body |
| `--border` | `#1e2f27` | `#c8cfc3` | `border-border` | 1px structural dividers |
| `--border-strong` | `#2d473b` | `#939e8d` | `border-border-strong` | Emphasized borders |
| `--border-focus` | `#38bdf8` | `#0369a1` | `border-border-focus` | Focus ring & active inputs |
| `--fg` | `#d6e6dc` | `#18201b` | `text-fg` | Body text |
| `--fg-strong` | `#f0f7f3` | `#0b100d` | `text-fg-strong` | Headings & emphasized text |
| `--fg-muted` | `#7c9688` | `#526058` | `text-fg-muted` | Secondary labels, timestamps |
| `--fg-dim` | `#4c6357` | `#7f8d84` | `text-fg-dim` | Line numbers, carets, hints |
| `--phosphor` | `#34d399` | `#047857` | `text-phosphor` | Primary CTA, success, live pulse |
| `--accent` | `#38bdf8` | `#0369a1` | `text-accent` | Running status, links, active pane |
| `--warn` | `#f59e0b` | `#b45309` | `text-warn` | Queued, warnings |
| `--danger` | `#f87171` | `#b91c1c` | `text-danger` | Failed, stderr, destructive actions |

### Legacy Aliases
`--card` (`--bg-elevated`), `--muted` (`--bg-surface`), `--muted-fg` (`--fg-muted`), `--primary` (`--phosphor`), `--on-primary` (`--bg`), `--success` (`--phosphor`), `--info` (`--accent`), `--ring` (`--border-focus`), `--log-bg` (`--bg-input`).

## Typography, Spacing, Radius & Motion
- **Fonts (offline local stacks)**: `--font-mono` (`Cascadia Mono`, `JetBrains Mono`, `Fira Code`, `Consolas`, monospace), `--font-sans` (`Segoe UI`, `system-ui`, sans-serif). Body default is `13px` monospace with `tabular-nums`.
- **Spacing**: `--sp-1..6` (`4px`, `8px`, `12px`, `16px`, `20px`, `24px`).
- **Radius**: `--radius-sm`, `--radius-md`, `--radius-lg`, `--radius` = `2px`.
- **Motion**: `--dur-snap: 80ms`, `--dur-fast: 110ms`, `--dur-pane: 140ms`, `--ease-snap: cubic-bezier(0.2, 0, 0, 1)`, `--ease-step: steps(4, end)`. Disabled automatically under `prefers-reduced-motion: reduce`.
