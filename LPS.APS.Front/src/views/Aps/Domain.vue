<script setup lang="ts">
/**
 * APS V1 4号位 — 排程域定义维护（页面 / Domain专项 Pkg-3）
 *
 * 依据：
 *  - 冻结文档《APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md》§四
 *  - 4号位开发包 v1.2 §Domain专项 1-9
 *
 * 业务约束：
 *  - 排程域是独立稳定排程边界（不等于 ProductFamily、不等于 Stage）
 *  - V1 ScopeType 只支持 FAMILY / FACTORY_FAMILY
 *  - domainKey 由系统生成、不可改；其它字段可改
 *  - FACTORY_FAMILY 必填 factory；FAMILY 不可填
 *  - 停用后下拉不可选、CTP/Candidate 不可见；已有 PlanVersion 不影响
 *
 * 角色权限：
 *  - aps.admin.system     可新增/修改/启停
 *  - 其它角色             只读列表 + 详情
 *  - 4号位不挂 meta.permission，按钮级门控；生产模式由 3号位后端二次校验
 */

import { computed, onMounted, reactive, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { formatUtcDateTime } from '@/utils/datetime'
import { useDomainStore } from '@/store/modules/aps/domain'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import {
  DOMAIN_SCOPE_TYPE_LABELS,
  type DomainDefinitionDto,
  type DomainScopeType,
  type FactorySummaryDto,
  type ProductFamilySummaryDto
} from '@/api/aps-v1/types'

import { domainApi } from '@/api/aps-v1/domain'

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
  ElInputNumber,
  ElMessage,
  ElOption,
  ElSelect,
  ElTag,
  ElTooltip
} from 'element-plus'

const domainStore = useDomainStore()
const apsAuth = useApsAuthStore()
const { loading, error, fetchStatus, allActive, byKey, definitions } = storeToRefs(domainStore)

/* ===== 角色门控（v1.2 §23.1 DDL 角色码）=====
 *  - 仅 aps.admin.system 可写；其余角色只读
 *  - 与 router meta apsRequiredRoles: ['aps.admin.system'] 路由级门控双保险
 *  - 不用权限码的原因：排程域维护是「治理类」操作，没有特定「编辑排程域」权限码，
 *    且与后端 [Authorize(Roles = "aps.admin.system")] 一致用角色码
 */
const canWrite = computed(() => apsAuth.roles.includes('aps.admin.system'))

/* ===== 字典 ===== */
const SCOPE_TYPE_OPTIONS: Array<{ value: DomainScopeType; label: string }> = [
  { value: 'FAMILY', label: DOMAIN_SCOPE_TYPE_LABELS.FAMILY },
  { value: 'FACTORY_FAMILY', label: DOMAIN_SCOPE_TYPE_LABELS.FACTORY_FAMILY }
]
const SCOPE_TYPE_TAG: Record<DomainScopeType, 'success' | 'warning'> = {
  FAMILY: 'success',
  FACTORY_FAMILY: 'warning'
}

/* ===== 列表过滤 ===== */
const statusFilter = ref<'ALL' | 'ACTIVE' | 'INACTIVE'>('ALL')
const filteredDefinitions = computed<DomainDefinitionDto[]>(() => {
  // 「全部」= 全部定义（含停用），按 sortOrder 升序
  // 「启用」/「停用」= 按 isActive 过滤
  const raw: DomainDefinitionDto[] = definitions.value as DomainDefinitionDto[]
  const list =
    statusFilter.value === 'ALL'
      ? raw
      : raw.filter((d: DomainDefinitionDto) =>
          statusFilter.value === 'ACTIVE' ? d.isActive : !d.isActive
        )
  return list
    .slice()
    .sort(
      (a: DomainDefinitionDto, b: DomainDefinitionDto) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0)
    )
})

/* ===== KPI ===== */
const kpiCounts = computed(() => {
  // definitions 是后端 raw list（active + inactive）；allActive 仅 active
  const raw: DomainDefinitionDto[] = definitions.value as DomainDefinitionDto[]
  const total = raw.length
  const active = allActive.value.length
  const inactive = raw.filter((d: DomainDefinitionDto) => !d.isActive).length
  const visible = domainStore.activeDomains.length
  return { total, active, inactive, visible }
})

