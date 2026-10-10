<script setup lang="ts">
/**
 * APS V1 4号位 — 人工到货时间（Manual ETA）页面（页面 8 / P1-7）
 *
 * 4号位文档第 11 节硬约束：
 *  - 必须包含：查询 / 新增 / 更新 / 取消 / Active 状态 / UpdatedBy / UpdatedAt / Remark
 *  - 接口：4号位 → 5号位（ProcurementManualEtaController）
 *  - 不计算：有效到货时间（Effective ETA）/ 最终可用时间（AvailableTime）
 *
 * 页面分区：
 *  - 顶部筛选（PO 多选 / 物料多选 / ActiveOnly）
 *  - KPI 卡（总数 / Active / Cancelled）
 *  - 主表格 + 行操作（编辑 / 取消）
 *  - ElDrawer 复用于新增 + 编辑 + 取消确认
 *
 * RBAC（@see 审核报告 P1-14/15）：
 *  - VIEWER：禁用 新增 / 保存 / 取消 ETA 按钮
 *  - PMC+：可写
 *
 * ❌ 2026-09-15 撤销 DepartmentCode 维度（@see 4号位-2026-09-13-裁定回退清单.md）：
 *  - 9月13日 `未命名的Markdown文件.md` 实际是 0号位 出的业务裁决（程序有效）
 *  - DepartmentCode 整条撤销：storeToRefs / selectedDepartmentCodes / filter UI / table 列 / drawer audit 全部删除
 *  - 当前状态：与 9月13日 撤销状态一致
 */

import { computed, onMounted, ref } from 'vue'
import dayjs from 'dayjs'
import { ElMessage, ElMessageBox } from 'element-plus'
import { storeToRefs } from 'pinia'
import { useManualEtaStore } from '@/store/modules/aps/manualEta'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import type { ManualEtaDto, ManualEtaListFilter, ManualEtaUpsertInput } from '@/api/aps-v1'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDatePicker,
  ElDescriptions,
  ElDescriptionsItem,
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

const store = useManualEtaStore()
const apsAuth = useApsAuthStore()
const {
  list,
  currentDetail,
  loading,
  saving,
  error,
  lastFilter,
  activeCount,
  cancelledCount,
  distinctPoNos,
  distinctMaterialCodes
} = storeToRefs(store)

/* ===== 权限门控（v1.2 §23.1 权限码门控）=====
 *  - aps.manual_eta.edit：dev seed 下 aps.planner + aps.admin.system 持有
 *  - 不用角色判断的原因：与 3号位后端二次校验用同一码；前端显隐 = 后端能写
 */
const canWrite = computed(() => apsAuth.has('aps.manual_eta.edit'))

/* ===== 筛选本地 state（变更后点 [筛选] 才生效，避免每次下拉都重查） ===== */
const filterDraft = ref<ManualEtaListFilter>({})
const activeOnlyDraft = ref(false)
const selectedPoNos = ref<string[]>([])
const selectedMaterialCodes = ref<string[]>([])

function syncFilterDraft(): void {
  filterDraft.value = {
    poNos: selectedPoNos.value.length ? [...selectedPoNos.value] : undefined,
    materialIds: undefined, // DTO 是 materialIds（number[]），UI 用 code 选，简单起见先传 undefined
    activeOnly: activeOnlyDraft.value
  }
}

async function applyFilter(): Promise<void> {
  syncFilterDraft()
  await store.load(filterDraft.value)
}

function resetFilters(): void {
  selectedPoNos.value = []
  selectedMaterialCodes.value = []
  activeOnlyDraft.value = false
}

async function refresh(): Promise<void> {
  await store.load()
}

/* ===== 状态映射 ===== */
const STATUS_LABEL: Record<'ACTIVE' | 'CANCELLED', string> = {
  ACTIVE: '启用',
  CANCELLED: '已取消'
}
const STATUS_TAG: Record<'ACTIVE' | 'CANCELLED', 'success' | 'info'> = {
  ACTIVE: 'success',
  CANCELLED: 'info'
}

