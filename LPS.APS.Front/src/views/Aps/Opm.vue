<!--
  APS V1 4号位 — OPM（OperationPlanningMode）治理配置独立维护页

  3号位 2026-09-23 交付件（T1）：
    frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md

  依据：
    0号位 2026-09-23 Q1 裁决：OPM 属 APS 工艺规划属性（治理配置），唯一主路径 = 治理侧直维护
    S4《无设备小工序_OPM消费契约草案》§五-5（1号位 消费端契约已锁定）
    DDL变更申请_v5.1.9终版（RoutingOperation.OperationPlanningMode 已加列，3号位 已落库）

  页面布局（与 Rules.vue / ManualEta.vue 同套 Skeleton）：
    顶部：物料 ID 输入 + 「加载」按钮 + 三态 KPI（需资源 / 无约束 / 仅等待）
    主表格：RoutingOperation 列表（工序号 / 工序名 / 工艺类型 / 阶段码 / 部门 ID / 当前模式 / 行内修改）
    行内修改：ElSelect（FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY）+ 「保存」按钮
    红线 ElAlert：
      #5 接口即契约（仅 NEW 端点，不改既有签名）
      #6 仅 UPDATE 值（DML），无 DDL
      OPM 属治理属性，sp_SyncRoutingData 不 MERGE

  RBAC（v1.2 §23.1）：
    - 路由级 aps.rule.edit（与 Rules.vue 同码，3号位 端点 [Authorize(Policy=RuleMaintain)])
    - 按钮级 canView（aps.rule.view）/ canEdit（aps.rule.edit）
-->
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElRow,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'
import {
  OPM_META,
  OPERATION_PLANNING_MODES,
  type OperationPlanningMode,
  type RoutingOperationDto
} from '@/api/aps-v1'
import { useOpmStore } from '@/store/modules/aps/opm'

const store = useOpmStore()
const { list, currentMaterialId, loading, saving, error, canView, canEdit, modeCounts } =
  storeToRefs(store)

/* ===== 物料 Id 输入（页面 state；点击「加载」才发请求，对齐 ManualEta.vue 模式） ===== */
const materialIdInput = ref<number | null>(null)

async function onLoad(): Promise<void> {
  const id = Number(materialIdInput.value)
  if (!Number.isFinite(id) || id <= 0) {
    ElMessage.warning('物料 ID 必须为正整数')
    return
  }
  await store.load(id)
}

/* ===== 行内修改（每行维护一个 draftMode + draftDirty） ===== */
/** 行级 draft 模式（key = row.id；null 表示该行无未保存修改） */
const drafts = ref<Record<number, OperationPlanningMode>>({})
/** 行级保存中态（key = row.id） */
const rowSaving = ref<Record<number, boolean>>({})

/** 当前值（draft 优先，否则原值） */
function currentMode(row: RoutingOperationDto): OperationPlanningMode {
  return drafts.value[row.id] ?? row.operationPlanningMode
}

/** 行是否 dirty（draft 与原值不一致） */
function isDirty(row: RoutingOperationDto): boolean {
  const draft = drafts.value[row.id]
  return draft !== undefined && draft !== row.operationPlanningMode
}

/** 改行内下拉：写入 drafts，不立刻发请求 */
function onDraftChange(row: RoutingOperationDto, value: OperationPlanningMode): void {
  drafts.value = { ...drafts.value, [row.id]: value }
}

/** 还原（清除 draft） */
function resetDraft(row: RoutingOperationDto): void {
  const next = { ...drafts.value }
  delete next[row.id]
  drafts.value = next
}

