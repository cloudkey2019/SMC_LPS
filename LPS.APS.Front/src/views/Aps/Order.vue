<script setup lang="ts">
/**
 * APS V1 4号位 — 订单/需求计划（页面 2 / U03 / U04 / U05）
 *
 * 4号位文档第 5 节约束：
 *  - 顶部展示 5 种订单摘要状态聚合
 *  - 列表支持按 status / productFamily / factory / orderNo 模糊筛选
 *  - 行点击弹出 Drawer，展示 4 块详情：basic / pegging / tasks / reasons
 *  - ESTIMATED_ONLY 状态必须在表格+Drawer 中显著展示 ESTIMATED_BADGE_TEXT（4号位文档第 7 节 / U07）
 *  - Task 状态仅展示 5 种枚举值（4号位文档第 6 节）
 *  - U05：NetQty 与 PlannedProcessQty 不混淆
 *
 * 写接口：本页查询部分只读；**唯一写入口 = §10A.1「已有订单提前」**
 * （EXISTING_ORDER_ADVANCE → POST /api/governance/run/candidate，Policy aps.plan.run）
 * @see 冻结文档《4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
 */

import { computed, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useOrderStore } from '@/store/modules/aps/order'
import { useDomainStore } from '@/store/modules/aps/domain'
import { useOverviewStore } from '@/store/modules/aps/overview'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { constrainOptionsByScope } from '@/store/modules/aps/scope'
import { triggerBusinessEntry } from '@/api/aps-v1'
import type { BusinessEntrySubmission, OrderBasicInfo } from '@/api/aps-v1'
import DomainUnavailableBadge from './components/DomainUnavailableBadge.vue'
import OrderAdvanceDialog from './components/OrderAdvanceDialog.vue'
import {
  ORDER_SUMMARY_STATUS_LABELS,
  ESTIMATED_BADGE_TEXT,
  type DomainKey,
  type OrderSummaryStatus,
  type PeggingSupplyLine,
  type SupplyType,
  type TaskLockMarker
} from '@/api/aps-v1/types'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDatePicker,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElEmpty,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'

const orderStore = useOrderStore()
const {
  list,
  listTotal,
  pageIndex,
  pageSize,
  listLoading,
  listError,
  summary,
  currentDetail,
  detailLoading,
  detailError,
  query
} = storeToRefs(orderStore)

const drawerVisible = ref(false)

// ===== 订单状态颜色（与 Overview 一致） =====
const ORDER_STATUS_COLORS: Record<OrderSummaryStatus, string> = {
  ON_TIME: '#059669',
  AT_RISK: '#f59e0b',
  DELAYED: '#ef4444',
  UNSCHEDULED: '#94a3b8',
  ESTIMATED_ONLY: '#8b5cf6'
}
const ORDER_STATUS_ORDER: OrderSummaryStatus[] = [
  'ON_TIME',
  'AT_RISK',
  'DELAYED',
  'ESTIMATED_ONLY',
  'UNSCHEDULED'
]

// ===== Pegging 维度常量 =====
const SUPPLY_LABEL: Record<SupplyType, string> = {
  INVENTORY: '库存',
  PI: 'PI 在制',
  PO: 'PO 在途',
  VMI: 'VMI 寄售',
  ARRIVED_NOT_INBOUND: '到货未入库',
  INTERPLANT_TRANSIT: '跨厂在途',
  RECEIVED: '已收货',
  PLANNED_PRODUCTION: '计划生产',
  PLANNING_PURCHASE_PLACEHOLDER: '采购估算（占位）'
}

const FACT_TAG_TYPE: Record<
  PeggingSupplyLine['factType'],
  'success' | 'warning' | 'info' | 'primary'
> = {
  FACT: 'success',
  RESULT: 'primary',
  ESTIMATED: 'warning',
  RECOMMENDATION: 'info'
}

/** 锁定标记（4号位文档第 6 节：EXECUTION / FIRM / FROZEN） */
const LOCK_LABEL: Record<TaskLockMarker, string> = {
  EXECUTION: '执行锁定',
  FIRM: '已确认',
  FROZEN: '冻结'
}
const LOCK_TAG_TYPE: Record<TaskLockMarker, 'danger' | 'warning' | 'info'> = {
  EXECUTION: 'danger',
  FIRM: 'warning',
  FROZEN: 'info'
}

/** Domain 选项（v1.2 Domain专项：从 useDomainStore.activeDomains 派生，含 ALL）
 *  U41：按 apsAuth.dataScope.domainKeys 收敛——非空(受限)时仅含授权 Domain，空(global)时全量 */
const domainStore = useDomainStore()
const apsAuth = useApsAuthStore()
const DOMAIN_OPTIONS = computed<Array<{ value: DomainKey | 'ALL'; label: string }>>(() => {
  const all = domainStore.activeDomains.map((d) => ({ value: d.domainKey, label: d.domainName }))
  const allowed = apsAuth.dataScope.domainKeys as readonly string[]
  const list = allowed.length === 0 ? all : all.filter((o) => allowed.includes(o.value))
  return [{ value: 'ALL' as const, label: '全部' }, ...list]
})