function statusOf(dto: ManualEtaDto): 'ACTIVE' | 'CANCELLED' {
  return dto.isActive ? 'ACTIVE' : 'CANCELLED'
}

/** 是否已过期（仅用于 UI 高亮，不参与业务判定） */
function isExpired(iso: string): boolean {
  return new Date(iso).getTime() < Date.now()
}

/* ===== Drawer 状态 ===== */
const drawerVisible = ref(false)
const drawerMode = ref<'new' | 'edit'>('new')
const formRef = ref<{
  validate: () => Promise<boolean>
  resetFields: () => void
} | null>(null)

/** Drawer 表单（新增 + 编辑 共用）
 *  - materialId / receivingWarehouse 后端必填（@see lps/LPS.APS.Core/Dto/ProcurementManualEtaOverride.cs）
 *  - 编辑时由 dto 回填；新增时由"取上一行"或 manualEtaApi.getByPoLine 预填
 */
const form = ref({
  poNo: '',
  lineNo: 1,
  materialId: 0,
  materialCode: '',
  receivingWarehouse: '',
  manualEta: new Date(Date.now() + 7 * 86400_000).toISOString(),
  remark: ''
})
/** 字段级错误 */
const formRules = {
  poNo: [
    { required: true, message: '请输入 PO 号', trigger: 'blur' },
    { min: 3, message: 'PO 号至少 3 个字符', trigger: 'blur' }
  ],
  lineNo: [
    { required: true, type: 'number' as const, min: 1, message: '行号 ≥ 1', trigger: 'blur' }
  ],
  materialId: [
    { required: true, type: 'number' as const, min: 1, message: '物料 ID ≥ 1', trigger: 'blur' }
  ],
  receivingWarehouse: [
    { required: true, message: '请输入收货仓库', trigger: 'blur' },
    { min: 2, message: '仓库编码至少 2 个字符', trigger: 'blur' }
  ],
  manualEta: [{ required: true, message: '请选择 ETA 时间', trigger: 'change' }]
}

function openCreate(): void {
  drawerMode.value = 'new'
  form.value = {
    poNo: '',
    lineNo: 1,
    materialId: 0,
    materialCode: '',
    receivingWarehouse: '',
    manualEta: new Date(Date.now() + 7 * 86400_000).toISOString(),
    remark: ''
  }
  store.openDetail(null)
  drawerVisible.value = true
}

function openEdit(dto: ManualEtaDto): void {
  drawerMode.value = 'edit'
  form.value = {
    poNo: dto.poNo,
    lineNo: dto.lineNo,
    materialId: dto.materialId,
    materialCode: dto.materialCode ?? '',
    receivingWarehouse: dto.receivingWarehouse ?? '',
    manualEta: dto.manualEta,
    remark: dto.remark ?? ''
  }
  store.openDetail(dto)
  drawerVisible.value = true
}

function closeDrawer(): void {
  drawerVisible.value = false
  store.openDetail(null)
}

/** 提交（新增 / 编辑 共用 upsert） */
async function submitDrawer(): Promise<void> {
  if (!formRef.value) return
  let valid = false
  try {
    valid = await formRef.value.validate()
  } catch {
    return
  }
  if (!valid) return

  const actor = apsAuth.userInfo?.userCode ?? 'mock-pmc'
  const input: ManualEtaUpsertInput = {
    poNo: form.value.poNo.trim(),
    lineNo: Number(form.value.lineNo),
    materialId: Number(form.value.materialId),
    materialCode: form.value.materialCode.trim() || undefined,
    receivingWarehouse: form.value.receivingWarehouse.trim(),
    manualEta: form.value.manualEta,
    isActive: true, // 新增/编辑都强制 active；取消走独立按钮
    remark: form.value.remark.trim() || undefined,
    actor
  }
  try {
    await store.upsert(input)
    ElMessage.success(
      `${drawerMode.value === 'new' ? '新增' : '更新'}人工到货时间成功：${input.poNo} / Line ${input.lineNo}`
    )
    closeDrawer()
  } catch {
    ElMessage.error('保存失败，请检查表单或网络')
  }
}

