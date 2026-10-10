<script setup lang="ts">
/**
 * APS V1 4号位 — Candidate 对比与确认（页面 5 / U10 / U11 / U12 / U13 / U14）
 *
 * 4号位文档第 9 节约束：
 *  - CTP / INSERT_IMPACT_ANALYSIS 触发产生 WHATIF Candidate（INSERT_ORDER_WHATIF），
 *    仅试算，不可激活（审核报告 P0-05）
 *  - LOCAL_RESCHEDULE / MANUAL_RESCHEDULE 触发产生可激活 Candidate
 *  - 4号位页面是确认入口，按钮触发走 Controller（后端产生新 ACTIVE 版本）
 *  - Confirmation 结果 Actor / Time 可追溯（U13）
 *  - 跨 Domain 阻挡原因可见（U14）
 *
 * 写接口：confirm() → 走 Controller，不直接 UPDATE PlanVersion
 */

import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import {
  formatUtcDateTime,
  formatUtcDateTimeSec,
  formatUtcMonthDay,
  formatUtcMonthDayHM
} from '@/utils/datetime'
import { storeToRefs } from 'pinia'
import { useCandidateStore, type CandidateListItem } from '@/store/modules/aps/candidate'
import { RUN_TYPE_LABEL } from '@/store/modules/aps/run'
import {
  CANDIDATE_PURPOSE_LABELS,
  CANDIDATE_SOURCE_REF_TYPE_LABELS,
  ESTIMATED_BADGE_TEXT,
  FACT_TYPE_LABELS,
  PLAN_VERSION_STATUS_LABELS,
  type CandidateReasonCode,
  type FactType
} from '@/api/aps-v1/types'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessageBox,
  ElTag,
  ElTable,
  ElTableColumn,
  ElTooltip
} from 'element-plus'

const candidateStore = useCandidateStore()
const route = useRoute()
const {
  list,
  currentComparison,
  lastConfirmResult,
  lastActivateResult,
  loading,
  listLoading,
  confirming,
  activating,
  error,
  currentCandidateId,
  currentHeader,
  canConfirm,
  canActivate,
  isWhatif,
  cannotConfirmReason,
  cannotActivateReason,
  // Pkg-4：v1.2 §10/§11/§13 共享资源/切片 computed 渲染辅助
  currentSharedResourceOccupancies,
  currentQuantityTimeSlices
} = storeToRefs(candidateStore)

/**
 * 跳转来源（route.query.source）
 *  - 'gantt'          → Gantt 任务 Drawer 的 "发起 MANUAL_RESCHEDULE"（P0）
 *  - 'resource-down'  → Gantt 任务 Drawer 的 "基于设备故障建议重排"（P1）
 *  - 其他 / 无        → 直接访问 Candidate 页
 */
type TriggerSource = 'gantt' | 'resource-down' | 'unknown'
const triggerSource = computed<TriggerSource>(() => {
  const s = route.query.source
  if (s === 'gantt') return 'gantt'
  if (s === 'resource-down') return 'resource-down'
  return 'unknown'
})

/** query 中的候选 ID（Gantt 抽屉跳转过来时携带） */
const targetCandidateId = computed<number | null>(() => {
  const raw = route.query.candidatePlanVersionId
  if (raw === undefined || raw === null || raw === '') return null
  const n = Number(raw)
  return Number.isFinite(n) ? n : null
})

/** query 中的对比基线 ID（Order 页"已有订单提前"跳转时携带）
 *  - compare-with 端点双参的第二参；优先取 URL，缺再回退 list（5号位 17 字段收口中）
 *  - URL 携带来源：Order.vue openAdvanceDialog 成功后 router.push 的 basePlanVersionId
 */
const queryBasePlanVersionId = computed<number | null>(() => {
  const raw = route.query.basePlanVersionId
  if (raw === undefined || raw === null || raw === '') return null
  const n = Number(raw)
  return Number.isFinite(n) && n > 0 ? n : null
})

/** Non-null 在 v-if 块内用 */
const compNN = computed(() => currentComparison.value!)
/** Header 同上（currentHeader 是 header|null；template v-if 不传播窄化） */
const headerNN = computed(() => compNN.value.header)

/** 来源 / 用途中文标签（可选字段，空值回退 '—'） */
const sourceRefLabel = computed(() => {
  const t = headerNN.value.sourceRefType
  if (!t) return '—'
  return CANDIDATE_SOURCE_REF_TYPE_LABELS[t] ?? t
})
const purposeLabel = computed(() => {
  const p = headerNN.value.purpose
  if (!p) return '—'
  return CANDIDATE_PURPOSE_LABELS[p] ?? p
})

/* ===== Confirm Modal ===== */
const dialogVisible = ref(false)
const confirmForm = ref({ actor: 'current-user', remark: '' })

/* ===== 字典 ===== */
const FACT_TAG: Record<FactType, 'success' | 'warning' | 'info' | 'primary'> = {
  FACT: 'success',
  RESULT: 'primary',
  ESTIMATED: 'warning',
  RECOMMENDATION: 'info'
}