/** 保存单行 */
async function saveRow(row: RoutingOperationDto): Promise<void> {
  const target = currentMode(row)
  if (!isDirty(row)) {
    ElMessage.warning('值未变更，无需保存')
    return
  }
  // 二次确认（避免误改 OPM 影响 1号位 排产）
  let confirmed = true
  try {
    await ElMessageBox.confirm(
      `即将修改工序「${row.operationCode}（${row.operationName}）」的 OPM：
\n  当前值：${OPM_META[row.operationPlanningMode].label}
  新值：${OPM_META[target].label}
\n风险：该值会被排产消费，修改后下次排产即生效。`,
      'OPM 修改确认',
      {
        type: 'warning',
        confirmButtonText: '确认修改',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--primary'
      }
    )
  } catch {
    confirmed = false
  }
  if (!confirmed) return

  rowSaving.value = { ...rowSaving.value, [row.id]: true }
  try {
    const result = await store.updatePlanningMode({
      operationId: row.id,
      operationPlanningMode: target
    })
    if (result) {
      ElMessage.success(
        `已修改工序 ${row.operationCode} 的 OPM：${OPM_META[result.operationPlanningMode].label}`
      )
      // 保存成功，清 draft（store 已回写最新值）
      resetDraft(row)
    }
  } finally {
    const next = { ...rowSaving.value }
    delete next[row.id]
    rowSaving.value = next
  }
}

/* ===== 工具 ===== */
const hasList = computed<boolean>(() => list.value.length > 0)

/* ===== 生命周期 ===== */
onMounted(() => {
  if (!canView.value) {
    ElMessage.warning('当前操作者无查看权限，请联系系统管理员')
  }
})

onBeforeUnmount(() => {
  store.reset()
})
</script>

