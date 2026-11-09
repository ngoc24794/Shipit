<script setup lang="ts">
import AppLayout from '../../layouts/AppLayout.vue'
import EmptyState from '../../components/EmptyState.vue'
import { Server } from 'lucide-vue-next'
</script>

<template>
  <AppLayout>
    <div class="space-y-4">
      <h2 class="text-lg font-semibold">Máy build (Slaves)</h2>
      <EmptyState
        title="Danh sách Máy build"
        description="View tạm thời cho task T6b (ui-config-pages)."
        :icon="Server"
      />
    </div>
  </AppLayout>
</template>
