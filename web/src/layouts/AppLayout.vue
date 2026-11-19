<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Play,
  Workflow,
  Boxes,
  ShieldCheck,
  Server,
  KeyRound,
  Sun,
  Moon,
  LogOut,
  User,
  PanelLeftClose,
  PanelLeftOpen,
  HelpCircle,
  Terminal
} from 'lucide-vue-next'
import { useAuthStore } from '../stores/auth'
import { useRunsStore } from '../stores/runs'
import { useTheme } from '../composables/useTheme'
import { useToast } from '../composables/useToast'
import { useConsoleLog } from '../composables/useConsoleLog'
import { useShortcuts } from '../composables/useShortcuts'
import AppToast from '../components/AppToast.vue'
import GlobalSearch from '../components/search/GlobalSearch.vue'
import ShortcutsOverlay from '../components/ShortcutsOverlay.vue'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const runsStore = useRunsStore()
const { theme, applyTheme, toggleTheme } = useTheme()
const toast = useToast()
const consoleLog = useConsoleLog()
const { registerMany, toggleOverlay } = useShortcuts()

const railCollapsed = ref(false)
const cmdInput = ref('')
const cmdInputRef = ref<HTMLInputElement | null>(null)

const navGroups = [
  {
    label: 'Vận hành',
    items: [
      { name: 'Lượt chạy', path: '/', icon: Play, hint: 'g r' },
      { name: 'Pipeline', path: '/pipelines', icon: Workflow, hint: 'g p' },
      { name: 'Job', path: '/jobs', icon: Boxes, hint: 'g j' }
    ]
  },
  {
    label: 'Thiết lập',
    items: [
      { name: 'Máy build', path: '/slaves', icon: Server, hint: 'g s' },
      { name: 'Thông tin xác thực', path: '/credentials', icon: KeyRound, hint: 'g c' },
      { name: 'Allowlist', path: '/settings/allowlist', icon: ShieldCheck, hint: 'g a' }
    ]
  }
]

const breadcrumbLabel = computed(() => {
  const title = route.meta?.title as string | undefined
  return title ? `~/${title.toLowerCase()}` : '~/shipit'
})

const runningCount = computed(() => {
  return runsStore.runs.filter((r) => r.status === 'running' || r.status === 'queued').length
})

const lastConsoleText = computed(() => {
  if (!consoleLog.lastEntry.value) {
    return 'Sẵn sàng · Nhấn : để nhập lệnh'
  }
  const entry = consoleLog.lastEntry.value
  return `[${entry.time}] ${entry.message}`
})

function isActive(path: string): boolean {
  if (path === '/') {
    return route.path === '/' || route.path.startsWith('/runs')
  }
  return route.path.startsWith(path)
}

function focusCommandBar(): void {
  cmdInputRef.value?.focus()
  cmdInputRef.value?.select()
}

function resolveRouteAlias(raw: string): string | null {
  const key = raw.trim().toLowerCase()
  const map: Record<string, string> = {
    r: '/',
    run: '/',
    runs: '/',
    p: '/pipelines',
    pipeline: '/pipelines',
    pipelines: '/pipelines',
    j: '/jobs',
    job: '/jobs',
    jobs: '/jobs',
    s: '/slaves',
    slave: '/slaves',
    slaves: '/slaves',
    c: '/credentials',
    cred: '/credentials',
    credentials: '/credentials',
    a: '/settings/allowlist',
    allow: '/settings/allowlist',
    allowlist: '/settings/allowlist'
  }
  if (map[key]) return map[key]
  if (key.startsWith('/')) return key
  return null
}

