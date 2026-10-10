<script setup lang="ts">
/**
 * APS V1 4号位 — PI Position / 供给追溯（页面 7 / U16 / U17 / U18）
 *
 * 4号位文档第 13 节约束：
 *  - 6 类 PositionType：ERP_REMAINING / PRODUCTION / TRANSIT / WAITING / UNLOCATED / CROSS_STAGE
 *  - 9 类 SupplyType：INVENTORY / PI / PO / VMI / ARRIVED_NOT_INBOUND /
 *                   INTERPLANT_TRANSIT / RECEIVED / PLANNED_PRODUCTION / PLANNING_PURCHASE_PLACEHOLDER
 *  - U16：UNLOCATED 必须显示数量 + "不可下发 MES"
 *  - U17：无 PI 规划 Task 显示不可 MES
 *  - U18：依赖 PLANNING_PURCHASE_PLACEHOLDER 的 Task 不允许下 MES
 *
 * 仅只读页面（阶段 D 部分），所有"修复"动作走 Controller，不直接 UPDATE
 */

import { computed, onMounted, watch } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { usePiStore } from '@/store/modules/aps/pi'
import { useDomainStore } from '@/store/modules/aps/domain'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import DomainUnavailableBadge from './components/DomainUnavailableBadge.vue'
import { ESTIMATED_BADGE_TEXT } from '@/api/aps-v1'
import type {
  PiPositionType,
  SupplyType,
  MesEligibility,
  DomainKey,
  PiIssueType
} from '@/api/aps-v1'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElEmpty,
  ElInput,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'

const piStore = usePiStore()
const {
  list,
  currentDetail,
  detailOpen,
  filter,
  loading,
  listLoading,
  error,
  positionTypeCounts,
  mesStats,
  totalCount,
  issueCount,
  unlocatedCount,
  placeholderCount,
  supplyTypeCounts
} = storeToRefs(piStore)

/** Non-null 在 drawer v-if 内用 */
const detailNN = computed(() => currentDetail.value!)

/* ===== 字典 ===== */
const POSITION_TAG: Record<PiPositionType, 'success' | 'warning' | 'info' | 'primary' | 'danger'> =
  {
    ERP_REMAINING: 'info',
    PRODUCTION: 'success',
    TRANSIT: 'primary',
    WAITING: 'warning',
    UNLOCATED: 'danger',
    CROSS_STAGE: 'info'
  }
const POSITION_LABEL: Record<PiPositionType, string> = {
  ERP_REMAINING: 'ERP 剩余',
  PRODUCTION: '生产中',
  TRANSIT: '在途',
  WAITING: '等待',
  UNLOCATED: '未分配',
  CROSS_STAGE: '跨阶段'
}

const SUPPLY_TAG: Record<SupplyType, 'success' | 'warning' | 'info' | 'primary' | 'danger'> = {
  INVENTORY: 'success',
  PI: 'primary',
  PO: 'info',
  VMI: 'primary',
  ARRIVED_NOT_INBOUND: 'warning',
  INTERPLANT_TRANSIT: 'primary',
  RECEIVED: 'success',
  PLANNED_PRODUCTION: 'info',
  PLANNING_PURCHASE_PLACEHOLDER: 'danger'
}
const SUPPLY_LABEL: Record<SupplyType, string> = {
  INVENTORY: '库存',
  PI: '生产指令',
  PO: '采购单',
  VMI: 'VMI 寄售',
  ARRIVED_NOT_INBOUND: '到货未入库',
  INTERPLANT_TRANSIT: '跨厂在途',
  RECEIVED: '已入库',
  PLANNED_PRODUCTION: '计划生产',
  PLANNING_PURCHASE_PLACEHOLDER: '采购占位'
}

const MES_TAG: Record<MesEligibility, 'success' | 'danger' | 'info'> = {
  ELIGIBLE: 'success',
  INELIGIBLE: 'danger',
  UNKNOWN: 'info'
}
const MES_LABEL: Record<MesEligibility, string> = {
  ELIGIBLE: '可下发',
  INELIGIBLE: '不可下发',
  UNKNOWN: '待判定'
}

/** Domain 选项（v1.2 Domain专项：从 useDomainStore.activeDomains 派生，含 ALL）
 *  U41：按 apsAuth.dataScope.domainKeys 收敛——非空(受限)时仅含授权 Domain，空(global)时全量 */