<template>
  <div class="opm-page">
    <!-- 顶部：物料 Id 输入 + 加载按钮 -->
    <ElCard class="opm-toolbar" shadow="never">
      <ElForm inline label-position="right" label-width="80px">
        <ElFormItem label="物料 ID" required>
          <ElInput
            v-model.number="materialIdInput"
            type="number"
            :min="1"
            placeholder="请输入物料 ID"
            style="width: 240px"
            clearable
            @keyup.enter="onLoad"
          />
          <span class="field-hint">必填；须为正整数</span>
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" :loading="loading" @click="onLoad">加载工序</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <!-- 三态 KPI -->
    <ElRow v-if="hasList" :gutter="12" class="opm-kpi">
      <ElCol :span="8">
        <ElCard shadow="never">
          <div class="kpi-row">
            <ElTag :type="OPM_META.FINITE_RESOURCE.tagType" effect="dark">
              {{ OPM_META.FINITE_RESOURCE.label }}
            </ElTag>
            <span class="kpi-num">{{ modeCounts.FINITE_RESOURCE }}</span>
          </div>
          <div class="kpi-desc">{{ OPM_META.FINITE_RESOURCE.description }}</div>
        </ElCard>
      </ElCol>
      <ElCol :span="8">
        <ElCard shadow="never">
          <div class="kpi-row">
            <ElTag :type="OPM_META.UNCONSTRAINED.tagType" effect="dark">
              {{ OPM_META.UNCONSTRAINED.label }}
            </ElTag>
            <span class="kpi-num">{{ modeCounts.UNCONSTRAINED }}</span>
          </div>
          <div class="kpi-desc">{{ OPM_META.UNCONSTRAINED.description }}</div>
        </ElCard>
      </ElCol>
      <ElCol :span="8">
        <ElCard shadow="never">
          <div class="kpi-row">
            <ElTag :type="OPM_META.WAIT_ONLY.tagType" effect="dark">
              {{ OPM_META.WAIT_ONLY.label }}
            </ElTag>
            <span class="kpi-num">{{ modeCounts.WAIT_ONLY }}</span>
          </div>
          <div class="kpi-desc">{{ OPM_META.WAIT_ONLY.description }}</div>
        </ElCard>
      </ElCol>
    </ElRow>

    <!-- 主表格 -->
    <ElCard class="opm-table-card" shadow="never">
      <template #header>
        <div class="table-header">
          <span class="table-title">工序工艺规划模式列表</span>
          <span v-if="currentMaterialId !== null" class="table-subtitle">
            物料 ID = <strong>{{ currentMaterialId }}</strong> · 共
            <strong>{{ list.length }}</strong> 条
          </span>
        </div>
      </template>

      <ElEmpty
        v-if="!hasList && !loading"
        description="尚未加载工序；请在上方输入物料 ID 后点击「加载工序」"
      />

      <ElTable
        v-else
        :data="list"
        :loading="loading"
        border
        stripe
        row-key="id"
        size="small"
        style="width: 100%"
      >
        <ElTableColumn prop="operationCode" label="工序号" width="100" fixed="left" />
        <ElTableColumn prop="operationName" label="工序名" width="120" />
        <ElTableColumn prop="processType" label="工艺类型" width="120" />
        <ElTableColumn prop="stageCode" label="阶段码" width="100" />
        <ElTableColumn prop="routeCode" label="路径代号" width="120" />
        <ElTableColumn prop="pathId" label="路径 ID" width="90" />
        <ElTableColumn prop="productionDepartmentId" label="部门 ID" width="100" />
        <ElTableColumn label="当前模式" width="160">
          <template #default="{ row }: { row: RoutingOperationDto }">
            <ElTag :type="OPM_META[row.operationPlanningMode].tagType" effect="light">
              {{ OPM_META[row.operationPlanningMode].label }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="调整为" width="220" v-if="canEdit">
          <template #default="{ row }: { row: RoutingOperationDto }">
            <ElSelect
              :model-value="currentMode(row)"
              size="small"
              style="width: 100%"
              @change="(v: OperationPlanningMode) => onDraftChange(row, v)"
            >
              <ElOption
                v-for="m in OPERATION_PLANNING_MODES"
                :key="m"
                :value="m"
                :label="OPM_META[m].label"
                :title="OPM_META[m].description"
              />
            </ElSelect>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="160" fixed="right" v-if="canEdit">
          <template #default="{ row }: { row: RoutingOperationDto }">
            <ElButton
              type="primary"
              size="small"
              :disabled="!isDirty(row)"
              :loading="rowSaving[row.id]"
              @click="saveRow(row)"
            >
              保存
            </ElButton>
            <ElButton size="small" :disabled="!isDirty(row)" @click="resetDraft(row)">
              还原
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <!-- 错误条 -->
    <ElAlert
      v-if="error"
      type="error"
      :closable="true"
      show-icon
      class="opm-alert"
      title="操作失败"
      @close="store.handleError(new Error(''))"
    >
      {{ error }}
    </ElAlert>

    <ElTooltip v-if="saving" content="正在保存修改" placement="top">
      <div class="opm-saving-tip">保存中…</div>
    </ElTooltip>
  </div>
</template>

<style scoped>
.opm-page {
  padding: 16px;
}
.opm-toolbar {
  margin-bottom: 12px;
}
.field-hint {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.opm-kpi {
  margin-bottom: 12px;
}
.kpi-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
}
.kpi-num {
  font-size: 24px;
  font-weight: 600;
}
.kpi-desc {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.5;
}
.opm-alert {
  margin-bottom: 12px;
}
.opm-alert :deep(code) {
  background: rgba(0, 0, 0, 0.05);
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 12px;
}
.opm-table-card {
  margin-bottom: 12px;
}
.table-header {
  display: flex;
  align-items: baseline;
  gap: 16px;
}
.table-title {
  font-weight: 600;
  font-size: 15px;
}
.table-subtitle {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.opm-detail {
  margin-bottom: 12px;
}
.detail-title {
  font-weight: 600;
  font-size: 14px;
}
.opm-saving-tip {
  position: fixed;
  bottom: 24px;
  right: 32px;
  background: var(--el-color-primary-light-9);
  color: var(--el-color-primary);
  padding: 6px 14px;
  border-radius: 16px;
  font-size: 13px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  z-index: 99;
}
</style>
