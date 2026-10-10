<!--
  APS V1 4号位 — §10A.1 已有订单提前（EXISTING_ORDER_ADVANCE）

  冻结依据：《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
  契约：LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode 可选 NORMAL / EXPEDITE（FREE）
  载荷：orderTargets[]{ orderCanonicalId(long), manualTargetDueDate }
  集成接口设计 v1.33 §5.3 + §6.5：OrderCanonicalId 必须字段；与 OrderCanonicalIds 一致性约束
  （前端不传 OrderCanonicalIds，与一致性约束兼容）

  2026-09-23 B4 已落地：OrderCanonicalId 由后端 OrderQuery 真实返回；
  本 Dialog 改为**自动带入**（从 orderCanonicalId 字段带入 canonicalId 列）+ **保留手填兜底**作
  兼容回退（异常数据 / 历史订单）。
  订单规范 Id（OrderCanonicalId）= `Order_Canonical.Id`，与订单列表的 `orderId`（`[Order].Id`）
  不是同一 ID 空间 —— 输入框占位显示已带入值或 orderNo 作对照；目标交期**不修改正式 DueDate**，
  仅作本次 Candidate 的手工目标。

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
  ElInput,
  ElMessage,
  ElOption,
  ElRadioButton,
  ElRadioGroup,
  ElSelect,
  ElTable,
  ElTableColumn
} from 'element-plus'
import { TRIGGER_RUN_MATRIX, validateScopeDraft } from '@/api/aps-v1'
import type { BusinessEntrySubmission, DomainKey, PriorityMode, ScopeDraft } from '@/api/aps-v1'

interface OrderAdvanceCandidate {
  orderId: number
  orderNo: string
  /** 订单规范化 Id（5号位 B4 已落地；自动带入可空由 Dialog watch 处理） */
  orderCanonicalId?: number
  materialCode?: string
  customerName?: string
  customerDueDate?: string
}

interface Row extends OrderAdvanceCandidate {
  /** 手填兜底（兼容回退；正常情况 = orderCanonicalId 自动带入） */
  canonicalId?: number
  manualTargetDueDate?: string
}

const props = defineProps<{
  visible: boolean
  submitting?: boolean
  /** 授权 Domain（Order 页已按 dataScope 收敛） */
  domainOptions: DomainKey[]
  /** 订单候选（已按页面筛选条件过滤） */
  orders: OrderAdvanceCandidate[]
  /** 预选订单（Order 页表格多选带过来） */
  preselectedOrderIds?: number[]
  defaultDomainKey?: DomainKey
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [payload: BusinessEntrySubmission]
}>()

const TRIGGER = 'EXISTING_ORDER_ADVANCE' as const
const spec = TRIGGER_RUN_MATRIX[TRIGGER]
const ISO_FMT = 'YYYY-MM-DDTHH:mm:ss[Z]'

const domainKey = ref<DomainKey | undefined>(undefined)
const priorityMode = ref<PriorityMode>('NORMAL')
const rows = ref<Row[]>([])
const selected = ref<Row[]>([])
const errors = ref<string[]>([])
const tableRef = ref<InstanceType<typeof ElTable> | null>(null)

function onSelectionChange(sel: Row[]): void {
  selected.value = sel
}

watch(
  () => props.visible,
  async (vis) => {
    if (!vis) return
    rows.value = props.orders.map((o) => ({
      ...o,
      // 自动带入（B4 已落地）：orderCanonicalId 存在则带入 canonicalId（保留手填兜底作兼容回退）
      canonicalId: o.orderCanonicalId
    }))
    selected.value = []
    errors.value = []
    priorityMode.value = 'NORMAL'
    domainKey.value = props.defaultDomainKey ?? props.domainOptions[0]
    await nextTick()
    const pre = new Set(props.preselectedOrderIds ?? [])
    if (pre.size > 0 && tableRef.value) {
      rows.value.forEach((r) => {
        if (pre.has(r.orderId)) tableRef.value?.toggleRowSelection(r, true)
      })
    }
  },
  { immediate: true }
)

