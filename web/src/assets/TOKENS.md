# Shipit D1 Terminal-native Design Tokens

## Theme Switching
- Dark theme ("Phosphor Refined", default) is active when `<html class="dark">` is present (managed by `web/src/composables/useTheme.ts`).
- Light theme ("Paper Terminal") is active when `.dark` is absent on `<html>`.

## Color Tokens & WCAG Contrast Ratios
| Token | Dark (`.dark`) | Light (`:root`) | Dark Contrast (on `--bg-elevated`) | Light Contrast (on `--bg-elevated`) | Usage |
|---|---|---|---|---|---|
| `--bg` | `#121719` | `#f4f5f0` | — | — | Primary canvas |
| `--bg-elevated` | `#181f22` | `#ffffff` | — | — | Topbar, rail, panels, modals |
| `--bg-surface` | `#20292d` | `#eaece5` | — | — | Table headers, status bar, footers |
| `--bg-hover` | `#283439` | `#dfe3d8` | `1.30:1` vs elevated | `1.30:1` vs elevated | Row/item hover |
| `--bg-selected` | `#1e3832` | `#d1e3d9` | `1.33:1` vs elevated | `1.34:1` vs elevated | Selected row / active nav |
| `--bg-input` | `#0e1214` | `#fbfcf9` | — | — | Form inputs, log body |
| `--border` | `#37454a` | `#b8c1b1` | `1.68:1` (`1.82:1` on `--bg`) | `1.86:1` (`1.70:1` on `--bg`) | 1px structural dividers |
| `--border-strong` | `#63767d` | `#7b8774` | `3.51:1` (`3.96:1` on `--bg-input`) | `3.78:1` (`3.67:1` on `--bg-input`) | Input, button, checkbox/radio borders |
| `--border-focus` | `#58ccf2` | `#0369a1` | `9.01:1` | `5.93:1` | Focus ring & active inputs |
| `--fg` | `#e1eae5` | `#161e19` | `13.60:1` (`12.07:1`–`15.33:1` all surfaces) | `17.02:1` (`14.29:1`–`17.02:1` all surfaces) | Body text |
| `--fg-strong` | `#eff5f2` | `#0b100d` | `14.8:1` | `18.8:1` | Headings & emphasized text |
| `--fg-muted` | `#afc0b7` | `#425048` | `8.78:1` (`7.80:1`–`9.90:1` all surfaces) | `8.49:1` (`7.13:1`–`8.49:1` all surfaces) | Secondary labels, timestamps, pagination |
| `--fg-dim` | `#8fa298` | `#5b6a61` | `6.19:1` (`5.50:1`–`6.98:1` all surfaces) | `5.71:1` (`4.79:1`–`5.71:1` all surfaces) | Line numbers, carets, shortcut hints, table headers |
| `--phosphor` | `#56dba2` | `#047857` | `9.58:1` (`7.07:1` on `--phosphor-bg`) | `5.48:1` (`4.76:1` on `--phosphor-bg`) | Primary CTA, success, live pulse |
| `--accent` | `#58ccf2` | `#0369a1` | `9.01:1` (`6.72:1` on `--accent-bg`) | `5.93:1` (`5.13:1` on `--accent-bg`) | Running status, links, active pane |
| `--warn` | `#fbbf24` | `#92400e` | `10.00:1` (`7.37:1` on `--warn-bg`) | `7.09:1` (`6.19:1` on `--warn-bg`) | Queued, warnings |
| `--danger` | `#f88686` | `#b91c1c` | `6.95:1` (`5.50:1` on `--danger-bg`) | `6.47:1` (`5.46:1` on `--danger-bg`) | Failed, stderr, destructive actions |

### Legacy Aliases
`--card` (`--bg-elevated`), `--muted` (`--bg-surface`), `--muted-fg` (`--fg-muted`), `--primary` (`--phosphor`), `--on-primary` (`--bg`), `--success` (`--phosphor`), `--info` (`--accent`), `--ring` (`--border-focus`), `--log-bg` (`--bg-input`).

## Typography, Spacing, Radius & Motion
- **Fonts (offline local stacks)**: `--font-mono` (`Cascadia Mono`, `JetBrains Mono`, `Fira Code`, `Consolas`, monospace), `--font-sans` (`Segoe UI`, `system-ui`, sans-serif). Body default is `13px` monospace with `tabular-nums`.
- **Spacing**: `--sp-1..6` (`4px`, `8px`, `12px`, `16px`, `20px`, `24px`).
- **Radius**: `--radius-sm`, `--radius-md`, `--radius-lg`, `--radius` = `2px`.
- **Motion**: `--dur-snap: 80ms`, `--dur-fast: 110ms`, `--dur-pane: 140ms`, `--ease-snap: cubic-bezier(0.2, 0, 0, 1)`, `--ease-step: steps(4, end)`. Disabled automatically under `prefers-reduced-motion: reduce`.
