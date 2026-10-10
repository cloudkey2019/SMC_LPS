<!--
  APS V1 4号位 — §10A.2 甘特图调整（GANTT_ADJUSTMENT）

  冻结依据：《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
  契约：LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode **固定 NORMAL（禁 EXPEDITE，省略即 NORMAL）**
  载荷：taskTargets[]{ taskId, targetTime(软目标时间，非空) }

  能力边界：DhxGantt 组件不支持多 Task 拖拽（payload.taskId 单值）——「一次多 Task」由本 Dialog
  表格多选承担；拖拽仅作单条预填（preselectedTaskIds + preselectTargetTime）。

  Dialog 只收集载荷；父组件持 loading 并调 triggerBusinessEntry。
-->
<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElDatePicker,
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

interface GanttAdjustTask {
  taskId: number
  taskNo: string
  orderNo?: string
  domainKey: DomainKey
  plannedStartTime?: string
  plannedEndTime?: string
  status: string
  lockMarker?: string
}

interface Row extends GanttAdjustTask {
  targetTime?: string
}

const props = defineProps<{
  visible: boolean
  submitting?: boolean
  domainOptions: DomainKey[]
  tasks: GanttAdjustTask[]
  /** 拖拽 / Drawer 单条预填 */
  preselectedTaskIds?: number[]
  preselectTargetTime?: string
  defaultDomainKey?: DomainKey
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [payload: BusinessEntrySubmission]
}>()

const TRIGGER = 'GANTT_ADJUSTMENT' as const
const spec = TRIGGER_RUN_MATRIX[TRIGGER]
const ISO_FMT = 'YYYY-MM-DDTHH:mm:ss[Z]'

const domainKey = ref<DomainKey | undefined>(undefined)
const rows = ref<Row[]>([])
const selected = ref<Row[]>([])
const errors = ref<string[]>([])
const tableRef = ref<InstanceType<typeof ElTable> | null>(null)

async function rebuildRows(preIds: number[] = []): Promise<void> {
  rows.value = props.tasks.filter((t) => t.domainKey === domainKey.value).map((t) => ({ ...t }))
  selected.value = []
  await nextTick()
  if (!tableRef.value) return
  const pre = new Set(preIds)
  rows.value.forEach((r) => {
    if (pre.has(r.taskId)) {
      if (props.preselectTargetTime) r.targetTime = props.preselectTargetTime
      tableRef.value?.toggleRowSelection(r, true)
    }
  })
}

function onSelectionChange(sel: Row[]): void {
  selected.value = sel
}

watch(
  () => props.visible,
  async (vis) => {
    if (!vis) return
    errors.value = []
    const preIds = props.preselectedTaskIds ?? []
    const preTask = props.tasks.find((t) => t.taskId === preIds[0])
    domainKey.value = props.defaultDomainKey ?? preTask?.domainKey ?? props.domainOptions[0]
    await rebuildRows(preIds)
  },
  { immediate: true }
)

watch(domainKey, async (next, prev) => {
  if (!props.visible || next === prev) return
  await rebuildRows()
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
    ElMessage.warning('请至少选择 1 个任务')
    return
  }
  const draft: ScopeDraft = {
    taskTargets: selected.value.map((r) => ({
      taskId: Number(r.taskId),
      targetTime: r.targetTime ?? ''
    }))
  }
  const errs = validateScopeDraft(TRIGGER, draft)
  errors.value = errs
  if (errs.length > 0) {
    ElMessage.error(`有 ${errs.length} 项未通过校验，请检查表单`)
    return
  }
  emit('submit', { trigger: TRIGGER, domainKey: domainKey.value, draft })
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
        <span class="field-hint">一次只处理一个排程域；切换排程域会清空已选任务</span>
      </ElFormItem>

      <ElFormItem label="优先模式">
        <ElTag type="info" effect="plain">普通（不加急）</ElTag>
        <span class="field-hint">本入口固定按普通优先处理，不支持加急</span>
      </ElFormItem>

      <ElFormItem label="任务目标" required>
        <ElTable
          ref="tableRef"
          :data="rows"
          size="small"
          border
          max-height="340"
          row-key="taskId"
          style="width: 100%"
          @selection-change="onSelectionChange"
        >
          <ElTableColumn type="selection" width="44" />
          <ElTableColumn prop="taskNo" label="任务号" width="110" />
          <ElTableColumn prop="orderNo" label="订单号" width="130">
            <template #default="{ row }">
              <span v-if="row?.orderNo">{{ row.orderNo }}</span>
              <span v-else class="muted-text">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="当前计划" width="270">
            <template #default="{ row }">
              <span v-if="row?.plannedStartTime" class="muted-text">
                {{ row.plannedStartTime.slice(0, 16) }} →
                {{ row.plannedEndTime ? row.plannedEndTime.slice(0, 16) : '—' }}
              </span>
              <span v-else class="muted-text">未定位（UNLOCATED）</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="状态" width="90" align="center">
            <template #default="{ row }">
              <ElTag size="small" effect="plain">{{ row?.status }}</ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="锁" width="80" align="center">
            <template #default="{ row }">
              <ElTag v-if="row?.lockMarker" size="small" type="info" effect="plain">
                {{ row.lockMarker }}
              </ElTag>
              <span v-else class="muted-text">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="软目标时间" width="200">
            <template #default="{ row }">
              <ElDatePicker
                v-model="row.targetTime"
                type="datetime"
                size="small"
                placeholder="必填"
                format="YYYY-MM-DD HH:mm"
                :value-format="ISO_FMT"
                style="width: 100%"
              />
            </template>
          </ElTableColumn>
        </ElTable>

        <div class="field-hint">
          已选 <strong>{{ selected.length }}</strong> / {{ rows.length }} 个任务（当前排程域）
        </div>
      </ElFormItem>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>软目标语义</template>
        目标时间为**软目标**（求解器可返回其它可行时间）；不直接改写正式任务，须经候选版本
        比较确认。默认取该任务当前计划开始时间，可逐行调整。
      </ElAlert>

      <ElAlert v-if="errors.length > 0" type="error" :closable="false" show-icon class="d-alert">
        <template #title>校验未通过（{{ errors.length }}）</template>
        <ul class="err-list">
          <li v-for="(e, i) in errors" :key="i">{{ e }}</li>
        </ul>
      </ElAlert>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>选择方式</template>
        在甘特图上拖拽某条任务，会<strong>自动带入该任务</strong>（一次一条）；
        需要一次调整多条任务时， 请在下方表格中多选。
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
