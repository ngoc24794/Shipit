<script setup lang="ts">
import AppLayout from '../../layouts/AppLayout.vue'
import EmptyState from '../../components/EmptyState.vue'
import { KeyRound } from 'lucide-vue-next'
</script>

<template>
  <AppLayout>
    <div class="space-y-4">
      <h2 class="text-lg font-semibold">Thông tin xác thực (Credentials)</h2>
      <EmptyState
        title="Danh sách Thông tin xác thực"
        description="View tạm thời cho task T6b (ui-config-pages)."
        :icon="KeyRound"
      />
    </div>
  </AppLayout>
</template>