/* ===== 降级 Badge：未配置 / 全部停用 =====
 * 触发条件：
 *   - 非加载中、无 fetchStatus 异常（unavailable/error 已有红黄 banner）
 *   - 系统中 0 个排程域定义              → 'no-config'（系统未配置）
 *   - 系统有定义但 activeDomains 全停用     → 'all-inactive'（下游无选项）
 * 与左面板 ElEmpty 「无符合条件」的关系：
 *   ElEmpty 仅表示「当前筛选条件下没有」，但用户易误读为「系统未配置」；
 *   本 Badge 显式分场景提示，避免误导
 */
type EmptyReason = 'no-config' | 'all-inactive'
const emptyReason = computed<EmptyReason | null>(() => {
  if (loading.value) return null
  if (fetchStatus.value !== 'ok') return null
  if (error.value) return null
  const total = definitions.value.length
  const active = allActive.value.length
  if (total === 0) return 'no-config'
  if (active === 0) return 'all-inactive'
  return null
})

/* ===== 选中 + 详情编辑 ===== */
const selectedKey = ref<DomainDefinitionDto['domainKey'] | null>(null)
const currentDetail = computed<DomainDefinitionDto | null>(() =>
  selectedKey.value ? (byKey.value.get(selectedKey.value) ?? null) : null
)

/** 编辑表单（domainKey 不可改 + isActive 由顶部「停用/启用」按钮走专用端点，PUT 不接受） */
const editForm = reactive<{
  domainName: string
  scopeType: DomainScopeType
  productFamilyId: number | undefined
  factoryId: number | undefined
  sortOrder: number
}>({
  domainName: '',
  scopeType: 'FAMILY',
  productFamilyId: undefined,
  factoryId: undefined,
  sortOrder: 100
})

/** 监听选中变化 → 重置编辑表单 + 清校验态 */
const editFormRef = ref<InstanceType<typeof ElForm> | null>(null)
const editSubmitting = ref(false)
const toggleSubmitting = ref(false)

watch(
  () => currentDetail.value,
  (d) => {
    if (!d) return
    editForm.domainName = d.domainName
    editForm.scopeType = d.scopeType
    editForm.productFamilyId = d.productFamilyId
    editForm.factoryId = d.factoryId
    editForm.sortOrder = d.sortOrder ?? 100
    // 切换行后清掉上一行的红字校验
    editFormRef.value?.clearValidate()
  },
  { immediate: true }
)

/* ===== 操作 ===== */
function selectDomain(domainKey: DomainDefinitionDto['domainKey']): void {
  selectedKey.value = domainKey
}

async function refreshList(): Promise<void> {
  await domainStore.refresh()
}

async function onSave(): Promise<void> {
  if (!currentDetail.value || !canWrite.value) return
  if (!editFormRef.value) return
  const valid = await editFormRef.value.validate().catch(() => false)
  if (!valid) return

  editSubmitting.value = true
  try {
    // 后端 PUT /api/governance/domain-definition/{id} 接 [FromBody] DomainDefinition 全量实体
    // UpdateAsync 入口会做 existing.DomainKey != input.DomainKey 校验；
    // 因此 patch 必须带 domainKey（即便后端会忽略修改值并强制写回 existing.DomainKey）
    const patch: Partial<Omit<DomainDefinitionDto, 'updatedBy' | 'updatedAt'>> = {
      domainKey: currentDetail.value.domainKey,
      domainName: editForm.domainName.trim(),
      scopeType: editForm.scopeType,
      productFamilyId: editForm.productFamilyId,
      factoryId: editForm.scopeType === 'FACTORY_FAMILY' ? editForm.factoryId : undefined,
      sortOrder: editForm.sortOrder
      // isActive 由后端 UpdateAsync 强制写回 existing.IsActive（不接受 input），
      // 启停用必须走专用 POST /enable 或 /disable 端点（顶部「停用/启用」按钮调用）
    }
    await domainStore.update(currentDetail.value.domainKey, patch)
    ElMessage.success('保存成功')
  } catch (err) {
    // 后端 4xx/5xx 已被 apsHttp 拦截器弹 ElMessage.error；此处仅 console 调试
    console.error('[APS 排程域页] 保存失败', err)
  } finally {
    editSubmitting.value = false
  }
}