function close(): void {
  emit('update:visible', false)
}

function onSubmit(): void {
  if (!domainKey.value) {
    ElMessage.warning('请选择排程域')
    return
  }
  if (selected.value.length === 0) {
    ElMessage.warning('请至少选择 1 个订单')
    return
  }
  const draft: ScopeDraft = {
    priorityMode: priorityMode.value,
    orderTargets: selected.value.map((r) => ({
      orderCanonicalId: Number(r.canonicalId),
      manualTargetDueDate: r.manualTargetDueDate ?? ''
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
    width="860px"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    @update:model-value="(v) => emit('update:visible', v)"
  >
    <ElForm label-width="120px" label-position="right">
      <ElFormItem label="排程域" required>
        <ElSelect v-model="domainKey" placeholder="选择单计划域" style="width: 260px">
          <ElOption v-for="d in domainOptions" :key="d" :label="d" :value="d" />
        </ElSelect>
        <span class="field-hint">一次只处理一个排程域；跨排程域请分多次提交</span>
      </ElFormItem>

      <ElFormItem label="优先模式">
        <ElRadioGroup v-model="priorityMode">
          <ElRadioButton value="NORMAL">普通（NORMAL）</ElRadioButton>
          <ElRadioButton value="EXPEDITE">加急（EXPEDITE）</ElRadioButton>
        </ElRadioGroup>
      </ElFormItem>

      <ElFormItem label="订单目标" required>
        <ElTable
          ref="tableRef"
          :data="rows"
          size="small"
          border
          max-height="320"
          row-key="orderId"
          style="width: 100%"
          @selection-change="onSelectionChange"
        >
          <ElTableColumn type="selection" width="44" />
          <ElTableColumn prop="orderNo" label="订单号" width="150" />
          <ElTableColumn prop="materialCode" label="物料" width="120">
            <template #default="{ row }">
              <span v-if="row?.materialCode">{{ row.materialCode }}</span>
              <span v-else class="muted-text">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="customerName" label="客户" width="120">
            <template #default="{ row }">
              <span v-if="row?.customerName">{{ row.customerName }}</span>
              <span v-else class="muted-text">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="正式交期" width="150">
            <template #default="{ row }">
              <span v-if="row?.customerDueDate">{{ row.customerDueDate.slice(0, 16) }}</span>
              <span v-else class="muted-text">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="订单规范 Id" width="180">
            <template #default="{ row }">
              <ElInput
                v-model="row.canonicalId"
                size="small"
                type="number"
                :placeholder="
                  row.orderCanonicalId
                    ? `已带入 ${row.orderCanonicalId}（可覆盖）`
                    : `手填（提示 ${row.orderNo}）`
                "
              />
            </template>
          </ElTableColumn>
          <ElTableColumn label="手工目标交期" width="200">
            <template #default="{ row }">
              <ElDatePicker
                v-model="row.manualTargetDueDate"
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
          已选 <strong>{{ selected.length }}</strong> / {{ rows.length }} 个订单
        </div>
      </ElFormItem>

      <ElAlert type="success" :closable="false" show-icon class="d-alert">
        <template #title>关于「订单规范 Id」</template>
        订单规范 Id 由系统按订单自动带入，通常无需修改； 若列表未带出（如历史订单 /
        异常数据），可手工填写。
        「手工目标交期」只用于本次重排计算，<strong>不会修改订单的正式交期</strong>。
      </ElAlert>

      <ElAlert v-if="errors.length > 0" type="error" :closable="false" show-icon class="d-alert">
        <template #title>校验未通过（{{ errors.length }}）</template>
        <ul class="err-list">
          <li v-for="(e, i) in errors" :key="i">{{ e }}</li>
        </ul>
      </ElAlert>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>提交后</template>
        将新建一次排产运行并生成候选版本（本入口只作用于当前计划域），需在「候选版本」页比较确认后采用。
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
