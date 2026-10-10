<!--
  APS V1 4号位 — Setup 未维护产品转换查询（v1.5 Setup 专项 §5，页面 2.3）

  职责：统计 Run 中所有未命中 EXACT / DEFAULT 规则的产品转换对（0 分钟兜底）
  用途：
   - 排查 0 分钟兜底是否合理
   - 指导 EXACT / DEFAULT 规则补全
   - 评估 Setup 规则覆盖率

  输入字段（§5.2）：RunId（必填）+ DepartmentCode/StageCode/ResourceCode（可选筛选）
  输出列表（§5.3）：8 字段（Department/Stage/Operation/Resource/FromMaterial/ToMaterial/Count/LastOccurrence）
  出口操作（§5.4）：去补 EXACT / 去补 DEFAULT / 导出 CSV
  红线（§5.5）：不在前端聚合（调后端聚合端点）/ 不展示 Run 外的统计

  权限：aps.setup.view（只读页面，无写操作）

  架构：复用 StrategyProfiles.vue 五段式（加载 / 过滤 / 表格 / 出口操作 / 红线注释）
-->

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { storeToRefs } from 'pinia'
import dayjs from 'dayjs'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInputNumber,
  ElMessage,
  ElOption,
  ElRow,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'
import { useSetupUncoveredStore } from '@/store/modules/aps/setupUncovered'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import type { SetupUncoveredQuery } from '@/api/aps-v1'
import { roleLabelOf, SETUP_SOURCES, SETUP_SOURCE_META } from '@/api/aps-v1'
import SetupRedlineAlert from './components/SetupRedlineAlert.vue'

defineOptions({ name: 'ApsSetupUncovered' })

const router = useRouter()
const uncoveredStore = useSetupUncoveredStore()
const apsAuth = useApsAuthStore()

const { stats, listLoading, error, statCount } = storeToRefs(uncoveredStore)

// ===== 查询表单 =====
const queryForm = reactive<SetupUncoveredQuery>({
  runId: 1, // 默认 RunId=1（dev 演示用）
  departmentCode: undefined,
  stageCode: undefined,
  resourceCode: undefined
})

// ===== 下拉源（mock 数据，等 3号位 API 落地后切真实端点） =====
const departmentOptions = ref([
  { label: 'DEPT_A（注塑车间）', value: 'DEPT_A' },
  { label: 'DEPT_B（装配车间）', value: 'DEPT_B' }
])

const stageOptions = ref([
  { label: 'STAGE_1（注塑工艺）', value: 'STAGE_1' },
  { label: 'STAGE_2（装配工艺）', value: 'STAGE_2' }
])

const resourceOptions = ref([
  { label: 'MC001（设备 001）', value: 'MC001' },
  { label: 'MC002（设备 002）', value: 'MC002' },
  { label: 'MC003（设备 003）', value: 'MC003' }
])

// ===== 加载 =====
async function loadStats() {
  if (!queryForm.runId) {
    ElMessage.warning('请填写 RunId（必填）')
    return
  }
  await uncoveredStore.loadStats({ ...queryForm })
}

onMounted(() => {
  // 默认加载一次（dev 演示用）
  void loadStats()
})

// ===== 出口操作（§5.4） =====

/** 去补 EXACT → 跳转页面 2.1，预填 4 字段 */
function goToExact(row: (typeof stats.value)[number]) {
  void router.push({
    path: '/aps/setup/exact',
    query: {
      departmentCode: row.departmentCode,
      stageCode: row.stageCode,
      operationCode: row.operationCode,
      resourceCode: row.resourceCode,
      fromMaterialCode: row.fromMaterialCode,
      toMaterialCode: row.toMaterialCode
    }
  })
}

/** 去补 DEFAULT → 跳转页面 2.2，预填 3 字段 */
function goToDefault(row: (typeof stats.value)[number]) {
  void router.push({
    path: '/aps/setup/default',
    query: {
      departmentCode: row.departmentCode,
      stageCode: row.stageCode,
      operationCode: row.operationCode,
      resourceCode: row.resourceCode
    }
  })
}

/** 导出 CSV */
function handleExportCsv() {
  const csv = uncoveredStore.exportCsv()
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' })
  const link = document.createElement('a')
  link.href = URL.createObjectURL(blob)
  link.download = `setup-uncovered-${dayjs().format('YYYYMMDD-HHmmss')}.csv`
  link.click()
  URL.revokeObjectURL(link.href)
  ElMessage.success(`已导出 ${statCount.value} 条记录`)
}

// ===== 格式化 =====
function formatDateTime(iso?: string) {
  return iso ? dayjs(iso).format('YYYY-MM-DD HH:mm:ss') : '—'
}
</script>