const domainStore = useDomainStore()
const apsAuth = useApsAuthStore()
const DOMAIN_OPTIONS = computed<Array<{ value: DomainKey | 'ALL'; label: string }>>(() => {
  const all = domainStore.activeDomains.map((d) => ({ value: d.domainKey, label: d.domainName }))
  const allowed = apsAuth.dataScope.domainKeys as readonly string[]
  const list = allowed.length === 0 ? all : all.filter((o) => allowed.includes(o.value))
  return [{ value: 'ALL' as const, label: '全部排程域' }, ...list]
})

const POSITION_OPTIONS: Array<{ value: PiPositionType | 'ALL'; label: string }> = [
  { value: 'ALL', label: '全部位置' },
  { value: 'ERP_REMAINING', label: POSITION_LABEL.ERP_REMAINING },
  { value: 'PRODUCTION', label: POSITION_LABEL.PRODUCTION },
  { value: 'TRANSIT', label: POSITION_LABEL.TRANSIT },
  { value: 'WAITING', label: POSITION_LABEL.WAITING },
  { value: 'UNLOCATED', label: POSITION_LABEL.UNLOCATED },
  { value: 'CROSS_STAGE', label: POSITION_LABEL.CROSS_STAGE }
]

/* ===== 操作 ===== */
// 2026-09-17 5号位 回执后：详情端点用 productionInstructionNo（字符串），非 ID
function selectPi(no: string): void {
  piStore.loadDetail(no)
}

function closeDrawer(): void {
  piStore.closeDetail()
}

function onDomainChange(v: DomainKey | 'ALL'): void {
  piStore.setDomainFilter(v)
  piStore.loadList()
}
function onPositionChange(v: PiPositionType | 'ALL'): void {
  piStore.setPositionFilter(v)
  piStore.loadList()
}
function onMaterialSearch(v: string): void {
  // 输入防抖：简化版用 watch
  piStore.setMaterialFilter(v)
}
watch(
  () => filter.value.materialCode,
  () => {
    // 搜索触发列表
    piStore.loadList()
  }
)
function resetFilters(): void {
  piStore.resetFilter()
  piStore.loadList()
}

/* ===== 工具 ===== */
function varianceText(v: number): string {
  if (v > 0) return `+${v}`
  if (v < 0) return `${v}`
  return '0'
}

function varianceClass(v: number): string {
  if (v === 0) return 'variance-zero'
  if (v > 0) return 'variance-pos'
  return 'variance-neg'
}

function elapsed(iso: string): string {
  const m = dayjs(iso).diff(dayjs(), 'minute')
  if (Math.abs(m) < 60) return m >= 0 ? `${m} 分钟后` : `${-m} 分钟前`
  const h = dayjs(iso).diff(dayjs(), 'hour')
  if (Math.abs(h) < 24) return h >= 0 ? `${h} 小时后` : `${-h} 小时前`
  const d = dayjs(iso).diff(dayjs(), 'day')
  return d >= 0 ? `${d} 天后` : `${-d} 天前`
}

/* ===== Issue 字典 ===== */
const ISSUE_LABEL: Record<PiIssueType, string> = {
  UNLOCATED_EXISTS: '未分配数量',
  NO_PI: '无 PI 规划任务',
  PLACEHOLDER_DEPENDENCY: '依赖采购占位',
  CROSS_STAGE_RISK: '跨阶段风险',
  OTHER: '其他'
}
const ISSUE_CLASS: Record<PiIssueType, string> = {
  UNLOCATED_EXISTS: 'u16-tag',
  NO_PI: 'u17-tag',
  PLACEHOLDER_DEPENDENCY: 'u18-tag',
  CROSS_STAGE_RISK: 'warn-tag',
  OTHER: 'muted'
}
function issueLabel(t: PiIssueType): string {
  return ISSUE_LABEL[t] ?? t
}
function issueClass(t: PiIssueType): string {
  return ISSUE_CLASS[t] ?? ''
}

onMounted(() => {
  piStore.loadList()
})
</script>

