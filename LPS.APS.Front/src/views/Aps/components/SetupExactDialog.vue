<!--
  APS V1 4号位 — EXACT Setup 规则创建/编辑 Dialog（v1.5 Setup 专项 §3，页面 2.1 子组件）

  职责：
   - 7 元组表单：Department/Stage/Operation/Resource/FromMaterial/ToMaterial + SetupMinutes（+ RuleSetVersionId）
   - 共同合法设备推荐算法 UI 流程（§3.3）：
       选定 OperationCode + FromMaterialCode + ToMaterialCode
       → 调 GET /api/governance/operation-resource-eligibility
       → 设备下拉只展示共同合法设备（非共同设备不可选）
   - 红线 UI 拦截（§7.1 + §7.2）：
       FromMaterial == ToMaterial → 提交按钮 disabled + 提示（同产品连续 = 0 分钟，不存规则）
       SetupMinutes <= 0 → 提交按钮 disabled + 提示
       FromMaterial/ToMaterial 任一空 → 提交按钮 disabled（EXACT 必填）
       设备不在共同合法设备列表 → 提交按钮 disabled（§3.3）
       重复 7 元组 → 后端 422，前端 ElMessage.error 原文透传

  复用：StrategyDraftDialog.vue 范式（props/emits 契约 + validate().catch(()=>false)）
  权限：仅 aps.setup.edit 持有者可提交（父页面已做按钮显隐，此处双保险）

  09-20 3号位 交付回执偏差适配：
   - 偏差 #1：remark 撤除（表单/载荷不含备注）
   - 偏差 #5：eligibility 传 Material.Id（内部主键非 Code）；编辑态优先取 DTO 双返回 fromMaterialId/toMaterialId

  09-20 lps 代码同步核实（Id-口径终定 v1.1，SetupRuleDtos.cs）：
   - 写输入提交 Id（productionDepartmentId/resourceId/fromMaterialId/toMaterialId），大工艺/工序传 Code
   - UI 层下拉/红线仍按 Code（双返回回带展示）；提交时 resolve*Id() 转换
   - ⚠️ P0 缺口：后端无主数据端点、eligibility 响应无 ResourceId → 真实模式 create 的 Id 暂用 mock 映射，
     解析失败时禁止提交（不发 Code 伪载荷）；编辑态 Id 取 DTO 双返回（权威）
-->

<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInputNumber,
  ElMessage,
  ElOption,
  ElSelect,
  ElTag,
  ElTooltip
} from 'element-plus'
import {
  setupApi,
  type SetupDialogPrefill,
  type SetupRuleDto,
  type SetupRuleExactInput
} from '@/api/aps-v1'
import { useSetupExactStore } from '@/store/modules/aps/setupExact'

defineOptions({ name: 'SetupExactDialog' })

interface Props {
  /** v-model 显隐 */
  modelValue: boolean
  /** create = 新增 / edit = 编辑 */
  mode: 'create' | 'edit'
  /** 编辑对象（mode=edit 时必填） */
  rule?: SetupRuleDto | null
  /** 预填字段（从页面 2.3 "去补 EXACT" 跳转带入，§5.4；Code 形态） */
  prefill?: SetupDialogPrefill | null
}

const props = withDefaults(defineProps<Props>(), {
  rule: null,
  prefill: null
})

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}>()

const exactStore = useSetupExactStore()

// ===== 表单（UI 层 Code-口径；提交时转 Id-口径 — v1.1 请求提交 Id / 响应回带 Code） =====
const formRef = ref<InstanceType<typeof ElForm>>()
const form = reactive({
  ruleSetVersionId: 0,
  departmentCode: '',
  stageCode: '',
  operationCode: '',
  resourceCode: '',
  fromMaterialCode: '',
  toMaterialCode: '',
  setupMinutes: 30
})

