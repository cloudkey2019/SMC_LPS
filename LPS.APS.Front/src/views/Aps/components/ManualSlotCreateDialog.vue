<!--
  APS V1 4号位 — 新增人工能力槽主档 Dialog

  依据：
    - 冻结文档 v1.3《Resource Calendar 资源日历能力补充冻结方案 v1.3》§四/§五
        「人工能力槽 = 生产部门 + 小工序 + 能力槽编码」，不是员工，不建人员档案 / HR 接口 / 技能矩阵
    - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》§二.3

  契约：POST /api/manual-capacity/slots  body { productionDepartmentId, operationName, slotCode }
    - 唯一键 (productionDepartmentId, operationName, slotCode)，重复 → 后端 400
    - manualSlotId 由库自增生成，新增时不传（回包带出）；后端回包不含 departmentName

  ⚠️ 已知缺口（发函 G3）：后端 DTO 只有 operationName（自由文本），无 operationCode
    → 本表单按自由文本实现，收到回执后若为字典则改为联动下拉
  ⚠️ 部门下拉数据源为过渡方案（发函 G2：无 GET /api/production-departments）
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElOption,
  ElSelect
} from 'element-plus'
import type { DepartmentCandidate, ManualSlotDraft } from '@/api/aps-v1'
import type { FormRules } from 'element-plus'

const props = defineProps<{
  visible: boolean
  submitting?: boolean
  /** 部门候选（过渡数据源；为空时退化为手填 Id） */
  departmentCandidates: DepartmentCandidate[]
  /** 是否过渡数据源（UI 明示） */
  isFallbackSource?: boolean
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [payload: ManualSlotDraft]
}>()

const formRef = ref<InstanceType<typeof ElForm> | null>(null)

const form = ref<ManualSlotDraft>({
  productionDepartmentId: 0,
  operationName: '',
  slotCode: ''
})

const rules = computed<FormRules>(() => ({
  productionDepartmentId: [
    {
      required: true,
      validator: (_r, v, cb) => {
        const n = Number(v)
        if (!Number.isFinite(n) || n <= 0) {
          cb(new Error('生产部门必填'))
          return
        }
        cb()
      },
      trigger: 'change'
    }
  ],
  operationName: [{ required: true, message: '小工序（工序名）必填', trigger: 'blur' }],
  slotCode: [{ required: true, message: '能力槽编码必填', trigger: 'blur' }]
}))

const hasDeptOptions = computed(() => props.departmentCandidates.length > 0)

function deptLabel(d: DepartmentCandidate): string {
  return d.productionDepartmentName ?? '未知部门'
}

watch(
  () => props.visible,
  (vis) => {
    if (!vis) return
    form.value = { productionDepartmentId: 0, operationName: '', slotCode: '' }
    formRef.value?.clearValidate()
  },
  { immediate: true }
)

function close(): void {
  emit('update:visible', false)
}

async function onSubmit(): Promise<void> {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) {
    ElMessage.warning('表单未通过校验，请检查红色提示项')
    return
  }
  emit('submit', {
    productionDepartmentId: Number(form.value.productionDepartmentId),
    operationName: form.value.operationName.trim(),
    slotCode: form.value.slotCode.trim()
  })
}
</script>

<template>
  <ElDialog
    :model-value="visible"
    title="新增人工能力槽"
    width="680px"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    @update:model-value="(v) => emit('update:visible', v)"
  >
    <ElForm ref="formRef" :model="form" :rules="rules" label-width="130px" label-position="right">
      <ElFormItem label="生产部门" prop="productionDepartmentId" required>
        <ElSelect
          v-if="hasDeptOptions"
          v-model="form.productionDepartmentId"
          filterable
          placeholder="选择生产部门"
          style="width: 280px"
        >
          <ElOption
            v-for="d in departmentCandidates"
            :key="d.productionDepartmentId"
            :label="deptLabel(d)"
            :value="d.productionDepartmentId"
          />
        </ElSelect>
        <ElInputNumber v-else v-model="form.productionDepartmentId" :min="1" :step="1" />
        <span class="field-hint">
          {{ hasDeptOptions ? '下拉取自当前计划版本已出现的部门' : '候选为空，请手填部门 Id' }}
        </span>
      </ElFormItem>

      <ElFormItem label="小工序" prop="operationName" required>
        <ElInput
          v-model="form.operationName"
          placeholder="如：精修 / 粗车 / 抛光"
          maxlength="64"
          style="width: 280px"
        />
        <span class="field-hint">填写工序名称，与排产工艺中的小工序对应</span>
      </ElFormItem>

      <ElFormItem label="能力槽编码" prop="slotCode" required>
        <ElInput
          v-model="form.slotCode"
          placeholder="如：精修01 / 精修02"
          maxlength="64"
          style="width: 280px"
        />
      </ElFormItem>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>人工能力槽不是员工</template>
        人工能力槽是有限产能计算中的<strong>抽象能力单元</strong>：生产部门 + 小工序 + 能力槽编码。
        系统不建人员档案、不做技能矩阵、不做人员排班。
        不同生产部门、不同小工序的能力<strong>不可直接混用</strong>。
      </ElAlert>

      <ElAlert type="warning" :closable="false" show-icon class="d-alert">
        <template #title>唯一键</template>
        <code>(生产部门, 小工序, 能力槽编码)</code> 三元组唯一，重复提交会被拒绝。 新建后该槽<strong
          >尚无日历窗口 = 不可排</strong
        >，请在「人工槽日历」页签补窗口。
      </ElAlert>
    </ElForm>

    <template #footer>
      <ElButton :disabled="submitting" @click="close">取消</ElButton>
      <ElButton type="primary" :loading="submitting" @click="onSubmit">确认新增</ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.field-hint {
  margin-left: 10px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.d-alert {
  margin-top: 12px;
}
.d-alert :deep(code) {
  background: rgba(0, 0, 0, 0.05);
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 12px;
}
</style>
