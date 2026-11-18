<script setup lang="ts">
import { ref } from 'vue'
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
  Menu,
  X,
  User
} from 'lucide-vue-next'
import { useAuthStore } from '../stores/auth'
import { useTheme } from '../composables/useTheme'
import AppToast from '../components/AppToast.vue'

const route = useRoute()
const router = useRouter()
const authStore = useAuthStore()
const { theme, toggleTheme } = useTheme()

const isMobileMenuOpen = ref(false)

const navGroups = [
  {
    label: 'Vận hành',
    items: [
      { name: 'Lượt chạy', path: '/', icon: Play },
      { name: 'Pipeline', path: '/pipelines', icon: Workflow },
      { name: 'Job', path: '/jobs', icon: Boxes }
    ]
  },
  {
    label: 'Thiết lập',
    items: [
      { name: 'Máy build', path: '/slaves', icon: Server },
      { name: 'Thông tin xác thực', path: '/credentials', icon: KeyRound },
      { name: 'Allowlist', path: '/settings/allowlist', icon: ShieldCheck }
    ]
  }
]

function isActive(path: string) {
  if (path === '/') {
    return route.path === '/' || route.path.startsWith('/runs')
  }
  return route.path.startsWith(path)
}

async function handleLogout() {
  await authStore.logout()
  router.push('/login')
}
</script>

<template>
  <div class="h-dvh overflow-hidden flex flex-col md:flex-row bg-bg text-fg">
    <AppToast />

    <!-- Mobile Top Header Bar (<768px) -->
    <header class="md:hidden shrink-0 flex items-center justify-between h-12 px-4 bg-card border-b border-border z-30">
      <div class="flex items-center gap-2">
        <button
          type="button"
          :aria-label="isMobileMenuOpen ? 'Đóng trình đơn' : 'Mở trình đơn'"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring cursor-pointer"
          @click="isMobileMenuOpen = !isMobileMenuOpen"
        >
          <X v-if="isMobileMenuOpen" class="w-5 h-5" aria-hidden="true" />
          <Menu v-else class="w-5 h-5" aria-hidden="true" />
        </button>
        <div class="flex items-center gap-2 font-bold tracking-tight text-primary">
          <span class="text-base">SHIPIT</span>
        </div>
      </div>

      <div class="flex items-center gap-2">
        <button
          type="button"
          :aria-label="theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring cursor-pointer"
          @click="toggleTheme"
        >
          <Sun v-if="theme === 'dark'" class="w-4 h-4" aria-hidden="true" />
          <Moon v-else class="w-4 h-4" aria-hidden="true" />
        </button>
      </div>
    </header>

    <!-- Sidebar (Desktop: 232px fixed height, Mobile: slide-down/overlay) -->
    <aside
      :class="[
        'w-[232px] flex-shrink-0 bg-card border-r border-border flex flex-col transition-all z-20',
        'md:h-full',
        isMobileMenuOpen ? 'fixed inset-y-0 left-0 shadow-2xl pt-12 md:pt-0' : 'hidden md:flex'
      ]"
    >
      <!-- Brand Logo Header (Desktop) -->
      <div class="hidden md:flex items-center justify-between h-12 px-5 border-b border-border">
        <router-link to="/" class="flex items-center gap-2 font-bold tracking-wider text-primary text-base select-none">
          <div class="w-2.5 h-2.5 rounded-full bg-primary" />
          <span>SHIPIT</span>
        </router-link>
      </div>

      <!-- Navigation links -->
      <nav aria-label="Điều hướng chính" class="flex-1 py-4 px-3 space-y-5 overflow-y-auto">
        <div v-for="group in navGroups" :key="group.label" class="space-y-1">
          <div class="px-3 mb-1.5 text-[11px] font-semibold uppercase tracking-wider text-muted-fg/80 select-none">
            {{ group.label }}
          </div>
          <router-link
            v-for="item in group.items"
            :key="item.path"
            :to="item.path"
            :class="[
              'flex items-center gap-3 px-3 py-2 rounded-[6px] text-sm font-medium transition-colors select-none cursor-pointer',
              isActive(item.path)
                ? 'bg-primary/10 text-primary border-l-2 border-primary font-semibold'
                : 'text-muted-fg hover:text-fg hover:bg-muted'
            ]"
            @click="isMobileMenuOpen = false"
          >
            <component :is="item.icon" class="w-4 h-4 flex-shrink-0" aria-hidden="true" />
            <span>{{ item.name }}</span>
          </router-link>
        </div>
      </nav>

      <!-- Sidebar Footer (System Info) -->
      <div class="p-3 border-t border-border text-xs text-muted-fg flex items-center justify-between">
        <span>Shipit v0.1</span>
        <span class="inline-flex items-center gap-1.5 text-success">
          <span class="w-1.5 h-1.5 rounded-full bg-success" />
          <span>Sẵn sàng</span>
        </span>
      </div>
    </aside>

    <!-- Backdrop for mobile menu -->
    <div
      v-if="isMobileMenuOpen"
      class="fixed inset-0 bg-black/50 z-10 md:hidden"
      aria-hidden="true"
      @click="isMobileMenuOpen = false"
    />

    <!-- Main Content Layout Area -->
    <div class="flex-1 flex flex-col min-w-0 min-h-0 overflow-hidden">
      <!-- Desktop Header (48px) -->
      <header class="hidden md:flex shrink-0 items-center justify-end h-12 px-6 bg-card border-b border-border z-10">
        <div class="flex items-center gap-4">
          <!-- Theme Toggle -->
          <button
            type="button"
            :aria-label="theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'"
            class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring cursor-pointer transition-colors"
            @click="toggleTheme"
          >
            <Sun v-if="theme === 'dark'" class="w-4 h-4" aria-hidden="true" />
            <Moon v-else class="w-4 h-4" aria-hidden="true" />
          </button>

          <div class="h-4 w-px bg-border" aria-hidden="true" />

          <!-- User info -->
          <div class="flex items-center gap-2 text-sm text-fg">
            <div class="w-6 h-6 rounded-full bg-muted flex items-center justify-center text-muted-fg">
              <User class="w-3.5 h-3.5" aria-hidden="true" />
            </div>
            <span class="font-medium text-xs">{{ authStore.user?.username || 'admin' }}</span>
          </div>

          <!-- Logout Button -->
          <button
            type="button"
            class="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs rounded-[6px] text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:outline-2 focus-visible:outline-ring cursor-pointer transition-colors"
            @click="handleLogout"
          >
            <LogOut class="w-3.5 h-3.5" aria-hidden="true" />
            <span>Đăng xuất</span>
          </button>
        </div>
      </header>

      <!-- Page Content (only scrolling region) -->
      <main class="flex-1 min-h-0 overflow-y-auto">
        <div class="max-w-[1440px] w-full mx-auto p-4 sm:p-6">
          <slot />
        </div>
      </main>
    </div>
  </div>
</template>
