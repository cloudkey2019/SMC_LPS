<!--
  APS V1 4号位 — 分配 Dialog 公共组件（Pkg-8 RBAC 管理 UI）

  用法（三处复用）：
    - 用户管理 → 分配角色
    - 角色管理 → 分配权限
    - 角色管理 → 分配范围

  关键红线（与 lps/LPS.APS.Engine/Services/Auth/RbacManagementService.cs 一致）：
   1. 覆盖式 PUT：DELETE 全表 → INSERT 当前数组；空数组 = 清空全部
   2. 错误预勾选会静默删权限 → 即使有 preCheckedIds，也必须有顶部 Alert + 提交前二次确认
   3. preCheckedIds 由父组件在打开 Dialog 前调用 4 读回端点加载；
      加载失败时父组件传空数组 + loadingPrecheck=false + showReadBackWarning=true → 顶部 Alert 升 error

  Props：
   - modelValue          v-model 控制显隐
   - title               Dialog 标题（"分配角色" / "分配权限" / "分配范围"）
   - dimension           描述性字段（"角色" / "权限" / "范围"）— 出现在 Alert 文案
   - targetLabel         目标对象标签（"用户 zhangsan" / "角色 aps.planner"）
   - groups              分组选项（按 module / scopeType / 单组）
   - submitting          提交中禁用
   - preCheckedIds       打开时已分配的 id（来自 4 读回端点；为空=默认全不勾选）
   - loadingPrecheck     父组件正在调读回端点（true 时显示 loading 遮罩）
   - showReadBackWarning 读回失败时的额外警告（true=Alert 升 type=error 并加错误文案）

  Emits：
   - update:modelValue  关闭
   - submit             ids: number[]（已去重 + 按勾选顺序）
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessageBox } from 'element-plus'
import { ElAlert, ElButton, ElCheckbox, ElCheckboxGroup, ElDialog, ElScrollbar } from 'element-plus'

export interface AssignOption {
  id: number
  label: string
  sub?: string
}
export interface AssignGroup {
  key: string
  label: string
  options: AssignOption[]
}

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    dimension: string
    targetLabel: string
    groups: AssignGroup[]
    submitting?: boolean
    preCheckedIds?: number[]
    loadingPrecheck?: boolean
    showReadBackWarning?: boolean
  }>(),
  {
    submitting: false,
    preCheckedIds: () => [],
    loadingPrecheck: false,
    showReadBackWarning: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'submit', ids: number[]): void
}>()

/** 当前所有分组勾选的 id（响应式） */
const checkedIds = ref<number[]>([])

/** Dialog 每次打开都把 checkedIds 重置为 preCheckedIds
 *  - 父组件打开 Dialog 前应先 setTarget + loadPrecheck → preCheckedIds 同步到达
 *  - 读回失败时父组件传 [] → 用户面对的是「默认全不勾选 + 顶部 Alert」语义（旧行为）
 */
watch(
  () => props.modelValue,
  (v) => {
    if (v) checkedIds.value = [...(props.preCheckedIds ?? [])]
  }
)

/** preCheckedIds 异步到达时（loadingPrecheck 由 true → false）也同步一次 */
watch(
  () => props.preCheckedIds,
  (ids) => {
    if (props.modelValue && Array.isArray(ids)) {
      checkedIds.value = [...ids]
    }
  }
)

const allOptionCount = computed(() => props.groups.reduce((sum, g) => sum + g.options.length, 0))
const checkedCount = computed(() => checkedIds.value.length)

/** 组内已勾选 id */
const groupCheckedIds = (group: AssignGroup): number[] =>
  checkedIds.value.filter((id) => group.options.some((o) => o.id === id))

/** 组内全选 / 清空 */
const selectAllInGroup = (group: AssignGroup): void => {
  const groupIds = group.options.map((o) => o.id)
  const others = checkedIds.value.filter((id) => !groupIds.includes(id))
  checkedIds.value = [...others, ...groupIds]
}
const clearAllInGroup = (group: AssignGroup): void => {
  const groupIds = new Set(group.options.map((o) => o.id))
  checkedIds.value = checkedIds.value.filter((id) => !groupIds.has(id))
}

/** 关闭 */
const handleClose = (): void => {
  if (props.submitting) return
  emit('update:modelValue', false)
}

/** 二次确认 + submit
 *  - 空数组：清空全部
 *  - 非空：覆盖为 N 项
 */
