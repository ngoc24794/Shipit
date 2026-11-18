<script setup lang="ts">
import { ArrowLeft, ArrowRight, Save, FileQuestion } from 'lucide-vue-next'
import AppLayout from '../layouts/AppLayout.vue'
import AppButton from './AppButton.vue'
import FormStepper, { type WizardStep } from './FormStepper.vue'

withDefaults(
  defineProps<{
    title: string
    backTo: string
    breadcrumbParent: string
    breadcrumbCurrent: string
    steps: WizardStep[]
    currentStep: number
    canNavigateTo?: (index: number) => boolean
    saveLabel?: string
    saving?: boolean
    disabled?: boolean
    loading?: boolean
    notFound?: boolean
    notFoundTitle?: string
    notFoundDescription?: string
  }>(),
  {
    canNavigateTo: () => true,
    saveLabel: 'Lưu',
    saving: false,
    disabled: false,
    loading: false,
    notFound: false,
    notFoundTitle: 'Không tìm thấy dữ liệu',
    notFoundDescription: 'Mục bạn tìm kiếm không tồn tại hoặc đã bị xoá.'
  }
)

const emit = defineEmits<{
  (e: 'cancel'): void
  (e: 'prev'): void
  (e: 'next'): void
  (e: 'save'): void
  (e: 'selectStep', index: number): void
}>()
</script>

<template>
  <AppLayout>
    <div class="h-full max-w-4xl w-full mx-auto flex flex-col min-h-0">
      <!-- Top Header & Stepper (Fixed) -->
      <div class="shrink-0 space-y-3 pb-3 border-b border-border">
        <div class="flex items-center justify-between gap-4 flex-nowrap">
          <div class="flex items-center gap-3 min-w-0">
            <router-link
              :to="backTo"
              aria-label="Quay lại danh sách"
              class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors focus-visible:outline-2 focus-visible:outline-ring shrink-0"
            >
              <ArrowLeft class="w-5 h-5" aria-hidden="true" />
            </router-link>
            <div class="min-w-0">
              <nav aria-label="Breadcrumb" class="text-xs text-muted-fg flex items-center gap-1.5">
                <router-link :to="backTo" class="hover:text-fg hover:underline">
                  {{ breadcrumbParent }}
                </router-link>
                <span aria-hidden="true">/</span>
                <span class="text-fg font-medium truncate">{{ breadcrumbCurrent }}</span>
              </nav>
              <h1 class="text-xl font-bold tracking-tight text-fg flex items-center gap-2 mt-0.5 truncate">
                <slot name="icon" />
                <span class="truncate">{{ title }}</span>
              </h1>
            </div>
          </div>

          <div class="shrink-0">
            <slot name="header-actions" />
          </div>
        </div>

        <FormStepper
          v-if="!loading && !notFound"
          :steps="steps"
          :current-step="currentStep"
          :can-navigate-to="canNavigateTo"
          @select="emit('selectStep', $event)"
        />
      </div>

      <!-- Loading Skeleton -->
      <div v-if="loading" class="flex-1 min-h-0 overflow-y-auto py-4">
        <div class="p-6 rounded-[8px] border border-border bg-card space-y-4" aria-busy="true">
          <div v-for="i in 4" :key="i" class="h-10 rounded bg-muted/70 animate-pulse" />
        </div>
      </div>

      <!-- Not Found -->
      <div v-else-if="notFound" class="flex-1 min-h-0 overflow-y-auto py-8">
        <div class="p-8 rounded-[8px] border border-border bg-card text-center space-y-4">
          <div class="w-12 h-12 rounded-full bg-muted flex items-center justify-center text-muted-fg mx-auto">
            <FileQuestion class="w-6 h-6" aria-hidden="true" />
          </div>
          <div>
            <h2 class="text-base font-semibold text-fg">{{ notFoundTitle }}</h2>
            <p class="text-sm text-muted-fg mt-1">{{ notFoundDescription }}</p>
          </div>
          <AppButton variant="secondary" @click="emit('cancel')">
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
            <span>Quay lại danh sách</span>
          </AppButton>
        </div>
      </div>

      <!-- Step Content (Internal Scroll Only) -->
      <div v-else class="flex-1 min-h-0 overflow-y-auto py-4 pr-1">
        <slot />
      </div>

      <!-- Bottom Action Footer (Fixed) -->
      <div
        v-if="!loading && !notFound"
        class="shrink-0 pt-3 border-t border-border bg-bg flex items-center justify-between gap-3"
      >
        <div class="flex items-center gap-2">
          <AppButton
            type="button"
            variant="ghost"
            :disabled="saving"
            @click="emit('cancel')"
          >
            Huỷ
          </AppButton>
          <AppButton
            v-if="currentStep > 0"
            type="button"
            variant="secondary"
            :disabled="saving"
            @click="emit('prev')"
          >
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
            <span>Quay lại</span>
          </AppButton>
        </div>

        <div class="flex items-center gap-2">
          <slot name="footer-extra" />

          <AppButton
            v-if="currentStep < steps.length - 1"
            type="button"
            variant="primary"
            :disabled="saving || disabled"
            @click="emit('next')"
          >
            <span>Tiếp tục</span>
            <ArrowRight class="w-4 h-4" aria-hidden="true" />
          </AppButton>

          <AppButton
            v-else
            type="button"
            variant="primary"
            :loading="saving"
            :disabled="saving || disabled"
            @click="emit('save')"
          >
            <Save class="w-4 h-4" aria-hidden="true" />
            <span>{{ saveLabel }}</span>
          </AppButton>
        </div>
      </div>
    </div>
  </AppLayout>
</template>