/** U41：Factory / ProductFamily 筛选下拉
 *  baseline = global 用户全量；受限用户由 constrainOptionsByScope 收敛到 dataScope 白名单。
 *  （冻结 v1.4 L1463：不允许选择未授权 Factory/ProductFamily/Domain） */
const FACTORY_BASE = [
  { value: 'F-SUZ-01', label: '苏州厂 F-SUZ-01' },
  { value: 'F-CDG-02', label: '成都厂 F-CDG-02' },
  { value: 'F-BJ-01', label: '北京厂 F-BJ-01' }
]
const PRODUCT_FAMILY_BASE = [
  { value: 'INJECTION', label: '注塑 (INJECTION)' },
  { value: 'ASSEMBLY', label: '装配 (ASSEMBLY)' },
  { value: 'TEST', label: '测试 (TEST)' }
]
const FACTORY_OPTIONS = computed(() =>
  constrainOptionsByScope(FACTORY_BASE, apsAuth.dataScope.factoryCodes)
)
const PRODUCT_FAMILY_OPTIONS = computed(() =>
  constrainOptionsByScope(PRODUCT_FAMILY_BASE, apsAuth.dataScope.productFamilyCodes)
)

/** 客户下拉（4号位文档第 5 节：客户维度过滤） */
const CUSTOMER_OPTIONS = [
  { value: 'CUST-001', label: '示例客户-华东' },
  { value: 'CUST-002', label: '示例客户-华南' },
  { value: 'CUST-003', label: '示例客户-华北' },
  { value: 'CUST-004', label: '示例客户-海外' },
  { value: 'CUST-005', label: '示例客户-西南' }
]

// ===== KPI 卡片（5 种订单状态） =====
const summaryItems = computed(() => {
  const items = summary.value ?? []
  const map = new Map(items.map((it) => [it.status, it.count]))
  return ORDER_STATUS_ORDER.map((status) => ({
    status,
    label: ORDER_SUMMARY_STATUS_LABELS[status],
    count: map.get(status) ?? 0,
    color: ORDER_STATUS_COLORS[status]
  }))
})
const totalOrders = computed(() => summaryItems.value.reduce((s, i) => s + i.count, 0))

// ===== KPI 单击直接套用为表格过滤 =====
const activeStatusFilter = computed<OrderSummaryStatus | undefined>(() => query.value.delayStatus)

function applyStatusFilter(status?: OrderSummaryStatus): void {
  orderStore.setFilter({ delayStatus: status })
  orderStore.loadList(true)
}

function clearAllFilters(): void {
  orderStore.clearFilter()
  orderStore.loadList(true)
}

// ===== 表格：搜索 / 工厂 / 产品族 =====
const searchInput = ref(query.value.orderNo ?? '')
const piInput = ref(query.value.productionInstructionNo ?? '')
const materialInput = ref(query.value.materialCode ?? '')
const customerCode = ref<string | undefined>(query.value.customerCode)
const dueDateRange = ref<[string, string] | undefined>(
  query.value.dueDateFrom && query.value.dueDateTo
    ? [query.value.dueDateFrom, query.value.dueDateTo]
    : undefined
)
const planVersionInput = ref(query.value.planVersionId ? String(query.value.planVersionId) : '')
const domainCode = ref<string | undefined>(query.value.domainKey)

/**
 * 必填 planVersionId（5号位 2026-09-23《order-query 分页契约修正回执》§一）
 *  - 后端 OrderQueryController.QueryOrders 签名 `[FromQuery] int planVersionId` 非空、无默认值 = 必填
 *  - UI 输入框标 required（红框拦截）
 *  - 默认值：onMounted 从 overviewApi.getOverview().activeVersions[0].planVersionId 取
 *  - store 层校验兜底（缺 planVersionId 时 loadList 不发请求）
 */
const planVersionInputRequired = computed<boolean>(() => {
  const num = Number(planVersionInput.value)
  return !planVersionInput.value || !Number.isFinite(num) || num <= 0
})

watch(searchInput, (v) => {
  orderStore.setFilter({ orderNo: v || undefined })
})
watch(piInput, (v) => {
  orderStore.setFilter({ productionInstructionNo: v || undefined })
})
watch(materialInput, (v) => {
  orderStore.setFilter({ materialCode: v || undefined })
})
watch(planVersionInput, (v) => {
  const num = Number(v)
  orderStore.setFilter({
    planVersionId: v && Number.isFinite(num) && num > 0 ? num : undefined
  })
})