async function executeCommand(): Promise<void> {
  const raw = cmdInput.value.trim()
  if (!raw) {
    cmdInputRef.value?.blur()
    return
  }

  const normalized = raw.startsWith(':') ? raw.slice(1).trim() : raw
  const [cmdRaw, ...rest] = normalized.split(/\s+/)
  const cmd = (cmdRaw || '').toLowerCase()
  const arg = rest.join(' ').trim()

  if (cmd === 'help' || cmd === '?') {
    toggleOverlay()
    consoleLog.log('Mở bảng phím tắt (:help)', 'info')
  } else if (cmd === 'clear') {
    consoleLog.clear()
    toast.info('Đã xoá nhật ký lệnh')
  } else if (cmd === 'theme') {
    if (arg === 'dark' || arg === 'light') {
      applyTheme(arg)
      consoleLog.log(`Đã chuyển giao diện: ${arg}`, 'success')
      toast.success(`Giao diện: ${arg === 'dark' ? 'Tối' : 'Sáng'}`)
    } else {
      toggleTheme()
      consoleLog.log(`Đã đổi giao diện: ${theme.value}`, 'success')
    }
  } else if (cmd === 'goto' || cmd === 'g') {
    const target = resolveRouteAlias(arg)
    if (target) {
      consoleLog.log(`Chuyển tới ${target}`, 'info')
      await router.push(target)
    } else {
      consoleLog.log(`Không rõ trang: ${arg || '(trống)'}`, 'warn')
      toast.error(`Không tìm thấy trang "${arg}"`)
    }
  } else if (cmd === 'filter') {
    const status = arg.toLowerCase()
    const valid = ['all', 'running', 'queued', 'success', 'failed', 'cancelled']
    if (!status || !valid.includes(status)) {
      consoleLog.log(`Trạng thái lọc không hợp lệ: ${arg}`, 'warn')
      toast.error('Dùng: filter <all|running|queued|success|failed|cancelled>')
    } else {
      await router.push({
        path: '/',
        query: status === 'all' ? {} : { status }
      })
      consoleLog.log(`Lọc lượt chạy: ${status}`, 'info')
    }
  } else if (cmd === 'run') {
    if (!arg) {
      await router.push({ path: '/', query: { action: 'new' } })
      consoleLog.log('Mở hộp thoại chạy mới', 'info')
    } else {
      await router.push({ path: '/', query: { search: arg, action: 'new' } })
      consoleLog.log(`Chuẩn bị chạy: ${arg}`, 'info')
    }
  } else {
    consoleLog.log(`Lệnh không hợp lệ: ${raw} (gõ help để xem hướng dẫn)`, 'error')
    toast.error(`Lệnh không hợp lệ: "${cmd}". Gõ "help" để xem phím tắt.`)
  }

  cmdInput.value = ''
  cmdInputRef.value?.blur()
}

function onCmdKeydown(e: KeyboardEvent): void {
  if (e.key === 'Enter') {
    e.preventDefault()
    void executeCommand()
  } else if (e.key === 'Escape') {
    e.stopPropagation()
    cmdInputRef.value?.blur()
  }
}

async function handleLogout(): Promise<void> {
  await authStore.logout()
  router.push('/login')
}

registerMany([
  { id: 'global-goto-runs', keys: 'g r', label: 'Đi tới Lượt chạy', scope: 'Điều hướng', handler: () => router.push('/') },
  { id: 'global-goto-pipelines', keys: 'g p', label: 'Đi tới Pipeline', scope: 'Điều hướng', handler: () => router.push('/pipelines') },
  { id: 'global-goto-jobs', keys: 'g j', label: 'Đi tới Job', scope: 'Điều hướng', handler: () => router.push('/jobs') },
  { id: 'global-goto-slaves', keys: 'g s', label: 'Đi tới Máy build', scope: 'Điều hướng', handler: () => router.push('/slaves') },
  { id: 'global-goto-credentials', keys: 'g c', label: 'Đi tới Thông tin xác thực', scope: 'Điều hướng', handler: () => router.push('/credentials') },
  { id: 'global-goto-allowlist', keys: 'g a', label: 'Đi tới Allowlist', scope: 'Điều hướng', handler: () => router.push('/settings/allowlist') },
  { id: 'global-cmd', keys: ':', label: 'Mở dòng lệnh (:)', scope: 'Toàn cục', handler: focusCommandBar },
  { id: 'global-help', keys: '?', label: 'Hiện/ẩn bảng phím tắt', scope: 'Toàn cục', handler: toggleOverlay }
])
</script>

