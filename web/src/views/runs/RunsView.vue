<script setup lang="ts">
import AppLayout from '../../layouts/AppLayout.vue'
import EmptyState from '../../components/EmptyState.vue'
import { Play } from 'lucide-vue-next'
</script>

<template>
  <AppLayout>
    <div class="space-y-4">
      <h2 class="text-lg font-semibold">Danh sách lượt chạy</h2>
      <EmptyState
        title="Danh sách lượt chạy"
        description="View tạm thời cho task T6c (ui-run-pages)."
        :icon="Play"
      />
    </div>
  </AppLayout>
</template>
