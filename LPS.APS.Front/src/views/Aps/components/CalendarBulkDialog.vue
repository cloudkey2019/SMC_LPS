<!--
  APS V1 4号位 — 批量铺窗 Dialog（设备资源日历 / 人工槽日历 共用）

  依据：
    - 冻结文档 v1.3《Resource Calendar 资源日历能力补充冻结方案 v1.3》§三/§五/§九
    - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》§二.1 / §二.5

  契约（两模块同构，仅键不同）：
    - 设备：POST /api/resource-calendar/slots          body { resourceId, ...BulkWindowDraft }
    - 人工：POST /api/manual-capacity/calendar         body { manualSlotId, ...BulkWindowDraft }
    - days 1..370（后端 400 校验）；days=1 即"逐条加一天"
    - endTime 须 > startTime（后端 400 校验）

  ⚠️ 追加语义（发函 D2 未决）：后端 BulkCreate 只 INSERT，不删同资源同区间旧窗口
    → 本 Dialog 显式列出"现有窗口"并提示"本次为追加，不覆盖已有 N 条"
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  ElAlert,
  ElButton,
  ElDatePicker,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus'
import { BULK_DAYS_MAX, BULK_DAYS_MIN } from '@/api/aps-v1'
import type { BulkWindowDraft } from '@/api/aps-v1'
import type { FormRules } from 'element-plus'

interface ExistingWindow {
  id: number
  startTime: string
  endTime: string
  availableFlag: boolean
  remark?: string
}

const props = defineProps<{
  visible: boolean
  submitting?: boolean
  /** DEVICE=设备资源日历 / MANUAL_SLOT=人工能力槽日历 */
  mode: 'DEVICE' | 'MANUAL_SLOT'
  /** 铺窗对象键（resourceId / manualSlotId） */
  targetId: number | null
  /** 对象展示名（如 "MC01 注塑机-01" / "精修01（精修）"） */
  targetLabel?: string
  /** 已加载的现有窗口（未查证时为空数组，配合 existingWindowsLoaded 区分） */
  existingWindows: ExistingWindow[]
  existingWindowsLoaded: boolean
}>()

const emit = defineEmits<{
  'update:visible': [v: boolean]
  submit: [payload: BulkWindowDraft]
}>()

/** 时段常用预设（避免手输错格式；夜班跨天会被后端拒，故不列） */
const TIME_PRESETS = [
  { label: '白班 08:00-17:00', start: '08:00:00', end: '17:00:00' },
  { label: '上午 08:00-12:00', start: '08:00:00', end: '12:00:00' },
  { label: '下午 13:00-17:00', start: '13:00:00', end: '17:00:00' }
]

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d:[0-5]\d$/

const formRef = ref<InstanceType<typeof ElForm> | null>(null)

const form = ref<{
  startDate: string
  days: number
  startTime: string
  endTime: string
  availableFlag: boolean
  remark: string
}>({
  startDate: '',
  days: 1,
  startTime: '08:00:00',
  endTime: '17:00:00',
  availableFlag: true,
  remark: ''
})

const rules = computed<FormRules>(() => ({
  startDate: [{ required: true, message: '请选择起始日期', trigger: 'change' }],
  days: [
    {
      required: true,
      validator: (_r, v, cb) => {
        const n = Number(v)
        if (!Number.isFinite(n) || n < BULK_DAYS_MIN || n > BULK_DAYS_MAX) {
          cb(new Error(`天数须为 ${BULK_DAYS_MIN}~${BULK_DAYS_MAX} 的整数`))
          return
        }
        cb()
      },
      trigger: 'change'
    }
  ],
  startTime: [
    {
      required: true,
      validator: (_r, v, cb) => {
        if (!TIME_RE.test(String(v))) {
          cb(new Error('时间格式须为 HH:mm:ss（如 08:00:00）'))
          return
        }
        cb()
      },
      trigger: 'blur'
    }
  ],
  endTime: [
    {
      required: true,
      validator: (_r, v, cb) => {
        const end = String(v)
        if (!TIME_RE.test(end)) {
          cb(new Error('时间格式须为 HH:mm:ss（如 17:00:00）'))
          return
        }
        // 与后端一致：不允许跨天（EndTime <= StartTime → 400）
        if (end <= form.value.startTime) {
          cb(
            new Error(
              `结束时间须晚于开始时间（${form.value.startTime}）；V1 不支持跨天窗口，跨天请拆两条`
            )
          )
          return
        }
        cb()
      },
      trigger: 'blur'
    }
  ]
}))

const modeLabel = computed(() => (props.mode === 'DEVICE' ? '设备资源日历' : '人工能力槽日历'))

const dateHint = computed(() =>
  form.value.days > 1 && form.value.startDate
    ? `将生成 ${form.value.days} 条窗口：${form.value.startDate} 起连续 ${form.value.days} 天，每天 ${form.value.startTime} ~ ${form.value.endTime}`
    : `将生成 1 条窗口：${form.value.startDate || '（未选日期）'} ${form.value.startTime} ~ ${form.value.endTime}`
)

function applyPreset(preset: (typeof TIME_PRESETS)[number]): void {
  form.value.startTime = preset.start
  form.value.endTime = preset.end
}

watch(
  () => props.visible,
  (vis) => {
    if (!vis) return
    form.value = {
      startDate: '',
      days: 1,
      startTime: '08:00:00',
      endTime: '17:00:00',
      availableFlag: true,
      remark: ''
    }
    formRef.value?.clearValidate()
  },
  { immediate: true }
)

function close(): void {
  emit('update:visible', false)
}