<template>
  <div class="aps-pi">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">PI Position / 供给追溯</h2>
        <p class="page-sub">
          6 类位置 × 9 类供给；未分配数量与采购占位依赖会自动判定 MES 下发资格
        </p>
      </div>
      <ElButton :loading="listLoading" @click="piStore.loadList">
        <Icon icon="vi-ep:refresh" /> 刷新列表
      </ElButton>
    </div>

    <!-- ===== 业务约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>使用说明</template>
      <span class="hint-text">
        存在未分配数量时会显示具体数量，并标注"不可下发 MES"； 无 PI 规划任务同样标注不可下发 MES；
        依赖"采购占位"的位置不允许下发 MES。
      </span>
    </ElAlert>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="error" />

    <!-- ===== KPI 横条 ===== -->
    <div class="kpi-strip">
      <div class="kpi-pill">
        <span class="pill-label">PI 总数</span>
        <span class="pill-val">{{ totalCount }}</span>
      </div>
      <div class="kpi-pill warn">
        <span class="pill-label">含问题</span>
        <span class="pill-val">{{ issueCount }}</span>
      </div>
      <div class="kpi-pill danger">
        <span class="pill-label">未分配数量</span>
        <span class="pill-val">{{ unlocatedCount }}</span>
      </div>
      <div class="kpi-pill placeholder">
        <span class="pill-label">采购占位依赖</span>
        <span class="pill-val">{{ placeholderCount }}</span>
      </div>
      <div class="kpi-pill ok">
        <span class="pill-label">MES 可下发</span>
        <span class="pill-val">{{ mesStats.ELIGIBLE }}</span>
      </div>
      <div class="kpi-pill bad">
        <span class="pill-label">MES 不可下发</span>
        <span class="pill-val">{{ mesStats.INELIGIBLE }}</span>
      </div>
      <div class="kpi-pill muted">
        <span class="pill-label">MES 待判定</span>
        <span class="pill-val">{{ mesStats.UNKNOWN }}</span>
      </div>
    </div>

    <!-- ===== Position Type 分布条 ===== -->
    <ElCard class="panel position-panel">
      <template #header>
        <span class="panel-title">位置类型分布（按 PI 主位置）</span>
      </template>
      <div class="position-grid">
        <div v-for="(count, type) in positionTypeCounts" :key="type" class="position-cell">
          <ElTag :type="POSITION_TAG[type as PiPositionType]" effect="dark" size="small">
            {{ POSITION_LABEL[type as PiPositionType] }}
          </ElTag>
          <span class="position-count">{{ count }}</span>
        </div>
      </div>
    </ElCard>

    <!-- ===== 过滤栏 ===== -->
    <ElCard class="panel filter-panel">
      <div class="filter-row">
        <div class="filter-item">
          <span class="filter-label">排程域</span>
          <ElSelect
            :model-value="filter.domainKey"
            placeholder="全部"
            style="width: 160px"
            @change="onDomainChange"
          >
            <ElOption
              v-for="opt in DOMAIN_OPTIONS"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </ElSelect>
          <DomainUnavailableBadge />
        </div>
        <div class="filter-item">
          <span class="filter-label">主位置</span>
          <ElSelect
            :model-value="filter.positionType"
            placeholder="全部"
            style="width: 160px"
            @change="onPositionChange"
          >
            <ElOption
              v-for="opt in POSITION_OPTIONS"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </ElSelect>
        </div>
        <div class="filter-item">
          <span class="filter-label">物料</span>
          <ElInput
            :model-value="filter.materialCode"
            placeholder="M-001 / M-002 ..."
            style="width: 200px"
            clearable
            @input="onMaterialSearch"
          />
        </div>
        <ElButton @click="resetFilters"> <Icon icon="vi-ep:refresh-left" /> 重置过滤 </ElButton>
      </div>
    </ElCard>

    <!-- ===== PI 列表 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>PI 列表</span>
          <ElTag size="small" effect="plain">{{ list.length }} 个</ElTag>
        </div>
      </template>
      <div v-if="!list.length && !listLoading" class="panel-empty">
        <ElEmpty description="无符合条件的 PI" />
      </div>
      <ElTable v-else :data="list" size="small" border stripe>
        <ElTableColumn label="PI 号" width="160">
          <template #default="{ row }">
            <code class="code-tag">{{ row.productionInstructionNo }}</code>
          </template>
        </ElTableColumn>
        <ElTableColumn label="排程域" width="100">
          <template #default="{ row }">
            <ElTag size="small" effect="plain">{{ row.domainKey }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="materialCode" label="物料" width="100" />
        <ElTableColumn label="ERP / 位置 / 差异" width="220">
          <template #default="{ row }">
            <div class="qty-cell">
              <span class="qty-erp">ERP {{ row.erpRemainingQty }}</span>
              <span class="qty-pos">位置 {{ row.positionTotalQty }}</span>
              <span :class="['qty-var', varianceClass(row.variance)]">
                {{ varianceText(row.variance) }}
              </span>
            </div>
          </template>
        </ElTableColumn>
        <ElTableColumn label="主位置" width="130">
          <template #default="{ row }">
            <ElTag :type="POSITION_TAG[row.mainPositionType]" effect="dark" size="small">
              {{ POSITION_LABEL[row.mainPositionType] }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="未分配数量" width="140" align="right">
          <template #default="{ row }">
            <span v-if="row.hasUnlocated" class="warn-strong">{{ row.unlocatedQty }}</span>
            <span v-else class="muted">0</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="采购占位" width="160" align="right">
          <template #default="{ row }">
            <span v-if="row.hasPlaceholderDependency" class="placeholder-strong">
              {{ row.placeholderQty }}
            </span>
            <span v-else class="muted">0</span>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="supplyCount" label="供给数" width="80" align="center" />
        <ElTableColumn label="MES 资格" width="120" align="center">
          <template #default="{ row }">
            <ElTag :type="MES_TAG[row.mesEligible]" size="small" effect="dark">
              {{ MES_LABEL[row.mesEligible] }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="问题" width="80" align="center">
          <template #default="{ row }">
            <ElTag v-if="row.issueCount > 0" size="small" type="warning">
              {{ row.issueCount }}
            </ElTag>
            <span v-else class="muted">0</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="更新时间" width="140">
          <template #default="{ row }">
            <ElTooltip
              :content="dayjs(row.updatedAt).format('YYYY-MM-DD HH:mm:ss')"
              placement="top"
            >
              <span class="muted">{{ elapsed(row.updatedAt) }}</span>
            </ElTooltip>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="80" align="center" fixed="right">
          <template #default="{ row }">
            <ElButton
              size="small"
              link
              type="primary"
              @click="selectPi(row.productionInstructionNo)"
            >
              <Icon icon="vi-ep:view" /> 查看
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <!-- ===== 加载态 ===== -->
    <div v-if="loading" class="loading-tip">加载 PI 详情中…</div>

    <!-- ===== 详情 Drawer ===== -->
    <ElDrawer
      :model-value="detailOpen"
      direction="rtl"
      size="900px"
      :with-header="false"
      @update:model-value="closeDrawer"
    >
      <div v-if="currentDetail" class="drawer-body">
        <!-- 抽屉头 -->
        <div class="drawer-header">
          <div class="drawer-title">
            <code class="code-tag">{{ detailNN.productionInstructionNo }}</code>
            <ElTag :type="POSITION_TAG[detailNN.mainPositionType]" effect="dark" size="small">
              {{ POSITION_LABEL[detailNN.mainPositionType] }}
            </ElTag>
            <ElTag size="small" effect="plain">{{ detailNN.materialCode }}</ElTag>
            <ElTag size="small" effect="plain">{{ detailNN.domainKey }}</ElTag>
          </div>
          <ElButton @click="closeDrawer"> <Icon icon="vi-ep:close" /> 关闭 </ElButton>
        </div>

        <!-- Summary -->
        <ElDescriptions :column="4" size="small" border>
          <ElDescriptionsItem label="ERP 剩余">{{ detailNN.erpRemainingQty }}</ElDescriptionsItem>
          <ElDescriptionsItem label="位置合计">{{ detailNN.positionTotalQty }}</ElDescriptionsItem>
          <ElDescriptionsItem label="差异">
            <span :class="varianceClass(detailNN.variance)">{{
              varianceText(detailNN.variance)
            }}</span>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="供给来源数">{{ detailNN.supplies.length }}</ElDescriptionsItem>
          <ElDescriptionsItem label="更新于">
            {{ dayjs(detailNN.updatedAt).format('YYYY-MM-DD HH:mm:ss') }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="问题" :span="3">
            <ElTag v-if="detailNN.issues.length === 0" type="success" size="small">无</ElTag>
            <ElTag v-else type="warning" size="small">{{ detailNN.issues.length }} 个</ElTag>
          </ElDescriptionsItem>
        </ElDescriptions>

        <!-- Issue 提示 -->
        <ElAlert
          v-for="(iss, idx) in detailNN.issues"
          :key="idx"
          :type="iss.issueType === 'UNLOCATED_EXISTS' ? 'error' : 'warning'"
          :closable="false"
          show-icon
          class="issue-alert"
        >
          <template #title>
            <span :class="issueClass(iss.issueType)">{{ issueLabel(iss.issueType) }}</span>
          </template>
          {{ iss.message }}
          <span v-if="iss.relatedSupplyKey" class="issue-ref">#{{ iss.relatedSupplyKey }}</span>
        </ElAlert>

        <!-- 位置明细 -->
        <ElCard class="panel">
          <template #header>
            <span>
              <Icon icon="vi-mdi:map-marker" />
              位置明细（{{ detailNN.positions.length }} 行）
            </span>
          </template>
          <div v-if="!detailNN.positions.length" class="panel-empty">
            <ElEmpty description="无位置数据（不可下发 MES）" />
          </div>
          <ElTable v-else :data="detailNN.positions" size="small" border>
            <ElTableColumn label="位置类型" width="130">
              <template #default="{ row }">
                <ElTag
                  :type="POSITION_TAG[row.positionType as PiPositionType]"
                  effect="dark"
                  size="small"
                >
                  {{ POSITION_LABEL[row.positionType as PiPositionType] }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn prop="stage" label="阶段" width="120">
              <template #default="{ row }">
                <span v-if="row.stage" class="code-tag">{{ row.stage }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="位置数量" width="120" align="right">
              <template #default="{ row }">{{ row.positionQty }}</template>
            </ElTableColumn>
            <ElTableColumn label="跨工厂" width="90" align="right">
              <template #default="{ row }">
                <span v-if="row.crossFactoryQty">{{ row.crossFactoryQty }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="在途" width="90" align="right">
              <template #default="{ row }">
                <span v-if="row.transitQty">{{ row.transitQty }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="等待" width="90" align="right">
              <template #default="{ row }">
                <span v-if="row.waitingQty">{{ row.waitingQty }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="未分配数量" width="140" align="right">
              <template #default="{ row }">
                <span v-if="row.unlocatedQty" class="warn-strong">{{ row.unlocatedQty }}</span>
                <span v-else class="muted">0</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="备注" min-width="200">
              <template #default="{ row }">
                <span v-if="row.issue" class="warn-strong">{{ row.issue }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElCard>

        <!-- 供给拆解 -->
        <ElCard class="panel">
          <template #header>
            <span>
              <Icon icon="vi-mdi:truck-delivery-outline" />
              供给拆解（{{ detailNN.supplies.length }} 条）
            </span>
          </template>
          <div v-if="!detailNN.supplies.length" class="panel-empty">
            <ElEmpty description="无供给来源" />
          </div>
          <ElTable v-else :data="detailNN.supplies" size="small" border>
            <ElTableColumn label="类型" width="180">
              <template #default="{ row }">
                <ElTag :type="SUPPLY_TAG[row.supplyType as SupplyType]" effect="dark" size="small">
                  {{ SUPPLY_LABEL[row.supplyType as SupplyType] }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="供给编号" min-width="160">
              <template #default="{ row }">
                <code class="code-tag">{{ row.supplyKey }}</code>
              </template>
            </ElTableColumn>
            <ElTableColumn label="数量" width="100" align="right" prop="qty" />
            <ElTableColumn label="可供应时间" width="180">
              <template #default="{ row }">
                <span v-if="row.availableTime">
                  <ElTooltip
                    :content="dayjs(row.availableTime).format('YYYY-MM-DD HH:mm:ss')"
                    placement="top"
                  >
                    <span
                      :class="{ 'est-text': row.isPlanningOnlyPlaceholder || row.isNotCommitted }"
                    >
                      {{ elapsed(row.availableTime) }}
                    </span>
                  </ElTooltip>
                </span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="未承诺" width="130" align="center">
              <template #default="{ row }">
                <ElTag v-if="row.isNotCommitted" size="small" type="warning">是</ElTag>
                <span v-else class="muted">否</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="估算值" width="140" align="center">
              <template #default="{ row }">
                <ElTooltip
                  v-if="row.isPlanningOnlyPlaceholder"
                  :content="ESTIMATED_BADGE_TEXT"
                  placement="top"
                >
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
            <ElTableColumn label="关联订单" width="160">
              <template #default="{ row }">
                <span v-if="row.relatedOrderNo" class="code-tag">{{ row.relatedOrderNo }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="说明" min-width="200">
              <template #default="{ row }">
                <span v-if="row.description" class="desc-cell">{{ row.description }}</span>
                <span v-else class="muted">—</span>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElCard>

        <!-- 供给类型分布 -->
        <ElCard class="panel">
          <template #header>
            <span>
              <Icon icon="vi-mdi:chart-pie" />
              供给类型分布
            </span>
          </template>
          <div class="supply-grid">
            <div
              v-for="(qty, t) in supplyTypeCounts"
              :key="t"
              class="supply-cell"
              :class="{ zero: qty === 0 }"
            >
              <ElTag :type="SUPPLY_TAG[t as SupplyType]" effect="plain" size="small">
                {{ SUPPLY_LABEL[t as SupplyType] }}
              </ElTag>
              <span class="supply-qty">{{ qty }}</span>
            </div>
          </div>
        </ElCard>
      </div>
    </ElDrawer>
  </div>
</template>

<style lang="less" scoped>
.aps-pi {
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

.panel-title {
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

.code-tag {
  padding: 2px 6px;
  font-family: 'Courier New', monospace;
  font-size: 12px;
  color: #475569;
  background: #f1f5f9;
  border-radius: 3px;
}

.loading-tip {
  padding: 30px;
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
}

/* KPI */
.kpi-strip {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.kpi-pill {
  display: inline-flex;
  padding: 6px 14px;
  font-size: 12px;
  background: #f8fafc;
  border-radius: 16px;
  align-items: center;
  gap: 6px;

  &.warn {
    background: #fef3c7;
  }

  &.danger {
    background: #fee2e2;
  }

  &.placeholder {
    background: #f3e8ff;
  }

  &.ok {
    background: #d1fae5;
  }

  &.bad {
    background: #fecaca;
  }

  &.muted {
    background: #f1f5f9;
  }

  .pill-label {
    color: #64748b;
  }

  .pill-val {
    font-weight: 600;
    color: #1e293b;
  }
}

/* Position 分布 */
.position-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

.position-cell {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background: #f8fafc;
  border-radius: 6px;

  .position-count {
    font-family: 'Courier New', monospace;
    font-size: 20px;
    font-weight: 700;
    color: #1e293b;
  }
}

/* 过滤 */
.filter-panel {
  padding: 0;
}

.filter-row {
  display: flex;
  gap: 16px;
  align-items: center;
  padding: 12px;
}

.filter-item {
  display: flex;
  align-items: center;
  gap: 8px;

  .filter-label {
    min-width: 50px;
    font-size: 12px;
    color: #64748b;
  }
}

/* 数量单元格 */
.qty-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-family: 'Courier New', monospace;
  font-size: 12px;

  .qty-erp {
    color: #94a3b8;
  }

  .qty-pos {
    font-weight: 600;
    color: #1e293b;
  }

  .qty-var {
    font-weight: 600;
  }
}

.variance-pos {
  color: #f59e0b;
}

.variance-neg {
  color: #ef4444;
}

.variance-zero {
  color: #94a3b8;
}

/* 警示 */
.warn-strong {
  font-weight: 700;
  color: #ef4444;
}

.placeholder-strong {
  font-weight: 700;
  color: #8b5cf6;
}

.est-text {
  font-weight: 600;
  color: #8b5cf6;
}

/* Issue 标签 */
.u16-tag {
  font-weight: 700;
  color: #ef4444;
}

.u17-tag {
  font-weight: 700;
  color: #f59e0b;
}

.u18-tag {
  font-weight: 700;
  color: #8b5cf6;
}

.warn-tag {
  font-weight: 700;
  color: #f59e0b;
}

.issue-ref {
  margin-left: 6px;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  color: #64748b;
}

/* Drawer */
.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 16px 24px 32px;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 12px;
  border-bottom: 2px solid #e2e8f0;

  .drawer-title {
    display: flex;
    gap: 8px;
    align-items: center;
  }
}

.issue-alert {
  margin-bottom: 8px;
}

.desc-cell {
  font-size: 12px;
  line-height: 1.4;
  color: #475569;
}

/* Supply 分布 */
.supply-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
}

.supply-cell {
  display: flex;
  padding: 8px 12px;
  background: #f8fafc;
  border-left: 3px solid #3b82f6;
  border-radius: 4px;
  justify-content: space-between;
  align-items: center;

  &.zero {
    background: #f9fafb;
    border-left-color: #e5e7eb;

    .supply-qty {
      color: #94a3b8;
    }
  }

  .supply-qty {
    font-family: 'Courier New', monospace;
    font-weight: 600;
    color: #1e293b;
  }
}
</style>
