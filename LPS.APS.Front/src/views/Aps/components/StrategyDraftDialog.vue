<!--
  APS V1 4号位 — 策略 DRAFT 创建/编辑 Dialog

  复用 Rules.vue Publish Dialog 模式（changeReason + ElAlert + 二次确认），扩展为完整 DRAFT 表单
  字段（与后端 StrategyProfileVersion 实体对齐）：
    - strategyProfileId（隐藏，外部传）
    - versionCode        必填
    - ruleSetVersionId   必填，int —— 通过 RuleSet → RuleSetVersion 双级 ElSelect 选择
    - parameterSetVersionId 必填，int —— 通过 ParameterSet → ParameterSetVersion 双级 ElSelect 选择
    - effectiveFrom      可选
    - effectiveTo        可选
    - isDefault          仅在 Profile 已有 PUBLISHED 时显示（红线 UI 三重防护 #1）

  v1.4 §十八.10 修复（P1-12 反馈 500 Internal Server Error）：
    原 ElInputNumber 让用户自由填 ID；FK_StrategyProfileVersion_RuleSetVersion 不通过即 SqlException → 500。
    现改 ElSelect 双级级联：必须先选主表 → 才能选版本；版本必须存在，避免 FK 违反。

  模式：
    - mode=create：创建 DRAFT，提交后仅调 POST /version
    - mode=edit：  编辑 DRAFT，提交后仅调 PUT /version/{id}

  提交成功通过 emit('submit') 抛回 StrategyVersionDraftInput，由父组件决定调 store.createDraft / updateDraft
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElCheckbox,
  ElDatePicker,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect
} from 'element-plus'
import {
  strategyProfileApi,
  type ParameterSetDto,
  type ParameterSetVersionDto,
  type RuleSetDto,
  type RuleSetVersionDto,
  type StrategyProfileDto,
  type StrategyProfileVersionDto,
  type StrategyVersionDraftInput,
  type StrategyVersionStatus
} from '@/api/aps-v1'

const props = defineProps<{
  visible: boolean
  mode: 'create' | 'edit'
  /** 当前选中的 Profile（Dialog 上下文） */
  profile: StrategyProfileDto | null
  /** 编辑模式时：当前版本（创建模式可空） */
  version?: StrategyProfileVersionDto | null
  /** 已有任何 PUBLISHED 版本 → 决定 IsDefault 勾选框是否显示 */
  hasPublished: boolean
  /** 提交中（父组件 loading 状态） */
  submitting?: boolean
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [input: StrategyVersionDraftInput]
}>()

interface DraftForm {
  versionCode: string
  ruleSetId: number | undefined
  ruleSetVersionId: number | undefined
  parameterSetId: number | undefined
  parameterSetVersionId: number | undefined
  effectiveFrom: string | undefined
  effectiveTo: string | undefined
  isDefault: boolean
}

const form = reactive<DraftForm>({
  versionCode: '',
  ruleSetId: undefined,
  ruleSetVersionId: undefined,
  parameterSetId: undefined,
  parameterSetVersionId: undefined,
  effectiveFrom: undefined,
  effectiveTo: undefined,
  isDefault: false
})

const formRef = ref<InstanceType<typeof ElForm> | null>(null)

/* ===== 下拉源数据 ===== */
const ruleSets = ref<RuleSetDto[]>([])
const ruleSetVersions = ref<RuleSetVersionDto[]>([])
const parameterSets = ref<ParameterSetDto[]>([])
const parameterSetVersions = ref<ParameterSetVersionDto[]>([])

const ruleSetsLoading = ref(false)
const ruleSetVersionsLoading = ref(false)
const parameterSetsLoading = ref(false)
const parameterSetVersionsLoading = ref(false)

async function loadRuleSets(): Promise<void> {
  ruleSetsLoading.value = true
  try {
    ruleSets.value = await strategyProfileApi.listRuleSets({ activeOnly: true, pageSize: 500 })
  } catch (err) {
    ElMessage.error(`加载规则集主表失败：${(err as Error).message}`)
  } finally {
    ruleSetsLoading.value = false
  }
}

async function loadRuleSetVersions(ruleSetId: number | undefined): Promise<void> {
  ruleSetVersions.value = []
  form.ruleSetVersionId = undefined
  if (ruleSetId === undefined) return
  ruleSetVersionsLoading.value = true
  try {
    ruleSetVersions.value = await strategyProfileApi.listRuleSetVersions(ruleSetId)
  } catch (err) {
    ElMessage.error(`加载规则集版本失败：${(err as Error).message}`)
  } finally {
    ruleSetVersionsLoading.value = false
  }
}

async function loadParameterSets(): Promise<void> {
  parameterSetsLoading.value = true
  try {
    parameterSets.value = await strategyProfileApi.listParameterSets({
      activeOnly: true,
      pageSize: 500
    })
  } catch (err) {
    ElMessage.error(`加载参数集主表失败：${(err as Error).message}`)
  } finally {
    parameterSetsLoading.value = false
  }
}