const formRules = {
  departmentCode: [{ required: true, message: '部门必填', trigger: 'change' }],
  stageCode: [{ required: true, message: '大工艺必填', trigger: 'change' }],
  operationCode: [{ required: true, message: '当前工序必填', trigger: 'change' }],
  resourceCode: [{ required: true, message: '设备必填', trigger: 'change' }],
  fromMaterialCode: [
    { required: true, message: '前产品必填（明确转换规则 7 元组）', trigger: 'change' }
  ],
  toMaterialCode: [
    { required: true, message: '后产品必填（明确转换规则 7 元组）', trigger: 'change' }
  ]
}

// ===== 下拉源（mock 数据，等 3号位 主数据端点落地后切真实源；Id-口径 v1.1 选项带 id） =====
const departmentOptions = [
  { label: 'DEPT_A（注塑车间）', value: 'DEPT_A', id: 1 },
  { label: 'DEPT_B（装配车间）', value: 'DEPT_B', id: 2 }
]

const stageOptions = [
  { label: 'STAGE_1（注塑工艺）', value: 'STAGE_1' },
  { label: 'STAGE_2（装配工艺）', value: 'STAGE_2' }
]

const operationOptions = [
  { label: 'OP10（注塑成型）', value: 'OP10' },
  { label: 'OP20（喷涂）', value: 'OP20' },
  { label: 'OP30（总装）', value: 'OP30' }
]

/** 物料下拉源（mock；09-20 交付回执偏差 #5：eligibility 按 Material.Id 查询，
 *  选项带 id 供推荐端点使用；真实模式待 3号位 物料主数据端点 Code→Id，见 09-20 回函） */
const materialOptions = [
  { label: 'MAT_A（产品 A）', value: 'MAT_A', id: 1 },
  { label: 'MAT_B（产品 B）', value: 'MAT_B', id: 2 },
  { label: 'MAT_C（产品 C）', value: 'MAT_C', id: 3 },
  { label: 'MAT_X（产品 X）', value: 'MAT_X', id: 4 },
  { label: 'MAT_Y（产品 Y）', value: 'MAT_Y', id: 5 }
]

// ===== 共同合法设备推荐（§3.3） =====
const eligibleResourceCodes = ref<string[] | null>(null)
const eligibilityLoading = ref(false)
const eligibilityError = ref<string | null>(null)

/** 设备下拉源：已加载共同合法设备时只展示交集；否则回退 mock 全量（灰色提示） */
const resourceOptions = computed(() => {
  if (eligibleResourceCodes.value) {
    return eligibleResourceCodes.value.map((code) => ({
      label: `${code}（共同合法）`,
      value: code,
      eligible: true
    }))
  }
  return [
    { label: 'MC001（设备 001）', value: 'MC001', eligible: false },
    { label: 'MC002（设备 002）', value: 'MC002', eligible: false },
    { label: 'MC003（设备 003）', value: 'MC003', eligible: false }
  ]
})

/**
 * Code→Id 解析源（Id-口径 v1.1：写输入提交 productionDepartmentId/resourceId）
 * ⚠️ P0 缺口（已提请 3号位）：后端无主数据端点，eligibility 响应仅回 ResourceCode 无 ResourceId
 * → 真实模式 create 流程 Id 无权威来源，暂用 mock 映射；编辑态优先取 DTO 双返回 Id（权威）。
 */
const MOCK_RESOURCE_IDS: Record<string, number> = { MC001: 1, MC002: 2, MC003: 3 }

/** Code → ProductionDepartmentId（编辑态双返回优先，其次 mock 下拉映射） */
function resolveDepartmentId(code: string): number | undefined {
  if (props.mode === 'edit' && props.rule && props.rule.departmentCode === code) {
    return props.rule.productionDepartmentId
  }
  return departmentOptions.find((d) => d.value === code)?.id
}

/** Code → ResourceId（编辑态双返回优先，其次 mock 映射；真实模式待主数据端点） */
function resolveResourceId(code: string): number | undefined {
  if (props.mode === 'edit' && props.rule && props.rule.resourceCode === code) {
    return props.rule.resourceId
  }
  return MOCK_RESOURCE_IDS[code]
}