function onCustomerChange(v: string): void {
  customerCode.value = v || undefined
  orderStore.setFilter({ customerCode: v || undefined })
  orderStore.loadList(true)
}
function onDueRangeChange(v: [string, string] | null | undefined): void {
  dueDateRange.value = v ?? undefined
  orderStore.setFilter({
    dueDateFrom: v?.[0],
    dueDateTo: v?.[1]
  })
  orderStore.loadList(true)
}
function onDomainChange(v: string): void {
  const val = v && v !== 'ALL' ? v : undefined
  domainCode.value = val
  orderStore.setFilter({ domainKey: val })
  orderStore.loadList(true)
}

function onSearchSubmit(): void {
  // 必填 planVersionId（5号位 2026-09-23 回执）：用户主动提交时校验
  if (planVersionInputRequired.value) {
    ElMessage.warning(
      'PlanVersionId 必填（订单按 PlanVersion 分区），请输入有效数字或等待总览自动填充'
    )
    return
  }
  orderStore.loadList(true)
}
function onResetSearch(): void {
  searchInput.value = ''
  piInput.value = ''
  materialInput.value = ''
  customerCode.value = undefined
  dueDateRange.value = undefined
  // 必填 planVersionId：重置时回到默认兜底（不空），避免后续查询触发 400
  const fallbackId = orderStore.defaultPlanVersionId
  planVersionInput.value = fallbackId !== null ? String(fallbackId) : ''
  domainCode.value = undefined
  clearAllFilters()
}

// ===== 页码 =====
function onPageChange(p: number): void {
  orderStore.setPage({ pageIndex: p })
  orderStore.loadList()
}
function onSizeChange(s: number): void {
  orderStore.setPage({ pageIndex: 1, pageSize: s })
  orderStore.loadList()
}

// ===== 行点击：打开抽屉 =====
async function openDetail(orderId: number): Promise<void> {
  drawerVisible.value = true
  await orderStore.loadDetail(orderId)
}
function closeDetail(): void {
  drawerVisible.value = false
}

/** 多选列点击不打开抽屉（避免勾选即弹详情） */
function onRowClick(row: OrderBasicInfo, column?: { type?: string }): void {
  if (column?.type === 'selection') return
  void openDetail(row.orderId)
}

/* ===== §10A.1 已有订单提前（EXISTING_ORDER_ADVANCE） =====
 * 冻结文档《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）：
 * 本页提供 5 个独立中文业务入口中的 1 个；无"万能局部调整"入口。
 * 契约：LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode 可选 NORMAL/EXPEDITE；
 *       载荷 orderTargets[]{ orderCanonicalId, manualTargetDueDate }
 * 端点权限码：aps.plan.run（与 Gantt 侧 4 入口同一码）
 */
const router = useRouter()
const canPlanRun = computed(() => apsAuth.has('aps.plan.run'))

const advanceVisible = ref(false)
const advanceSubmitting = ref(false)
const selectedOrders = ref<OrderBasicInfo[]>([])
const tableRef = ref<InstanceType<typeof ElTable> | null>(null)

/** Dialog 候选行（仅取所需字段；OrderCanonicalId 自动带入由 Dialog watch 处理） */
const advanceCandidates = computed(() =>
  list.value.map((o) => ({
    orderId: o.orderId,
    orderNo: o.orderNo,
    orderCanonicalId: o.orderCanonicalId, // B4 已落地（B4 SQL 补列）；Dialog 自动带入
    materialCode: o.materialCode,
    customerName: o.customerName,
    customerDueDate: o.customerDueDate
  }))
)

/** Dialog Domain 选项（剔除"全部"；已按 U41 dataScope 收敛） */
const advanceDomainOptions = computed<DomainKey[]>(() =>
  DOMAIN_OPTIONS.value.filter((o) => o.value !== 'ALL').map((o) => o.value as DomainKey)
)

/** Dialog 默认 Domain：当前筛选 Domain，否则首个授权 Domain */
const advanceDefaultDomain = computed<DomainKey | undefined>(() => {
  if (domainCode.value) return domainCode.value
  return advanceDomainOptions.value[0]
})

function onOrderSelectionChange(rows: OrderBasicInfo[]): void {
  selectedOrders.value = rows
}

function openAdvanceDialog(): void {
  if (selectedOrders.value.length === 0) {
    ElMessage.warning('请先在列表中勾选要提前的订单')
    return
  }
  advanceVisible.value = true
}

async function submitOrderAdvance(payload: BusinessEntrySubmission): Promise<void> {
  advanceSubmitting.value = true
  try {
    const result = await triggerBusinessEntry({
      trigger: payload.trigger,
      domainKey: payload.domainKey,
      draft: payload.draft,
      actor: apsAuth.userInfo?.userCode ?? 'unknown'
    })
    ElMessage.success(
      `已发起：Run#${result.newRunId}，Candidate PlanVersion#${result.candidatePlanVersionId}。跳转对比页…`
    )
    advanceVisible.value = false
    selectedOrders.value = []
    tableRef.value?.clearSelection()
    router.push({
      name: 'ApsCandidate',
      query: {
        candidatePlanVersionId: String(result.candidatePlanVersionId),
        basePlanVersionId: String(result.basePlanVersionId),
        source: 'order-advance'
      }
    })
  } catch (err) {
    ElMessage.error(`发起订单提前失败：${(err as Error)?.message ?? '未知错误'}`)
  } finally {
    advanceSubmitting.value = false
  }
}

