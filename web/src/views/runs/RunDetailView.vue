<script setup lang="ts">
import { useRoute } from 'vue-router'
import AppLayout from '../../layouts/AppLayout.vue'
import EmptyState from '../../components/EmptyState.vue'
import { Play } from 'lucide-vue-next'

const route = useRoute()
</script>

<template>
  <AppLayout>
    <div class="space-y-4">
      <h2 class="text-lg font-semibold">Chi tiết lượt chạy #{{ route.params.id }}</h2>
      <EmptyState
        :title="`Chi tiết lượt chạy #${route.params.id}`"
        description="View tạm thời cho task T6c (ui-run-pages)."
        :icon="Play"
      />
    </div>
  </AppLayout>
</template>