<template>
  <div class="h-screen w-screen overflow-hidden flex flex-col bg-bg text-fg">
    <AppToast />
    <ShortcutsOverlay />

    <!-- Top Command Bar (40px) -->
    <header class="h-10 shrink-0 bg-bg-elevated border-b border-border flex items-center justify-between px-3 gap-3 z-20">
      <div class="flex items-center gap-2 whitespace-nowrap shrink-0 min-w-0">
        <router-link
          to="/"
          class="inline-flex items-center gap-1.5 font-bold text-[13px] tracking-[0.06em] text-phosphor select-none whitespace-nowrap focus-visible:outline-2 focus-visible:outline-border-focus rounded-[2px]"
        >
          <span class="text-fg-dim" aria-hidden="true">$</span>
          <span>SHIPIT</span>
        </router-link>
        <span class="text-fg-dim text-xs" aria-hidden="true">/</span>
        <span class="text-xs text-fg-muted truncate" :title="breadcrumbLabel">
          {{ breadcrumbLabel }}
        </span>
      </div>

      <!-- ':' Command Line Input -->
      <div
        role="search"
        class="flex-1 min-w-[220px] flex items-center gap-2 bg-bg-input border border-border focus-within:border-border-focus rounded-[2px] px-2 h-7 transition-colors"
        data-testid="command-bar-form"
      >
        <span class="text-phosphor font-bold text-xs select-none" aria-hidden="true">:</span>
        <input
          ref="cmdInputRef"
          v-model="cmdInput"
          type="text"
          aria-label="Dòng lệnh nhanh"
          placeholder="run <tên> | goto <trang> | filter <trạng thái|all> | theme <dark|light> | help"
          class="flex-1 min-w-0 bg-transparent border-none outline-none text-xs font-mono text-fg-strong placeholder:text-fg-dim"
          data-testid="command-bar-input"
          @keydown="onCmdKeydown"
        />
        <kbd class="shrink-0 text-[10px] font-mono px-1.5 py-0.5 rounded-[2px] bg-bg-surface border border-border text-fg-muted">
          :
        </kbd>
      </div>

      <!-- Topbar Right Actions -->
      <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
        <GlobalSearch />

        <button
          type="button"
          aria-label="Bảng phím tắt (?)"
          title="Bảng phím tắt (?)"
          class="h-7 px-2 inline-flex items-center gap-1 rounded-[2px] border border-border bg-bg-surface text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer text-xs focus-visible:outline-2 focus-visible:outline-border-focus"
          data-testid="shortcuts-trigger"
          @click="toggleOverlay"
        >
          <HelpCircle class="w-3.5 h-3.5" aria-hidden="true" />
          <kbd class="text-[10px] font-mono">?</kbd>
        </button>

        <button
          type="button"
          :aria-label="theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'"
          :title="theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'"
          class="h-7 w-7 inline-flex items-center justify-center rounded-[2px] border border-border bg-bg-surface text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
          data-testid="theme-toggle"
          @click="toggleTheme"
        >
          <Sun v-if="theme === 'dark'" class="w-3.5 h-3.5" aria-hidden="true" />
          <Moon v-else class="w-3.5 h-3.5" aria-hidden="true" />
        </button>

        <div class="h-4 w-px bg-border" aria-hidden="true" />

        <div class="flex items-center gap-1.5 text-xs text-fg">
          <User class="w-3.5 h-3.5 text-fg-muted shrink-0" aria-hidden="true" />
          <span class="font-medium">{{ authStore.user?.username || 'admin' }}</span>
        </div>

        <button
          type="button"
          class="h-7 inline-flex items-center gap-1 px-2 text-xs rounded-[2px] border border-transparent text-fg-muted hover:text-danger hover:bg-danger-bg hover:border-danger/30 focus-visible:outline-2 focus-visible:outline-border-focus cursor-pointer transition-colors"
          @click="handleLogout"
        >
          <LogOut class="w-3.5 h-3.5" aria-hidden="true" />
          <span>Đăng xuất</span>
        </button>
      </div>
    </header>

    <!-- Middle Shell: Left Rail + Main Workspace -->
    <div class="h-full flex-1 min-h-0 overflow-hidden flex flex-row">
      <!-- Left Rail (196px expanded, 52px collapsed) -->
      <aside
        :class="[
          'shrink-0 h-full bg-bg-elevated border-r border-border flex flex-col z-10 transition-[width] duration-[var(--dur-fast)]',
          railCollapsed ? 'w-[52px]' : 'w-[196px]'
        ]"
      >
        <nav aria-label="Điều hướng chính" class="flex-1 min-h-0 py-3 px-2 space-y-4 overflow-y-auto">
          <div v-for="group in navGroups" :key="group.label" class="space-y-0.5">
            <div
              v-if="!railCollapsed"
              class="px-2 mb-1 text-[10px] font-bold uppercase tracking-[0.08em] text-fg-dim select-none whitespace-nowrap"
            >
              {{ group.label }}
            </div>
            <router-link
              v-for="item in group.items"
              :key="item.path"
              :to="item.path"
              :title="railCollapsed ? `${item.name} (${item.hint})` : item.name"
              :class="[
                'flex items-center gap-2 px-2 py-1.5 rounded-[2px] border-l-2 text-[12.5px] transition-colors select-none cursor-pointer whitespace-nowrap overflow-hidden',
                isActive(item.path)
                  ? 'bg-bg-selected text-phosphor border-phosphor font-semibold'
                  : 'border-transparent text-fg-muted hover:text-fg-strong hover:bg-bg-hover'
              ]"
            >
              <component :is="item.icon" class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
              <span v-if="!railCollapsed" class="flex-1 truncate">{{ item.name }}</span>
              <span v-if="!railCollapsed" class="text-[10px] text-fg-dim shrink-0">{{ item.hint }}</span>
            </router-link>
          </div>
        </nav>

        <!-- Rail Footer -->
        <div class="px-2.5 py-2 border-t border-border text-[11px] text-fg-muted flex items-center justify-between shrink-0 whitespace-nowrap gap-1">
          <template v-if="!railCollapsed">
            <span>Shipit v0.1</span>
            <span class="inline-flex items-center gap-1.5 text-phosphor">
              <span class="w-1.5 h-1.5 rounded-full bg-phosphor" />
              <span>Sẵn sàng</span>
            </span>
          </template>
          <button
            type="button"
            :aria-label="railCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'"
            :title="railCollapsed ? 'Mở rộng thanh bên' : 'Thu gọn thanh bên'"
            class="p-1 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
            data-testid="rail-collapse-toggle"
            @click="railCollapsed = !railCollapsed"
          >
            <PanelLeftOpen v-if="railCollapsed" class="w-3.5 h-3.5" aria-hidden="true" />
            <PanelLeftClose v-else class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>
      </aside>

      <!-- Workspace (never scrolls vertically; min-w-[1024px] prevents layout wrapping) -->
      <main class="flex-1 min-h-0 overflow-x-auto overflow-y-hidden flex flex-col bg-bg">
        <div class="min-w-[1024px] w-full p-4 flex-1 min-h-0 flex flex-col animate-[viewEnter_var(--dur-fast)_var(--ease-snap)]">
          <slot />
        </div>
      </main>
    </div>

    <!-- Bottom Status Bar (26px) -->
    <footer
      class="h-[26px] shrink-0 bg-bg-surface border-t border-border flex items-center justify-between px-3 gap-3 text-[11px] text-fg-muted z-20 whitespace-nowrap"
      data-testid="status-bar"
    >
      <div class="flex items-center gap-3 shrink-0">
        <span class="inline-flex items-center gap-1.5 text-fg-strong">
          <span class="w-[7px] h-[7px] rounded-full bg-phosphor live-pulse" aria-hidden="true" />
          <span>TRỰC TIẾP</span>
        </span>
        <span class="text-fg-dim" aria-hidden="true">|</span>
        <span data-testid="status-bar-running">{{ runningCount }} đang chạy</span>
        <span class="text-fg-dim" aria-hidden="true">|</span>
        <span class="text-fg-muted">{{ route.meta?.title || 'Shipit' }}</span>
      </div>

      <button
        type="button"
        class="flex-1 min-w-0 truncate text-center hover:text-fg-strong cursor-pointer flex items-center justify-center gap-1.5 focus-visible:outline-2 focus-visible:outline-border-focus rounded-[2px]"
        title="Nhấn để mở nhật ký console"
        data-testid="status-bar-console"
        @click="consoleLog.toggleDrawer()"
      >
        <Terminal class="w-3 h-3 text-phosphor shrink-0" aria-hidden="true" />
        <span class="truncate">{{ lastConsoleText }}</span>
      </button>

      <div class="flex items-center gap-2 shrink-0 text-fg-dim">
        <span>? phím tắt</span>
        <span>·</span>
        <span>: lệnh</span>
      </div>
    </footer>
  </div>
</template>