/**
 * Code → Material.Id 解析（09-20 交付回执偏差 #5：eligibility 传内部主键非 Code）
 * 优先取编辑对象的双返回字段（fromMaterialId/toMaterialId，v1.1 §3 双返回机制），
 * 其次查 materialOptions（mock 映射）；解析不到 = 无法调推荐端点。
 */
function resolveMaterialId(code: string, side: 'from' | 'to'): number | undefined {
  if (props.mode === 'edit' && props.rule) {
    const dualId = side === 'from' ? props.rule.fromMaterialId : props.rule.toMaterialId
    const dualCode = side === 'from' ? props.rule.fromMaterialCode : props.rule.toMaterialCode
    if (dualCode === code && dualId != null) return dualId
  }
  return materialOptions.find((m) => m.value === code)?.id
}

/** 选定 Operation + From/To Material → 调共同合法设备推荐端点 */
async function loadEligibility() {
  if (!form.operationCode || !form.fromMaterialCode || !form.toMaterialCode) {
    eligibleResourceCodes.value = null
    return
  }
  if (form.fromMaterialCode === form.toMaterialCode) {
    eligibleResourceCodes.value = null
    return
  }
  const fromMaterialId = resolveMaterialId(form.fromMaterialCode, 'from')
  const toMaterialId = resolveMaterialId(form.toMaterialCode, 'to')
  if (fromMaterialId == null || toMaterialId == null) {
    // 无法解析 Material.Id（真实模式待物料主数据端点落地）
    eligibilityError.value = '无法解析物料 Id'
    eligibleResourceCodes.value = null
    return
  }
  eligibilityLoading.value = true
  eligibilityError.value = null
  try {
    const dto = await setupApi.getOperationResourceEligibility({
      operationCode: form.operationCode,
      materialId: fromMaterialId,
      toMaterialId
    })
    eligibleResourceCodes.value = dto.resourceCodes
    // 当前选中设备不在交集内 → 清空重选（§3.3 红线：共同合法设备外不允许保存）
    if (form.resourceCode && !dto.resourceCodes.includes(form.resourceCode)) {
      form.resourceCode = ''
      ElMessage.warning('原选设备不在共同合法设备列表内，已清空，请重新选择')
    }
  } catch (err) {
    eligibilityError.value = (err as Error)?.message ?? '共同合法设备查询失败'
    eligibleResourceCodes.value = null
  } finally {
    eligibilityLoading.value = false
  }
}

watch(
  () => [form.operationCode, form.fromMaterialCode, form.toMaterialCode],
  () => {
    if (props.modelValue) void loadEligibility()
  }
)

// ===== 红线拦截（§7.1 + §7.2） =====
/** 提交拦截原因（空串 = 可提交） */
const submitBlockReason = computed<string>(() => {
  if (!form.departmentCode || !form.stageCode || !form.operationCode || !form.resourceCode) {
    return '请填写完整 5 元组基础字段（部门/大工艺/工序/设备）'
  }
  if (!form.fromMaterialCode || !form.toMaterialCode) {
    return '明确转换规则必须填前产品 + 后产品'
  }
  if (form.fromMaterialCode === form.toMaterialCode) {
    return '同产品连续 = 0 分钟，不存规则'
  }
  if (form.setupMinutes <= 0) {
    return '换型分钟数必须大于 0（0 分钟由兜底自动命中，不需建规则）'
  }
  if (eligibleResourceCodes.value && !eligibleResourceCodes.value.includes(form.resourceCode)) {
    return '设备不在共同合法设备列表内，不允许保存'
  }
  return ''
})

