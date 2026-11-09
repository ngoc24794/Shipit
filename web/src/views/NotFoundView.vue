<script setup lang="ts">
import { useRouter } from 'vue-router'
import { FileQuestion, ArrowLeft } from 'lucide-vue-next'
import AppButton from '../components/AppButton.vue'

const router = useRouter()
</script>

<template>
  <div class="min-h-screen flex flex-col justify-center items-center p-4 bg-bg text-fg text-center">
    <div class="w-16 h-16 rounded-full bg-muted flex items-center justify-center text-muted-fg mb-4">
      <FileQuestion class="w-8 h-8" aria-hidden="true" />
    </div>

    <h1 class="text-4xl font-bold tracking-tight mb-2">404</h1>
    <h2 class="text-lg font-semibold text-fg mb-1">Không tìm thấy trang</h2>
    <p class="text-sm text-muted-fg max-w-sm mb-6">
      Đường dẫn bạn yêu cầu không tồn tại hoặc đã được di chuyển.
    </p>

    <AppButton variant="primary" @click="router.push('/')">
      <ArrowLeft class="w-4 h-4" aria-hidden="true" />
      <span>Về trang chủ</span>
    </AppButton>
  </div>
</template>