async function onToggleActive(): Promise<void> {
  if (!currentDetail.value || !canWrite.value) return
  const wasActive = currentDetail.value.isActive
  toggleSubmitting.value = true
  try {
    if (wasActive) {
      await domainStore.disable(currentDetail.value.domainKey)
      ElMessage.success('已停用')
    } else {
      await domainStore.enable(currentDetail.value.domainKey)
      ElMessage.success('已启用')
    }
  } catch (err) {
    console.error('[APS 排程域页] 启停用失败', err)
  } finally {
    toggleSubmitting.value = false
  }
}

/* ===== 新建 Dialog ===== */
const createDialog = ref(false)
const createSubmitting = ref(false)
const createForm = reactive<{
  domainKey: string
  domainName: string
  scopeType: DomainScopeType
  productFamilyId: number | undefined
  factoryId: number | undefined
  sortOrder: number
}>({
  domainKey: '',
  domainName: '',
  scopeType: 'FAMILY',
  productFamilyId: undefined,
  factoryId: undefined,
  sortOrder: 100
})

const createFormRef = ref<InstanceType<typeof ElForm> | null>(null)

/** domainKey 格式：大写字母/数字/下划线/连字符，4-64 字符（与后端 DomainKey 命名兼容） */
function validateDomainKeyFormat(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  if (!/^[A-Z][A-Z0-9_-]{3,49}$/.test(value.trim())) {
    cb(new Error('须以大写字母开头，仅含大写字母/数字/下划线/连字符，长度 4-50'))
  } else {
    cb()
  }
}

/** domainKey 唯一性（前端先拦，后端 400 兜底） */
function validateDomainKeyUnique(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  const dup = definitions.value.some((d) => d.domainKey === value.trim())
  if (dup) cb(new Error(`排程域标识「${value}」已存在`))
  else cb()
}

const createRules = {
  domainKey: [
    { required: true, message: '请输入排程域标识', trigger: 'blur' },
    { validator: validateDomainKeyFormat, trigger: 'blur' },
    { validator: validateDomainKeyUnique, trigger: 'blur' }
  ],
  domainName: [
    { required: true, message: '请输入排程域名称', trigger: 'blur' },
    { max: 32, message: '最长 32 个字符', trigger: 'blur' }
  ],
  productFamilyId: [
    {
      required: true,
      message: '产品族不能为空',
      trigger: 'change',
      validator: (_rule: unknown, v: number | undefined, cb: (err?: Error) => void) => {
        if (v === undefined) cb(new Error('产品族不能为空'))
        else cb()
      }
    }
  ],
  factoryId: [
    {
      validator: (_rule: unknown, _v: number | undefined, cb: (err?: Error) => void) => {
        if (createForm.scopeType === 'FACTORY_FAMILY' && createForm.factoryId === undefined) {
          cb(new Error('按「工厂+产品族」范围时必须选择工厂'))
        } else {
          cb()
        }
      },
      trigger: 'change'
    }
  ],
  sortOrder: [
    {
      validator: (_rule: unknown, v: number, cb: (err?: Error) => void) => {
        if (typeof v !== 'number' || v < 0 || v > 9999) {
          cb(new Error('排序权重须在 0-9999 之间'))
        } else {
          cb()
        }
      },
      trigger: 'blur'
    }
  ]
}

const editRules = {
  domainName: [
    { required: true, message: '排程域名称不能为空', trigger: 'blur' },
    { max: 32, message: '最长 32 个字符', trigger: 'blur' }
  ],
  productFamilyId: [
    {
      required: true,
      message: '产品族不能为空',
      trigger: 'change',
      validator: (_rule: unknown, v: number | undefined, cb: (err?: Error) => void) => {
        if (v === undefined) cb(new Error('产品族不能为空'))
        else cb()
      }
    }
  ],
  factoryId: [
    {
      validator: (_rule: unknown, _v: number | undefined, cb: (err?: Error) => void) => {
        if (editForm.scopeType === 'FACTORY_FAMILY' && editForm.factoryId === undefined) {
          cb(new Error('按「工厂+产品族」范围时必须选择工厂'))
        } else {
          cb()
        }
      },
      trigger: 'change'
    }
  ],
  sortOrder: [
    {
      validator: (_rule: unknown, v: number, cb: (err?: Error) => void) => {
        if (typeof v !== 'number' || v < 0 || v > 9999) {
          cb(new Error('排序权重须在 0-9999 之间'))
        } else {
          cb()
        }
      },
      trigger: 'blur'
    }
  ]
}