// ===== 打开时初始化 =====
watch(
  () => props.modelValue,
  (visible) => {
    if (!visible) return
    eligibilityError.value = null
    eligibleResourceCodes.value = null
    if (props.mode === 'edit' && props.rule) {
      form.ruleSetVersionId = props.rule.ruleSetVersionId
      form.departmentCode = props.rule.departmentCode ?? ''
      form.stageCode = props.rule.stageCode
      form.operationCode = props.rule.operationCode
      form.resourceCode = props.rule.resourceCode ?? ''
      form.fromMaterialCode = props.rule.fromMaterialCode ?? ''
      form.toMaterialCode = props.rule.toMaterialCode ?? ''
      form.setupMinutes = props.rule.setupMinutes
      void loadEligibility()
    } else {
      form.ruleSetVersionId = exactStore.selectedRuleSetVersionId ?? 0
      form.departmentCode = props.prefill?.departmentCode ?? ''
      form.stageCode = props.prefill?.stageCode ?? ''
      form.operationCode = props.prefill?.operationCode ?? ''
      form.resourceCode = props.prefill?.resourceCode ?? ''
      form.fromMaterialCode = props.prefill?.fromMaterialCode ?? ''
      form.toMaterialCode = props.prefill?.toMaterialCode ?? ''
      form.setupMinutes = 30
      if (props.prefill) void loadEligibility()
    }
    formRef.value?.clearValidate()
  }
)

// ===== 提交 =====
async function handleSubmit() {
  if (submitBlockReason.value) {
    ElMessage.warning(submitBlockReason.value)
    return
  }
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  // Id-口径转换（v1.1：请求提交 Id；解析失败 = 主数据缺口，禁止发 Code 伪载荷）
  const productionDepartmentId = resolveDepartmentId(form.departmentCode)
  const resourceId = resolveResourceId(form.resourceCode)
  const fromMaterialId = resolveMaterialId(form.fromMaterialCode, 'from')
  const toMaterialId = resolveMaterialId(form.toMaterialCode, 'to')
  if (
    productionDepartmentId == null ||
    resourceId == null ||
    fromMaterialId == null ||
    toMaterialId == null
  ) {
    ElMessage.error('无法解析主数据 Id，请检查部门 / 设备 / 产品输入')
    return
  }

  const input: SetupRuleExactInput = {
    ruleSetVersionId: form.ruleSetVersionId,
    productionDepartmentId,
    stageCode: form.stageCode,
    operationCode: form.operationCode,
    resourceId,
    fromMaterialId,
    toMaterialId,
    setupMinutes: form.setupMinutes
  }

  if (props.mode === 'edit' && props.rule) {
    const ok = await exactStore.updateRule(props.rule.id, input)
    if (ok) {
      ElMessage.success('明确转换规则（EXACT）更新成功')
      emit('update:modelValue', false)
      emit('saved')
    } else if (exactStore.error) {
      // 后端 422（7 元组重复）等错误原文透传（§7.2 红线）
      ElMessage.error(exactStore.error)
    }
  } else {
    const created = await exactStore.createRule(input)
    if (created) {
      ElMessage.success('明确转换规则（EXACT）创建成功')
      emit('update:modelValue', false)
      emit('saved')
    } else if (exactStore.error) {
      ElMessage.error(exactStore.error)
    }
  }
}

function handleClose() {
  emit('update:modelValue', false)
}

const dialogTitle = computed(() =>
  props.mode === 'edit' ? '编辑明确转换规则（EXACT）' : '新增明确转换规则（EXACT）'
)
</script>