async function loadParameterSetVersions(parameterSetId: number | undefined): Promise<void> {
  parameterSetVersions.value = []
  form.parameterSetVersionId = undefined
  if (parameterSetId === undefined) return
  parameterSetVersionsLoading.value = true
  try {
    parameterSetVersions.value = await strategyProfileApi.listParameterSetVersions(parameterSetId)
  } catch (err) {
    ElMessage.error(`加载参数集版本失败：${(err as Error).message}`)
  } finally {
    parameterSetVersionsLoading.value = false
  }
}

onMounted(() => {
  loadRuleSets()
  loadParameterSets()
})

/** 选 RuleSet 时清空 RuleSetVersion 并加载下属版本 */
watch(
  () => form.ruleSetId,
  (newId, oldId) => {
    if (newId === oldId) return
    void loadRuleSetVersions(newId)
  }
)

/** 选 ParameterSet 时清空 ParameterSetVersion 并加载下属版本 */
watch(
  () => form.parameterSetId,
  (newId, oldId) => {
    if (newId === oldId) return
    void loadParameterSetVersions(newId)
  }
)

/** 打开 / 切换模式时重置表单 */
watch(
  () => [props.visible, props.mode, props.version?.id],
  async ([vis]) => {
    if (!vis) return
    if (props.mode === 'edit' && props.version) {
      form.versionCode = props.version.versionCode
      form.ruleSetVersionId = props.version.ruleSetVersionId
      form.parameterSetVersionId = props.version.parameterSetVersionId
      form.effectiveFrom = props.version.effectiveFrom ?? undefined
      form.effectiveTo = props.version.effectiveTo ?? undefined
      form.isDefault = props.version.isDefault
      // 编辑模式：根据已知 ruleSetVersionId 反查父 RuleSet ID
      if (form.ruleSetVersionId !== undefined) {
        try {
          const allSets = await strategyProfileApi.listRuleSets({ pageSize: 500 })
          // 找不到精确 ruleSetId 时不强写，让用户重选
          for (const rs of allSets) {
            const vers = await strategyProfileApi.listRuleSetVersions(rs.id).catch(() => [])
            if (vers.some((v: RuleSetVersionDto) => v.id === form.ruleSetVersionId)) {
              form.ruleSetId = rs.id
              await loadRuleSetVersions(rs.id)
              form.ruleSetVersionId = props.version.ruleSetVersionId
              break
            }
          }
        } catch {
          /* 静默：用户可手动重选 */
        }
      }
      if (form.parameterSetVersionId !== undefined) {
        try {
          const allSets = await strategyProfileApi.listParameterSets({ pageSize: 500 })
          for (const ps of allSets) {
            const vers = await strategyProfileApi.listParameterSetVersions(ps.id).catch(() => [])
            if (vers.some((v: ParameterSetVersionDto) => v.id === form.parameterSetVersionId)) {
              form.parameterSetId = ps.id
              await loadParameterSetVersions(ps.id)
              form.parameterSetVersionId = props.version.parameterSetVersionId
              break
            }
          }
        } catch {
          /* 静默 */
        }
      }
    } else {
      form.versionCode = ''
      form.ruleSetId = undefined
      form.ruleSetVersionId = undefined
      form.parameterSetId = undefined
      form.parameterSetVersionId = undefined
      form.effectiveFrom = undefined
      form.effectiveTo = undefined
      form.isDefault = false
      ruleSetVersions.value = []
      parameterSetVersions.value = []
    }
  },
  { immediate: true }
)

const rules = computed(() => ({
  versionCode: [
    { required: true, message: '请输入版本号', trigger: 'blur' },
    { min: 3, max: 64, message: '长度 3-64 字符', trigger: 'blur' }
  ],
  ruleSetId: [{ required: true, message: '请选择规则集', trigger: 'change' }],
  ruleSetVersionId: [{ required: true, message: '请选择规则集版本', trigger: 'change' }],
  parameterSetId: [{ required: true, message: '请选择参数集', trigger: 'change' }],
  parameterSetVersionId: [{ required: true, message: '请选择参数集版本', trigger: 'change' }]
}))

const title = computed<string>(() =>
  props.mode === 'create'
    ? '新建策略版本（DRAFT）'
    : `编辑策略版本 ${props.version?.versionCode ?? ''}`
)

function close(): void {
  emit('update:visible', false)
}

const versionStatusLabel: Record<StrategyVersionStatus, string> = {
  DRAFT: '草稿',
  SUBMITTED: '已提交',
  APPROVED: '已批准',
  PUBLISHED: '已发布',
  DISABLED: '已退役',
  ARCHIVED: '已归档'
}

async function onSubmit(): Promise<void> {
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) return
  if (!props.profile) return
  const input: StrategyVersionDraftInput = {
    strategyProfileId: props.profile.id,
    versionCode: form.versionCode.trim(),
    ruleSetVersionId: form.ruleSetVersionId!,
    parameterSetVersionId: form.parameterSetVersionId!,
    effectiveFrom: form.effectiveFrom ?? null,
    effectiveTo: form.effectiveTo ?? null,
    isDefault: form.isDefault
  }
  emit('submit', input)
}
</script>