async function onSubmit(): Promise<void> {
  if (props.targetId == null || props.targetId <= 0) {
    ElMessage.error('铺窗对象无效（键缺失）')
    return
  }
  if (!formRef.value) return
  const valid = await formRef.value.validate().catch(() => false)
  if (!valid) {
    ElMessage.warning('表单未通过校验，请检查红色提示项')
    return
  }
  emit('submit', {
    startDate: form.value.startDate,
    days: Number(form.value.days),
    startTime: form.value.startTime,
    endTime: form.value.endTime,
    availableFlag: form.value.availableFlag,
    remark: form.value.remark?.trim() ? form.value.remark.trim() : undefined
  })
}
</script>

<template>
  <ElDialog
    :model-value="visible"
    :title="`批量铺窗 —— ${modeLabel}`"
    width="820px"
    :close-on-click-modal="false"
    :close-on-press-escape="!submitting"
    @update:model-value="(v) => emit('update:visible', v)"
  >
    <ElForm ref="formRef" :model="form" :rules="rules" label-width="130px" label-position="right">
      <ElFormItem label="铺窗对象">
        <ElTag type="primary" effect="plain">
          {{ targetLabel || '（未选择）' }}
        </ElTag>
        <span class="field-hint">键 = {{ targetId ?? '—' }}</span>
      </ElFormItem>

      <ElFormItem label="起始日期" prop="startDate" required>
        <ElDatePicker
          v-model="form.startDate"
          type="date"
          value-format="YYYY-MM-DD"
          placeholder="选择起始日期（含当日）"
          style="width: 220px"
        />
      </ElFormItem>

      <ElFormItem label="连续天数" prop="days" required>
        <ElInputNumber v-model="form.days" :min="BULK_DAYS_MIN" :max="BULK_DAYS_MAX" :step="1" />
        <span class="field-hint">{{ BULK_DAYS_MIN }}~{{ BULK_DAYS_MAX }}；填 1 即"逐条加一天"</span>
      </ElFormItem>

      <ElFormItem label="每日时段" required>
        <div class="preset-row">
          <ElButton
            v-for="p in TIME_PRESETS"
            :key="p.label"
            size="small"
            plain
            @click="applyPreset(p)"
          >
            {{ p.label }}
          </ElButton>
        </div>
        <span class="field-hint">一键填入下方时间（不改变天数）</span>
      </ElFormItem>

      <ElFormItem label="开始时间" prop="startTime" required>
        <ElInput v-model="form.startTime" placeholder="08:00:00" style="width: 160px" />
      </ElFormItem>

      <ElFormItem label="结束时间" prop="endTime" required>
        <ElInput v-model="form.endTime" placeholder="17:00:00" style="width: 160px" />
        <span class="field-hint">须晚于开始时间；V1 一个窗口不得跨天</span>
      </ElFormItem>

      <ElFormItem label="可用标记">
        <ElSwitch v-model="form.availableFlag" active-text="可用" inactive-text="禁用" />
        <span class="field-hint">
          禁用窗口（availableFlag=false）不参与排程；仅用于显式表达停机
        </span>
      </ElFormItem>

      <ElFormItem label="备注">
        <ElInput
          v-model="form.remark"
          placeholder="如：白班 / 加班 / 临时维护"
          maxlength="200"
          show-word-limit
          style="width: 420px"
        />
      </ElFormItem>

      <ElAlert type="info" :closable="false" show-icon class="d-alert">
        <template #title>本次将生成</template>
        {{ dateHint }}
      </ElAlert>

      <ElAlert type="warning" :closable="false" show-icon class="d-alert">
        <template #title>追加语义（不覆盖）</template>
        后端铺窗<strong>只追加、不删除已有窗口</strong>。
        同一对象重复铺窗会产生<strong>重叠窗口</strong>；如需替换，请先在列表中删除旧窗口再铺。
      </ElAlert>

      <ElFormItem label="现有窗口" v-if="existingWindowsLoaded">
        <div style="width: 100%">
          <div class="field-hint" style="margin-left: 0">
            当前已有 <strong>{{ existingWindows.length }}</strong> 条窗口（铺窗后为
            {{ existingWindows.length + Number(form.days || 0) }} 条）
          </div>
          <ElTable
            v-if="existingWindows.length > 0"
            :data="existingWindows"
            size="small"
            border
            max-height="200"
            style="width: 100%"
          >
            <ElTableColumn prop="id" label="窗口 Id" width="110" />
            <ElTableColumn label="开始" min-width="180">
              <template #default="{ row }">{{
                row.startTime?.replace('T', ' ').slice(0, 16)
              }}</template>
            </ElTableColumn>
            <ElTableColumn label="结束" min-width="180">
              <template #default="{ row }">{{
                row.endTime?.replace('T', ' ').slice(0, 16)
              }}</template>
            </ElTableColumn>
            <ElTableColumn label="可用" width="80" align="center">
              <template #default="{ row }">
                <ElTag :type="row.availableFlag ? 'success' : 'danger'" size="small">
                  {{ row.availableFlag ? '可用' : '禁用' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn prop="remark" label="备注" min-width="140" show-overflow-tooltip />
          </ElTable>
          <span v-else class="field-hint" style="margin-left: 0">
            尚无窗口 —— 该对象当前 <strong>未配日历 = 不可排</strong>
          </span>
        </div>
      </ElFormItem>
    </ElForm>

    <template #footer>
      <ElButton :disabled="submitting" @click="close">取消</ElButton>
      <ElButton type="primary" :loading="submitting" @click="onSubmit">
        确认铺窗（{{ form.days }} 条）
      </ElButton>
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
</style>
