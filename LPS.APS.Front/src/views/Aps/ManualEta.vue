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

import { computed, onMounted, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { ElMessage, ElMessageBox } from 'element-plus'
import { storeToRefs } from 'pinia'
import { useManualEtaStore } from '@/store/modules/aps/manualEta'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { materialLookupApi } from '@/api/aps-v1/materialLookup'
import { manualEtaApi } from '@/api/aps-v1/manualEta'
import type {
  ManualEtaDto,
  ManualEtaListFilter,
  ManualEtaUpsertInput,
  MaterialLookupItem,
  WarehouseOption
} from '@/api/aps-v1'

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
  ElPagination,
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
  distinctMaterialCodes,
  pageIndex,
  pageSize,
  total
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
    materialIds: undefined, // 物料编码路径：UI 用 code 选 → 后端 materialCodes 筛（10-07 ME10）
    materialCodes: selectedMaterialCodes.value.length
      ? [...selectedMaterialCodes.value]
      : undefined,
    activeOnly: activeOnlyDraft.value
  }
}

async function applyFilter(): Promise<void> {
  syncFilterDraft()
  // 筛选条件变化 → 重置到第 1 页（避免留在第 N 页时筛选无交集导致空态错觉）
  await store.load({ ...filterDraft.value, pageIndex: 1 })
}

function resetFilters(): void {
  selectedPoNos.value = []
  selectedMaterialCodes.value = []
  activeOnlyDraft.value = false
}

async function refresh(): Promise<void> {
  await store.load()
}

/** 分页：当前页切换 */
async function onPageChange(p: number): Promise<void> {
  await store.load({ ...store.lastFilter, pageIndex: p })
}