<template>
  <ElDialog
    :model-value="modelValue"
    :title="dialogTitle"
    width="640px"
    :close-on-click-modal="false"
    @update:model-value="handleClose"
  >
    <!-- 规则提示（明确转换规则专项） -->
    <ElAlert type="warning" :closable="false" show-icon style="margin-bottom: 16px">
      <template #title>明确转换规则要求</template>
      前产品 ≠ 后产品（同产品连续 = 0 分钟，不存规则）；换型分钟数必须大于 0；
      设备必须在共同合法设备列表内。
    </ElAlert>

    <ElForm ref="formRef" :model="form" :rules="formRules" label-width="120px">
      <ElFormItem label="部门" prop="departmentCode">
        <ElSelect v-model="form.departmentCode" placeholder="选择部门" style="width: 100%">
          <ElOption
            v-for="opt in departmentOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
      </ElFormItem>
      <ElFormItem label="大工艺" prop="stageCode">
        <ElSelect v-model="form.stageCode" placeholder="选择大工艺" style="width: 100%">
          <ElOption
            v-for="opt in stageOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
      </ElFormItem>
      <ElFormItem label="当前工序" prop="operationCode">
        <ElSelect
          v-model="form.operationCode"
          placeholder="选择当前工序（唯一工序维度）"
          style="width: 100%"
        >
          <ElOption
            v-for="opt in operationOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
      </ElFormItem>
      <ElFormItem label="前产品" prop="fromMaterialCode">
        <ElSelect v-model="form.fromMaterialCode" placeholder="选择前产品" style="width: 100%">
          <ElOption
            v-for="opt in materialOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
      </ElFormItem>
      <ElFormItem label="后产品" prop="toMaterialCode">
        <ElSelect v-model="form.toMaterialCode" placeholder="选择后产品" style="width: 100%">
          <ElOption
            v-for="opt in materialOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
            :disabled="opt.value === form.fromMaterialCode"
          >
            <span>{{ opt.label }}</span>
            <ElTag
              v-if="opt.value === form.fromMaterialCode"
              type="danger"
              size="small"
              style="margin-left: 8px"
            >
              同产品 = 0 分钟
            </ElTag>
          </ElOption>
        </ElSelect>
        <div
          v-if="form.fromMaterialCode && form.toMaterialCode === form.fromMaterialCode"
          class="redline-hint"
        >
          同产品连续 = 0 分钟，不存规则
        </div>
      </ElFormItem>

      <!-- 共同合法设备推荐（§3.3） -->
      <ElFormItem label="设备" prop="resourceCode">
        <ElSelect
          v-model="form.resourceCode"
          placeholder="选择设备（共同合法设备）"
          :loading="eligibilityLoading"
          style="width: 100%"
        >
          <ElOption
            v-for="opt in resourceOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
        <div v-if="eligibilityLoading" class="eligibility-hint">共同合法设备查询中…</div>
        <div v-else-if="eligibilityError" class="redline-hint"
          >共同合法设备查询失败：{{ eligibilityError }}</div
        >
        <div v-else-if="eligibleResourceCodes" class="eligibility-hint">
          共同合法设备 {{ eligibleResourceCodes.length }} 台（当前工序与前后产品的交集）
        </div>
        <div v-else class="eligibility-hint"> 选定工序 + 前后产品后自动查询共同合法设备 </div>
      </ElFormItem>

      <ElFormItem label="换型分钟数" required>
        <ElInputNumber
          v-model="form.setupMinutes"
          :min="0"
          :step="5"
          controls-position="right"
          style="width: 180px"
        />
        <span class="unit-label">分钟</span>
        <div v-if="form.setupMinutes <= 0" class="redline-hint"> 换型分钟数必须大于 0 </div>
      </ElFormItem>
    </ElForm>

    <template #footer>
      <ElButton @click="handleClose">取消</ElButton>
      <ElTooltip :content="submitBlockReason" :disabled="!submitBlockReason" placement="top">
        <span>
          <ElButton
            type="primary"
            :loading="exactStore.actionRunning"
            :disabled="!!submitBlockReason"
            @click="handleSubmit"
          >
            {{ mode === 'edit' ? '保存修改' : '创建规则' }}
          </ElButton>
        </span>
      </ElTooltip>
    </template>
  </ElDialog>
</template>

<style scoped>
.redline-hint {
  font-size: 12px;
  color: var(--el-color-danger);
  line-height: 1.5;
  margin-top: 4px;
}

.eligibility-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.5;
  margin-top: 4px;
}

.unit-label {
  margin-left: 8px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
</style>
