<script setup lang="ts">
/**
 * APS V1 4号位 — 运行/版本/MES（页面 9 / U01 / U02 / U19 / U16-U18）
 *
 * 4号位文档第 15-16 节约束：
 *  - 当前 ACTIVE 版本按 Domain 分组展示（U01）
 *  - PARTIAL_SUCCESS 必须明确拆 success/failed/blocked 三类（U02）
 *  - FAILED Run 恢复是新建 Run，原始 Run 仍 FAILED（U19）
 *  - MES 下发资格分 ELIGIBLE/INELIGIBLE/UNKNOWN 三档，原因清晰（U16-U18）
 *  - 设备故障不直接改 Task 状态（U20；V1 不建设 PAUSE/RESUME 状态闭环）
 *
 * 写接口：
 *  - recoverFailedRun：PMC 触发，原始 FAILED Run 保留 + 新 Run 产生（U19）
 *  - dispatchMes：PMC 触发，ELIGIBLE Task 走 5号位中转下发到 MES（§十九.MES展示/操作）
 *
 * 页面分区：
 *  - 顶部 Run 状态 KPI（5 类）
 *  - Recent Runs 表格（含 FAILED 详情 / duration）
 *  - 当前 ACTIVE 版本（按 Domain 分组）
 *  - PARTIAL_SUCCESS 拆解（三列 success / failed / blocked）
 *  - MES 下发资格样本（含 7 种原因 + 下发按钮）
 *  - MES 资格 KPI（ELIGIBLE/INELIGIBLE/UNKNOWN）
 *  - 最近一次 MES 下发结果（MesDispatchResult）
 */

import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  useRunStore,
  RUN_STATUS_LABEL,
  RUN_STATUS_TAG,
  RUN_TYPE_LABEL
} from '@/store/modules/aps/run'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { useDomainStore } from '@/store/modules/aps/domain'
import type { ScheduleRunDto } from '@/api/aps-v1'
import { PLAN_VERSION_STATUS_LABELS, type PlanVersionStatus } from '@/api/aps-v1/types'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElEmpty,
  ElTag,
  ElTable,
  ElTableColumn,
  ElTooltip
} from 'element-plus'

const runStore = useRunStore()
const apsAuth = useApsAuthStore()
const domainStore = useDomainStore()
/**
 * DomainKey → humanLabel（v1.2 §十三 跨域阻挡 Run 侧展示用）
 *  - 与 Candidate.vue/Ctp.vue 一致：通过 useDomainStore().label() 解析
 *  - 找不到回退 raw key（不抛错）
 */
function humanLabel(domainKey: string | undefined | null): string {
  if (!domainKey) return '—'
  return domainStore.label(domainKey)
}
const {
  loading,
  error,
  runs,
  activeVersions,
  mesSamples,
  partialBreakdown,
  runStatusCounts,
  mesStats,
  recovering
} = storeToRefs(runStore)

/* ===== 计划版本状态中文标签（状态枚举中文映射） ===== */
function planVersionStatusLabel(status: string): string {
  return PLAN_VERSION_STATUS_LABELS[status as PlanVersionStatus] ?? status
}

/* ===== MES 下发资格筛选（值 → 中文标签） ===== */
const MES_FILTER_OPTIONS = [
  { value: 'ALL', label: '全部' },
  { value: 'ELIGIBLE', label: '可下发' },
  { value: 'INELIGIBLE', label: '不可下发' },
  { value: 'UNKNOWN', label: '未判定' }
] as const

/* ===== MES 下发资格中文标签 ===== */
function mesEligibilityLabel(eligibility: string): string {
  return MES_FILTER_OPTIONS.find((o) => o.value === eligibility)?.label ?? eligibility
}

/* ===== MES 不可下发原因中文 ===== */
const REASON_LABEL: Record<string, string> = {
  CANDIDATE: '候选版本（不可下发）',
  UNLOCATED: 'PI 位置未定位',
  NO_PI: '无 PI 在制',
  PLANNING_PLACEHOLDER_DEPENDENCY: '依赖采购估算',
  OUT_OF_DISPATCH_WINDOW: '超出下发窗口',
  CANCELLED: '已取消'
}

/* ===== Run 持续时间（秒） ===== */
function runDuration(r: { startedAt: string; completedAt?: string }): string {
  const end = r.completedAt ? new Date(r.completedAt).getTime() : Date.now()
  const dur = Math.round((end - new Date(r.startedAt).getTime()) / 1000)
  if (dur < 60) return `${dur} 秒`
  if (dur < 3600) return `${Math.floor(dur / 60)} 分 ${dur % 60} 秒`
  return `${Math.floor(dur / 3600)} 时 ${Math.floor((dur % 3600) / 60)} 分`
}