// ===== 工具：截止日期显示 =====
function fmtDue(due: string): string {
  const dueMs = new Date(due).getTime()
  const nowMs = Date.now()
  const days = Math.round((dueMs - nowMs) / 86400000)
  if (days >= 0) return `${dayjs(due).format('MM-DD')}（剩 ${days} 天）`
  return `${dayjs(due).format('MM-DD')}（已过 ${-days} 天）`
}

// 详情里 Pegging 列表是否含有"估算占位"行
const peggingHasEstimated = computed(() =>
  (currentDetail.value?.pegging ?? []).some((p) => p.factType === 'ESTIMATED')
)

// 详情里 reasons 列表
const REASON_LABELS: Record<string, string> = {
  MATERIAL_SHORTAGE: '物料短缺',
  CAPACITY_INSUFFICIENT: '产能不足',
  PREDECESSOR_DELAY: '前序延迟',
  FIRM_FROZEN: '固化冻结',
  CROSS_DOMAIN_BLOCK: '跨域阻断',
  PURCHASE_ESTIMATED_RISK: '采购估算风险',
  OTHER: '其他'
}

// ===== 加载入口 =====
// 必填 planVersionId（5号位 2026-09-23 回执）：onMounted 先取 active-plan 兜底值，再触发列表 + summary
const overviewStore = useOverviewStore()
onMounted(async () => {
  // 1) 取排产总览 → 默认 PlanVersionId（activeVersions[0]）
  await overviewStore.load().catch(() => {
    /* 静默：loadList 校验会兜底提示 */
  })
  const activeVersions = overviewStore.data?.activeVersions ?? []
  const fallbackId = activeVersions.length > 0 ? activeVersions[0].planVersionId : null
  orderStore.setDefaultPlanVersionId(fallbackId)
  // 2) 同步到 UI 输入框（仅当用户未手填）
  if (!planVersionInput.value && fallbackId !== null) {
    planVersionInput.value = String(fallbackId)
  }
  // 3) 触发列表 + summary
  await Promise.all([orderStore.loadList(), orderStore.loadSummary()])
})
</script>

