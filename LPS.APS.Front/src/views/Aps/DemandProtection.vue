<script setup lang="ts">
/**
 * APS V1 4号位 — 手工释放需求保护（Demand Protection Release）页面（页面 9 / P1-11）
 *
 * 4号位文档第 13 节硬约束：
 *  - 链路：4号位 → 5号位（DemandProtectionController）→ 2号位 Service
 *  - 4号位只提交：Demand 标识 + 当前保护数量 + 释放范围 + Reason + 当前用户身份
 *  - 5号位不得 UPDATE 表 / 计算保护数量 / 复制规则
 *  - 4号位不计算：Effective ETA / 最终可用时间（AvailableTime）（此处无关）
 *
 * 本期范围（用户决策）：
 *  - 只支持 Full Release（与 2号位 IDemandSupplyHardLockRepository.ReleaseLocksAsync 1:1）
 *  - 列表接口返回详情（list = preview，Drawer 不发二次请求）
 *  - 复用 Manual ETA 样板（KPI + 筛选 + Table + Drawer）
 *
 * 页面分区：
 *  - 顶部 page-header（标题 / 副标题 / 刷新）
 *  - 约束提示条（4→5→2 链路 / 不计算保护数量 / 只支持 Full Release）
 *  - 错误条
 *  - KPI 4 卡（总数 / Active / 已释放 / 锁定总量）
 *  - 筛选 + 操作（DemandType / LockType / ActiveOnly / 重置 / 筛选 / 批量释放 (N)）
 *  - 主表格（带 selection 行 + Lock 明细列）
 *  - 详情 Drawer（基本信息 / 锁明细 / 释放表单 / 已释放只读 / 严格绑定提示）
 *  - 批量释放 Modal（共享 reason + 汇总）
 *
 * RBAC（@see 审核报告 P1-14/15）：
 *  - VIEWER：禁用 批量释放 / 行内 [释放] / Drawer [确认释放] 按钮
 *  - PMC+：可写
 *  - STRICT_BINDING：仅展示不允许释放（tooltip 说明）
 */

