<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { Sun, Moon, LogIn } from 'lucide-vue-next'
import { useAuthStore } from '../stores/auth'
import { useTheme } from '../composables/useTheme'
import AppField from '../components/AppField.vue'
import AppInput from '../components/AppInput.vue'
import AppSecretInput from '../components/AppSecretInput.vue'
import AppButton from '../components/AppButton.vue'
import AppToast from '../components/AppToast.vue'

const router = useRouter()
const authStore = useAuthStore()
const { theme, toggleTheme } = useTheme()

const username = ref('')
const password = ref('')
const usernameError = ref<string | null>(null)
const passwordError = ref<string | null>(null)

function validate(): boolean {
  let valid = true
  usernameError.value = null
  passwordError.value = null

  if (!username.value.trim()) {
    usernameError.value = 'Vui lòng nhập tên đăng nhập'
    valid = false
  }
  if (!password.value) {
    passwordError.value = 'Vui lòng nhập mật khẩu'
    valid = false
  }
  return valid
}

async function onSubmit() {
  if (!validate()) return

  try {
    await authStore.login({
      username: username.value.trim(),
      password: password.value
    })
    router.push('/')
  } catch {
    // Error is set in authStore.error
  }
}
</script>

<template>
  <div class="min-h-screen flex flex-col justify-center items-center p-4 bg-bg text-fg">
    <AppToast />

    <!-- Top theme switcher -->
    <div class="fixed top-4 right-4">
      <button
        type="button"
        :aria-label="theme === 'dark' ? 'Chuyển sang giao diện sáng' : 'Chuyển sang giao diện tối'"
        class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring cursor-pointer transition-colors"
        @click="toggleTheme"
      >
        <Sun v-if="theme === 'dark'" class="w-5 h-5" aria-hidden="true" />
        <Moon v-else class="w-5 h-5" aria-hidden="true" />
      </button>
    </div>

    <!-- Login Card -->
    <div class="w-full max-w-sm p-6 sm:p-8 rounded-[8px] border border-border bg-card shadow-lg">
      <!-- Brand & Title -->
      <div class="flex flex-col items-center mb-6 text-center">
        <div class="flex items-center gap-2 mb-2 font-bold text-2xl tracking-wider text-primary">
          <div class="w-3 h-3 rounded-full bg-primary" />
          <span>SHIPIT</span>
        </div>
        <p class="text-xs text-muted-fg">Hệ thống triển khai ứng dụng nội bộ</p>
      </div>

      <!-- Error message alert -->
      <div
        v-if="authStore.error"
        role="alert"
        class="mb-4 p-3 rounded-[6px] text-xs font-medium bg-danger/10 border border-danger/30 text-danger leading-relaxed"
      >
        {{ authStore.error }}
      </div>

      <!-- Login Form -->
      <form class="space-y-4" @submit.prevent="onSubmit">
        <AppField
          id="login-username"
          label="Tên đăng nhập"
          required
          :error="usernameError"
        >
          <template #default="{ id, error: hasErr, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="username"
              name="username"
              placeholder="admin"
              :error="hasErr"
              :aria-describedby="ariaDescribedby"
              autocomplete="username"
            />
          </template>
        </AppField>

        <AppField
          id="login-password"
          label="Mật khẩu"
          required
          :error="passwordError"
        >
          <template #default="{ id, error: hasErr, ariaDescribedby }">
            <AppSecretInput
              :id="id"
              v-model="password"
              name="password"
              placeholder="••••••••"
              :error="hasErr"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <div class="pt-2">
          <AppButton
            type="submit"
            variant="primary"
            size="md"
            class="w-full"
            :loading="authStore.loading"
          >
            <LogIn class="w-4 h-4" aria-hidden="true" />
            <span>Đăng nhập</span>
          </AppButton>
        </div>
      </form>
    </div>
  </div>
</template>