function openCreateDialog(): void {
  createForm.domainKey = ''
  createForm.domainName = ''
  createForm.scopeType = 'FAMILY'
  createForm.productFamilyId = undefined
  createForm.factoryId = undefined
  createForm.sortOrder = (allActive.value.length + 1) * 10
  createDialog.value = true
  createFormRef.value?.clearValidate()
}

async function onCreate(): Promise<void> {
  if (!createFormRef.value) return
  const valid = await createFormRef.value.validate().catch(() => false)
  if (!valid) return

  const dk = createForm.domainKey.trim()
  const pfId = createForm.productFamilyId as number // validate 通过后必非空
  createSubmitting.value = true
  try {
    await domainStore.create({
      domainKey: dk,
      domainName: createForm.domainName.trim(),
      scopeType: createForm.scopeType,
      productFamilyId: pfId,
      factoryId: createForm.scopeType === 'FACTORY_FAMILY' ? createForm.factoryId : undefined,
      isActive: true,
      sortOrder: createForm.sortOrder
    })
    createDialog.value = false
    selectedKey.value = dk
    // 新建的排程域 isActive=true：若用户停留在「停用」筛选下会看不到新建条目，
    // 强制重置为「全部」让用户立刻在左侧列表看到新条目
    statusFilter.value = 'ALL'
    ElMessage.success(`排程域「${dk}」创建成功`)
  } catch (err) {
    console.error('[APS 排程域页] 创建失败', err)
  } finally {
    createSubmitting.value = false
  }
}

/* ===== 字典（下拉数据源）=====
 * 加载时机：组件挂载时与 domainStore.refresh() 并行
 * lookup 用法：getProductFamilyName(id) / getFactoryName(id)
 * 字段变更（2026-09-16，3号位契约收口）：替代旧的 string code 输入
 */
const productFamilies = ref<ProductFamilySummaryDto[]>([])
const factories = ref<FactorySummaryDto[]>([])

async function loadDictionaries(): Promise<void> {
  try {
    const [pfs, fcs] = await Promise.all([
      domainApi.listProductFamilies(),
      domainApi.listFactories()
    ])
    productFamilies.value = pfs
    factories.value = fcs
  } catch (err) {
    console.warn('[APS 排程域页] 加载产品族/工厂字典失败', err)
  }
}

/** 表格列展示：lookup productFamilyId → `${code}（${name}）` */
function getProductFamilyName(id: number): string {
  const pf = productFamilies.value.find((p) => p.id === id)
  return pf ? `${pf.code}（${pf.name}）` : `#${id}`
}

/** 表格列展示：lookup factoryId → `${code}（${name}）` */
function getFactoryName(id: number): string {
  const fc = factories.value.find((f) => f.id === id)
  return fc ? `${fc.code}（${fc.name}）` : `#${id}`
}

onMounted(async () => {
  await Promise.all([domainStore.refresh(), loadDictionaries()])
})
</script>