<template>
  <div class="setup-uncovered-page">
    <!-- 页面头部 -->
    <div class="page-header">
      <div class="header-left">
        <h2>未维护换型产品转换查询</h2>
        <p class="subtitle">查询未维护换型规则的产品转换（按 0 分钟兜底）</p>
      </div>
      <div class="header-right">
        <ElTooltip
          :content="`当前操作者：${apsAuth.userInfo?.userCode ?? '—'}（${apsAuth.roles.map(roleLabelOf).join('、')}）`"
        >
          <ElTag type="info" size="small">{{ apsAuth.userInfo?.userCode ?? '—' }}</ElTag>
        </ElTooltip>
      </div>
    </div>

    <!-- 红线 Alert（§5.5） -->
    <SetupRedlineAlert page-type="uncovered" />

    <!-- 错误提示 -->
    <ElAlert
      v-if="error"
      type="error"
      :title="error"
      :closable="false"
      show-icon
      class="error-alert"
    />

    <!-- 查询表单（§5.2） -->
    <ElCard class="query-card" shadow="never">
      <ElForm :model="queryForm" inline>
        <ElFormItem label="排产批次" required>
          <ElInputNumber
            v-model="queryForm.runId"
            :min="1"
            :step="1"
            controls-position="right"
            style="width: 120px"
          />
        </ElFormItem>
        <ElFormItem label="部门">
          <ElSelect
            v-model="queryForm.departmentCode"
            placeholder="全部"
            clearable
            style="width: 180px"
          >
            <ElOption
              v-for="opt in departmentOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="大工艺">
          <ElSelect v-model="queryForm.stageCode" placeholder="全部" clearable style="width: 180px">
            <ElOption
              v-for="opt in stageOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="设备">
          <ElSelect
            v-model="queryForm.resourceCode"
            placeholder="全部"
            clearable
            style="width: 180px"
          >
            <ElOption
              v-for="opt in resourceOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem>
          <ElButton type="primary" :loading="listLoading" @click="loadStats">
            <Icon icon="vi-ep:search" class="mr-4" />
            查询
          </ElButton>
          <ElButton @click="uncoveredStore.reset()">重置</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <!-- KPI 条 -->
    <ElRow :gutter="16" class="kpi-row">
      <ElCol :span="6">
        <ElCard shadow="never" class="kpi-card">
          <div class="kpi-value">{{ statCount }}</div>
          <div class="kpi-label">未覆盖转换对</div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard shadow="never" class="kpi-card">
          <div class="kpi-value">{{ Object.keys(uncoveredStore.statsByDepartment).length }}</div>
          <div class="kpi-label">涉及部门</div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard shadow="never" class="kpi-card">
          <div class="kpi-value">{{ Object.keys(uncoveredStore.statsByStage).length }}</div>
          <div class="kpi-label">涉及工艺</div>
        </ElCard>
      </ElCol>
      <ElCol :span="6">
        <ElCard shadow="never" class="kpi-card">
          <div class="kpi-value danger">0 分钟</div>
          <div class="kpi-label">兜底换型时间</div>
        </ElCard>
      </ElCol>
    </ElRow>

    <!-- 换型来源 5 值说明（v1.5 §9/§10；标签取自 SETUP_SOURCE_META，与 Gantt.vue Drawer、总览页一致） -->
    <ElAlert type="info" :closable="false" show-icon class="legend-alert">
      <template #title>
        换型来源五值说明（本页统计仅含「无规则兜底」：有前后产品但无规则 → 0 分钟兜底；
        「冷启动首单」：班头首单 / 冷启动，属正常生产事实，<strong>不进</strong>未覆盖统计）
      </template>
      <div class="legend-row">
        <ElTag
          v-for="s in SETUP_SOURCES"
          :key="s"
          size="small"
          :type="SETUP_SOURCE_META[s].tag"
          effect="dark"
        >
          {{ SETUP_SOURCE_META[s].label }}
        </ElTag>
      </div>
    </ElAlert>

    <!-- 统计列表（§5.3） -->
    <ElCard class="table-card" shadow="never">
      <template #header>
        <div class="card-header">
          <span>未维护换型产品转换列表</span>
          <ElButton
            type="success"
            size="small"
            :disabled="statCount === 0"
            @click="handleExportCsv"
          >
            <Icon icon="vi-ep:download" class="mr-4" />
            导出 CSV
          </ElButton>
        </div>
      </template>

      <ElTable
        v-loading="listLoading"
        :data="stats"
        border
        stripe
        empty-text="无未覆盖的产品转换对（所有转换均已命中 EXACT 或 DEFAULT 规则）"
      >
        <ElTableColumn type="index" label="#" width="50" />
        <ElTableColumn prop="departmentCode" label="部门" width="120" />
        <ElTableColumn prop="stageCode" label="大工艺" width="120" />
        <ElTableColumn prop="operationCode" label="当前工序" width="120" />
        <ElTableColumn prop="resourceCode" label="设备" width="120" />
        <ElTableColumn prop="fromMaterialCode" label="前产品" width="140" />
        <ElTableColumn prop="toMaterialCode" label="后产品" width="140" />
        <ElTableColumn prop="count" label="出现次数" width="100" align="right">
          <template #default="{ row }">
            <ElTag
              :type="row.count > 10 ? 'danger' : row.count > 5 ? 'warning' : 'info'"
              size="small"
            >
              {{ row.count }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="lastOccurrence" label="最近出现" width="180">
          <template #default="{ row }">
            {{ formatDateTime(row.lastOccurrence) }}
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <ElButton type="primary" size="small" link @click="goToExact(row)">
              去补 EXACT
            </ElButton>
            <ElButton type="warning" size="small" link @click="goToDefault(row)">
              去补 DEFAULT
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>

      <ElEmpty v-if="!listLoading && statCount === 0" description="无未覆盖的产品转换对" />
    </ElCard>
  </div>
</template>

<style scoped>
.setup-uncovered-page {
  padding: 16px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
}

.header-left h2 {
  margin: 0 0 4px 0;
  font-size: 20px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.header-left .subtitle {
  margin: 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.error-alert {
  margin-bottom: 16px;
}

.query-card {
  margin-bottom: 16px;
}

.kpi-row {
  margin-bottom: 16px;
}

.kpi-card {
  text-align: center;
}

.kpi-value {
  font-size: 28px;
  font-weight: 700;
  color: var(--el-color-primary);
  margin-bottom: 4px;
}

.kpi-value.danger {
  color: var(--el-color-danger);
}

.kpi-label {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.table-card {
  margin-bottom: 16px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.mr-4 {
  margin-right: 4px;
}
</style>
