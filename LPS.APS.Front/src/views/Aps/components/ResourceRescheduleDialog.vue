<!--
  APS V1 4号位 — §10A.3 设备故障后重排（EQUIPMENT_FAILURE）/ §10A.4 资源日历调整后重排（RESOURCE_CALENDAR_CHANGE）

  冻结依据：《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
  契约：两者均 LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode **固定 NORMAL（禁 EXPEDITE）**
  载荷：changedResourceIds[]{ 已正式不可用 / 已正式改 Calendar 的资源 Id }

  两码结构完全一致（Domain 单选 + 资源多选），仅文案与事实前提不同，故合并为一个组件由 trigger 分流：
    - EQUIPMENT_FAILURE        ：仅选已发生故障 / 已确认不可用的资源（不得假设设备故障）
    - RESOURCE_CALENDAR_CHANGE ：仅选已正式修改 Calendar 的资源（前端 V1 无 Calendar 维护功能）

  Dialog 只收集载荷；父组件持 loading 并调 triggerBusinessEntry。
-->
<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus'
import { TRIGGER_RUN_MATRIX, validateScopeDraft } from '@/api/aps-v1'
import type { BusinessEntrySubmission, DomainKey, ScopeDraft } from '@/api/aps-v1'

interface RescheduleResource {
  resourceId: number
  resourceCode: string
  resourceName: string
  domainKey: DomainKey
  unavailableWindows?: Array<{ from: string; to: string; reason: string }>
}

const props = defineProps<{
  visible: boolean
  submitting?: boolean
  /** 10A.3 / 10A.4 分流 */
  trigger: 'EQUIPMENT_FAILURE' | 'RESOURCE_CALENDAR_CHANGE'
  domainOptions: DomainKey[]
  resources: RescheduleResource[]
  preselectedResourceIds?: number[]
  defaultDomainKey?: DomainKey
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [payload: BusinessEntrySubmission]
}>()

const ISO_HINT_LEN = 16

const spec = computed(() => TRIGGER_RUN_MATRIX[props.trigger])

/** 事实前提提示（两码差异仅在此） */
const FACT_HINT: Record<'EQUIPMENT_FAILURE' | 'RESOURCE_CALENDAR_CHANGE', string> = {
  EQUIPMENT_FAILURE:
    '设备故障后重排：仅选择**已发生故障 / 已确认不可用**的资源。系统不得假设设备故障——未落库的故障事实请先在设备侧登记，再从此入口发起建议重排。',
  RESOURCE_CALENDAR_CHANGE:
    '资源日历调整后重排：仅可选**已正式修改 Calendar**（维护窗口 / 停机日历）的资源。前端 V1 不提供 Calendar 维护功能，日历变更由外部系统落库后在此引用。'
}

const domainKey = ref<DomainKey | undefined>(undefined)
const rows = ref<RescheduleResource[]>([])
const selected = ref<RescheduleResource[]>([])
const errors = ref<string[]>([])
const tableRef = ref<InstanceType<typeof ElTable> | null>(null)

/** 不可用窗口非空的资源排在前（偏好展示） */
const orderedRows = computed(() =>
  [...rows.value].sort(
    (a, b) => (b.unavailableWindows?.length ?? 0) - (a.unavailableWindows?.length ?? 0)
  )
)

function windowSummary(r: RescheduleResource): string {
  const w = r.unavailableWindows ?? []
  if (w.length === 0) return '—'
  const first = w[0]
  if (!first) return '—'
  return `${first.from.slice(0, ISO_HINT_LEN)} → ${first.to.slice(0, ISO_HINT_LEN)}（${first.reason}）${w.length > 1 ? ` 等 ${w.length} 段` : ''}`
}

function onSelectionChange(sel: RescheduleResource[]): void {
  selected.value = sel
}

watch(
  () => props.visible,
  async (vis) => {
    if (!vis) return
    errors.value = []
    domainKey.value = props.defaultDomainKey ?? props.domainOptions[0]
    rows.value = props.resources
      .filter((r) => r.domainKey === domainKey.value)
      .map((r) => ({ ...r }))
    selected.value = []
    await nextTick()
    const pre = new Set(props.preselectedResourceIds ?? [])
    if (pre.size > 0 && tableRef.value) {
      rows.value.forEach((r) => {
        if (pre.has(r.resourceId)) tableRef.value?.toggleRowSelection(r, true)
      })
    }
  },
  { immediate: true }
)