<template>
  <div class="aps-order">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">订单 / 需求计划</h2>
        <p class="page-sub">按订单检索排产详情，支持打开 Pegging / 任务 / 原因</p>
      </div>
      <ElButton :loading="listLoading" @click="orderStore.loadList()">
        <Icon icon="vi-ep:refresh" /> 刷新
      </ElButton>
      <!-- §10A.1 已有订单提前：5 个独立中文业务入口中的本页唯一入口 -->
      <ElButton
        type="warning"
        plain
        :disabled="!canPlanRun || selectedOrders.length === 0"
        :title="canPlanRun ? '已有订单提前 — 先在列表勾选订单' : '当前账号无排程执行权限'"
        @click="openAdvanceDialog"
      >
        <Icon icon="vi-mdi:calendar-arrow-right" />
        已有订单提前（{{ selectedOrders.length }}）
      </ElButton>
    </div>

    <!-- ===== 错误条 ===== -->
    <ElAlert
      v-if="listError"
      type="error"
      :closable="false"
      show-icon
      :title="`加载失败：${listError}`"
    />

    <!-- ===== KPI 卡片 ===== -->
    <div class="kpi-row">
      <div
        v-for="item in summaryItems"
        :key="item.status"
        class="kpi-card"
        :class="{ active: activeStatusFilter === item.status }"
        :style="{ borderTopColor: item.color }"
        @click="applyStatusFilter(activeStatusFilter === item.status ? undefined : item.status)"
      >
        <div class="kpi-label">{{ item.label }}</div>
        <div class="kpi-value" :style="{ color: item.color }">{{ item.count }}</div>
        <div class="kpi-ratio">
          {{ totalOrders > 0 ? Math.round((item.count / totalOrders) * 100) : 0 }}%
          <span v-if="activeStatusFilter === item.status" class="active-tip">已选中</span>
        </div>
      </div>
      <div class="kpi-card total">
        <div class="kpi-label">订单合计</div>
        <div class="kpi-value">{{ totalOrders }}</div>
        <div class="kpi-ratio">
          <a
            v-if="activeStatusFilter"
            href="javascript:;"
            @click.stop="applyStatusFilter(undefined)"
            >清空筛选</a
          >
          <span v-else>5 类汇总</span>
        </div>
      </div>
    </div>

    <!-- ===== 过滤栏 ===== -->
    <ElCard class="filter-bar">
      <div class="filter-row">
        <ElInput
          v-model="searchInput"
          placeholder="订单号 ORD-2026-0001"
          clearable
          class="filter-search"
          @keyup.enter="onSearchSubmit"
        >
          <template #prefix>
            <Icon icon="vi-ep:search" />
          </template>
        </ElInput>
        <ElInput
          v-model="piInput"
          placeholder="生产指令 PI-00001"
          clearable
          class="filter-search-sm"
          @keyup.enter="onSearchSubmit"
        />
        <ElInput
          v-model="materialInput"
          placeholder="物料 M-001"
          clearable
          class="filter-search-sm"
          @keyup.enter="onSearchSubmit"
        />
        <ElSelect
          :model-value="customerCode"
          placeholder="客户"
          clearable
          class="filter-select"
          @change="onCustomerChange"
        >
          <ElOption
            v-for="c in CUSTOMER_OPTIONS"
            :key="c.value"
            :label="c.label"
            :value="c.value"
          />
        </ElSelect>
        <ElDatePicker
          v-model="dueDateRange"
          type="daterange"
          range-separator="~"
          start-placeholder="交期起"
          end-placeholder="交期止"
          value-format="YYYY-MM-DD"
          class="filter-date"
          @change="onDueRangeChange"
        />
        <ElInput
          v-model="planVersionInput"
          placeholder="计划版本 ID（必填，自动取自总览）"
          clearable
          class="filter-search-sm"
          :class="{ 'filter-search-sm-required': planVersionInputRequired }"
          @keyup.enter="onSearchSubmit"
        >
          <template #prefix>
            <span class="required-mark" title="必填">*</span>
          </template>
        </ElInput>
        <ElSelect
          :model-value="query.domainKey ?? domainCode ?? 'ALL'"
          placeholder="排程域"
          clearable
          class="filter-select-sm"
          @change="onDomainChange"
        >
          <ElOption v-for="d in DOMAIN_OPTIONS" :key="d.value" :label="d.label" :value="d.value" />
        </ElSelect>
        <DomainUnavailableBadge />
        <ElSelect
          :model-value="query.productFamilyCode"
          placeholder="产品族"
          clearable
          class="filter-select-sm"
          @change="
            (v: string) => {
              orderStore.setFilter({ productFamilyCode: v || undefined })
              orderStore.loadList(true)
            }
          "
        >
          <ElOption
            v-for="p in PRODUCT_FAMILY_OPTIONS"
            :key="p.value"
            :label="p.label"
            :value="p.value"
          />
        </ElSelect>
        <ElSelect
          :model-value="query.factoryCode"
          placeholder="工厂"
          clearable
          class="filter-select-sm"
          @change="
            (v: string) => {
              orderStore.setFilter({ factoryCode: v || undefined })
              orderStore.loadList(true)
            }
          "
        >
          <ElOption v-for="f in FACTORY_OPTIONS" :key="f.value" :label="f.label" :value="f.value" />
        </ElSelect>
        <ElButton type="primary" :disabled="planVersionInputRequired" @click="onSearchSubmit">
          <Icon icon="vi-ep:search" /> 查询
        </ElButton>
        <ElButton @click="onResetSearch">重置</ElButton>
      </div>
    </ElCard>

    <!-- ===== 订单表 ===== -->
    <ElCard class="table-card">
      <template #header>
        <div class="panel-header">
          <span>订单列表</span>
          <span class="header-total">{{ orderStore.filteredTotalLabel }}</span>
        </div>
      </template>
      <ElTable
        ref="tableRef"
        v-loading="listLoading"
        :data="list"
        stripe
        size="small"
        row-key="orderId"
        empty-text="暂无订单"
        @row-click="onRowClick"
        @selection-change="onOrderSelectionChange"
      >
        <ElTableColumn type="selection" width="44" :selectable="() => canPlanRun" />
        <ElTableColumn prop="orderNo" label="订单号" min-width="170" />
        <ElTableColumn prop="materialCode" label="物料" min-width="160">
          <template #default="{ row }">
            <span>{{ row.materialCode }}</span>
            <span class="muted"> / {{ row.materialName }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="customerName" label="客户" min-width="130" />
        <ElTableColumn prop="factoryCode" label="工厂" min-width="120" />
        <ElTableColumn prop="productFamilyCode" label="族" width="100" />
        <ElTableColumn prop="orderQty" label="数量" width="100" align="right">
          <template #default="{ row }">
            <span>{{ row.orderQty }}</span>
            <span class="muted"> {{ row.uom }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="交期" width="160">
          <template #default="{ row }">
            <span :class="{ overdue: new Date(row.customerDueDate).getTime() < Date.now() }">
              {{ fmtDue(row.customerDueDate) }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="prioritySegmentCode" label="优先级" width="80" align="center">
          <template #default="{ row }">
            <ElTag
              size="small"
              :type="
                row.prioritySegmentCode === 'P1'
                  ? 'danger'
                  : row.prioritySegmentCode === 'P2'
                    ? 'warning'
                    : 'info'
              "
            >
              {{ row.prioritySegmentCode }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="160">
          <template #default="{ row }">
            <ElTag
              size="small"
              effect="light"
              :style="{
                background: ORDER_STATUS_COLORS[row.status as OrderSummaryStatus],
                color: '#fff',
                borderColor: ORDER_STATUS_COLORS[row.status as OrderSummaryStatus]
              }"
            >
              {{ ORDER_SUMMARY_STATUS_LABELS[row.status as OrderSummaryStatus] }}
            </ElTag>
            <ElTooltip
              v-if="row.status === 'ESTIMATED_ONLY'"
              :content="ESTIMATED_BADGE_TEXT"
              placement="top"
            >
              <Icon class="est-icon" icon="vi-mdi:alert-decagram" />
            </ElTooltip>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="80" align="center" fixed="right">
          <template #default="{ row }">
            <a href="javascript:;" @click.stop="openDetail(row.orderId)">详情</a>
          </template>
        </ElTableColumn>
        <template #empty>
          <ElEmpty description="暂无订单" />
        </template>
      </ElTable>
      <div class="pager-row">
        <ElPagination
          v-model:current-page="pageIndex"
          v-model:page-size="pageSize"
          :total="listTotal"
          :page-sizes="[10, 20, 50]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          @current-change="onPageChange"
          @size-change="onSizeChange"
        />
      </div>
    </ElCard>

    <!-- ===== 详情 Drawer ===== -->
    <ElDrawer
      v-model="drawerVisible"
      size="540px"
      direction="rtl"
      :destroy-on-close="false"
      @close="closeDetail"
    >
      <template #header>
        <div class="drawer-head">
          <span class="drawer-title">订单详情</span>
          <span v-if="currentDetail" class="drawer-sub">
            {{ currentDetail.basic.orderNo }}
          </span>
        </div>
      </template>
      <template v-if="currentDetail">
        <ElAlert
          v-if="peggingHasEstimated"
          type="warning"
          :closable="false"
          show-icon
          class="drawer-est-alert"
          :title="ESTIMATED_BADGE_TEXT"
        />

        <!-- ===== basic ===== -->
        <div class="detail-section">
          <h4 class="section-title">基本信息</h4>
          <ElDescriptions :column="2" size="small" border>
            <ElDescriptionsItem label="订单号">{{
              currentDetail.basic.orderNo
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="生产指令">{{
              currentDetail.basic.productionInstructionNo
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="物料">
              {{ currentDetail.basic.materialCode }} / {{ currentDetail.basic.materialName }}
            </ElDescriptionsItem>
            <ElDescriptionsItem label="客户">
              {{ currentDetail.basic.customerName }}
            </ElDescriptionsItem>
            <ElDescriptionsItem label="工厂">{{
              currentDetail.basic.factoryCode
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="产品族">{{
              currentDetail.basic.productFamilyCode
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="订单数量">
              {{ currentDetail.basic.orderQty }} {{ currentDetail.basic.uom }}
            </ElDescriptionsItem>
            <ElDescriptionsItem label="客户交期">
              {{ dayjs(currentDetail.basic.customerDueDate).format('YYYY-MM-DD HH:mm') }}
            </ElDescriptionsItem>
            <ElDescriptionsItem label="优先级段">{{
              currentDetail.basic.prioritySegmentCode
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="状态">
              <ElTag
                size="small"
                effect="light"
                :style="{
                  background: ORDER_STATUS_COLORS[currentDetail.basic.status],
                  color: '#fff',
                  borderColor: ORDER_STATUS_COLORS[currentDetail.basic.status]
                }"
              >
                {{ ORDER_SUMMARY_STATUS_LABELS[currentDetail.basic.status] }}
              </ElTag>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="计划版本">
              {{ currentDetail.basic.planVersionId ?? '-' }}
              <span v-if="currentDetail.basic.planVersionStatus" class="muted">
                ({{ currentDetail.basic.planVersionStatus }})
              </span>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="排程域">{{
              currentDetail.basic.domainKey ?? '-'
            }}</ElDescriptionsItem>
          </ElDescriptions>
        </div>

        <!-- ===== pegging ===== -->
        <div class="detail-section">
          <h4 class="section-title"> Pegging 供应承接 </h4>
          <ElTable :data="currentDetail.pegging" size="small" border>
            <ElTableColumn label="来源类型" min-width="120">
              <template #default="{ row }">
                {{ SUPPLY_LABEL[row.supplyType as SupplyType] || row.supplyType }}
              </template>
            </ElTableColumn>
            <ElTableColumn prop="supplyKey" label="供应 Key" min-width="110" />
            <ElTableColumn prop="allocatedQty" label="承接数量" width="90" align="right">
              <template #default="{ row }">
                <span :class="{ 'est-qty': row.factType === 'ESTIMATED' }">
                  {{ row.allocatedQty }}
                </span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="就绪时间" width="150">
              <template #default="{ row }">
                {{ row.availableTime ? dayjs(row.availableTime).format('YYYY-MM-DD HH:mm') : '-' }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="履约承诺" width="100">
              <template #default="{ row }">
                <ElTag
                  v-if="row.commitment"
                  size="small"
                  :type="row.commitment === 'COMMITMENT' ? 'success' : 'info'"
                  effect="light"
                >
                  {{
                    row.commitment === 'COMMITMENT'
                      ? '已承诺'
                      : row.commitment === 'NONE'
                        ? '无承诺'
                        : row.commitment
                  }}
                </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="置信度" width="80">
              <template #default="{ row }">
                <ElTag
                  v-if="row.confidence"
                  size="small"
                  :type="
                    row.confidence === 'HIGH'
                      ? 'success'
                      : row.confidence === 'LOW'
                        ? 'danger'
                        : 'warning'
                  "
                  effect="plain"
                >
                  {{ row.confidence }}
                </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="信息分类" width="100">
              <template #default="{ row }">
                <ElTag
                  size="small"
                  :type="FACT_TAG_TYPE[row.factType as keyof typeof FACT_TAG_TYPE]"
                >
                  {{ row.factType }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="硬锁" width="60" align="center">
              <template #default="{ row }">
                <ElTag v-if="row.isHardLocked" size="small" type="warning">锁</ElTag>
                <span v-else>-</span>
              </template>
            </ElTableColumn>
          </ElTable>
        </div>

        <!-- ===== tasks ===== -->
        <div class="detail-section">
          <h4 class="section-title"> 生产任务 </h4>
          <ElTable :data="currentDetail.tasks" size="small" border>
            <ElTableColumn prop="taskNo" label="任务号" width="90" />
            <ElTableColumn label="阶段" width="80">
              <template #default="{ row }">
                <ElTag v-if="row.stageCode" size="small" effect="plain">
                  {{ row.stageCode }}
                </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="工序" width="90">
              <template #default="{ row }">
                {{ row.operationCode }} (Seq {{ row.operationSeq }})
              </template>
            </ElTableColumn>
            <ElTableColumn prop="resourceCode" label="资源" width="70" />
            <ElTableColumn label="开始" width="140">
              <template #default="{ row }">
                {{ row.startTime ? dayjs(row.startTime).format('MM-DD HH:mm') : '-' }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="结束" width="140">
              <template #default="{ row }">
                {{ row.endTime ? dayjs(row.endTime).format('MM-DD HH:mm') : '-' }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="净产出" width="90" align="right">
              <template #default="{ row }">
                <span class="hl-num">{{ row.netQty }}</span> {{ row.uom }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="计划加工" width="90" align="right">
              <template #default="{ row }">
                <span :class="{ 'hl-warn': row.plannedProcessQty !== row.netQty }">
                  {{ row.plannedProcessQty }}
                </span>
                {{ row.uom }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="状态" width="80">
              <template #default="{ row }">
                <ElTag size="small">
                  {{ row.status }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="锁定" width="80" align="center">
              <template #default="{ row }">
                <ElTag
                  v-if="row.lockMarker"
                  size="small"
                  :type="LOCK_TAG_TYPE[row.lockMarker]"
                  effect="plain"
                >
                  {{ LOCK_LABEL[row.lockMarker] }}
                </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="任务份额" min-width="120">
              <template #default="{ row }">
                <span v-if="!row.taskShares?.length" class="muted">-</span>
                <span v-else>
                  {{ row.taskShares[0].orderNo }}
                  <span class="muted">({{ row.taskShares[0].shareQty }})</span>
                </span>
              </template>
            </ElTableColumn>
          </ElTable>
          <p class="u05-hint">
            净产出 <span class="hl-num">净产出量</span> 与计划加工
            <span class="hl-warn">计划加工量</span> 是两个不同口径：
            计划加工含良率损耗，净产出为最终交付数量。
          </p>
        </div>

        <!-- ===== reasons ===== -->
        <div class="detail-section">
          <h4 class="section-title"> 原因解释 </h4>
          <div v-if="!currentDetail.reasons.length" class="muted empty-tip">
            当前订单无解释信息
          </div>
          <div v-else class="reason-list">
            <div v-for="(r, idx) in currentDetail.reasons" :key="idx" class="reason-item">
              <ElTag size="small" type="danger" effect="light">
                {{ REASON_LABELS[r.reasonCode] || r.reasonCode }}
              </ElTag>
              <div class="reason-desc">{{ r.description }}</div>
              <div v-if="r.rootCause" class="reason-root"> 根因：{{ r.rootCause }} </div>
              <div v-if="r.relatedObjectRef" class="reason-rel">
                关联对象：<code>{{ r.relatedObjectRef }}</code>
              </div>
            </div>
          </div>
        </div>
      </template>
      <template v-else>
        <div v-loading="detailLoading" class="drawer-loading">
          <ElEmpty v-if="!detailLoading" :description="detailError || '请选择订单查看详情'" />
        </div>
      </template>
    </ElDrawer>

    <!-- ===== §10A.1 已有订单提前 Dialog（EXISTING_ORDER_ADVANCE） ===== -->
    <OrderAdvanceDialog
      :visible="advanceVisible"
      :submitting="advanceSubmitting"
      :domain-options="advanceDomainOptions"
      :orders="advanceCandidates"
      :preselected-order-ids="selectedOrders.map((o) => o.orderId)"
      :default-domain-key="advanceDefaultDomain"
      @update:visible="(v) => (advanceVisible = v)"
      @submit="submitOrderAdvance"
    />
  </div>
</template>

<style lang="less" scoped>
.aps-order {
  display: flex;
  min-height: calc(100vh - 100px);
  padding: 16px;
  background: #f5f7fa;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;

  .page-title {
    margin: 0;
    font-size: 20px;
    font-weight: 600;
    color: #1e293b;
  }

  .page-sub {
    margin: 4px 0 0;
    font-size: 12px;
    color: #94a3b8;
  }
}

.hint-bar {
  :deep(.el-alert__title) {
    font-weight: 600;
  }

  .hint-text {
    margin-left: 8px;
    font-size: 13px;
    color: #475569;
  }
}

.kpi-row {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
  gap: 12px;
}

.kpi-card {
  padding: 14px 16px;
  cursor: pointer;
  background: #fff;
  border-top: 3px solid #94a3b8;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);
  transition: box-shadow 0.2s;

  &:hover {
    box-shadow: 0 4px 12px rgb(0 0 0 / 10%);
  }

  .kpi-label {
    font-size: 12px;
    color: #94a3b8;
  }

  .kpi-value {
    margin-top: 4px;
    font-size: 22px;
    font-weight: 600;
    color: #1e293b;
  }

  .kpi-ratio {
    margin-top: 2px;
    font-size: 11px;
    color: #cbd5e1;

    .active-tip {
      margin-left: 6px;
      font-weight: 600;
      color: #3b82f6;
    }
  }

  &.active {
    box-shadow:
      0 0 0 2px #3b82f6 inset,
      0 4px 12px rgb(59 130 246 / 18%);
  }

  &.total {
    cursor: default;
    background: #f1f5f9;

    .kpi-value {
      color: #475569;
    }
  }
}

.filter-bar {
  border-radius: 8px;
}

.filter-row {
  display: flex;
  gap: 12px;
  align-items: center;
  flex-wrap: wrap;
}

.filter-search {
  width: 240px;
}

.filter-search-sm {
  width: 160px;
}

.filter-select {
  width: 170px;
}

.filter-select-sm {
  width: 130px;
}

.filter-date {
  width: 260px;
}

.table-card {
  border-radius: 8px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 14px;
  font-weight: 600;
  color: #1e293b;
}

.header-total {
  font-size: 12px;
  font-weight: 400;
  color: #94a3b8;
}

.pager-row {
  display: flex;
  margin-top: 12px;
  justify-content: flex-end;
}

.muted {
  color: #94a3b8;
}

.est-icon {
  margin-left: 6px;
  color: #8b5cf6;
  vertical-align: middle;
  cursor: pointer;
}

.est-qty {
  font-weight: 600;
  color: #8b5cf6;
}

.hl-num {
  font-weight: 600;
  color: #059669;
}

.hl-warn {
  font-weight: 600;
  color: #f59e0b;
}

.overdue {
  font-weight: 600;
  color: #ef4444;
}

.u05-hint {
  padding: 8px 10px;
  margin: 8px 0 0;
  font-size: 12px;
  line-height: 1.6;
  color: #64748b;
  background: #f8fafc;
  border-left: 3px solid #3b82f6;
  border-radius: 4px;
}

.drawer-head {
  display: flex;
  align-items: center;
  gap: 12px;

  .drawer-title {
    font-size: 16px;
    font-weight: 600;
    color: #1e293b;
  }

  .drawer-sub {
    font-size: 12px;
    color: #94a3b8;
  }
}

.drawer-est-alert {
  margin-bottom: 12px;
}

.detail-section {
  margin-bottom: 18px;

  .section-title {
    display: flex;
    margin: 0 0 8px;
    font-size: 13px;
    font-weight: 600;
    color: #475569;
    align-items: center;
    gap: 8px;
  }
}

.empty-tip {
  padding: 12px;
  background: #f8fafc;
  border-radius: 4px;
}

.reason-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.reason-item {
  padding: 10px 12px;
  background: #fef2f2;
  border-left: 3px solid #ef4444;
  border-radius: 4px;

  .reason-desc {
    margin-top: 6px;
    font-size: 13px;
    color: #1e293b;
  }

  .reason-root {
    margin-top: 4px;
    font-size: 12px;
    color: #475569;
  }

  .reason-rel {
    margin-top: 4px;
    font-size: 12px;
    color: #94a3b8;

    code {
      padding: 1px 4px;
      font-family: 'Courier New', monospace;
      background: #f1f5f9;
      border-radius: 2px;
    }
  }
}

.drawer-loading {
  display: flex;
  height: 200px;
  justify-content: center;
  align-items: center;
}
</style>