/**
 * 最近成功时间格式化（带 "x 分钟前" 友好文案）
 *  - 1 小时内：显示 "x 分钟前"
 *  - 1 天内：显示 "x 小时前"
 *  - 超过：直接显示绝对时间
 */
function formatLastSuccessTime(iso: string): string {
  const d = dayjs(iso)
  const diffMin = Date.now() - d.valueOf()
  if (diffMin < 60 * 1000) return `${Math.max(1, Math.floor(diffMin / 60000))} 分钟前`
  if (diffMin < 24 * 3600 * 1000) return `${Math.floor(diffMin / 3600000)} 小时前`
  return d.format('MM-DD HH:mm')
}

/* ===== 顶部 KPI ===== */
const kpiItems = computed(() => {
  const c = runStatusCounts.value
  return [
    { key: 'SUCCESS', label: '成功', count: c.SUCCESS, color: '#059669', tag: 'success' as const },
    {
      key: 'PARTIAL_SUCCESS',
      label: '部分成功',
      count: c.PARTIAL_SUCCESS,
      color: '#f59e0b',
      tag: 'warning' as const
    },
    {
      key: 'RUNNING',
      label: '运行中',
      count: c.RUNNING,
      color: '#3b82f6',
      tag: 'primary' as const
    },
    { key: 'FAILED', label: '失败', count: c.FAILED, color: '#ef4444', tag: 'danger' as const },
    { key: 'PENDING', label: '排队', count: c.PENDING, color: '#94a3b8', tag: 'info' as const }
  ]
})

/* ===== 当前 PARTIAL 是否存在 ===== */
const hasPartial = computed(
  () => partialBreakdown.value !== null && partialBreakdown.value !== undefined
)
/** 模板内拿到的就一定是 NonNull 的（与 hasPartial 配套使用） */
const partialBreakdownNN = computed(() => partialBreakdown.value!)

/** 未参与当前 Run 的 Domain（G1：4号位文档 §十四要求区分「未参与」这一档）
 *  - 依据：currentPartialBreakdown.scheduleRunId → recentRuns 里该 Run 的 expectedDomainKeys
 *  - 取差集：expectedDomainKeys 中未出现在 success/failed/blocked 任一类里的 Domain
 *  - 找不到对应 Run（后端未返回 expectedDomainKeys）时返回空数组，不误判
 */