<template>
  <ElDialog
    :model-value="visible"
    :title="title"
    width="640px"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    @update:model-value="(v) => emit('update:visible', v)"
  >
    <ElForm ref="formRef" :model="form" :rules="rules" label-width="140px" label-position="right">
      <ElFormItem label="所属 Profile">
        <span class="d-val">{{ profile?.strategyProfileCode ?? '—' }}</span>
        <span class="d-sub">（{{ profile?.strategyProfileName }}）</span>
      </ElFormItem>

      <ElFormItem label="版本号" prop="versionCode">
        <ElInput
          v-model="form.versionCode"
          placeholder="如 v1.2.0 或 v1.2.0-draft"
          :disabled="mode === 'edit'"
        />
      </ElFormItem>

      <ElFormItem label="规则集" prop="ruleSetId">
        <ElSelect
          v-model="form.ruleSetId"
          placeholder="先选规则集主表"
          filterable
          :loading="ruleSetsLoading"
          style="width: 100%"
        >
          <ElOption
            v-for="rs in ruleSets"
            :key="rs.id"
            :value="rs.id"
            :label="`${rs.ruleSetCode} — ${rs.ruleSetName}`"
          />
        </ElSelect>
      </ElFormItem>

      <ElFormItem label="规则集版本" prop="ruleSetVersionId">
        <ElSelect
          v-model="form.ruleSetVersionId"
          placeholder="先选规则集再选版本"
          filterable
          :loading="ruleSetVersionsLoading"
          :disabled="form.ruleSetId === undefined"
          style="width: 100%"
        >
          <ElOption
            v-for="v in ruleSetVersions"
            :key="v.id"
            :value="v.id"
            :label="`${v.versionCode} (${versionStatusLabel[v.status] ?? v.status})`"
          />
        </ElSelect>
      </ElFormItem>

      <ElFormItem label="参数集" prop="parameterSetId">
        <ElSelect
          v-model="form.parameterSetId"
          placeholder="先选参数集主表"
          filterable
          :loading="parameterSetsLoading"
          style="width: 100%"
        >
          <ElOption
            v-for="ps in parameterSets"
            :key="ps.id"
            :value="ps.id"
            :label="`${ps.parameterSetCode} — ${ps.parameterSetName}`"
          />
        </ElSelect>
      </ElFormItem>

      <ElFormItem label="参数集版本" prop="parameterSetVersionId">
        <ElSelect
          v-model="form.parameterSetVersionId"
          placeholder="先选参数集再选版本"
          filterable
          :loading="parameterSetVersionsLoading"
          :disabled="form.parameterSetId === undefined"
          style="width: 100%"
        >
          <ElOption
            v-for="v in parameterSetVersions"
            :key="v.id"
            :value="v.id"
            :label="`${v.versionCode} (${versionStatusLabel[v.status] ?? v.status})`"
          />
        </ElSelect>
      </ElFormItem>

      <ElFormItem label="生效起始">
        <ElDatePicker
          v-model="form.effectiveFrom"
          type="datetime"
          placeholder="可选"
          format="YYYY-MM-DD HH:mm:ss"
          value-format="YYYY-MM-DDTHH:mm:ss[Z]"
          style="width: 100%"
        />
      </ElFormItem>

      <ElFormItem label="生效截止">
        <ElDatePicker
          v-model="form.effectiveTo"
          type="datetime"
          placeholder="可选"
          format="YYYY-MM-DD HH:mm:ss"
          value-format="YYYY-MM-DDTHH:mm:ss[Z]"
          style="width: 100%"
        />
      </ElFormItem>

      <ElFormItem v-if="hasPublished" label="设为默认">
        <ElCheckbox v-model="form.isDefault"> 标记此版本为 Profile 的默认 PUBLISHED </ElCheckbox>
      </ElFormItem>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>状态流转</template>
        {{ mode === 'create' ? '创建后状态固定为 DRAFT' : '仅 DRAFT 状态可编辑' }}； 发布时调用
        <code>POST /strategy-profile/version/{id}/publish</code>， 退役仅对 PUBLISHED 生效。
      </ElAlert>

      <ElAlert
        v-if="hasPublished && form.isDefault"
        type="warning"
        :closable="false"
        show-icon
        class="d-alert"
      >
        <template #title>设为默认版本的影响</template>
        发布后，同一策略包下原有的默认版本会被自动取消默认。
      </ElAlert>
    </ElForm>
    <template #footer>
      <ElButton :disabled="submitting" @click="close">取消</ElButton>
      <ElButton type="primary" :loading="submitting" @click="onSubmit">
        {{ mode === 'create' ? '创建 DRAFT' : '保存' }}
      </ElButton>
    </template>
  </ElDialog>
</template>

<style scoped>
.d-val {
  font-weight: 600;
  margin-right: 6px;
}
.d-sub {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.d-alert {
  margin-top: 12px;
}
</style>