watch(domainKey, (next, prev) => {
  if (!props.visible || next === prev) return
  rows.value = props.resources.filter((r) => r.domainKey === next).map((r) => ({ ...r }))
  selected.value = []
})

function close(): void {
  emit('update:visible', false)
}

function onSubmit(): void {
  if (!domainKey.value) {
    ElMessage.warning('请选择排程域')
    return
  }
  if (selected.value.length === 0) {
    ElMessage.warning('请至少选择 1 个资源')
    return
  }
  const draft: ScopeDraft = {
    changedResourceIds: selected.value.map((r) => Number(r.resourceId))
  }
  const errs = validateScopeDraft(props.trigger, draft)
  errors.value = errs
  if (errs.length > 0) {
    ElMessage.error(`有 ${errs.length} 项未通过校验，请检查表单`)
    return
  }
  emit('submit', { trigger: props.trigger, domainKey: domainKey.value, draft })
}
</script>

<template>
  <ElDialog
    :model-value="visible"
    :title="spec.entryTitle"
    width="900px"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    @update:model-value="(v) => emit('update:visible', v)"
  >
    <ElForm label-width="120px" label-position="right">
      <ElFormItem label="排程域" required>
        <ElSelect v-model="domainKey" placeholder="选择单计划域" style="width: 260px">
          <ElOption v-for="d in domainOptions" :key="d" :label="d" :value="d" />
        </ElSelect>
        <span class="field-hint">一次只处理一个排程域；切换排程域会清空已选资源</span>
      </ElFormItem>

      <ElFormItem label="优先模式">
        <ElTag type="info" effect="plain">普通（不加急）</ElTag>
        <span class="field-hint">本入口固定按普通优先处理，不支持加急</span>
      </ElFormItem>

      <ElFormItem label="受影响资源" required>
        <ElTable
          ref="tableRef"
          :data="orderedRows"
          size="small"
          border
          max-height="340"
          row-key="resourceId"
          style="width: 100%"
          @selection-change="onSelectionChange"
        >
          <ElTableColumn type="selection" width="44" />
          <ElTableColumn prop="resourceCode" label="资源编码" width="140" />
          <ElTableColumn prop="resourceName" label="资源名称" width="180" />
          <ElTableColumn label="不可用窗口" min-width="280">
            <template #default="{ row }">
              <span :class="{ 'muted-text': !row?.unavailableWindows?.length }">
                {{ windowSummary(row) }}
              </span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="影响任务" width="110" align="center">
            <template #default="{ row }">
              <ElTag size="small" effect="plain">
                {{
                  row?.unavailableWindows?.reduce(
                    (n, w) => n + (w.impactedTaskIds?.length ?? 0),
                    0
                  ) ?? 0
                }}
              </ElTag>
            </template>
          </ElTableColumn>
        </ElTable>

        <div class="field-hint">
          已选 <strong>{{ selected.length }}</strong> /
          {{ rows.length }} 个资源（当前排程域；不可用窗口非空的资源已排在前）
        </div>
      </ElFormItem>

      <ElAlert type="warning" :closable="false" show-icon class="d-alert">
        <template #title>事实前提（不得虚构）</template>
        {{ FACT_HINT[trigger] }}
      </ElAlert>

      <ElAlert v-if="errors.length > 0" type="error" :closable="false" show-icon class="d-alert">
        <template #title>校验未通过（{{ errors.length }}）</template>
        <ul class="err-list">
          <li v-for="(e, i) in errors" :key="i">{{ e }}</li>
        </ul>
      </ElAlert>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>提交后</template>
        本入口只发起<strong>建议重排</strong>，不会直接改动任务状态；是否采纳由 PMC 决策。
        提交后新建排产运行并生成候选版本，需在「候选版本」页比较确认。
      </ElAlert>
    </ElForm>

    <template #footer>
      <ElButton :disabled="submitting" @click="close">取消</ElButton>
      <ElButton type="primary" :loading="submitting" @click="onSubmit">
        发起重排（{{ selected.length }}）
      </ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.field-hint {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.d-alert {
  margin-top: 12px;
}
.err-list {
  margin: 0;
  padding-left: 18px;
}
.muted-text {
  color: var(--el-text-color-secondary);
}
</style>