/** Pkg-4 (v1.2 §10/§11/§13)：reasonCode 配色
 *  - CAPACITY_REALLOCATE / CTP_WHATIF / INSERT_IMPACT_WHATIF：信息（info）
 *  - CROSS_DOMAIN_BLOCK / SHARED_RESOURCE_OCCUPANCY：警告（warning，黄）
 *  - EXTERNAL_DOMAIN_BLOCK：危险（danger，红）
 *  - QUANTITY_TIME_SLICE：成功（success，绿 — 数据流已正确分段）
 */
const REASON_TAG: Record<CandidateReasonCode, 'info' | 'warning' | 'success' | 'danger'> = {
  CAPACITY_REALLOCATE: 'info',
  CROSS_DOMAIN_BLOCK: 'warning',
  CTP_WHATIF: 'info',
  INSERT_IMPACT_WHATIF: 'info',
  SHARED_RESOURCE_OCCUPANCY: 'warning',
  EXTERNAL_DOMAIN_BLOCK: 'danger',
  QUANTITY_TIME_SLICE: 'success'
}

/** Pkg-4：reasonCode 标签文案 */
const REASON_LABEL: Record<CandidateReasonCode, string> = {
  CAPACITY_REALLOCATE: '容量再分配',
  CROSS_DOMAIN_BLOCK: '跨域阻挡',
  CTP_WHATIF: '交期试算',
  INSERT_IMPACT_WHATIF: '插单影响试算',
  SHARED_RESOURCE_OCCUPANCY: '共享资源占用',
  EXTERNAL_DOMAIN_BLOCK: '外部排程域阻挡',
  QUANTITY_TIME_SLICE: '数量-时间切片'
}

/* ===== 操作 ===== */
async function selectCandidate(item: CandidateListItem): Promise<void> {
  candidateStore.reset()
  // v1.4 §十二：compare-with 端点需 candidateId + baseId
  await candidateStore.loadComparison(item.candidatePlanVersionId, item.basePlanVersionId)
}

function openConfirmDialog(): void {
  confirmForm.value = { actor: 'current-user', remark: '' }
  dialogVisible.value = true
}
async function onConfirm(): Promise<void> {
  const h = currentHeader.value
  if (!h) return
  await candidateStore.confirm({
    candidatePlanVersionId: h.candidatePlanVersionId,
    basePlanVersionId: h.basePlanVersionId,
    actor: confirmForm.value.actor,
    remark: confirmForm.value.remark || undefined
  })
  if (!error.value) {
    dialogVisible.value = false
  }
}

/**
 * 激活（CANDIDATE → ACTIVE，v1.2 §十三 P0-08）
 *  - 走 ElMessageBox.confirm 二次确认（前端仅可激活门控 + 后端 400/403/409 双保险）
 *  - 后端返回 200 → store.activate 已自动刷新 list + comparison
 *  - 不可与 confirm 并发：confirm 是留痕，activate 是落地，逻辑独立但用户操作流应顺序进行
 */