import { computed, nextTick, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { ElMessage, ElMessageBox } from 'element-plus'
import { storeToRefs } from 'pinia'
import { useDemandProtectionStore } from '@/store/modules/aps/demandProtection'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import type {
  DemandProtectionDto,
  DemandProtectionListFilter,
  DemandProtectionReleaseResult,
  DemandProtectionStatus,
  HardLockStatus,
  HardLockSupplyType,
  ReleaseDemandProtectionInput
} from '@/api/aps-v1'
import {
  DEMAND_TYPE_LABELS,
  HARD_LOCK_STATUS_LABELS,
  HARD_LOCK_SUPPLY_TYPE_LABELS,
  LOCK_TYPE_LABELS
} from '@/api/aps-v1'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElDrawer,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'

const store = useDemandProtectionStore()
const apsAuth = useApsAuthStore()
const {
  list,
  currentDetail,
  loading,
  releasing,
  error,
  lastFilter,
  activeCount,
  releasedCount,
  totalLockedQty,
  distinctDemandTypes,
  distinctLockTypes
} = storeToRefs(store)

/* ===== 权限门控（v1.2 §23.1 权限码门控）=====
 *  - aps.demand_protection.release：dev seed 下仅 aps.admin.system 持有
 *  - 行为变化（vs v1.1）：原 4 角色（PMC/RULE_ADMIN/RULE_PUBLISHER/SYSTEM_ADMIN）皆可释放；
 *    切换到权限码后只有 SYSTEM_ADMIN 可见释放按钮，符合 §二十五 后端权威职责
 *  - 不用角色判断的原因：与 3号位后端二次校验用同一码
 */
const canWrite = computed(() => apsAuth.has('aps.demand_protection.release'))

/* ===== 可释放判定（页面级单一真源） ===== */
function canRelease(row: DemandProtectionDto): boolean {
  if (!canWrite.value) return false
  if (row.status !== 'ACTIVE') return false
  if (row.lockType === 'STRICT_BINDING') return false
  if (row.lockIds.length === 0) return false
  return true
}

/* ===== 状态 / 类型 映射 ===== */
const STATUS_TAG: Record<DemandProtectionStatus, 'success' | 'info' | 'warning'> = {
  ACTIVE: 'success',
  RELEASED: 'info',
  PARTIALLY_RELEASED: 'warning'
}

const LOCK_TYPE_TAG: Record<'DEMAND_PROTECTION' | 'STRICT_BINDING', 'warning' | 'danger'> = {
  DEMAND_PROTECTION: 'warning',
  STRICT_BINDING: 'danger'
}

const SUPPLY_STATUS_TAG: Record<HardLockStatus, 'success' | 'info' | 'danger'> = {
  ACTIVE: 'success',
  RELEASED: 'info',
  BROKEN: 'danger'
}

/* ===== 筛选本地 state（变更后点 [筛选] 才生效） ===== */
const filterDraft = ref<DemandProtectionListFilter>({})
const activeOnlyDraft = ref(false)
const selectedDemandTypes = ref<string[]>([])
const selectedLockTypes = ref<string[]>([])

function syncFilterDraft(): void {
  filterDraft.value = {
    demandTypes: selectedDemandTypes.value.length
      ? (selectedDemandTypes.value as DemandProtectionListFilter['demandTypes'])
      : undefined,
    lockTypes: selectedLockTypes.value.length
      ? (selectedLockTypes.value as DemandProtectionListFilter['lockTypes'])
      : undefined,
    activeOnly: activeOnlyDraft.value
  }
}

async function applyFilter(): Promise<void> {
  syncFilterDraft()
  await store.load(filterDraft.value)
}

function resetFilters(): void {
  selectedDemandTypes.value = []
  selectedLockTypes.value = []
  activeOnlyDraft.value = false
}

async function refresh(): Promise<void> {
  selectedRows.value = []
  await store.load()
}

/* ===== 主表格多选 + 行点击 ===== */
const selectedRows = ref<DemandProtectionDto[]>([])

function onSelectionChange(rows: DemandProtectionDto[]): void {
  selectedRows.value = rows
}

/** 行是否可选：不可释放的行复选框直接不可勾 */
function selectable(row: DemandProtectionDto): boolean {
  return canRelease(row)
}

/** 可释放的选中行（防御性过滤，避免外部绕过 selectable 传入） */
const releasableSelection = computed(() => selectedRows.value.filter((r) => canRelease(r)))

/** 批量释放总 lock 数 + 总 lockedQty（仅 releasable 选中部分） */
const batchSummary = computed(() => {
  const rows = releasableSelection.value
  return {
    demandCount: rows.length,
    lockCount: rows.reduce((sum, r) => sum + r.lockIds.length, 0),
    lockedQty: rows.reduce((sum, r) => sum + r.lockedQty, 0)
  }
})

/* ===== 详情 Drawer ===== */
const drawerVisible = ref(false)
const drawerFormRef = ref<{
  validate: () => Promise<boolean>
  resetFields: () => void
} | null>(null)
const drawerReason = ref('')

/**
 * 释放原因校验规则
 * ⚠️ 2026-09-14 治理红线复核说明（@see APS_V1_各号位AI冻结基线治理红线_v1.0_20260914.md §4.2）：
 *  - 「必填」= 冻结业务基线审计需求（@see 4号位文档第 X 节）
 *  - 历史曾加 ≥5 字符硬约束，来自 9月13日 1号位 `未命名的Markdown文件.md` 解读，非 0 号位正式裁决（@see §十三）
 *  - 1 号位 ≠ 0 号位 → 该硬约束不能直接落地
 *  - 当前仅保留「必填」+ 200 字符上限（审计可读性）；如 0 号位正式裁决再补 ≥N 字符约束
 *  - 批量释放 batchRules 同理
 */
const drawerRules = {
  reason: [{ required: true, message: '请输入释放原因', trigger: 'blur' }]
}

function openDetail(row: DemandProtectionDto): void {
  store.openDetail(row)
  drawerReason.value = ''
  drawerVisible.value = true
}

function openReleaseFromRow(row: DemandProtectionDto): void {
  // 行内 [释放] 直接打开 Drawer 并聚焦 reason（reason 是审计强需求，不做无 reason 快捷释放）
  openDetail(row)
  nextTick(() => {
    const el = document.querySelector(
      '.dp-drawer .el-textarea__inner'
    ) as HTMLTextAreaElement | null
    el?.focus()
  })
}

function closeDrawer(): void {
  drawerVisible.value = false
  drawerReason.value = ''
  store.openDetail(null)
}

async function confirmRelease(): Promise<void> {
  if (!currentDetail.value) return
  if (!drawerFormRef.value) return
  let valid = false
  try {
    valid = await drawerFormRef.value.validate()
  } catch {
    return
  }
  if (!valid) return

  try {
    await ElMessageBox.confirm(
      `将释放需求保护：${currentDetail.value.demandKey}\n\n` +
        `范围：该需求下全部 ${currentDetail.value.lockCount} 条 Lock（共 ${currentDetail.value.lockedQty} 数量）\n` +
        `原因：${drawerReason.value.trim()}\n\n` +
        `释放后对应供给回到可分配池，且不可撤销。`,
      '确认释放',
      {
        confirmButtonText: '确认释放',
        cancelButtonText: '不释放',
        type: 'warning'
      }
    )
  } catch {
    return
  }

  const releasedBy = apsAuth.userInfo?.userCode ?? 'mock-pmc'
  const input: ReleaseDemandProtectionInput = {
    lockIds: [...currentDetail.value.lockIds],
    releasedBy,
    releaseReason: drawerReason.value.trim()
  }
  try {
    const perLock = await store.release(input)
    showReleaseOutcome(perLock)
    closeDrawer()
  } catch (err) {
    ElMessage.error(`释放失败：${(err as Error)?.message ?? '未知错误'}`)
  }
}

/** 释放结果展示：成功汇总 toast，失败明细 alert */
function showReleaseOutcome(perLock: DemandProtectionReleaseResult[]): void {
  const released = perLock.filter((r) => r.status === 'RELEASED')
  const failed = perLock.filter((r) => r.status === 'FAILED')
  if (failed.length === 0) {
    ElMessage.success(`已释放 ${released.length} 条锁`)
    return
  }
  if (released.length === 0) {
    ElMessage.error(`全部 ${failed.length} 条锁释放失败`)
    ElMessageBox.alert(
      failed.map((f) => `· LockId=${f.lockId}：${f.failureReason ?? '未知'}`).join('\n'),
      '释放失败明细',
      { type: 'error' }
    )
    return
  }
  ElMessageBox.alert(
    `成功 ${released.length} 条，失败 ${failed.length} 条\n\n` +
      `失败明细：\n` +
      failed.map((f) => `· LockId=${f.lockId}：${f.failureReason ?? '未知'}`).join('\n'),
    '部分失败',
    { type: 'warning' }
  )
}

/* ===== 批量释放 Modal ===== */
const batchModalVisible = ref(false)
const batchReason = ref('')
const batchFormRef = ref<{
  validate: () => Promise<boolean>
  resetFields: () => void
} | null>(null)

/**
 * 批量释放原因校验规则（@see drawerRules 注释 / 治理红线 §4.2）
 */
const batchRules = {
  reason: [{ required: true, message: '请输入批量释放原因', trigger: 'blur' }]
}

function openBatchModal(): void {
  if (!releasableSelection.value.length) return
  batchReason.value = ''
  batchModalVisible.value = true
}

function closeBatchModal(): void {
  batchModalVisible.value = false
  batchReason.value = ''
}

async function submitBatch(): Promise<void> {
  if (!batchFormRef.value) return
  let valid = false
  try {
    valid = await batchFormRef.value.validate()
  } catch {
    return
  }
  if (!valid) return

  try {
    await ElMessageBox.confirm(
      `将批量释放 ${batchSummary.value.demandCount} 条需求保护（共 ${batchSummary.value.lockCount} 条 Lock / 锁定量 ${batchSummary.value.lockedQty}）\n\n` +
        `原因：${batchReason.value.trim()}\n\n` +
        `本操作不可撤销。`,
      '确认批量释放',
      {
        confirmButtonText: '确认批量释放',
        cancelButtonText: '取消',
        type: 'warning'
      }
    )
  } catch {
    return
  }

  const releasedBy = apsAuth.userInfo?.userCode ?? 'mock-pmc'
  const inputs: ReleaseDemandProtectionInput[] = releasableSelection.value.map((row) => ({
    lockIds: [...row.lockIds],
    releasedBy,
    releaseReason: batchReason.value.trim()
  }))
  const result = await store.releaseBatch(inputs)

  if (result.failed.length === 0) {
    const releasedCount = result.perLock.filter(
      (r: DemandProtectionReleaseResult) => r.status === 'RELEASED'
    ).length
    ElMessage.success(`已批量释放 ${result.succeeded.length} 条需求保护 / ${releasedCount} 条锁`)
    selectedRows.value = []
    closeBatchModal()
  } else if (result.succeeded.length === 0) {
    ElMessage.error(`批量释放失败：${result.failed[0]?.message ?? '未知错误'}`)
  } else {
    const releasedLockCount = result.perLock.filter(
      (r: DemandProtectionReleaseResult) => r.status === 'RELEASED'
    ).length
    ElMessageBox.alert(
      `成功 ${result.succeeded.length} 条需求保护 / ${releasedLockCount} 条锁；失败 ${result.failed.length} 条需求保护\n\n` +
        `失败明细：\n` +
        result.failed.map((f) => `· ${f.demandKey}：${f.message}`).join('\n'),
      '部分失败',
      { type: 'warning' }
    )
    closeBatchModal()
  }
}

onMounted(refresh)
</script>

<template>
  <div class="aps-dp">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">需求保护释放</h2>
        <p class="page-sub"> 按需求稳定键聚合，查看锁定的供给份额，支持整条释放 </p>
      </div>
      <ElButton :loading="loading" @click="refresh">
        <Icon icon="vi-ep:refresh" />
        刷新
      </ElButton>
    </div>

    <!-- ===== 文档约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>只支持整条释放</template>
      <span class="hint-text">
        本页只做释放提交：Demand 标识 + 当前保护数量 + 释放范围 + 原因 + 当前用户身份。
        保护数量的计算与规则的复制均不在本页发生，本页也不直接改库。
      </span>
    </ElAlert>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== KPI ===== -->
    <div class="kpi-row kpi-row-4">
      <div class="kpi-card" style="border-top-color: #3b82f6">
        <div class="kpi-label">总需求数</div>
        <div class="kpi-value" style="color: #3b82f6">{{ list.length }}</div>
        <div class="kpi-ratio">按需求稳定键聚合</div>
      </div>
      <div class="kpi-card" style="border-top-color: #059669">
        <div class="kpi-label">生效中</div>
        <div class="kpi-value" style="color: #059669">{{ activeCount }}</div>
        <div class="kpi-ratio">可释放</div>
      </div>
      <div class="kpi-card" style="border-top-color: #94a3b8">
        <div class="kpi-label">已释放</div>
        <div class="kpi-value" style="color: #94a3b8">{{ releasedCount }}</div>
        <div class="kpi-ratio">已释放 / 部分释放</div>
      </div>
      <div class="kpi-card" style="border-top-color: #d97706">
        <div class="kpi-label">锁定总量</div>
        <div class="kpi-value" style="color: #d97706">{{ totalLockedQty }}</div>
        <div class="kpi-ratio">生效锁聚合求和</div>
      </div>
    </div>

    <!-- ===== 筛选 + 操作 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>筛选条件</span>
          <span class="hint-text">
            <span v-if="lastFilter.activeOnly">仅生效中</span>
            <span v-if="lastFilter.demandTypes?.length">
              · 需求类型 {{ lastFilter.demandTypes.length }} 个</span
            >
            <span v-if="lastFilter.lockTypes?.length">
              · 锁类型 {{ lastFilter.lockTypes.length }} 个</span
            >
            <span
              v-if="
                !lastFilter.activeOnly &&
                !lastFilter.demandTypes?.length &&
                !lastFilter.lockTypes?.length
              "
              >全部</span
            >
          </span>
        </div>
      </template>
      <div class="filter-row">
        <div class="filter-item">
          <label>需求类型</label>
          <ElSelect
            v-model="selectedDemandTypes"
            multiple
            filterable
            clearable
            collapse-tags
            placeholder="全部需求类型"
            style="width: 220px"
          >
            <ElOption
              v-for="t in distinctDemandTypes"
              :key="t"
              :label="DEMAND_TYPE_LABELS[t] + ' (' + t + ')'"
              :value="t"
            />
          </ElSelect>
        </div>
        <div class="filter-item">
          <label>锁类型</label>
          <ElSelect
            v-model="selectedLockTypes"
            multiple
            filterable
            clearable
            collapse-tags
            placeholder="全部锁类型"
            style="width: 220px"
          >
            <ElOption
              v-for="t in distinctLockTypes"
              :key="t"
              :label="LOCK_TYPE_LABELS[t] + ' (' + t + ')'"
              :value="t"
            />
          </ElSelect>
        </div>
        <div class="filter-item">
          <label>仅生效中</label>
          <ElSwitch v-model="activeOnlyDraft" />
        </div>
        <div class="filter-actions">
          <ElButton @click="resetFilters">重置</ElButton>
          <ElButton type="primary" :loading="loading" @click="applyFilter">筛选</ElButton>
          <ElTooltip
            :content="
              releasableSelection.length
                ? `将释放 ${batchSummary.demandCount} 条需求 / ${batchSummary.lockCount} 条锁`
                : '请先勾选可释放的记录'
            "
            placement="top"
          >
            <ElButton
              type="danger"
              plain
              :disabled="!canWrite || releasableSelection.length === 0"
              @click="openBatchModal"
            >
              <Icon icon="vi-mdi:shield-off-outline" />
              批量释放{{ releasableSelection.length ? ` (${releasableSelection.length})` : '' }}
            </ElButton>
          </ElTooltip>
        </div>
      </div>
    </ElCard>

    <!-- ===== 主表格 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>需求保护列表（{{ list.length }} 条）</span>
          <span class="hint-text">点击行可查看锁明细；不可释放行复选框自动禁用</span>
        </div>
      </template>
      <div v-if="!list.length" class="panel-empty">
        <ElEmpty description="无需求保护数据" />
      </div>
      <ElTable
        v-else
        :data="list"
        :row-key="(r: DemandProtectionDto) => r.demandKey"
        size="small"
        border
        stripe
        row-class-name="row-clickable"
        style="width: 100%"
        @selection-change="onSelectionChange"
        @row-click="openDetail"
      >
        <ElTableColumn type="selection" width="46" :selectable="selectable" />
        <ElTableColumn label="需求类型" width="120" align="center">
          <template #default="{ row }">
            <ElTag size="small" type="info">{{ DEMAND_TYPE_LABELS[row.demandType] }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="需求稳定键" min-width="220">
          <template #default="{ row }">
            <ElTooltip :content="row.demandKey" placement="top">
              <span class="code-tag">{{ row.demandKey }}</span>
            </ElTooltip>
          </template>
        </ElTableColumn>
        <ElTableColumn label="物料" width="120">
          <template #default="{ row }">
            <span v-if="row.materialCode">{{ row.materialCode }}</span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="工厂" width="80" align="center">
          <template #default="{ row }">
            <span v-if="row.factory">{{ row.factory }}</span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="锁类型" width="110" align="center">
          <template #default="{ row }">
            <ElTag :type="LOCK_TYPE_TAG[row.lockType]" size="small">
              {{ LOCK_TYPE_LABELS[row.lockType] }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="锁条数" prop="lockCount" width="80" align="center" />
        <ElTableColumn label="锁定量" prop="lockedQty" width="90" align="right">
          <template #default="{ row }">
            <span v-if="row.status === 'ACTIVE'" class="qty-active">{{ row.lockedQty }}</span>
            <span v-else class="muted">{{ row.lockedQty }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="100" align="center">
          <template #default="{ row }">
            <ElTag :type="STATUS_TAG[row.status]" size="small">
              {{
                row.status === 'ACTIVE'
                  ? 'Active'
                  : row.status === 'RELEASED'
                    ? '已释放'
                    : '部分释放'
              }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="创建" min-width="170">
          <template #default="{ row }">
            <span class="muted small code-tag">{{ row.createdBy }}</span>
            <span class="muted small"> · {{ dayjs(row.createdAt).format('MM-DD HH:mm') }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="释放" min-width="170">
          <template #default="{ row }">
            <template v-if="row.releasedAt">
              <span class="muted small code-tag">{{ row.releasedBy ?? '-' }}</span>
              <span class="muted small"> · {{ dayjs(row.releasedAt).format('MM-DD HH:mm') }}</span>
            </template>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="180" fixed="right" align="center">
          <template #default="{ row }">
            <ElButton size="small" plain @click.stop="openDetail(row)">
              <Icon icon="vi-mdi:eye-outline" />
              查看
            </ElButton>
            <ElTooltip
              v-if="row.lockType === 'STRICT_BINDING'"
              content="严格绑定锁不支持手工释放，请走重排流程"
              placement="top"
            >
              <ElButton size="small" type="danger" plain :disabled="true" @click.stop>
                <Icon icon="vi-mdi:lock-outline" />
                释放
              </ElButton>
            </ElTooltip>
            <ElButton
              v-else
              size="small"
              type="danger"
              plain
              :disabled="!canRelease(row)"
              @click.stop="openReleaseFromRow(row)"
            >
              <Icon icon="vi-mdi:shield-off-outline" />
              释放
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <!-- ===== 详情 / 释放 Drawer ===== -->
    <ElDrawer
      v-model="drawerVisible"
      :title="currentDetail?.demandKey ?? '需求保护详情'"
      size="720px"
      direction="rtl"
      :close-on-click-modal="false"
      @close="closeDrawer"
    >
      <div v-if="currentDetail" class="dp-drawer">
        <!-- 状态徽：已释放 -->
        <ElAlert
          v-if="currentDetail.status === 'RELEASED'"
          type="success"
          :closable="false"
          show-icon
          class="status-banner"
        >
          <template #title>已释放</template>
          <span class="hint-text">
            释放人 {{ currentDetail.releasedBy ?? '-' }} · 释放时间
            {{
              currentDetail.releasedAt
                ? dayjs(currentDetail.releasedAt).format('YYYY-MM-DD HH:mm')
                : '-'
            }}
          </span>
          <div v-if="currentDetail.releaseReason" class="release-reason">
            原因：{{ currentDetail.releaseReason }}
          </div>
        </ElAlert>
        <!-- 状态徽：部分释放 -->
        <ElAlert
          v-else-if="currentDetail.status === 'PARTIALLY_RELEASED'"
          type="warning"
          :closable="false"
          show-icon
          class="status-banner"
        >
          <template #title>部分释放</template>
          <span class="hint-text">历史混合态（前端不生产该状态）</span>
        </ElAlert>

        <!-- 严格绑定提示 -->
        <ElAlert
          v-if="currentDetail.lockType === 'STRICT_BINDING'"
          type="warning"
          :closable="false"
          show-icon
          class="status-banner"
        >
          <template #title>严格绑定锁</template>
          <span class="hint-text">不支持手工释放。如需调整，请走重排流程。</span>
        </ElAlert>

        <!-- 基本信息 -->
        <ElDescriptions :column="2" border size="small" title="基本信息">
          <ElDescriptionsItem label="需求类型">{{
            DEMAND_TYPE_LABELS[currentDetail.demandType]
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="锁类型">
            <ElTag :type="LOCK_TYPE_TAG[currentDetail.lockType]" size="small">
              {{ LOCK_TYPE_LABELS[currentDetail.lockType] }}
            </ElTag>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="需求稳定键" :span="2">
            <span class="code-tag">{{ currentDetail.demandKey }}</span>
          </ElDescriptionsItem>
          <ElDescriptionsItem v-if="currentDetail.demandLabel" label="描述" :span="2">{{
            currentDetail.demandLabel
          }}</ElDescriptionsItem>
          <ElDescriptionsItem label="锁条数">{{ currentDetail.lockCount }}</ElDescriptionsItem>
          <ElDescriptionsItem label="锁定量">
            <span v-if="currentDetail.status === 'ACTIVE'" class="qty-active">
              {{ currentDetail.lockedQty }}
            </span>
            <span v-else>{{ currentDetail.lockedQty }}</span>
          </ElDescriptionsItem>
          <ElDescriptionsItem v-if="currentDetail.sourcePlanVersionId" label="来源计划版本">
            {{ currentDetail.sourcePlanVersionId }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="创建">
            {{ currentDetail.createdBy }} ·
            {{ dayjs(currentDetail.createdAt).format('YYYY-MM-DD HH:mm') }}
          </ElDescriptionsItem>
        </ElDescriptions>

        <!-- Lock 明细（只读） -->
        <div class="lock-details">
          <h4 class="section-title">锁明细（{{ currentDetail.lockDetails.length }} 条）</h4>
          <ElTable :data="currentDetail.lockDetails" size="small" border stripe style="width: 100%">
            <ElTableColumn label="锁 ID" prop="lockId" width="100" align="center" />
            <ElTableColumn label="供给类型" width="120" align="center">
              <template #default="{ row }">
                <ElTag size="small">
                  {{ HARD_LOCK_SUPPLY_TYPE_LABELS[row.supplyType as HardLockSupplyType] }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="供给稳定键" min-width="220">
              <template #default="{ row }">
                <span class="code-tag">{{ row.supplyKey }}</span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="锁定量" prop="lockedQty" width="90" align="right" />
            <ElTableColumn label="状态" width="90" align="center">
              <template #default="{ row }">
                <ElTag :type="SUPPLY_STATUS_TAG[row.status]" size="small">
                  {{ HARD_LOCK_STATUS_LABELS[row.status] }}
                </ElTag>
              </template>
            </ElTableColumn>
          </ElTable>
        </div>

        <!-- 释放表单（仅 ACTIVE + DEMAND_PROTECTION + canWrite 显示） -->
        <div v-if="canRelease(currentDetail)" class="release-form">
          <h4 class="section-title">释放操作</h4>
          <ElAlert type="warning" :closable="false" show-icon class="confirm-hint">
            <template #title>整条释放（Full Release）</template>
            <span>
              将释放该需求下全部
              <strong>{{ currentDetail.lockCount }}</strong> 条 Lock（共
              <strong>{{ currentDetail.lockedQty }}</strong>
              数量），释放后对应供给回到可分配池，且不可撤销。
            </span>
          </ElAlert>
          <ElForm
            ref="drawerFormRef"
            :model="{ reason: drawerReason }"
            :rules="drawerRules"
            label-width="90px"
            label-position="right"
            class="dp-form"
          >
            <ElFormItem label="释放原因" prop="reason">
              <ElInput
                v-model="drawerReason"
                type="textarea"
                :rows="3"
                placeholder="请输入释放原因（必填，落审计）"
                maxlength="200"
                show-word-limit
              />
            </ElFormItem>
          </ElForm>
        </div>

        <div class="drawer-actions">
          <ElButton @click="closeDrawer">关闭</ElButton>
          <ElButton
            v-if="canRelease(currentDetail)"
            type="danger"
            :loading="releasing"
            @click="confirmRelease"
          >
            <Icon icon="vi-mdi:shield-off-outline" />
            确认释放
          </ElButton>
        </div>
      </div>
    </ElDrawer>

    <!-- ===== 批量释放 Modal ===== -->
    <ElDialog
      v-model="batchModalVisible"
      title="批量释放需求保护"
      width="560px"
      :close-on-click-modal="false"
      @close="closeBatchModal"
    >
      <div class="batch-modal">
        <ElAlert type="info" :closable="false" show-icon class="batch-summary">
          <template #title>释放范围</template>
          <div class="batch-summary-grid">
            <div class="batch-summary-item">
              <div class="batch-summary-label">需求数</div>
              <div class="batch-summary-value">{{ batchSummary.demandCount }}</div>
            </div>
            <div class="batch-summary-item">
              <div class="batch-summary-label">锁条数</div>
              <div class="batch-summary-value">{{ batchSummary.lockCount }}</div>
            </div>
            <div class="batch-summary-item">
              <div class="batch-summary-label">锁定总量</div>
              <div class="batch-summary-value">{{ batchSummary.lockedQty }}</div>
            </div>
          </div>
        </ElAlert>

        <div class="batch-list">
          <h4 class="section-title">选中明细</h4>
          <ul class="batch-list-ul">
            <li v-for="row in releasableSelection" :key="row.demandKey">
              <span class="code-tag">{{ row.demandKey }}</span>
              <span class="muted small">
                · {{ row.lockCount }} 条锁 · 锁定量 {{ row.lockedQty }}
              </span>
            </li>
          </ul>
        </div>

        <ElForm
          ref="batchFormRef"
          :model="{ reason: batchReason }"
          :rules="batchRules"
          label-width="90px"
          label-position="right"
          class="batch-form"
        >
          <ElFormItem label="共享原因" prop="reason">
            <ElInput
              v-model="batchReason"
              type="textarea"
              :rows="3"
              placeholder="请输入批量释放原因（必填，落审计）"
              maxlength="200"
              show-word-limit
            />
          </ElFormItem>
        </ElForm>
      </div>
      <template #footer>
        <ElButton @click="closeBatchModal">取消</ElButton>
        <ElButton type="danger" :loading="releasing" @click="submitBatch">
          <Icon icon="vi-mdi:shield-off-outline" />
          确认批量释放
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style lang="less" scoped>
.aps-dp {
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

    code {
      padding: 0 4px;
      font-family: 'Courier New', monospace;
      font-size: 12px;
      background: #f1f5f9;
      border-radius: 3px;
    }
  }
}

.kpi-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

.kpi-row-4 {
  grid-template-columns: repeat(4, 1fr);
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

.code-tag {
  padding: 1px 6px;
  font-family: 'Courier New', monospace;
  font-size: 11px;
  background: #f1f5f9;
  border-radius: 3px;
}

.qty-active {
  font-weight: 600;
  color: #d97706;
}

/* 筛选条 */
.filter-row {
  display: flex;
  align-items: flex-end;
  flex-wrap: wrap;
  gap: 14px;
}

.filter-item {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 4px;

  label {
    font-size: 12px;
    font-weight: 600;
    color: #64748b;
  }
}

.filter-actions {
  display: flex;
  margin-left: auto;
  gap: 8px;
}

/* Drawer */
.dp-drawer {
  display: flex;
  padding: 0 4px;
  flex-direction: column;
  gap: 16px;
}

.status-banner {
  margin: 0;

  .release-reason {
    margin-top: 6px;
    font-size: 12px;
    color: #475569;
  }
}

.section-title {
  margin: 0 0 10px;
  font-size: 13px;
  font-weight: 600;
  color: #1e293b;
}

.lock-details {
  /* 让明细表格占满 drawer 宽度 */
}

.release-form {
  .confirm-hint {
    margin-bottom: 12px;
  }
}

.dp-form,
.batch-form {
  :deep(.el-form-item) {
    margin-bottom: 12px;
  }
}

.batch-modal {
  display: flex;
  flex-direction: column;
  gap: 14px;

  .batch-summary {
    .batch-summary-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 12px;
      margin-top: 8px;
    }

    .batch-summary-item {
      padding: 8px 10px;
      text-align: center;
      background: #fff;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
    }

    .batch-summary-label {
      font-size: 11px;
      color: #94a3b8;
    }

    .batch-summary-value {
      margin-top: 2px;
      font-size: 18px;
      font-weight: 600;
      color: #1e293b;
    }
  }

  .batch-list {
    .batch-list-ul {
      max-height: 160px;
      padding: 8px 12px;
      margin: 0;
      overflow-y: auto;
      list-style: none;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 4px;

      li {
        padding: 4px 0;
        font-size: 12px;
      }
    }
  }
}

.drawer-actions {
  display: flex;
  padding: 12px 0;
  border-top: 1px solid #e2e8f0;
  justify-content: flex-end;
  gap: 8px;
}

:deep(.row-clickable) {
  cursor: pointer;
}
</style>