const notInvolvedDomains = computed<string[]>(() => {
  if (!hasPartial.value) return []
  const run = runs.value.find(
    (r: ScheduleRunDto) => r.runId === partialBreakdownNN.value.scheduleRunId
  )
  if (!run?.expectedDomainKeys?.length) return []
  const involved = new Set<string>()
  for (const d of [
    ...partialBreakdownNN.value.successDomains,
    ...partialBreakdownNN.value.failedDomains,
    ...partialBreakdownNN.value.blockedDomains
  ]) {
    involved.add(d.domainKey)
  }
  return run.expectedDomainKeys.filter((dk: string) => !involved.has(dk))
})
const recentRunsSorted = computed(() =>
  [...runs.value].sort((a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime())
)

/* ===== 最近状态摘要（用于右栏 quick stats） ===== */
/** 最近一次 SUCCESS Run（按 completedAt 倒序） */
const lastSuccessRun = computed<ScheduleRunDto | null>(() => {
  const candidates = runs.value.filter(
    (r: ScheduleRunDto) => r.status === 'SUCCESS' && r.completedAt
  )
  if (!candidates.length) return null
  return candidates.sort(
    (a: ScheduleRunDto, b: ScheduleRunDto) =>
      new Date(b.completedAt!).getTime() - new Date(a.completedAt!).getTime()
  )[0]
})
/** 最近一次 FAILED Run（按 startedAt 倒序） */
const lastFailedRun = computed<ScheduleRunDto | null>(() => {
  const candidates = runs.value.filter((r: ScheduleRunDto) => r.status === 'FAILED')
  if (!candidates.length) return null
  return candidates.sort(
    (a: ScheduleRunDto, b: ScheduleRunDto) =>
      new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
  )[0]
})
/** 最近一次 PARTIAL_SUCCESS Run */
const lastPartialRun = computed<ScheduleRunDto | null>(() => {
  const candidates = runs.value.filter((r: ScheduleRunDto) => r.status === 'PARTIAL_SUCCESS')
  if (!candidates.length) return null
  return candidates.sort(
    (a: ScheduleRunDto, b: ScheduleRunDto) =>
      new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
  )[0]
})
/** 成功率 = SUCCESS / 总数（含 FAILED 不算 SUCCESS） */
const successRate = computed(() => {
  const total = runs.value.length
  if (!total) return { pct: 0, label: '0%', successCount: 0, total: 0 }
  const successCount = runs.value.filter((r: ScheduleRunDto) => r.status === 'SUCCESS').length
  const pct = Math.round((successCount / total) * 100)
  return { pct, label: `${pct}%`, successCount, total }
})
/** MES 资格总览：当前样本中可下发占比 */
const mesEligibleRatio = computed(() => {
  const total = mesStats.value.ELIGIBLE + mesStats.value.INELIGIBLE + mesStats.value.UNKNOWN
  if (!total) return { pct: 0, label: '0%', count: 0, total: 0 }
  const pct = Math.round((mesStats.value.ELIGIBLE / total) * 100)
  return { pct, label: `${pct}%`, count: mesStats.value.ELIGIBLE, total }
})

/* ===== MES 资格筛选 ===== */
const mesFilter = ref<'ALL' | 'ELIGIBLE' | 'INELIGIBLE' | 'UNKNOWN'>('ALL')
const filteredMesSamples = computed(() => {
  if (mesFilter.value === 'ALL') return mesSamples.value
  return mesSamples.value.filter((s) => s.eligibility === mesFilter.value)
})

/* ===== 加载 ===== */
function refresh(): void {
  runStore.load()
}
onMounted(refresh)

/* ===== U19：FAILED Run 人工恢复 =====
 * 4号位文档第 15 节：「FAILED 人工恢复按钮必须调用"新建 ScheduleRun"，不能把 FAILED 直接改回 RUNNING」
 * 权限：aps.plan.run（v1.2 §23.1 权限码门控；dev seed 下 aps.planner + aps.admin.system 持有）
 */
const canRecoverRun = computed(() => apsAuth.has('aps.plan.run'))

/* ===== §十九.MES展示/操作：MES 下发按钮 + handler =====
 *  - 仅 ELIGIBLE + aps.mes.dispatch 可点击
 *  - v1.2 §23.1 权限码门控：dev seed 下仅 aps.admin.system 持有（PMC 仅持 aps.mes.view）
 *  - 行为变化（vs v1.1）：原 4 角色皆可下发；切换到权限码后只有 SYSTEM_ADMIN 可见下发按钮
 *  - P1-15：domainKey 业务范围断言（store 内部）
 *  - 成功后展示 MesDispatchResult.mesDispatchId（MES 工单号）
 */
const canDispatchMes = computed(() => apsAuth.has('aps.mes.dispatch'))

/** 行内"下发"按钮是否可点（额外要求 eligibility=ELIGIBLE） */
function rowDispatchable(row: { eligibility: string; domainKey?: string }): boolean {
  if (!canDispatchMes.value) return false
  if (row.eligibility !== 'ELIGIBLE') return false
  // P1-15 前端防御：domainKey 不在 scope 内直接禁用（避免弹错）
  const allowed = apsAuth.dataScope.domainKeys
  if (allowed.length > 0 && row.domainKey && !allowed.includes(row.domainKey)) {
    return false
  }
  return true
}

async function handleDispatchMes(row: {
  taskId: number
  taskNo: string
  domainKey?: string
}): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `将下发任务 ${row.taskNo}（${row.domainKey ?? '?'}）到 MES：\n\n` +
        `走服务中转下发到 MES。\n` +
        `下发后该任务由 MES 接管。`,
      '确认下发到 MES',
      {
        confirmButtonText: '确认下发',
        cancelButtonText: '不下发',
        type: 'warning'
      }
    )
  } catch {
    return // 取消
  }
  try {
    const result = await runStore.dispatchMes({
      taskId: row.taskId,
      actor: 'current-user' // mock 由 JWT 注入
    })
    ElMessage.success(`已下发：${row.taskNo} → MES 工单 ${result.mesDispatchId}`)
  } catch (err) {
    const msg = err instanceof Error ? err.message : '下发失败'
    ElMessage.error(`MES 下发失败：${msg}`)
  }
}

async function handleRecoverFailedRun(row: {
  runId: number
  expectedDomainKeys: string[]
  errorMessage?: string
}): Promise<void> {
  if (!canRecoverRun.value) {
    ElMessage.warning('当前账号无排程执行权限，不可恢复失败批次')
    return
  }
  let reason = ''
  try {
    const res = await ElMessageBox.prompt(
      `将对 FAILED Run #${row.runId} 发起人工恢复。\n` +
        `排程域：${row.expectedDomainKeys.join(' / ')}\n` +
        `原错误：${row.errorMessage ?? '-'}\n\n` +
        `恢复会新建一次排产运行，原运行状态保持 FAILED。\n` +
        `请填写恢复原因（写入审计）：`,
      '人工恢复 FAILED Run',
      {
        confirmButtonText: '确认恢复',
        cancelButtonText: '取消',
        inputType: 'textarea',
        inputPlaceholder: '例如：TEST 域 PI Position 数据已补齐，重新触发排程',
        inputValidator: (val: string) => (val && val.trim().length >= 4 ? true : '至少 4 个字符'),
        inputErrorMessage: '至少 4 个字符'
      }
    )
    reason = (res.value ?? '').trim()
  } catch {
    return
  }
  const actor = apsAuth.userInfo?.userCode ?? 'unknown'
  try {
    // v1.4 §十九.2：预校验（store.recoverFailedRun 内部已调；如失败会抛错到这里）
    const result = await runStore.recoverFailedRun({
      originalRunId: row.runId,
      domainKeys: row.expectedDomainKeys as never,
      reason,
      actor
    })
    ElMessage.success(
      `已发起恢复 → 新批次 #${result.newRunId}（原批次 #${result.originalRunId} 仍为 ${result.originalRunStatus}）。列表已刷新。`
    )
  } catch (err) {
    // v1.4 §十九.2：校验失败时给出明确提示
    const msg = (err as Error)?.message ?? '未知错误'
    if (msg.includes('DomainKey 冻结规则')) {
      ElMessageBox.alert(
        `${msg}\n\n恢复被中止，请检查该 FAILED Run 的 expectedDomainKeys 配置（FULL_SCHEDULE ≥ 1 / RESCHEDULE = 1）后重试。`,
        '回代校验失败',
        { type: 'error' }
      )
    } else {
      ElMessage.error(`恢复失败：${msg}`)
    }
  }
}
</script>