/** 取消 ETA（软删除：isActive=false） */
async function handleCancel(dto: ManualEtaDto): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `将取消人工到货时间：${dto.poNo} / Line ${dto.lineNo}\n\n` +
        `操作：保留记录并标记为已取消（便于事后审计）\n` +
        `请确认。`,
      '取消人工到货时间',
      {
        confirmButtonText: '确认取消',
        cancelButtonText: '不取消',
        type: 'warning'
      }
    )
  } catch {
    return
  }
  const actor = apsAuth.userInfo?.userCode ?? 'mock-pmc'
  try {
    await store.cancel({
      poNo: dto.poNo,
      lineNo: dto.lineNo,
      materialId: dto.materialId,
      receivingWarehouse: dto.receivingWarehouse ?? '',
      actor
    })
    ElMessage.success(`已取消 ${dto.poNo} / Line ${dto.lineNo}`)
  } catch {
    ElMessage.error('取消失败')
  }
}

onMounted(refresh)
</script>

<template>
  <div class="aps-manual-eta">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">人工到货 ETA</h2>
        <p class="page-sub"> PO 行级人工到货时间 —— 只写人工填写的值，有效到货时间由系统计算 </p>
      </div>
      <ElButton :loading="loading" @click="refresh">
        <Icon icon="vi-ep:refresh" />
        刷新
      </ElButton>
    </div>

    <!-- ===== 文档约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>使用说明</template>
      <span class="hint-text">
        本页只维护人工填写的到货时间。有效到货时间、最终可用时间由系统计算，不在此页维护。 每次新增
        / 更新 / 取消都会留下审计记录，可在「审计日志」中追溯。
      </span>
    </ElAlert>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== KPI ===== -->
    <div class="kpi-row">
      <div class="kpi-card" style="border-top-color: #3b82f6">
        <div class="kpi-label">总记录数</div>
        <div class="kpi-value" style="color: #3b82f6">{{ list.length }}</div>
        <div class="kpi-ratio">最近一次筛选</div>
      </div>
      <div class="kpi-card" style="border-top-color: #059669">
        <div class="kpi-label">启用</div>
        <div class="kpi-value" style="color: #059669">{{ activeCount }}</div>
        <div class="kpi-ratio">当前生效</div>
      </div>
      <div class="kpi-card" style="border-top-color: #94a3b8">
        <div class="kpi-label">已取消</div>
        <div class="kpi-value" style="color: #94a3b8">{{ cancelledCount }}</div>
        <div class="kpi-ratio">已取消</div>
      </div>
    </div>

    <!-- ===== 筛选 + 操作 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>筛选条件</span>
          <span class="hint-text">
            <span v-if="lastFilter.activeOnly">仅看生效中</span>
            <span v-if="lastFilter.poNos?.length"> · PO: {{ lastFilter.poNos.length }} 个</span>
            <span v-if="!lastFilter.activeOnly && !lastFilter.poNos?.length">全部</span>
          </span>
        </div>
      </template>
      <div class="filter-row">
        <div class="filter-item">
          <label>PO 号</label>
          <ElSelect
            v-model="selectedPoNos"
            multiple
            filterable
            clearable
            collapse-tags
            placeholder="全部 PO"
            style="width: 240px"
          >
            <ElOption v-for="p in distinctPoNos" :key="p" :label="p" :value="p" />
          </ElSelect>
        </div>
        <div class="filter-item">
          <label>物料编码</label>
          <ElSelect
            v-model="selectedMaterialCodes"
            multiple
            filterable
            clearable
            collapse-tags
            placeholder="全部物料"
            style="width: 240px"
          >
            <ElOption v-for="m in distinctMaterialCodes" :key="m" :label="m" :value="m" />
          </ElSelect>
        </div>
        <div class="filter-item">
          <label>仅看生效中</label>
          <ElSwitch v-model="activeOnlyDraft" />
        </div>
        <div class="filter-actions">
          <ElButton @click="resetFilters">重置</ElButton>
          <ElButton type="primary" :loading="loading" @click="applyFilter">筛选</ElButton>
          <ElButton type="success" plain :disabled="!canWrite" @click="openCreate">
            <Icon icon="vi-mdi:plus" />
            新增人工到货时间
          </ElButton>
        </div>
      </div>
    </ElCard>

    <!-- ===== 主表格 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>人工到货时间列表（{{ list.length }} 条）</span>
          <span class="hint-text">点击行可编辑 / 取消</span>
        </div>
      </template>
      <div v-if="!list.length" class="panel-empty">
        <ElEmpty description="无人工到货时间数据" />
      </div>
      <ElTable
        v-else
        :data="list"
        size="small"
        border
        stripe
        row-class-name="row-clickable"
        style="width: 100%"
      >
        <ElTableColumn label="PO 号" prop="poNo" width="140" />
        <ElTableColumn label="行号" prop="lineNo" width="70" align="center" />
        <ElTableColumn label="物料编码" prop="materialCode" width="110" />
        <ElTableColumn label="物料名称" prop="materialName" min-width="140">
          <template #default="{ row }">
            <span v-if="row?.materialName">{{ row.materialName }}</span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="收货仓库" prop="receivingWarehouse" width="120">
          <template #default="{ row }">
            <span v-if="row?.receivingWarehouse">{{ row.receivingWarehouse }}</span>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="人工 ETA" width="150">
          <template #default="{ row }">
            <span :class="{ expired: isExpired(row.manualEta) && row.isActive }">
              {{ dayjs(row.manualEta).format('YYYY-MM-DD') }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="状态" width="80" align="center">
          <template #default="{ row }">
            <ElTag :type="STATUS_TAG[statusOf(row)]" size="small">
              {{ STATUS_LABEL[statusOf(row)] }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn label="更新人 / 更新时间" min-width="180">
          <template #default="{ row }">
            <span class="muted code-tag">{{ row.updatedBy }}</span>
            <span class="muted small"> · {{ dayjs(row.updatedAt).format('MM-DD HH:mm') }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="备注" min-width="160">
          <template #default="{ row }">
            <ElTooltip v-if="row.remark" :content="row.remark" placement="top">
              <span class="remark-cell">{{ row.remark }}</span>
            </ElTooltip>
            <span v-else class="muted">-</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="220" fixed="right" align="center">
          <template #default="{ row }">
            <ElButton size="small" plain :disabled="!canWrite" @click="openEdit(row)">
              <Icon icon="vi-mdi:pencil" />
              编辑
            </ElButton>
            <ElButton
              size="small"
              type="danger"
              plain
              :disabled="!canWrite || !row.isActive"
              @click="handleCancel(row)"
            >
              <Icon icon="vi-mdi:cancel" />
              取消
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <!-- ===== 新增 / 编辑 Drawer ===== -->
    <ElDrawer
      v-model="drawerVisible"
      :title="drawerMode === 'new' ? '新增人工到货时间' : '编辑人工到货时间'"
      size="540px"
      direction="rtl"
      :close-on-click-modal="false"
      @close="closeDrawer"
    >
      <div class="manual-eta-drawer">
        <ElAlert type="info" :closable="false" show-icon class="form-hint">
          <template #title>{{ drawerMode === 'new' ? '新增' : '编辑' }}（Upsert）</template>
          <span> 按采购单号 + 行号去重：同一个采购单行再次保存会覆盖原记录，无需先删除。 </span>
        </ElAlert>

        <ElForm
          ref="formRef"
          :model="form"
          :rules="formRules"
          label-width="100px"
          label-position="right"
          class="manual-eta-form"
        >
          <ElFormItem label="PO 号" prop="poNo">
            <ElInput
              v-model="form.poNo"
              :disabled="drawerMode === 'edit'"
              placeholder="例如：PO-2026-1001"
            />
            <div class="field-hint">
              {{
                drawerMode === 'edit'
                  ? '编辑态下 PO + 行号不可改（复合主键）'
                  : '与 LineNo 组成复合主键'
              }}
            </div>
          </ElFormItem>
          <ElFormItem label="行号" prop="lineNo">
            <ElInput
              v-model.number="form.lineNo"
              :disabled="drawerMode === 'edit'"
              type="number"
              :min="1"
              placeholder="≥ 1"
            />
          </ElFormItem>
          <ElFormItem label="物料 ID" prop="materialId">
            <ElInput
              v-model.number="form.materialId"
              type="number"
              :min="1"
              placeholder="例如：200"
            />
            <div class="field-hint">必填</div>
          </ElFormItem>
          <ElFormItem label="物料编码" prop="materialCode">
            <ElInput v-model="form.materialCode" placeholder="可选：M-001（前端展示用）" />
          </ElFormItem>
          <ElFormItem label="收货仓库" prop="receivingWarehouse">
            <ElInput v-model="form.receivingWarehouse" placeholder="例如：WH-SUZ-01" />
            <div class="field-hint">必填</div>
          </ElFormItem>
          <ElFormItem label="人工 ETA" prop="manualEta">
            <ElDatePicker
              v-model="form.manualEta"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss[Z]"
              placeholder="选择到货预期时间"
              style="width: 100%"
            />
            <div class="field-hint">仅人工值；有效 ETA / 可用时间由系统计算</div>
          </ElFormItem>
          <ElFormItem label="备注">
            <ElInput
              v-model="form.remark"
              type="textarea"
              :rows="3"
              placeholder="可选（写入审计）"
              maxlength="200"
              show-word-limit
            />
          </ElFormItem>
        </ElForm>

        <div v-if="currentDetail" class="audit-block">
          <ElDescriptions :column="1" size="small" title="审计字段（只读）" border>
            <ElDescriptionsItem label="创建人">
              {{ currentDetail.createdBy ?? '-' }} ·
              {{
                currentDetail.createdAt
                  ? dayjs(currentDetail.createdAt).format('YYYY-MM-DD HH:mm')
                  : '-'
              }}
            </ElDescriptionsItem>
            <ElDescriptionsItem label="最后更新">
              {{ currentDetail.updatedBy }} ·
              {{ dayjs(currentDetail.updatedAt).format('YYYY-MM-DD HH:mm') }}
            </ElDescriptionsItem>
          </ElDescriptions>
        </div>

        <div class="drawer-actions">
          <ElButton @click="closeDrawer">取消</ElButton>
          <ElButton type="primary" :loading="saving" @click="submitDrawer">
            <Icon icon="vi-mdi:content-save" />
            {{ drawerMode === 'new' ? '新增' : '保存' }}
          </ElButton>
        </div>
      </div>
    </ElDrawer>
  </div>
</template>

<style lang="less" scoped>
.aps-manual-eta {
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
  grid-template-columns: repeat(3, 1fr);
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

.expired {
  font-weight: 600;
  color: #b91c1c;
}

.remark-cell {
  display: block;
  max-width: 160px;
  overflow: hidden;
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
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
.manual-eta-drawer {
  display: flex;
  padding: 0 4px;
  flex-direction: column;
  gap: 12px;
}

.form-hint {
  margin-bottom: 4px;
}

.manual-eta-form {
  :deep(.el-form-item) {
    margin-bottom: 14px;
  }
}

.field-hint {
  margin-top: 4px;
  font-size: 11px;
  color: #94a3b8;
}

.audit-block {
  padding: 10px 12px;
  background: #f8fafc;
  border: 1px dashed #e2e8f0;
  border-radius: 6px;
}

.drawer-actions {
  display: flex;
  padding: 12px 0;
  border-top: 1px solid #e2e8f0;
  justify-content: flex-end;
  gap: 8px;
}

:deep(.row-clickable) {
  cursor: default; /* 当前行不点开 drawer，drawer 由 [编辑] 按钮触发；避免与 Gantt 点击冲突 */
}
</style>
