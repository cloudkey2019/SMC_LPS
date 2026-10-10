<!--
  APS V1 4号位 — DEFAULT Setup 规则创建/编辑 Dialog（v1.5 Setup 专项 §4，页面 2.2 子组件）

  职责：
   - 5 元组表单：Department/Stage/Operation/Resource + SetupMinutes（+ RuleSetVersionId）
   - 红线 UI 拦截（§7.1 + §7.2）：
       表单不渲染 FromMaterial/ToMaterial 字段（DEFAULT 必须都空，与 EXACT 互斥）
       SetupMinutes <= 0 → 提交按钮 disabled + 提示
       同 5 元组重复（EXACT+DEFAULT / DEFAULT+DEFAULT）→ 后端 422，前端 ElMessage.error 原文透传

  复用：StrategyDraftDialog.vue 范式（props/emits 契约 + validate().catch(()=>false)）
  权限：仅 aps.setup.edit 持有者可提交（父页面已做按钮显隐，此处双保险）

  09-20 3号位 交付回执偏差适配：偏差 #1 remark 撤除（表单/载荷不含备注）

  09-20 lps 代码同步核实（Id-口径终定 v1.1，SetupRuleDtos.cs）：
   - 写输入提交 Id（productionDepartmentId/resourceId），大工艺/工序传 Code；UI 层仍按 Code 展示
   - 后端 [JsonExtensionData]：DEFAULT 载荷出现含 "material" 键 → 422（前端结构上不可能发出）
   - ⚠️ P0 缺口：无主数据端点 → 真实模式 create 的 Id 暂用 mock 映射，解析失败禁止提交
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
  ElTooltip
} from 'element-plus'
import type { SetupDialogPrefill, SetupRuleDto, SetupRuleDefaultInput } from '@/api/aps-v1'
import { useSetupDefaultStore } from '@/store/modules/aps/setupDefault'

defineOptions({ name: 'SetupDefaultDialog' })