<template>
  <div class="aps-run">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">运行 / 版本 / MES</h2>
        <p class="page-sub"> 排产运行 / 版本 / MES 下发状态总览 </p>
      </div>
      <ElButton :loading="loading" @click="refresh"> <Icon icon="vi-ep:refresh" /> 刷新 </ElButton>
    </div>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== Run 状态 KPI ===== -->
    <div class="kpi-row">
      <div
        v-for="item in kpiItems"
        :key="item.key"
        class="kpi-card"
        :style="{ borderTopColor: item.color }"
      >
        <div class="kpi-label">{{ item.label }}</div>
        <div class="kpi-value" :style="{ color: item.color }">{{ item.count }}</div>
        <div class="kpi-ratio">最近 {{ runs.length }} 次排产</div>
      </div>
    </div>

    <!-- ===== 部分成功拆解 ===== -->
    <ElCard v-if="hasPartial" class="partial-card">
      <template #header>
        <div class="panel-header">
          <span>
            <ElTag type="warning" effect="dark">部分成功</ElTag>
            当前排产批次 #{{ partialBreakdownNN.scheduleRunId }} 拆解
          </span>
          <span class="hint-text"
            >四类排程域状态各自独立列出（成功 / 失败 / 上游阻断 / 未参与）</span
          >
        </div>
      </template>
      <div class="partial-grid">
        <!-- 成功 -->
        <div class="partial-col success">
          <div class="col-head">
            <span class="col-name">成功排程域</span>
            <ElTag type="success" size="small">
              {{ partialBreakdownNN.successDomains.length }}
            </ElTag>
          </div>
          <div v-for="d in partialBreakdownNN.successDomains" :key="d.domainKey" class="col-item">
            <ElTag type="success" effect="light">{{ humanLabel(d.domainKey) }}</ElTag>
            <span class="muted">V#{{ d.planVersionId }}</span>
          </div>
          <div v-if="!partialBreakdownNN.successDomains.length" class="col-empty"> 无 </div>
        </div>

        <!-- 失败 -->
        <div class="partial-col failed">
          <div class="col-head">
            <span class="col-name">失败排程域</span>
            <ElTag type="danger" size="small">
              {{ partialBreakdownNN.failedDomains.length }}
            </ElTag>
          </div>
          <div
            v-for="d in partialBreakdownNN.failedDomains"
            :key="d.domainKey"
            class="col-item error-item"
          >
            <ElTag type="danger" effect="light">{{ humanLabel(d.domainKey) }}</ElTag>
            <div v-if="d.errorMessage" class="err-msg">{{ d.errorMessage }}</div>
          </div>
          <div v-if="!partialBreakdownNN.failedDomains.length" class="col-empty">无</div>
        </div>

        <!-- 上游阻断（v1.2 §十三：跨 Domain 失败连锁 — warning 警示） -->
        <div class="partial-col blocked">
          <div class="col-head">
            <span class="col-name">上游阻断</span>
            <ElTag type="warning" size="small">
              {{ partialBreakdownNN.blockedDomains.length }}
            </ElTag>
          </div>
          <div
            v-for="d in partialBreakdownNN.blockedDomains"
            :key="d.domainKey"
            class="col-item error-item"
          >
            <ElTag type="warning" effect="light">
              <Icon icon="vi-mdi:block-helper" style="margin-right: 4px" />
              {{ humanLabel(d.domainKey) }}
            </ElTag>
            <div v-if="d.blockedByDomainKey" class="muted blocked-by">
              <Icon icon="vi-mdi:link-variant-off" style="font-size: 11px; margin-right: 2px" />
              被
              <strong>{{ humanLabel(d.blockedByDomainKey) }}</strong>
              阻断
              <span class="muted small">（{{ d.blockedByDomainKey }}）</span>
            </div>
            <div v-else class="muted small">上游失败，未启动</div>
          </div>
          <div v-if="!partialBreakdownNN.blockedDomains.length" class="col-empty">无</div>
        </div>

        <!-- 未参与当前 Run（G1：4号位文档 §十四，避免把「未参与」误显示成「失败」） -->
        <div class="partial-col not-involved">
          <div class="col-head">
            <span class="col-name">未参与本次排产</span>
            <ElTag type="info" size="small">
              {{ notInvolvedDomains.length }}
            </ElTag>
          </div>
          <div v-for="dk in notInvolvedDomains" :key="dk" class="col-item">
            <ElTag type="info" effect="plain">{{ humanLabel(dk) }}</ElTag>
            <span class="muted small">（{{ dk }}）</span>
          </div>
          <div v-if="!notInvolvedDomains.length" class="col-empty">无</div>
        </div>
      </div>
    </ElCard>

    <!-- ===== Recent Runs（全宽；11 列必须横向铺开） ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span> 最近排产记录 </span>
          <span class="hint-text">人工恢复会新建一次运行，原失败运行状态保持失败</span>
        </div>
      </template>
      <ElTable v-if="runs.length" :data="recentRunsSorted" size="small" border stripe>
        <ElTableColumn prop="runId" label="批次" width="70" fixed="left" />
        <ElTableColumn label="类型" width="100">
          <template #default="{ row }">
            {{ RUN_TYPE_LABEL[row.runType as keyof typeof RUN_TYPE_LABEL] }}
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="100">
          <template #default="{ row }">
            <ElTag :type="RUN_STATUS_TAG[row.status as keyof typeof RUN_STATUS_TAG]" size="small">
              {{ RUN_STATUS_LABEL[row.status as keyof typeof RUN_STATUS_LABEL] }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="排程域" width="170">
          <template #default="{ row }">
            <span v-for="dk in row.expectedDomainKeys" :key="dk">
              <ElTag size="small" effect="plain" class="domain-tag">
                {{ humanLabel(dk) }}
              </ElTag>
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="数据截止" width="140">
          <template #default="{ row }">
            <span class="muted">{{ dayjs(row.dataCutoffTime).format('MM-DD HH:mm') }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="策略版本" width="120">
          <template #default="{ row }">
            <span v-if="row.strategyProfileVersion" class="muted code-tag">
              {{ row.strategyProfileVersion }}
            </span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="耗时" width="80">
          <template #default="{ row }">
            <span class="muted">{{ runDuration(row) }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="开始时间" width="140">
          <template #default="{ row }">
            {{ dayjs(row.startedAt).format('MM-DD HH:mm') }}
          </template>
        </ElTableColumn>
        <ElTableColumn label="完成时间" width="140">
          <template #default="{ row }">
            <span v-if="row.completedAt">
              {{ dayjs(row.completedAt).format('MM-DD HH:mm') }}
            </span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="错误信息" min-width="180">
          <template #default="{ row }">
            <span v-if="row.errorMessage" class="err-cell">
              {{ row.errorMessage }}
            </span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="130" fixed="right">
          <template #default="{ row }">
            <ElButton
              v-if="row.status === 'FAILED'"
              size="small"
              type="danger"
              :loading="recovering"
              :disabled="!canRecoverRun"
              @click="handleRecoverFailedRun(row)"
            >
              <Icon icon="vi-mdi:restart-alert" />
              人工恢复
            </ElButton>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
      </ElTable>
      <ElEmpty v-else description="暂无排产批次历史" />
    </ElCard>

    <!-- ===== 两列：Active Versions + 最近状态摘要 ===== -->
    <div class="content-row">
      <!-- 当前生效版本（多排程域） -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span> 当前计划版本 </span>
            <ElTag v-if="activeVersions.length" type="primary" size="small">
              {{ activeVersions.length }} 排程域
            </ElTag>
          </div>
        </template>
        <div v-if="!activeVersions.length" class="panel-empty">
          <ElEmpty description="当前无生效版本" />
        </div>
        <div v-else class="version-list">
          <div
            v-for="v in activeVersions"
            :key="v.planVersionId"
            class="version-card"
            :class="v.status.toLowerCase()"
          >
            <div class="version-head">
              <span class="version-code">{{ v.versionCode }}</span>
              <ElTag
                :type="
                  v.status === 'ACTIVE'
                    ? 'success'
                    : v.status === 'CANDIDATE'
                      ? 'warning'
                      : v.status === 'FAILED'
                        ? 'danger'
                        : v.status === 'BUILDING'
                          ? 'primary'
                          : 'info'
                "
                size="small"
              >
                {{ planVersionStatusLabel(v.status) }}
              </ElTag>
            </div>
            <ElDescriptions :column="2" size="small" class="version-desc">
              <ElDescriptionsItem label="排程域">{{ humanLabel(v.domainKey) }}</ElDescriptionsItem>
              <ElDescriptionsItem label="激活时间">
                {{ v.activatedAt ? dayjs(v.activatedAt).format('MM-DD HH:mm') : '-' }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="计划区间">
                {{ dayjs(v.planHorizonStart).format('YYYY-MM-DD') }}
                ~
                {{ dayjs(v.planHorizonEnd).format('YYYY-MM-DD') }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="基础版本">
                {{ v.basePlanVersionId ?? '—' }}
              </ElDescriptionsItem>
            </ElDescriptions>
            <div v-if="v.errorMessage" class="version-err">
              {{ v.errorMessage }}
            </div>
          </div>
        </div>
      </ElCard>

      <!-- 最近状态摘要（quick stats，2 列布局轻量化） -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span> 最近状态摘要 </span>
            <span class="hint-text">关键排产批次时间线 + 资格概览</span>
          </div>
        </template>

        <!-- 总体成功率 -->
        <div
          class="summary-rate"
          :class="successRate.pct >= 80 ? 'good' : successRate.pct >= 50 ? 'mid' : 'bad'"
        >
          <div class="rate-num">{{ successRate.label }}</div>
          <div class="rate-sub">
            总体成功率 · {{ successRate.successCount }} / {{ successRate.total }} 次排产
          </div>
        </div>

        <ul class="summary-list">
          <li class="summary-row">
            <span class="row-label">
              <Icon icon="vi-mdi:check-circle" class="row-icon good" />
              最近成功
            </span>
            <span v-if="lastSuccessRun" class="row-val">
              <code class="code-tag">#{{ lastSuccessRun.runId }}</code>
              <span class="muted">
                {{ formatLastSuccessTime(lastSuccessRun.completedAt ?? '') }}
              </span>
            </span>
            <span v-else class="muted">无</span>
          </li>

          <li class="summary-row">
            <span class="row-label">
              <Icon icon="vi-mdi:alert-circle" class="row-icon bad" />
              最近失败
            </span>
            <span v-if="lastFailedRun" class="row-val">
              <code class="code-tag">#{{ lastFailedRun.runId }}</code>
              <ElTag size="small" type="danger" effect="light" class="err-mini">
                {{ lastFailedRun.errorMessage?.slice(0, 24) ?? '-' }}
              </ElTag>
            </span>
            <span v-else class="muted">无</span>
          </li>

          <li class="summary-row">
            <span class="row-label">
              <Icon icon="vi-mdi:alert" class="row-icon warn" />
              最近部分成功
            </span>
            <span v-if="lastPartialRun" class="row-val">
              <code class="code-tag">#{{ lastPartialRun.runId }}</code>
              <span class="muted">
                {{ dayjs(lastPartialRun.startedAt).format('MM-DD HH:mm') }}
              </span>
            </span>
            <span v-else class="muted">无</span>
          </li>

          <li class="summary-row">
            <span class="row-label">
              <Icon icon="vi-mdi:layers-triple" class="row-icon" />
              生效版本
            </span>
            <span class="row-val">
              <strong>{{ activeVersions.length }}</strong>
              <span class="muted">排程域</span>
            </span>
          </li>

          <li class="summary-row">
            <span class="row-label">
              <Icon icon="vi-mdi:send" class="row-icon" />
              MES 可下发
            </span>
            <span class="row-val">
              <strong>{{ mesEligibleRatio.label }}</strong>
              <span class="muted">
                {{ mesEligibleRatio.count }} / {{ mesEligibleRatio.total }} 样本
              </span>
            </span>
          </li>
        </ul>

        <div class="summary-tip">
          <Icon icon="vi-mdi:information-outline" />
          阶段 A 不直接触发写库。失败恢复需 PMC 及以上角色进入该批次行点击操作列按钮。
        </div>
      </ElCard>
    </div>

    <!-- ===== MES 下发资格 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span> MES 下发资格（样本） </span>
          <div class="mes-filter">
            <span class="muted">筛选：</span>
            <ElButton
              v-for="opt in MES_FILTER_OPTIONS"
              :key="opt.value"
              size="small"
              :type="mesFilter === opt.value ? 'primary' : 'default'"
              @click="mesFilter = opt.value"
            >
              {{ opt.label }} ({{
                opt.value === 'ALL'
                  ? mesStats.ELIGIBLE + mesStats.INELIGIBLE + mesStats.UNKNOWN
                  : mesStats[opt.value]
              }})
            </ElButton>
          </div>
        </div>
      </template>

      <!-- MES KPI -->
      <div class="mes-stat-row">
        <div class="mes-stat eligible">
          <span class="mes-label">可下发</span>
          <span class="mes-val">{{ mesStats.ELIGIBLE }}</span>
        </div>
        <div class="mes-stat ineligible">
          <span class="mes-label">不可下发</span>
          <span class="mes-val">{{ mesStats.INELIGIBLE }}</span>
        </div>
        <div class="mes-stat unknown">
          <span class="mes-label">未判定</span>
          <span class="mes-val">{{ mesStats.UNKNOWN }}</span>
        </div>
      </div>

      <div v-if="!filteredMesSamples.length" class="panel-empty">
        <ElEmpty description="无匹配样本" />
      </div>
      <ElTable v-else :data="filteredMesSamples" size="small" border>
        <ElTableColumn prop="taskNo" label="任务号" width="90" />
        <ElTableColumn label="资格" width="110">
          <template #default="{ row }">
            <ElTag
              :type="
                row.eligibility === 'ELIGIBLE'
                  ? 'success'
                  : row.eligibility === 'INELIGIBLE'
                    ? 'danger'
                    : 'info'
              "
              size="small"
            >
              {{ mesEligibilityLabel(row.eligibility) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="不可下发原因" min-width="320">
          <template #default="{ row }">
            <span v-if="!row.reasons?.length" class="muted">-</span>
            <span v-else>
              <ElTooltip
                v-for="r in row.reasons"
                :key="r"
                :content="REASON_LABEL[r] || r"
                placement="top"
              >
                <ElTag size="small" type="danger" effect="light" class="reason-tag">
                  {{ REASON_LABEL[r] || r }}
                </ElTag>
              </ElTooltip>
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="下发窗口" width="260">
          <template #default="{ row }">
            <span v-if="!row.dispatchWindow" class="muted">-</span>
            <span v-else>
              {{ dayjs(row.dispatchWindow.from).format('MM-DD HH:mm') }}
              ~
              {{ dayjs(row.dispatchWindow.to).format('MM-DD HH:mm') }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <ElTooltip v-if="!canDispatchMes" content="仅 PMC 及以上可下发" placement="top">
              <ElButton size="small" type="primary" disabled>下发</ElButton>
            </ElTooltip>
            <ElTooltip
              v-else-if="row.eligibility !== 'ELIGIBLE'"
              :content="`当前${mesEligibilityLabel(row.eligibility)}，不可下发`"
              placement="top"
            >
              <ElButton size="small" type="primary" disabled>下发</ElButton>
            </ElTooltip>
            <ElTooltip
              v-else-if="!rowDispatchable(row)"
              :content="`排程域 ${row.domainKey ?? '?'} 不在授权范围内`"
              placement="top"
            >
              <ElButton size="small" type="primary" disabled>下发</ElButton>
            </ElTooltip>
            <ElButton
              v-else
              size="small"
              type="primary"
              :loading="runStore.dispatching"
              @click="handleDispatchMes(row)"
            >
              <Icon icon="vi-ep:promotion" /> 下发
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <!-- ===== §十九.MES展示/操作：最近一次下发结果 ===== -->
    <ElCard v-if="runStore.lastDispatch" class="panel dispatch-card">
      <template #header>
        <div class="panel-header">
          <span>
            <Icon icon="vi-ep:promotion" />
            最近 MES 下发结果
            <ElTag size="small" type="success" effect="plain">已下发</ElTag>
          </span>
        </div>
      </template>
      <ElDescriptions :column="2" size="small" border>
        <ElDescriptionsItem label="任务">
          {{ runStore.lastDispatch.taskNo }} (#{{ runStore.lastDispatch.taskId }})
        </ElDescriptionsItem>
        <ElDescriptionsItem label="MES 工单号">
          <code>{{ runStore.lastDispatch.mesDispatchId }}</code>
        </ElDescriptionsItem>
        <ElDescriptionsItem label="下发时间">
          {{ dayjs(runStore.lastDispatch.dispatchedAt).format('YYYY-MM-DD HH:mm:ss') }}
        </ElDescriptionsItem>
        <ElDescriptionsItem label="操作人">{{ runStore.lastDispatch.actor }}</ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>
  </div>
</template>

<style lang="less" scoped>
.aps-run {
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

.kpi-row {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
}

.kpi-card {
  padding: 14px 16px;
  background: #fff;
  border-top: 3px solid #94a3b8;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);

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
  }
}

.content-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
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

  .hint-text {
    font-size: 12px;
    font-weight: 400;
    color: #94a3b8;
  }
}

.panel-empty {
  padding: 20px 0;
}

.muted {
  color: #94a3b8;
}

.small {
  font-size: 11px;
}

/* v1.2 §十三：上游阻断 Domain 来源提示 */
.blocked-by {
  margin-top: 4px;
  padding: 4px 8px;
  font-size: 12px;
  color: #92400e;
  background: #fffbeb;
  border-left: 2px solid #f59e0b;
  border-radius: 3px;
}

/* PARTIAL_SUCCESS 三列 */
.partial-card {
  border-radius: 8px;
}

.partial-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
}

.partial-col {
  padding: 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  &.success {
    background: #ecfdf5;
    border-color: #a7f3d0;
  }

  &.failed {
    background: #fef2f2;
    border-color: #fecaca;
  }

  &.blocked {
    /* v1.2 §十三：跨域阻挡是 warning 级别（不是 info），用暖色背景区分 */
    background: #fffbeb;
    border-color: #fde68a;
  }

  &.not-involved {
    /* G1：未参与当前 Run — 中性灰，与「失败」的红色彻底区分 */
    background: #f8fafc;
    border-color: #e2e8f0;
  }

  .col-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 10px;

    .col-name {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }
  }

  .col-item {
    padding: 8px 10px;
    margin-bottom: 6px;
    background: rgb(255 255 255 / 70%);
    border-radius: 4px;

    &.error-item {
      background: #fff;
      border-left: 3px solid #ef4444;
    }
  }

  .col-empty {
    padding: 8px 0;
    font-size: 12px;
    color: #94a3b8;
    text-align: center;
  }

  .err-msg {
    margin-top: 6px;
    font-size: 12px;
    color: #7f1d1d;
  }
}

/* ACTIVE 版本卡片 */
.version-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.version-card {
  padding: 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  &.active {
    background: #ecfdf5;
    border-color: #a7f3d0;
  }

  &.candidate {
    background: #fff7ed;
    border-color: #fed7aa;
  }

  &.failed {
    background: #fef2f2;
    border-color: #fecaca;
  }

  .version-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
  }

  .version-code {
    font-weight: 600;
    color: #1e293b;
  }

  .version-desc {
    :deep(.el-descriptions__label) {
      width: 60px;
      color: #94a3b8;
    }
  }

  .version-err {
    padding: 8px 10px;
    margin-top: 8px;
    font-size: 12px;
    color: #7f1d1d;
    background: #fee2e2;
    border-radius: 4px;
  }
}

.domain-tag {
  margin-right: 4px;

  &:last-child {
    margin-right: 0;
  }
}

.err-cell {
  display: block;
  max-width: 220px;
  overflow: hidden;
  font-size: 12px;
  color: #b91c1c;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.code-tag {
  padding: 1px 6px;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  background: #f1f5f9;
  border-radius: 3px;
}

/* 最近状态摘要 */
.summary-rate {
  padding: 12px 16px;
  margin-bottom: 12px;
  border-radius: 6px;

  &.good {
    background: #ecfdf5;
    border: 1px solid #a7f3d0;
  }

  &.mid {
    background: #fff7ed;
    border: 1px solid #fed7aa;
  }

  &.bad {
    background: #fef2f2;
    border: 1px solid #fecaca;
  }

  .rate-num {
    font-family: 'Courier New', monospace;
    font-size: 26px;
    font-weight: 700;
    color: #1e293b;
  }

  .rate-sub {
    margin-top: 2px;
    font-size: 12px;
    color: #64748b;
  }
}

.summary-list {
  padding: 0;
  margin: 0 0 12px;
  list-style: none;
}

.summary-row {
  display: flex;
  padding: 6px 4px;
  font-size: 13px;
  border-bottom: 1px dashed #e2e8f0;
  align-items: center;
  justify-content: space-between;

  &:last-child {
    border-bottom: none;
  }

  .row-label {
    display: inline-flex;
    font-size: 12px;
    color: #64748b;
    align-items: center;
    gap: 6px;
  }

  .row-val {
    display: inline-flex;
    font-size: 12px;
    color: #1e293b;
    align-items: center;
    gap: 6px;
  }

  .row-icon {
    font-size: 14px;

    &.good {
      color: #059669;
    }

    &.bad {
      color: #ef4444;
    }

    &.warn {
      color: #f59e0b;
    }
  }

  .err-mini {
    max-width: 130px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
}

.summary-tip {
  display: inline-flex;
  padding: 8px 12px;
  font-size: 11px;
  color: #475569;
  background: #f8fafc;
  border: 1px dashed #cbd5e1;
  border-radius: 4px;
  align-items: center;
  gap: 4px;
}

/* MES */
.mes-filter {
  display: flex;
  align-items: center;
  gap: 6px;
}

.mes-stat-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-bottom: 14px;
}

.mes-stat {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 16px;
  background: #f8fafc;
  border-radius: 6px;

  &.eligible {
    background: #ecfdf5;
  }

  &.ineligible {
    background: #fef2f2;
  }

  &.unknown {
    background: #f0f9ff;
  }

  .mes-label {
    font-size: 12px;
    color: #64748b;
  }

  .mes-val {
    font-size: 22px;
    font-weight: 600;
    color: #1e293b;
  }
}

.reason-tag {
  margin-right: 4px;

  &:last-child {
    margin-right: 0;
  }
}
</style>