<template>
  <div class="aps-domain">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">排程域定义维护</h2>
        <p class="page-sub">
          按产品族 / 工厂+产品族维护排程域；排程域标识由系统生成、不可改；停用后下拉不可选
        </p>
      </div>
      <div class="header-actions">
        <ElButton :loading="loading" @click="refreshList">
          <Icon icon="vi-ep:refresh" /> 刷新
        </ElButton>
        <ElButton v-if="canWrite" type="primary" @click="openCreateDialog">
          <Icon icon="vi-mdi:plus" /> 新增排程域
        </ElButton>
      </div>
    </div>

    <!-- ===== 接口降级条（接口不可用 404，红色）=====
     *  dev/prod 都展示：避免 dev 用户被「空页面 + 假数据」误导
     *  dev 如需使用 mock 数据：设 VITE_USE_MOCK=true 后点击「重试」
     -->
    <ElAlert
      v-if="fetchStatus === 'unavailable'"
      type="error"
      :closable="false"
      show-icon
      class="degraded-banner"
    >
      <template #title>排程域数据暂不可用</template>
      <div class="degraded-content">
        <p class="hint-text">
          服务端当前不可用，页面无法加载排程域数据，新增 / 修改 /
          停用均不可用。请点击「重试」，或联系系统管理员。
        </p>
        <div class="degraded-actions">
          <ElButton size="small" :loading="loading" @click="refreshList">
            <Icon icon="vi-ep:refresh" /> 重试
          </ElButton>
        </div>
      </div>
    </ElAlert>

    <!-- ===== 错误条（其它错误，黄色）=====
     *  fetchStatus==='unavailable' 时被上面红条替代，不重复显示
     -->
    <ElAlert v-else-if="error" type="warning" :closable="false" show-icon :title="error" />

    <!-- ===== 降级 Badge：未配置 / 全部停用（区别于 unavailable/error 已有红黄 banner）=====
     *  - 'no-config'   系统未创建任何排程域定义
     *  - 'all-inactive' 系统有定义但全部停用（下游无选项）
     *  - admin 看「新增排程域」按钮 / viewer 看「请联系系统管理员」CTA
     -->
    <ElAlert
      v-if="emptyReason"
      :type="emptyReason === 'no-config' ? 'warning' : 'info'"
      :closable="false"
      show-icon
      class="empty-state-badge"
    >
      <template #title>
        {{ emptyReason === 'no-config' ? '当前未配置任何排程域' : '无可用排程域（全部停用）' }}
      </template>
      <div class="empty-state-content">
        <p v-if="emptyReason === 'no-config'" class="hint-text">
          系统中尚未创建任何排程域定义。排程域是排程边界，CTP / Candidate / Run
          等下游功能均依赖排程域选域；未配置时下游页面将无可选项。
        </p>
        <p v-else class="hint-text">
          系统中已有
          {{ definitions.length }}
          个排程域定义，但均已停用。可停用排程域不会出现在下游选项中，也不参与排程计算。
        </p>
        <div class="empty-state-actions">
          <ElButton
            v-if="canWrite && emptyReason === 'no-config'"
            type="primary"
            size="small"
            @click="openCreateDialog"
          >
            <Icon icon="vi-mdi:plus" /> 新增排程域
          </ElButton>
          <ElTooltip
            v-else-if="!canWrite && emptyReason === 'no-config'"
            content="需要系统管理员角色"
            placement="top"
          >
            <ElButton size="small" disabled>
              <Icon icon="vi-mdi:account-cog-outline" /> 请联系系统管理员
            </ElButton>
          </ElTooltip>
          <span v-else-if="canWrite && emptyReason === 'all-inactive'" class="hint-text">
            在左侧列表切换「停用」过滤，点击「启用」按钮恢复
          </span>
          <span v-else class="hint-text"> 请联系系统管理员启用 </span>
        </div>
      </div>
    </ElAlert>

    <!-- ===== KPI ===== -->
    <div class="kpi-strip">
      <div class="kpi-pill">
        <span class="pill-label">排程域总数</span>
        <span class="pill-val">{{ kpiCounts.total }}</span>
      </div>
      <div class="kpi-pill ok">
        <span class="pill-label">启用</span>
        <span class="pill-val">{{ kpiCounts.active }}</span>
      </div>
      <div class="kpi-pill muted">
        <span class="pill-label">停用</span>
        <span class="pill-val">{{ kpiCounts.inactive }}</span>
      </div>
      <div class="kpi-pill warn">
        <span class="pill-label">当前用户可见</span>
        <span class="pill-val">{{ kpiCounts.visible }}</span>
      </div>
    </div>

    <!-- ===== 状态过滤 ===== -->
    <div class="filter-row">
      <span class="filter-label">状态：</span>
      <ElSelect v-model="statusFilter" size="small" style="width: 160px">
        <ElOption label="全部" value="ALL" />
        <ElOption label="启用" value="ACTIVE" />
        <ElOption label="停用" value="INACTIVE" />
      </ElSelect>
    </div>

    <div class="content-row">
      <!-- ===== 左：排程域列表 ===== -->
      <ElCard class="panel left-panel">
        <template #header>
          <div class="panel-header">
            <span>排程域列表</span>
            <ElTag size="small" effect="plain">{{ filteredDefinitions.length }}</ElTag>
          </div>
        </template>
        <div v-if="!filteredDefinitions.length && !loading" class="panel-empty">
          <ElEmpty description="无符合条件的排程域" />
        </div>
        <div v-else class="domain-list">
          <div
            v-for="d in filteredDefinitions"
            :key="d.domainKey"
            class="domain-card"
            :class="{ active: selectedKey === d.domainKey, inactive: !d.isActive }"
            @click="selectDomain(d.domainKey)"
          >
            <div class="dm-head">
              <span class="dm-key">{{ d.domainKey }}</span>
              <ElTag :type="SCOPE_TYPE_TAG[d.scopeType]" size="small" effect="plain">
                {{ SCOPE_TYPE_OPTIONS.find((o) => o.value === d.scopeType)?.label ?? d.scopeType }}
              </ElTag>
            </div>
            <div class="dm-name">{{ d.domainName }}</div>
            <div class="dm-meta">
              <span class="meta-key">产品族</span>
              <span class="meta-val">{{ getProductFamilyName(d.productFamilyId) }}</span>
              <span v-if="d.factoryId" class="meta-factory"
                >@ {{ getFactoryName(d.factoryId) }}</span
              >
            </div>
            <div class="dm-foot">
              <ElTag v-if="d.isActive" size="small" type="success" effect="dark">启用</ElTag>
              <ElTag v-else size="small" type="info" effect="dark">停用</ElTag>
              <span class="meta-sort">排序权重 {{ d.sortOrder ?? '-' }}</span>
            </div>
          </div>
        </div>
      </ElCard>

      <!-- ===== 右：详情 ===== -->
      <ElCard class="panel right-panel">
        <template v-if="!currentDetail">
          <ElEmpty description="从左侧选择一个排程域" />
        </template>

        <div v-else>
          <!-- Header -->
          <div class="detail-head-row">
            <div>
              <ElTag :type="SCOPE_TYPE_TAG[currentDetail.scopeType]" size="small" effect="dark">
                {{
                  SCOPE_TYPE_OPTIONS.find((o) => o.value === currentDetail!.scopeType)?.label ??
                  currentDetail.scopeType
                }}
              </ElTag>
              <span class="detail-key">{{ currentDetail.domainKey }}</span>
              <ElTag
                v-if="currentDetail.isActive"
                size="small"
                type="success"
                effect="plain"
                class="ml"
              >
                启用
              </ElTag>
              <ElTag v-else size="small" type="info" effect="plain" class="ml">停用</ElTag>
            </div>
            <div class="head-actions">
              <ElButton
                v-if="canWrite"
                size="small"
                :type="currentDetail.isActive ? 'warning' : 'success'"
                plain
                :loading="toggleSubmitting"
                :disabled="toggleSubmitting"
                @click="onToggleActive"
              >
                <Icon :icon="currentDetail.isActive ? 'vi-mdi:archive' : 'vi-mdi:check'" />
                {{ currentDetail.isActive ? '停用' : '启用' }}
              </ElButton>
            </div>
          </div>

          <!-- 编辑表单 -->
          <ElForm
            ref="editFormRef"
            :model="editForm"
            :rules="editRules"
            label-width="120px"
            size="default"
            class="detail-form"
            @submit.prevent
          >
            <ElFormItem label="排程域标识">
              <ElTooltip content="系统生成、不可改" placement="top">
                <ElTag effect="plain" type="info">{{ currentDetail.domainKey }}</ElTag>
              </ElTooltip>
            </ElFormItem>
            <ElFormItem label="排程域名称" prop="domainName">
              <ElInput
                v-model="editForm.domainName"
                :disabled="!canWrite"
                placeholder="如：注塑域"
                maxlength="32"
                show-word-limit
              />
            </ElFormItem>
            <ElFormItem label="范围类型" prop="scopeType">
              <ElSelect
                v-model="editForm.scopeType"
                :disabled="!canWrite"
                placeholder="选择范围类型"
                style="width: 100%"
                @change="editForm.factoryId = undefined"
              >
                <ElOption
                  v-for="o in SCOPE_TYPE_OPTIONS"
                  :key="o.value"
                  :label="o.label"
                  :value="o.value"
                />
              </ElSelect>
            </ElFormItem>
            <ElFormItem label="产品族" prop="productFamilyId">
              <ElSelect
                v-model="editForm.productFamilyId"
                :disabled="!canWrite"
                placeholder="选择产品族"
                style="width: 100%"
                filterable
              >
                <ElOption
                  v-for="pf in productFamilies"
                  :key="pf.id"
                  :label="`${pf.code}（${pf.name}）`"
                  :value="pf.id"
                />
              </ElSelect>
            </ElFormItem>
            <ElFormItem
              v-if="editForm.scopeType === 'FACTORY_FAMILY'"
              label="工厂"
              prop="factoryId"
            >
              <ElSelect
                v-model="editForm.factoryId"
                :disabled="!canWrite"
                placeholder="选择工厂"
                style="width: 100%"
                filterable
              >
                <ElOption
                  v-for="fc in factories"
                  :key="fc.id"
                  :label="`${fc.code}（${fc.name}）`"
                  :value="fc.id"
                />
              </ElSelect>
            </ElFormItem>
            <ElFormItem label="排序权重" prop="sortOrder">
              <ElInputNumber
                v-model="editForm.sortOrder"
                :disabled="!canWrite"
                :min="0"
                :max="9999"
              />
            </ElFormItem>
          </ElForm>

          <!-- 元信息 -->
          <ElDescriptions :column="2" size="small" border class="meta-desc">
            <ElDescriptionsItem label="启用状态">
              <ElTag :type="currentDetail.isActive ? 'success' : 'info'" size="small">
                {{ currentDetail.isActive ? '已启用' : '已停用' }}
              </ElTag>
              <span class="meta-hint"> · 启停用请用右上方按钮（专用端点） </span>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="更新人">{{
              currentDetail.updatedBy ?? '—'
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="更新时间">{{
              formatUtcDateTime(currentDetail.updatedAt)
            }}</ElDescriptionsItem>
          </ElDescriptions>

          <!-- 保存按钮 -->
          <div class="detail-actions">
            <ElTooltip v-if="!canWrite" content="需要系统管理员角色" placement="top">
              <ElButton type="primary" disabled>
                <Icon icon="vi-mdi:content-save" /> 保存
              </ElButton>
            </ElTooltip>
            <ElButton
              v-else
              type="primary"
              :loading="editSubmitting"
              :disabled="editSubmitting"
              @click="onSave"
            >
              <Icon icon="vi-mdi:content-save" /> 保存
            </ElButton>
          </div>
        </div>
      </ElCard>
    </div>

    <!-- ===== 新建排程域 Dialog ===== -->
    <ElDialog
      v-model="createDialog"
      title="新增排程域"
      width="520px"
      :close-on-click-modal="false"
      :close-on-press-escape="!createSubmitting"
    >
      <ElForm
        ref="createFormRef"
        :model="createForm"
        :rules="createRules"
        label-width="120px"
        size="default"
        @submit.prevent
      >
        <ElFormItem label="排程域标识" prop="domainKey">
          <ElInput
            v-model="createForm.domainKey"
            placeholder="如：FAMILY_INJECTION 或 BJ_FAMILY_INJECTION"
            maxlength="64"
          />
        </ElFormItem>
        <ElFormItem label="排程域名称" prop="domainName">
          <ElInput
            v-model="createForm.domainName"
            placeholder="如：注塑域 / 北京注塑"
            maxlength="32"
          />
        </ElFormItem>
        <ElFormItem label="范围类型" prop="scopeType">
          <ElSelect
            v-model="createForm.scopeType"
            placeholder="选择范围类型"
            style="width: 100%"
            @change="createForm.factoryId = undefined"
          >
            <ElOption
              v-for="o in SCOPE_TYPE_OPTIONS"
              :key="o.value"
              :label="o.label"
              :value="o.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="产品族" prop="productFamilyId">
          <ElSelect
            v-model="createForm.productFamilyId"
            placeholder="选择产品族"
            style="width: 100%"
            filterable
          >
            <ElOption
              v-for="pf in productFamilies"
              :key="pf.id"
              :label="`${pf.code}（${pf.name}）`"
              :value="pf.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem v-if="createForm.scopeType === 'FACTORY_FAMILY'" label="工厂" prop="factoryId">
          <ElSelect
            v-model="createForm.factoryId"
            placeholder="选择工厂"
            style="width: 100%"
            filterable
          >
            <ElOption
              v-for="fc in factories"
              :key="fc.id"
              :label="`${fc.code}（${fc.name}）`"
              :value="fc.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="排序权重" prop="sortOrder">
          <ElInputNumber v-model="createForm.sortOrder" :min="0" :max="9999" />
        </ElFormItem>
        <ElAlert type="warning" :closable="false" show-icon>
          <template #title>排程域标识不可改</template>
          创建后此字段不可修改；如需修改请删除重建。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton :disabled="createSubmitting" @click="createDialog = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="createSubmitting"
          :disabled="createSubmitting"
          @click="onCreate"
        >
          <Icon icon="vi-mdi:check" /> 确认创建
        </ElButton>
      </template>
    </ElDialog>

    <!-- 加载态 -->
    <div v-if="loading" class="loading-tip">加载排程域列表中…</div>
  </div>
</template>

<style lang="less" scoped>
.aps-domain {
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

  .header-actions {
    display: flex;
    gap: 8px;
    align-items: center;
  }
}

.degraded-banner {
  .degraded-content {
    margin-top: 4px;
    margin-left: 8px;
    font-size: 13px;
    line-height: 1.7;
    color: #475569;

    code {
      padding: 1px 6px;
      font-family: 'Courier New', monospace;
      font-size: 12px;
      background: #fee2e2;
      border-radius: 3px;
      color: #b91c1c;
    }
  }

  .degraded-actions {
    display: flex;
    gap: 8px;
    margin-top: 8px;
    margin-left: 8px;
  }
}

.empty-state-badge {
  .empty-state-content {
    margin-top: 4px;
    margin-left: 8px;
    font-size: 13px;
    line-height: 1.7;
    color: #475569;
  }

  .empty-state-actions {
    display: flex;
    gap: 8px;
    margin-top: 8px;
    margin-left: 8px;
    align-items: center;
  }
}

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

  &.ok {
    background: #d1fae5;
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

.filter-row {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  .filter-label {
    margin-right: 8px;
    font-size: 12px;
    color: #64748b;
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

.content-row {
  display: grid;
  grid-template-columns: 380px 1fr;
  gap: 16px;
}

.domain-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 720px;
  overflow-y: auto;
}

.domain-card {
  padding: 12px 14px;
  cursor: pointer;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-left: 3px solid #94a3b8;
  border-radius: 4px;
  transition: all 0.2s;

  &:hover {
    border-color: #3b82f6;
  }

  &.active {
    background: #eff6ff;
    border-color: #3b82f6;
    box-shadow: 0 0 0 2px rgb(59 130 246 / 12%);
  }

  &.inactive {
    opacity: 0.65;
    border-left-color: #cbd5e1;
  }

  .dm-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 6px;

    .dm-key {
      font-family: 'Courier New', monospace;
      font-weight: 600;
      font-size: 13px;
      color: #1e293b;
    }
  }

  .dm-name {
    margin-bottom: 8px;
    font-size: 13px;
    color: #334155;
  }

  .dm-meta {
    display: flex;
    margin-bottom: 6px;
    font-size: 12px;
    gap: 6px;
    align-items: center;

    .meta-key {
      min-width: 60px;
      color: #94a3b8;
    }

    .meta-val {
      font-weight: 600;
      color: #1e293b;
    }

    .meta-factory {
      color: #64748b;
    }
  }

  .dm-foot {
    display: flex;
    justify-content: space-between;
    align-items: center;

    .meta-sort {
      font-size: 11px;
      color: #94a3b8;
    }
  }
}

.detail-head-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-bottom: 12px;
  margin-bottom: 16px;
  border-bottom: 1px solid #e2e8f0;

  .detail-key {
    margin-left: 8px;
    font-family: 'Courier New', monospace;
    font-weight: 600;
    color: #1e293b;
  }

  .ml {
    margin-left: 6px;
  }

  .head-actions {
    display: flex;
    gap: 6px;
  }
}

.detail-form {
  margin-bottom: 16px;
}

.meta-desc {
  margin-bottom: 16px;
}
.meta-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-left: 4px;
}

.detail-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 12px;
  border-top: 1px solid #e2e8f0;
}

.loading-tip {
  padding: 30px;
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
}
</style>