interface Props {
  /** v-model 显隐 */
  modelValue: boolean
  /** create = 新增 / edit = 编辑 */
  mode: 'create' | 'edit'
  /** 编辑对象（mode=edit 时必填） */
  rule?: SetupRuleDto | null
  /** 预填字段（从页面 2.3 "去补 DEFAULT" 跳转带入，§5.4；Code 形态） */
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

const defaultStore = useSetupDefaultStore()

// ===== 表单（UI 层 Code-口径，提交时转 Id-口径 v1.1；无 FromMaterial/ToMaterial 字段 —
// §7.1 红线：DEFAULT 必须都空，后端 JsonExtensionData 对含 "material" 键直接 422） =====
const formRef = ref<InstanceType<typeof ElForm>>()
const form = reactive({
  ruleSetVersionId: 0,
  departmentCode: '',
  stageCode: '',
  operationCode: '',
  resourceCode: '',
  setupMinutes: 30
})

const formRules = {
  departmentCode: [{ required: true, message: '部门必填', trigger: 'change' }],
  stageCode: [{ required: true, message: '大工艺必填', trigger: 'change' }],
  operationCode: [{ required: true, message: '当前工序必填', trigger: 'change' }],
  resourceCode: [{ required: true, message: '设备必填', trigger: 'change' }]
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

const resourceOptions = [
  { label: 'MC001（设备 001）', value: 'MC001', id: 1 },
  { label: 'MC002（设备 002）', value: 'MC002', id: 2 },
  { label: 'MC003（设备 003）', value: 'MC003', id: 3 }
]

/**
 * Code→Id 解析（Id-口径 v1.1：写输入提交 productionDepartmentId/resourceId）
 * 编辑态优先取 DTO 双返回 Id（权威）；新建态查 mock 下拉映射。
 * ⚠️ P0 缺口（已提请 3号位）：后端无主数据端点 → 真实模式 create 的 Id 无权威来源。
 */
function resolveDepartmentId(code: string): number | undefined {
  if (props.mode === 'edit' && props.rule && props.rule.departmentCode === code) {
    return props.rule.productionDepartmentId
  }
  return departmentOptions.find((d) => d.value === code)?.id
}

function resolveResourceId(code: string): number | undefined {
  if (props.mode === 'edit' && props.rule && props.rule.resourceCode === code) {
    return props.rule.resourceId
  }
  return resourceOptions.find((r) => r.value === code)?.id
}

// ===== 红线拦截（§7.1 + §7.2） =====
/** 提交拦截原因（空串 = 可提交） */
const submitBlockReason = computed<string>(() => {
  if (!form.departmentCode || !form.stageCode || !form.operationCode || !form.resourceCode) {
    return '请填写完整 5 元组基础字段（部门/大工艺/工序/设备）'
  }
  if (form.setupMinutes <= 0) {
    return '换型分钟数必须大于 0（0 分钟由兜底自动命中，不需建规则）'
  }
  return ''
})

// ===== 打开时初始化 =====
watch(
  () => props.modelValue,
  (visible) => {
    if (!visible) return
    if (props.mode === 'edit' && props.rule) {
      form.ruleSetVersionId = props.rule.ruleSetVersionId
      form.departmentCode = props.rule.departmentCode ?? ''
      form.stageCode = props.rule.stageCode
      form.operationCode = props.rule.operationCode
      form.resourceCode = props.rule.resourceCode ?? ''
      form.setupMinutes = props.rule.setupMinutes
    } else {
      form.ruleSetVersionId = defaultStore.selectedRuleSetVersionId ?? 0
      form.departmentCode = props.prefill?.departmentCode ?? ''
      form.stageCode = props.prefill?.stageCode ?? ''
      form.operationCode = props.prefill?.operationCode ?? ''
      form.resourceCode = props.prefill?.resourceCode ?? ''
      form.setupMinutes = 30
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
  if (productionDepartmentId == null || resourceId == null) {
    ElMessage.error('无法解析主数据 Id，请检查部门 / 设备输入')
    return
  }

  // ⚠️ 载荷严禁出现含 "material" 的键（后端 JsonExtensionData 拦截 → 422，§7.1 互斥红线）
  const input: SetupRuleDefaultInput = {
    ruleSetVersionId: form.ruleSetVersionId,
    productionDepartmentId,
    stageCode: form.stageCode,
    operationCode: form.operationCode,
    resourceId,
    setupMinutes: form.setupMinutes
  }

  if (props.mode === 'edit' && props.rule) {
    const ok = await defaultStore.updateRule(props.rule.id, input)
    if (ok) {
      ElMessage.success('默认换型规则（DEFAULT）更新成功')
      emit('update:modelValue', false)
      emit('saved')
    } else if (defaultStore.error) {
      // 后端 422（5 元组重复）等错误原文透传（§7.2 红线）
      ElMessage.error(defaultStore.error)
    }
  } else {
    const created = await defaultStore.createRule(input)
    if (created) {
      ElMessage.success('默认换型规则（DEFAULT）创建成功')
      emit('update:modelValue', false)
      emit('saved')
    } else if (defaultStore.error) {
      ElMessage.error(defaultStore.error)
    }
  }
}

function handleClose() {
  emit('update:modelValue', false)
}

const dialogTitle = computed(() =>
  props.mode === 'edit' ? '编辑默认换型规则（DEFAULT）' : '新增默认换型规则（DEFAULT）'
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
    <!-- 规则提示（DEFAULT 专项） -->
    <ElAlert type="warning" :closable="false" show-icon style="margin-bottom: 16px">
      <template #title>默认换型规则要求</template>
      默认换型规则不填前后产品（前产品与后产品必须都为空，与明确转换规则互斥）；换型分钟数必须大于
      0。
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
      <ElFormItem label="设备" prop="resourceCode">
        <ElSelect v-model="form.resourceCode" placeholder="选择设备" style="width: 100%">
          <ElOption
            v-for="opt in resourceOptions"
            :key="opt.value"
            :label="opt.label"
            :value="opt.value"
          />
        </ElSelect>
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
            :loading="defaultStore.actionRunning"
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

.unit-label {
  margin-left: 8px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
</style>
