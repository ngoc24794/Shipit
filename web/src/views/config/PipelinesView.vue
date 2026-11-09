<script setup lang="ts">
import AppLayout from '../../layouts/AppLayout.vue'
import EmptyState from '../../components/EmptyState.vue'
import { Workflow } from 'lucide-vue-next'
</script>

<template>
  <AppLayout>
    <div class="space-y-4">
      <h2 class="text-lg font-semibold">Cấu hình Pipeline</h2>
      <EmptyState
        title="Danh sách Pipeline"
        description="View tạm thời cho task T6b (ui-config-pages)."
        :icon="Workflow"
      />
    </div>
  </AppLayout>
</template>