async function onActivate(): Promise<void> {
  const h = currentHeader.value
  if (!h) return
  if (!canActivate.value) {
    ElMessageBox.alert(cannotActivateReason.value ?? '当前候选版本不允许激活', '无法激活', {
      type: 'warning'
    })
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认将「${h.candidateVersionCode}」（排程域 ${h.domainKey}）激活为正式采用版本？激活后该排程域的旧生效版本将被自动归档。`,
      '激活候选版本',
      {
        type: 'warning',
        confirmButtonText: '确认激活',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--warning'
      }
    )
  } catch {
    // 用户取消
    return
  }
  await candidateStore.activate({ candidatePlanVersionId: h.candidatePlanVersionId })
}

/* ===== 工具 ===== */
function delayHours(hours: number): string {
  const sign = hours >= 0 ? '+' : ''
  return `${sign}${hours}h`
}

onMounted(async () => {
  await candidateStore.loadList()
  // Gantt 抽屉 / Order 页跳转过来时，携带 candidatePlanVersionId：自动加载对比详情
  if (targetCandidateId.value !== null) {
    // v1.4 §十二：basePlanVersionId 优先取 URL（Order 页跳转携带），缺再回退 list（5号位 17 字段收口中）
    const found = list.value.find(
      (c: CandidateListItem) => c.candidatePlanVersionId === targetCandidateId.value
    )
    await candidateStore.loadComparison(
      targetCandidateId.value,
      queryBasePlanVersionId.value ?? found?.basePlanVersionId
    )
  }
})

/** 监听 route.query 变化：用户在 Gantt 抽屉连续触发不同 Task 时，刷新对比 */
watch(
  () => route.query.candidatePlanVersionId,
  async (newId) => {
    if (newId === undefined || newId === null || newId === '') return
    const n = Number(newId)
    if (!Number.isFinite(n)) return
    if (candidateStore.currentCandidateId === n) return
    await candidateStore.loadList()
    const found = list.value.find((c: CandidateListItem) => c.candidatePlanVersionId === n)
    await candidateStore.loadComparison(n, queryBasePlanVersionId.value ?? found?.basePlanVersionId)
  }
)
</script>

<template>
  <div class="aps-candidate">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">候选版本对比与确认</h2>
        <p class="page-sub">选择候选版本 → 对比基准版本差异 → 人工确认采用</p>
      </div>
      <ElButton :loading="listLoading" @click="candidateStore.loadList">
        <Icon icon="vi-ep:refresh" /> 刷新列表
      </ElButton>
    </div>

    <!-- ===== 来源提示（Gantt 抽屉跳转过来时，按 source 分文案） ===== -->
    <ElAlert
      v-if="triggerSource === 'gantt'"
      type="success"
      :closable="false"
      show-icon
      class="from-gantt-bar"
      title="本次候选版本由甘特图任务详情的「发起人工重排」产生，已自动加载对比"
    />
    <ElAlert
      v-else-if="triggerSource === 'resource-down'"
      type="warning"
      :closable="false"
      show-icon
      class="from-resource-bar"
      title="本次候选版本由甘特图任务详情中的「基于设备故障建议重排」产生，已自动加载对比"
    />

    <!-- ===== 业务约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>使用说明</template>
      <span class="hint-text">
        可靠交期判断 / 插单影响分析产生的候选版本<strong>只能查看，不能"采用"</strong>；
        确认动作会记录操作人与时间，事后可追溯； 跨排程域的阻挡原因会在下方逐条列出。
        <strong>能否激活以系统判定结果为准</strong>，页面据此区分 "可激活" 与
        "试算只读"，不自行判定。
      </span>
      <span class="hint-text hint-text-v12">
        其它排程域在共享资源上的占用，会作为<strong>不可移动的时间块</strong>传入，候选版本不会跨域借资源；
        跨域的数量-时间切片<strong>必须保留分段</strong>（如 "40 件 @9/15 / 60 件
        @9/17"），不会压平为 100 件。
      </span>
    </ElAlert>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="error" />

    <!-- ===== Candidate 列表 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>候选版本列表</span>
          <ElTag v-if="list.length" type="warning" size="small"> {{ list.length }} 个 </ElTag>
        </div>
      </template>
      <div v-if="!list.length && !listLoading" class="panel-empty">
        <ElEmpty description="当前无候选版本" />
      </div>
      <div v-else class="candidate-grid">
        <div
          v-for="item in list"
          :key="item.candidatePlanVersionId"
          class="candidate-card"
          :class="{ active: currentCandidateId === item.candidatePlanVersionId }"
          @click="selectCandidate(item)"
        >
          <div class="card-head">
            <span class="card-code">{{ item.candidateVersionCode }}</span>
            <div class="card-head-tags">
              <ElTag size="small" effect="dark">{{ item.status }}</ElTag>
              <!-- P0-05：区分可激活 vs WHATIF -->
              <ElTag
                v-if="item.runType === 'INSERT_ORDER_WHATIF'"
                size="small"
                type="info"
                effect="plain"
              >
                WHATIF
              </ElTag>
              <ElTag v-else-if="!item.canActivate" size="small" type="danger" effect="plain">
                禁止激活
              </ElTag>
              <ElTag v-else size="small" type="success" effect="plain"> 可激活 </ElTag>
            </div>
          </div>
          <div class="card-meta">
            <div class="meta-row">
              <span class="meta-label">排程域</span>
              <ElTag size="small" effect="plain">{{ item.domainKey }}</ElTag>
            </div>
            <div class="meta-row">
              <span class="meta-label">创建</span>
              <span>{{ formatUtcMonthDayHM(item.createdAt) }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">运行类型</span>
              <span class="run-type">{{
                item.runType ? (RUN_TYPE_LABEL[item.runType] ?? item.runType) : '—'
              }}</span>
            </div>
            <div class="meta-row">
              <span class="meta-label">影响订单</span>
              <span :class="{ warn: item.impactSummary.impactedOrderCount > 0 }">
                {{ item.impactSummary.impactedOrderCount }}
              </span>
            </div>
            <div class="meta-row">
              <span class="meta-label">新增延期</span>
              <span :class="{ danger: item.impactSummary.newDelayCount > 0 }">
                {{ item.impactSummary.newDelayCount }}
              </span>
            </div>
          </div>
        </div>
      </div>
    </ElCard>

    <!-- ===== 加载态 ===== -->
    <div v-if="loading" class="loading-tip">加载候选版本对比中…</div>

    <!-- ===== 对比详情 ===== -->
    <template v-if="currentComparison">
      <!-- Header 条 -->
      <ElCard class="panel header-card">
        <template #header>
          <div class="panel-header">
            <span>
              <ElTag type="warning" effect="dark">候选版本</ElTag>
              {{ headerNN.candidateVersionCode }}
              <span class="muted-tag">对比</span>
              <ElTag type="success" effect="dark">基准 {{ headerNN.baseVersionCode }}</ElTag>
              <!-- P0-05：WHATIF 候选 → 头部显式标识 -->
              <ElTag v-if="isWhatif" type="info" effect="dark" style="margin-left: 8px">
                试算只读
              </ElTag>
            </span>
            <div class="header-actions">
              <ElButton v-if="canConfirm" type="primary" @click="openConfirmDialog">
                <Icon icon="vi-ep:check" /> 确认采用
              </ElButton>
              <ElTooltip
                v-else-if="cannotConfirmReason"
                :content="cannotConfirmReason"
                placement="top"
              >
                <ElTag v-if="isWhatif" type="info" size="small" effect="plain">
                  <Icon icon="vi-ep:lock" /> 试算版本不可激活
                </ElTag>
                <ElTag v-else type="danger" size="small" effect="plain">
                  <Icon icon="vi-ep:lock" /> 禁止激活
                </ElTag>
              </ElTooltip>
              <!-- P0-08 激活按钮（CANDIDATE → ACTIVE）：独立于 confirm，每域单一正式采用版本 -->
              <ElButton v-if="canActivate" type="warning" :loading="activating" @click="onActivate">
                <Icon icon="vi-ep:promotion" /> 激活
              </ElButton>
            </div>
          </div>
        </template>
        <ElDescriptions :column="4" size="small" border>
          <ElDescriptionsItem label="候选版本">{{
            headerNN.candidateVersionCode
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="基础版本">{{ headerNN.baseVersionCode }}</ElDescriptionsItem>
          <ElDescriptionsItem label="排程域">{{ headerNN.domainKey }}</ElDescriptionsItem>
          <ElDescriptionsItem label="状态">
            <ElTag type="warning" size="small">{{
              PLAN_VERSION_STATUS_LABELS[headerNN.status] ?? headerNN.status
            }}</ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="创建时间">
            {{ formatUtcDateTimeSec(headerNN.createdAt) }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="来源">
            <!-- P0-05 修复：sourceRefType 与 runType 相同时不重复展示 -->
            <ElTag size="small" effect="plain">{{ sourceRefLabel }}</ElTag>
            <ElTag
              v-if="headerNN.runType && headerNN.runType !== headerNN.sourceRefType"
              size="small"
              effect="plain"
              style="margin-left: 4px"
            >
              {{ RUN_TYPE_LABEL[headerNN.runType] ?? headerNN.runType }}
            </ElTag>
            <span v-if="headerNN.sourceRefId" class="muted-ref"> #{{ headerNN.sourceRefId }} </span>
            <ElTag
              v-if="headerNN.purpose"
              size="small"
              type="info"
              effect="plain"
              style="margin-left: 4px"
            >
              {{ purposeLabel }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="激活权限">
            <ElTag v-if="canConfirm" type="success" size="small">可激活</ElTag>
            <ElTag v-else-if="isWhatif" type="info" size="small">试算只读</ElTag>
            <ElTag v-else type="danger" size="small">禁止激活</ElTag>
            <span class="muted-ref"> 是否可激活由服务端判定 </span>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="影响订单">
            {{ compNN.impactSummary.impactedOrderCount }}
          </ElDescriptionsItem>
        </ElDescriptions>

        <!-- 关键 KPI 横条 -->
        <div class="kpi-strip">
          <div class="kpi-pill">
            <span class="pill-label">新增任务</span>
            <span class="pill-val ok">+{{ compNN.taskChangeSummary.added }}</span>
          </div>
          <div class="kpi-pill">
            <span class="pill-label">删除任务</span>
            <span class="pill-val bad">-{{ compNN.taskChangeSummary.removed }}</span>
          </div>
          <div class="kpi-pill">
            <span class="pill-label">时间移动</span>
            <span class="pill-val">{{ compNN.taskChangeSummary.timeShifted }}</span>
          </div>
          <div class="kpi-pill">
            <span class="pill-label">资源变更</span>
            <span class="pill-val">{{ compNN.taskChangeSummary.resourceChanged }}</span>
          </div>
          <div class="kpi-pill" :class="{ warn: compNN.taskChangeSummary.crossDomainBlocked > 0 }">
            <span class="pill-label">跨域阻挡</span>
            <span
              class="pill-val"
              :class="{ danger: compNN.taskChangeSummary.crossDomainBlocked > 0 }"
            >
              {{ compNN.taskChangeSummary.crossDomainBlocked }}
            </span>
          </div>
          <!-- Pkg-4 v1.2 §10/§13：共享资源占用（其它 Domain ACTIVE 占用的不可移动时间块） -->
          <div
            class="kpi-pill kpi-pill-shared"
            :class="{ hot: (compNN.impactSummary.sharedResourceOccupancyCount ?? 0) > 0 }"
          >
            <span class="pill-label">共享资源占用</span>
            <span
              class="pill-val"
              :class="{ purple: (compNN.impactSummary.sharedResourceOccupancyCount ?? 0) > 0 }"
            >
              {{ compNN.impactSummary.sharedResourceOccupancyCount ?? 0 }}
            </span>
          </div>
          <!-- Pkg-4 v1.2 §11：跨域 Quantity-Time 切片总数（必须保留分段，禁止压平） -->
          <div
            class="kpi-pill kpi-pill-qts"
            :class="{ hot: (compNN.impactSummary.quantityTimeSliceCount ?? 0) > 0 }"
          >
            <span class="pill-label">数量-时间切片</span>
            <span
              class="pill-val"
              :class="{ green: (compNN.impactSummary.quantityTimeSliceCount ?? 0) > 0 }"
            >
              {{ compNN.impactSummary.quantityTimeSliceCount ?? 0 }}
            </span>
          </div>
        </div>
      </ElCard>

      <!-- 跨域阻挡提示 -->
      <ElAlert
        v-if="compNN.impactSummary.crossDomainImpacted"
        type="warning"
        :closable="false"
        show-icon
        class="u14-alert"
        title="本次候选版本涉及跨排程域影响，外排程域共享设备阻挡原因已列出"
      />

      <!-- 影响摘要 -->
      <div class="summary-row">
        <div class="sum-pill" :class="{ hot: compNN.impactSummary.impactedOrderCount > 0 }">
          <span class="sum-label">受影响订单数</span>
          <span class="sum-val">{{ compNN.impactSummary.impactedOrderCount }}</span>
        </div>
        <div class="sum-pill" :class="{ hot: compNN.impactSummary.newDelayCount > 0 }">
          <span class="sum-label">新增延期</span>
          <span class="sum-val">{{ compNN.impactSummary.newDelayCount }}</span>
        </div>
        <div class="sum-pill" :class="{ hot: compNN.impactSummary.estimatedOnlyCount > 0 }">
          <span class="sum-label">仅估算</span>
          <span class="sum-val">{{ compNN.impactSummary.estimatedOnlyCount }}</span>
        </div>
        <div class="sum-pill" :class="{ hot: compNN.impactSummary.crossDomainImpacted }">
          <span class="sum-label">跨排程域</span>
          <span class="sum-val">{{ compNN.impactSummary.crossDomainImpacted ? '是' : '否' }}</span>
        </div>
      </div>

      <!-- Pkg-4 v1.2 §10/§13：共享资源占用（其它 Domain ACTIVE 占用的不可移动时间块） -->
      <ElCard v-if="currentSharedResourceOccupancies.length > 0" class="panel shared-resource-card">
        <template #header>
          <span>
            共享资源占用
            <ElTooltip
              content="其它排程域当前生效版本在共享资源上的占用，本候选版本不可抢占"
              placement="top"
            >
              <Icon icon="vi-ep:question-filled" />
            </ElTooltip>
            <ElTag size="small" effect="plain" style="margin-left: 8px">
              {{ currentSharedResourceOccupancies.length }} 条
            </ElTag>
          </span>
        </template>
        <ElTable :data="currentSharedResourceOccupancies" size="small" border>
          <ElTableColumn label="共享资源" width="180">
            <template #default="{ row }">
              <span class="resource-code">{{ row.resourceCode ?? `#${row.resourceRefId}` }}</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="占用方排程域" width="200">
            <template #default="{ row }">
              <ElTag size="small" type="info" effect="plain">
                {{ candidateStore.humanLabel(row.domainKey) }}
              </ElTag>
              <span class="muted-ref" style="margin-left: 6px">({{ row.domainKey }})</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="占用时间窗" width="320">
            <template #default="{ row }">
              {{ formatUtcDateTime(row.occupiedFrom) }} →
              {{ formatUtcDateTime(row.occupiedTo) }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="来源计划版本" width="160" align="center">
            <template #default="{ row }"> #{{ row.sourcePlanVersionId }} </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <!-- Pkg-4 v1.2 §11：Quantity-Time 切片（必须保留分段：40件@15日 / 60件@17日，禁止压平为 100件） -->
      <ElCard v-if="currentQuantityTimeSlices.length > 0" class="panel qts-card">
        <template #header>
          <span>
            跨域数量-时间切片
            <ElTooltip
              content="上游排程域传入的物料+数量+可用时间；必须保留分段，禁止合并为单条总量"
              placement="top"
            >
              <Icon icon="vi-ep:question-filled" />
            </ElTooltip>
            <ElTag size="small" effect="plain" style="margin-left: 8px">
              {{ currentQuantityTimeSlices.length }} 段
            </ElTag>
          </span>
        </template>
        <ElAlert type="success" :closable="false" show-icon class="qts-alert">
          <template #title>分段保留（未压平）</template>
          以下切片按"物料 + 数量 + 可用时间"逐段记录，如
          <strong>40 件 @9/15 / 60 件 @9/17</strong>
          不会合并为 100 件。
        </ElAlert>
        <ElTable :data="currentQuantityTimeSlices" size="small" border>
          <ElTableColumn label="物料" width="160">
            <template #default="{ row }">
              <span class="material-code">{{ row.materialCode }}</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="数量" width="120" align="right">
            <template #default="{ row }">
              <span class="qty-val">{{ row.quantity }}</span>
              <span class="muted" style="margin-left: 4px">件</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="可用时间" width="200">
            <template #default="{ row }">
              {{ formatUtcDateTime(row.availableTime) }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="来源排程域" width="200">
            <template #default="{ row }">
              <ElTag size="small" type="success" effect="plain">
                {{ candidateStore.humanLabel(row.sourceDomainKey) }}
              </ElTag>
              <span class="muted-ref" style="margin-left: 6px">({{ row.sourceDomainKey }})</span>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <!-- 4 块详情 -->
      <div class="content-row">
        <!-- ① 新订单 -->
        <ElCard class="panel">
          <template #header>
            <span> <ElTag size="small" effect="plain">①</ElTag> 新订单完成情况 </span>
          </template>
          <div v-if="!compNN.newOrderDiffs.length" class="panel-empty">
            <ElEmpty description="无新订单" />
          </div>
          <ElTable v-else :data="compNN.newOrderDiffs" size="small" border>
            <ElTableColumn prop="orderNo" label="订单号" width="150" />
            <ElTableColumn label="请求交期" width="120">
              <template #default="{ row }">
                {{ formatUtcMonthDay(row.requestedDueDate) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="基准完成" width="120">
              <template #default="{ row }">
                {{ row.baseCompletion ? formatUtcMonthDay(row.baseCompletion) : '-' }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="候选完成" width="120">
              <template #default="{ row }">
                <span :class="{ ok: row.onTime, danger: !row.onTime }">
                  {{ formatUtcMonthDay(row.candidateCompletion) }}
                </span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="按期" width="70" align="center">
              <template #default="{ row }">
                <ElTag size="small" :type="row.onTime ? 'success' : 'danger'">
                  {{ row.onTime ? '是' : '否' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="仅估算" width="100" align="center">
              <template #default="{ row }">
                <ElTooltip v-if="row.isEstimated" :content="ESTIMATED_BADGE_TEXT" placement="top">
                  <ElTag
                    size="small"
                    effect="dark"
                    style="background: #8b5cf6; border-color: #8b5cf6"
                  >
                    是
                  </ElTag>
                </ElTooltip>
                <span v-else class="muted">否</span>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElCard>

        <!-- ② 既有订单影响 -->
        <ElCard class="panel">
          <template #header>
            <span> <ElTag size="small" effect="plain">②</ElTag> 既有订单影响 </span>
          </template>
          <div v-if="!compNN.impactedOrders.length" class="panel-empty">
            <ElEmpty description="无受影响订单" />
          </div>
          <ElTable v-else :data="compNN.impactedOrders" size="small" border>
            <ElTableColumn prop="orderNo" label="订单号" width="150" />
            <ElTableColumn label="基准完成" width="120">
              <template #default="{ row }">
                {{ formatUtcMonthDayHM(row.baseCompletion) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="候选完成" width="120">
              <template #default="{ row }">
                {{ formatUtcMonthDayHM(row.candidateCompletion) }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="差值" width="100" align="right">
              <template #default="{ row }">
                <span :class="{ danger: row.deltaHours > 0 }">
                  {{ delayHours(row.deltaHours) }}
                </span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="新增延期" width="80" align="center">
              <template #default="{ row }">
                <ElTag size="small" :type="row.becomesDelayed ? 'danger' : 'success'">
                  {{ row.becomesDelayed ? '是' : '否' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="需求保护冲突" width="120" align="center">
              <template #default="{ row }">
                <ElTag v-if="row.hasProtectionConflict" size="small" type="warning"> 冲突 </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
            <!-- Pkg-4 v1.2 §13：受影响订单若被共享资源阻挡，引用 SharedResourceOccupancy.resourceRefId -->
            <ElTableColumn label="阻挡资源" width="160" align="center">
              <template #default="{ row }">
                <ElTooltip
                  v-if="row.blockedBySharedResourceRefId"
                  :content="`受共享资源 #${row.blockedBySharedResourceRefId} 占用阻挡`"
                  placement="top"
                >
                  <ElTag size="small" type="warning" effect="dark">
                    {{ candidateStore.resourceCodeByRefId(row.blockedBySharedResourceRefId) }}
                  </ElTag>
                </ElTooltip>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElCard>
      </div>

      <!-- ③ 原因 -->
      <ElCard v-if="compNN.reasons.length" class="panel">
        <template #header>
          <span> <ElTag size="small" effect="plain">③</ElTag> 根因 </span>
        </template>
        <div class="reason-list">
          <div v-for="(r, idx) in compNN.reasons" :key="idx" class="reason-item">
            <span class="reason-rank">{{ idx + 1 }}</span>
            <div class="reason-body">
              <div class="reason-body-main">
                <!-- Pkg-4：reasonCode 标签 + 描述 -->
                <div class="reason-tags">
                  <ElTag size="small" :type="REASON_TAG[r.reasonCode]" effect="dark">
                    {{ REASON_LABEL[r.reasonCode] ?? r.reasonCode }}
                  </ElTag>
                  <ElTag size="small" :type="FACT_TAG[r.factType]" effect="plain">
                    {{ FACT_TYPE_LABELS[r.factType] ?? r.factType }}
                  </ElTag>
                </div>
                <div class="reason-desc">{{ r.description }}</div>
              </div>
            </div>
          </div>
        </div>
      </ElCard>

      <!-- ④ 最近确认结果（U13 可追溯） -->
      <ElCard v-if="lastConfirmResult" class="panel confirm-card">
        <template #header>
          <span>
            <Icon icon="vi-mdi:history" />
            本次确认结果
          </span>
        </template>
        <ElDescriptions :column="3" size="small" border>
          <ElDescriptionsItem label="候选版本">{{
            lastConfirmResult.candidatePlanVersionId
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="基础版本">{{
            lastConfirmResult.basePlanVersionId
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="确认时间">
            {{ formatUtcDateTimeSec(lastConfirmResult.confirmedAt) }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="操作者">{{ lastConfirmResult.actor }}</ElDescriptionsItem>
          <ElDescriptionsItem label="备注" :span="2">
            {{ lastConfirmResult.remark || '（无）' }}
          </ElDescriptionsItem>
        </ElDescriptions>
      </ElCard>

      <!-- ⑤ 最近激活结果（P0-08 落地：v1.2 §十三 ACTIVE 转换留痕） -->
      <ElCard v-if="lastActivateResult" class="panel activate-card">
        <template #header>
          <span>
            <Icon icon="vi-mdi:check-decagram" />
            本次激活结果（候选版本已转为正式生效版本）
          </span>
        </template>
        <ElDescriptions :column="3" size="small" border>
          <ElDescriptionsItem label="候选版本">{{
            lastActivateResult.candidatePlanVersionId
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="激活时间">
            {{ formatUtcDateTimeSec(lastActivateResult.activatedAt) }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="激活人">{{
            lastActivateResult.activatedBy
          }}</ElDescriptionsItem>
          <ElDescriptionsItem
            v-if="lastActivateResult.replacedActivePlanVersionId"
            label="替换的旧生效版本"
            :span="2"
          >
            #{{ lastActivateResult.replacedActivePlanVersionId }}（已归档）
          </ElDescriptionsItem>
        </ElDescriptions>
      </ElCard>
    </template>

    <!-- ===== 确认 Dialog ===== -->
    <ElDialog
      v-model="dialogVisible"
      title="确认采用候选版本"
      width="480px"
      :close-on-click-modal="false"
    >
      <ElForm label-width="100px" size="default">
        <ElFormItem label="候选版本">
          <span class="d-val">{{ currentHeader?.candidateVersionCode }}</span>
        </ElFormItem>
        <ElFormItem label="基准版本">
          <span class="d-val">{{ currentHeader?.baseVersionCode }}</span>
        </ElFormItem>
        <ElFormItem label="操作者">
          <ElInput v-model="confirmForm.actor" placeholder="由登录身份自动带入" />
        </ElFormItem>
        <ElFormItem label="备注">
          <ElInput
            v-model="confirmForm.remark"
            type="textarea"
            :rows="3"
            placeholder="选填；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert type="warning" :closable="false" show-icon class="d-alert">
          <template #title>确认后生效说明</template>
          提交后将产生新的生效版本，原候选版本自动归档；本页面仅显示审计信息。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton @click="dialogVisible = false">取消</ElButton>
        <ElButton v-if="canConfirm" type="primary" :loading="confirming" @click="onConfirm">
          确认采用
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style lang="less" scoped>
.aps-candidate {
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
    line-height: 1.8;
    color: #475569;
  }

  .hint-text-v12 {
    display: block;
    margin-top: 4px;
    margin-left: 8px;
    padding-top: 4px;
    font-size: 12px;
    line-height: 1.7;
    color: #64748b;
    border-top: 1px dashed #cbd5e1;
  }
}

/* Gantt 抽屉跳转过来时的来源条（绿色，区别于普通约束提示） */
.from-gantt-bar {
  :deep(.el-alert__title) {
    font-weight: 600;
  }
}

/* P1：设备故障来源条 - 黄色（与设备故障视觉一致） */
.from-resource-bar {
  :deep(.el-alert__title) {
    font-weight: 600;
  }
}

.panel {
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

.panel-empty {
  padding: 20px 0;
}

.muted {
  color: #94a3b8;
}

.muted-tag {
  margin: 0 6px;
  font-size: 12px;
  font-weight: 400;
  color: #94a3b8;
}

.muted-ref {
  margin-left: 6px;
  font-size: 11px;
  font-weight: 400;
  color: #94a3b8;
}

.loading-tip {
  padding: 30px;
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
}

/* 候选版本卡 */
.candidate-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}

.candidate-card {
  padding: 14px 16px;
  cursor: pointer;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  transition: all 0.2s;

  &:hover {
    border-color: #3b82f6;
  }

  &.active {
    background: #eff6ff;
    border-color: #3b82f6;
    box-shadow: 0 0 0 2px rgb(59 130 246 / 12%);
  }

  .card-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 10px;

    .card-code {
      font-family: 'Courier New', monospace;
      font-weight: 600;
      color: #1e293b;
    }
  }

  .card-meta {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 6px 12px;
    font-size: 12px;

    .meta-row {
      display: flex;
      gap: 6px;

      .meta-label {
        color: #94a3b8;
      }

      .warn {
        font-weight: 600;
        color: #f59e0b;
      }

      .danger {
        font-weight: 600;
        color: #ef4444;
      }
    }
  }
}

/* Header 卡 */
.header-card .header-actions {
  display: flex;
  gap: 8px;
}

/* KPI 条 */
.kpi-strip {
  display: flex;
  gap: 10px;
  margin-top: 12px;
  flex-wrap: wrap;
}

.kpi-pill {
  display: inline-flex;
  padding: 6px 12px;
  font-size: 12px;
  background: #f8fafc;
  border-radius: 16px;
  align-items: center;
  gap: 6px;

  &.warn {
    background: #fef3c7;
  }

  /* Pkg-4 v1.2 §10/§13：共享资源占用（紫色） */
  &.kpi-pill-shared {
    background: #f5f3ff;

    &.hot {
      background: #ede9fe;
    }
  }

  /* Pkg-4 v1.2 §11：Quantity-Time 切片（绿色） */
  &.kpi-pill-qts {
    background: #ecfdf5;

    &.hot {
      background: #d1fae5;
    }
  }

  .pill-label {
    color: #64748b;
  }

  .pill-val {
    font-weight: 600;
    color: #1e293b;

    &.ok {
      color: #059669;
    }

    &.bad {
      color: #ef4444;
    }

    &.danger {
      color: #ef4444;
    }

    &.purple {
      color: #7c3aed;
    }

    &.green {
      color: #059669;
    }
  }
}

/* U14 跨域提示 */
.u14-alert {
  margin-bottom: 12px;
}

/* 影响摘要 */
.summary-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}

.sum-pill {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  background: #f8fafc;
  border-radius: 6px;

  &.hot {
    background: #fef3c7;

    .sum-val {
      color: #b45309;
    }
  }

  .sum-label {
    font-size: 12px;
    color: #64748b;
  }

  .sum-val {
    font-size: 18px;
    font-weight: 600;
    color: #1e293b;
  }
}

.content-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

/* Diff 颜色 */
.ok {
  font-weight: 600;
  color: #059669;
}

.danger {
  font-weight: 600;
  color: #ef4444;
}

/* 原因 */
.reason-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.reason-item {
  display: flex;
  gap: 10px;
  padding: 10px 12px;
  background: #f8fafc;
  border-left: 3px solid #3b82f6;
  border-radius: 4px;

  .reason-rank {
    width: 22px;
    height: 22px;
    font-size: 12px;
    font-weight: 600;
    line-height: 22px;
    color: #fff;
    text-align: center;
    background: #3b82f6;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .reason-body {
    flex: 1;
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 8px;

    .reason-body-main {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .reason-tags {
      display: flex;
      gap: 6px;
      align-items: center;
    }

    .reason-desc {
      font-size: 13px;
      line-height: 1.6;
      color: #475569;
    }
  }
}

/* Pkg-4 v1.2 §10/§13：共享资源占用卡（紫色卡片头） */
.shared-resource-card {
  :deep(.el-card__header) {
    background: #f5f3ff;
    border-bottom: 1px solid #ddd6fe;
  }
}

/* Pkg-4 v1.2 §11：Quantity-Time 切片卡（绿色卡片头） */
.qts-card {
  :deep(.el-card__header) {
    background: #ecfdf5;
    border-bottom: 1px solid #a7f3d0;
  }
}

.qts-alert {
  margin-bottom: 12px;
}

.resource-code,
.material-code {
  font-family: 'Courier New', monospace;
  font-weight: 600;
  color: #1e293b;
}

.qty-val {
  font-size: 14px;
  font-weight: 600;
  color: #059669;
}

/* 确认结果卡 */
.confirm-card {
  background: #f0fdf4;
  border: 1px solid #86efac;
}

/* Dialog */
.d-val {
  font-family: 'Courier New', monospace;
  color: #1e293b;
}

.d-alert {
  margin-bottom: -10px;
}
</style>