const handleSubmit = async (): Promise<void> => {
  const ids = Array.from(new Set(checkedIds.value))
  const isEmpty = ids.length === 0

  const confirmText = isEmpty
    ? `确认将「${props.targetLabel}」的${props.dimension}清空？此操作不可撤销。`
    : `确认将「${props.targetLabel}」的${props.dimension}整体替换为 ${ids.length} 项？此操作不可撤销。`

  try {
    await ElMessageBox.confirm(confirmText, '覆盖式写入确认', {
      type: 'warning',
      confirmButtonText: isEmpty ? '确认清空' : '确认替换',
      cancelButtonText: '取消'
    })
  } catch {
    // 用户取消
    return
  }

  emit('submit', ids)
}
</script>

<template>
  <ElDialog
    :model-value="modelValue"
    :title="title"
    width="640"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting && !loadingPrecheck"
    :show-close="!submitting && !loadingPrecheck"
    @update:model-value="(v: boolean) => emit('update:modelValue', v)"
    @close="handleClose"
  >
    <!-- 关键红线：覆盖式写入 -->
    <ElAlert
      :type="showReadBackWarning ? 'error' : 'warning'"
      :closable="false"
      show-icon
      class="assign-dialog-alert"
    >
      <template #title>
        覆盖式写入：提交后「{{ targetLabel }}」的{{ dimension }}将被替换为当前勾选的
        <b>{{ checkedCount }}</b> 项，未勾选项一律移除。
      </template>
      <div class="assign-dialog-alert-detail">
        <template v-if="showReadBackWarning">
          <strong>⚠️ 读取现有分配失败，本弹窗不代表当前生效配置。</strong>
          误操作可能静默移除全部{{ dimension }}，请仔细核对后再提交。
        </template>
        <template v-else> 已按当前生效配置预勾选；如与实际不符，请核对后再提交。 </template>
      </div>
    </ElAlert>

    <div v-loading="loadingPrecheck" element-loading-text="正在加载当前分配…">
      <ElScrollbar max-height="50vh">
        <div v-for="group in groups" :key="group.key" class="assign-group">
          <div class="assign-group-header">
            <span class="assign-group-label">{{ group.label }}</span>
            <span class="assign-group-meta">
              已选 {{ groupCheckedIds(group).length }} / {{ group.options.length }}
            </span>
            <span class="assign-group-actions">
              <ElButton
                link
                type="primary"
                size="small"
                :disabled="submitting || loadingPrecheck"
                @click="selectAllInGroup(group)"
              >
                本组全选
              </ElButton>
              <ElButton
                link
                type="info"
                size="small"
                :disabled="submitting || loadingPrecheck"
                @click="clearAllInGroup(group)"
              >
                本组清空
              </ElButton>
            </span>
          </div>

          <ElCheckboxGroup v-model="checkedIds" :disabled="submitting || loadingPrecheck">
            <div class="assign-options">
              <ElCheckbox
                v-for="opt in group.options"
                :key="opt.id"
                :value="opt.id"
                class="assign-option"
              >
                <span class="assign-option-label">{{ opt.label }}</span>
                <span v-if="opt.sub" class="assign-option-sub">{{ opt.sub }}</span>
              </ElCheckbox>
            </div>
          </ElCheckboxGroup>
        </div>
      </ElScrollbar>
    </div>

    <template #footer>
      <div class="assign-dialog-footer">
        <span class="assign-dialog-stat">
          已选 <b>{{ checkedCount }}</b> 项 / 总计 {{ allOptionCount }} 项
        </span>
        <div class="assign-dialog-actions">
          <ElButton :disabled="submitting" @click="handleClose">取消</ElButton>
          <ElButton type="warning" :loading="submitting" @click="handleSubmit">
            {{ checkedCount === 0 ? '确认清空' : '确认替换' }}
          </ElButton>
        </div>
      </div>
    </template>
  </ElDialog>
</template>

<style scoped>
.assign-dialog-alert {
  margin-bottom: 16px;
}
.assign-dialog-alert-detail {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-regular);
}
.assign-group {
  margin-bottom: 16px;
  padding: 12px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
}
.assign-group-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 8px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.assign-group-label {
  font-weight: 600;
  font-size: 14px;
}
.assign-group-meta {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.assign-group-actions {
  margin-left: auto;
  display: flex;
  gap: 8px;
}
.assign-options {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.assign-option {
  width: 100%;
}
.assign-option-label {
  font-weight: 500;
}
.assign-option-sub {
  margin-left: 8px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.assign-dialog-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.assign-dialog-stat {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.assign-dialog-actions {
  display: flex;
  gap: 8px;
}
</style>