/** 分页：每页条数切换 → 回到第 1 页 */
async function onSizeChange(s: number): Promise<void> {
  await store.load({ ...store.lastFilter, pageIndex: 1, pageSize: s })
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
 *  - materialKey：物料下拉选中的 masterId（number）；watch 同步 → materialId + materialCode
 */
const form = ref({
  poNo: '',
  lineNo: 1,
  materialId: 0,
  materialCode: '',
  materialKey: null as number | null,
  receivingWarehouse: '',
  manualEta: new Date(Date.now() + 7 * 86400_000).toISOString(),
  remark: ''
})

/** 物料下拉数据源（real API，10-07 接入）
 *  - 端点：GET /api/governance/lookups/materials?activeOnly=true
 *  - 复用 materialLookupApi.listActiveMaterials（与 CTP 同源）
 *  - 失败兜底：空数组（下拉空态提示「暂无物料主数据」）
 */
const materialOptions = ref<MaterialLookupItem[]>([])
async function loadMaterialOptions(): Promise<void> {
  try {
    materialOptions.value = await materialLookupApi.listActiveMaterials()
  } catch {
    materialOptions.value = []
  }
}

/** 收货仓库下拉数据源（5号位 2026-10-08 落地）
 *  - 端点：GET /api/procurement-manual-eta/warehouses?search=（非 /api/governance/lookups/warehouses）
 *  - 数据源 ext_MES_ProcessCode_View；返回 [{ code, name }]
 *  - 失败兜底：空数组（提示「暂无仓库数据」）
 */
const warehouseOptions = ref<WarehouseOption[]>([])
async function loadWarehouseOptions(): Promise<void> {
  try {
    warehouseOptions.value = await manualEtaApi.listWarehouses()
  } catch {
    warehouseOptions.value = []
  }
}

/** 仓库编码 → name 映射（用于表格列展示）
 *  - 数据源 warehouseOptions（已通过 listWarehouses 异步加载）
 *  - 兜底：name 找不到时只显示 code（脏数据 / 测试输入 / view 未覆盖）
 */
const warehouseNameMap = computed(() => {
  const m = new Map<string, string>()
  for (const w of warehouseOptions.value) m.set(w.code, w.name)
  return m
})

/** 物料下拉 watch：选中 masterId → 同步 materialId + materialCode */
watch(
  () => form.value.materialKey,
  (key) => {
    if (key == null) {
      form.value.materialId = 0
      form.value.materialCode = ''
      return
    }
    const found = materialOptions.value.find((m) => m.masterId === key)
    if (found) {
      form.value.materialId = found.masterId
      form.value.materialCode = found.code
    }
  }
)

/** 兜底 watch：options 异步加载完后，若 materialKey 还是 null 但 materialId 有值（编辑态），反查填回 */
watch(
  () => materialOptions.value,
  (opts) => {
    if (form.value.materialKey == null && form.value.materialId > 0) {
      const found = opts.find((m) => m.masterId === form.value.materialId)
      if (found) form.value.materialKey = found.masterId
    }
  }
)

/** 物料下拉 v-model 包装：null ↔ undefined 转换（ElSelect 类型不允许 null，但 form 内部用 null 区分"未选"）
 *  - getter：form.materialKey ?? undefined
 *  - setter：v → form.materialKey = v ?? null
 */
const materialKeyModel = computed<number | undefined>({
  get: () => form.value.materialKey ?? undefined,
  set: (v) => {
    form.value.materialKey = v ?? null
  }
})

/** 字段级错误 */
const formRules = {
  poNo: [
    { required: true, message: '请输入采购订单', trigger: 'blur' },
    { min: 3, message: '采购订单至少 3 个字符', trigger: 'blur' }
  ],
  lineNo: [
    { required: true, type: 'number' as const, min: 1, message: '行号 ≥ 1', trigger: 'blur' }
  ],
  materialId: [
    { required: true, type: 'number' as const, min: 1, message: '物料 ID ≥ 1', trigger: 'blur' }
  ],
  materialKey: [{ required: true, message: '请选择物料', trigger: 'change' }],
  receivingWarehouse: [
    { required: true, message: '请选择收货仓库', trigger: 'change' },
    { min: 2, message: '仓库编码至少 2 个字符', trigger: 'blur' }
  ],
  manualEta: [{ required: true, message: '请选择到货时间', trigger: 'change' }]
}

function openCreate(): void {
  drawerMode.value = 'new'
  form.value = {
    poNo: '',
    lineNo: 1,
    materialId: 0,
    materialCode: '',
    materialKey: null,
    receivingWarehouse: '',
    manualEta: new Date(Date.now() + 7 * 86400_000).toISOString(),
    remark: ''
  }
  store.openDetail(null)
  drawerVisible.value = true
}

function openEdit(dto: ManualEtaDto): void {
  drawerMode.value = 'edit'
  // 编辑态：优先用 materialId 反查 materialKey；若 options 还没加载完（异步），留 null 由 watch 兜底回填
  const found = materialOptions.value.find((m) => m.masterId === dto.materialId)
  form.value = {
    poNo: dto.poNo,
    lineNo: dto.lineNo,
    materialId: dto.materialId,
    materialCode: dto.materialCode ?? '',
    materialKey: found ? found.masterId : null,
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

onMounted(() => {
  refresh()
  loadMaterialOptions()
  loadWarehouseOptions()
})
</script>

<template>
  <div class="aps-manual-eta">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">人工到货时间</h2>
        <p class="page-sub">仅人工值；有效到货时间由系统计算</p>
      </div>
      <ElButton :loading="loading" @click="refresh">
        <Icon icon="vi-ep:refresh" />
        刷新
      </ElButton>
    </div>

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
          <label>采购订单</label>
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
          <label>物料</label>
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
          <span>人工到货时间列表（{{ list.length }} 条 / 共 {{ total }} 条）</span>
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
        <ElTableColumn label="采购订单" prop="poNo" width="140" />
        <ElTableColumn label="行号" prop="lineNo" width="70" align="center" />
        <ElTableColumn label="物料" min-width="160">
          <template #default="{ row }">
            <span>{{ row?.materialCode ?? '-' }}</span>
            <span v-if="row?.materialName" class="muted small"> · {{ row.materialName }}</span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="收货仓库" min-width="160">
          <template #default="{ row }">
            <span v-if="row?.receivingWarehouse">{{ row.receivingWarehouse }}</span>
            <span v-else class="muted">-</span>
            <span
              v-if="row?.receivingWarehouse && warehouseNameMap.get(row.receivingWarehouse)"
              class="muted small"
            >
              · {{ warehouseNameMap.get(row.receivingWarehouse) }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="到货时间" width="150">
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
      <div v-if="total > 0" class="pagination-bar">
        <ElPagination
          :current-page="pageIndex"
          :page-size="pageSize"
          :total="total"
          :page-sizes="[10, 20, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          background
          @current-change="onPageChange"
          @size-change="onSizeChange"
        />
      </div>
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
        <ElForm
          ref="formRef"
          :model="form"
          :rules="formRules"
          label-width="100px"
          label-position="right"
          class="manual-eta-form"
        >
          <ElFormItem label="采购订单" prop="poNo">
            <ElInput
              v-model="form.poNo"
              :disabled="drawerMode === 'edit'"
              placeholder="例如：PO-2026-1001"
            />
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
          <ElFormItem label="物料" prop="materialKey">
            <ElSelect
              v-model="materialKeyModel"
              :disabled="drawerMode === 'edit'"
              placeholder="选择物料"
              filterable
              clearable
              style="width: 100%"
            >
              <ElOption
                v-for="m in materialOptions"
                :key="m.masterId"
                :label="`${m.code} / ${m.name}`"
                :value="m.masterId"
              />
            </ElSelect>
          </ElFormItem>
          <ElFormItem label="收货仓库" prop="receivingWarehouse">
            <ElSelect
              v-model="form.receivingWarehouse"
              placeholder="请选择收货仓库"
              filterable
              clearable
              style="width: 100%"
            >
              <ElOption
                v-for="w in warehouseOptions"
                :key="w.code"
                :label="`${w.code} / ${w.name}`"
                :value="w.code"
              />
            </ElSelect>
          </ElFormItem>
          <ElFormItem label="到货时间" prop="manualEta">
            <ElDatePicker
              v-model="form.manualEta"
              type="datetime"
              value-format="YYYY-MM-DDTHH:mm:ss[Z]"
              placeholder="选择到货预期时间"
              style="width: 100%"
            />
          </ElFormItem>
          <ElFormItem label="备注">
            <ElInput
              v-model="form.remark"
              type="textarea"
              :rows="3"
              placeholder="可选"
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

.pagination-bar {
  display: flex;
  padding: 16px 0 4px;
  justify-content: flex-end;
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

.manual-eta-form {
  :deep(.el-form-item) {
    margin-bottom: 14px;
  }
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
