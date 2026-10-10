/**
 * §10A 白天人工调整 —— 业务触发八码 ↔ RunType×Purpose 矩阵 + scope 载荷构建/预校验
 *
 * @owner 4号位（前端预校验 + 出口封装）
 * @see 冻结文档《4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
 * @see lps/LPS.APS.Application/Services/ScopeJsonV2Validator.cs（后端同源校验，只读参照）
 *
 * 职责：
 *  1. `TRIGGER_RUN_MATRIX`：八码 → (runType, purpose, PriorityMode 轴规则, 多对象字段) 单一真相；
 *  2. `validateScopeDraft()`：**前端唯一预校验入口**——中文错误数组，覆盖 PriorityMode 轴 /
 *     三类 targets 非空 + 去重 + 字段必填（后端违规 → HTTP 400，前端提前拦）；
 *  3. `buildScope()`：草稿 → ScopeJsonV2；**MUST_OMIT 码必须 delete priorityMode 键**
 *     （10A.5 传 `"priorityMode": null` 亦 400，须省略）；
 *  4. `triggerBusinessEntry()`：五入口统一出口 → runApi.triggerReschedule。
 *
 * ⚠️ 大小写敏感精确匹配（后端 Ordinal 比较）；scope 缺省 = 后端静默放行（旧调用兼容）。
 */

import { runApi } from './run'
import type {
  BusinessTriggerType,
  DomainKey,
  IsoDateTime,
  OrderTargetDto,
  PriorityMode,
  ScopeJsonV2,
  TaskTargetDto,
  TriggerRescheduleResult
} from './types'

/** PriorityMode 轴规则（对齐 ScopeJsonV2Validator.ValidatePriorityMode） */
export type PriorityModeRule =
  /** 不限制（EXISTING_ORDER_ADVANCE / NEW_ORDER_INSERT） */
  | 'FREE'
  /** 固定 NORMAL，禁 EXPEDITE */
  | 'FORBID_EXPEDITE'
  /** 固定 EXPEDITE，禁 NORMAL */
  | 'FORBID_NORMAL'
  /** 须省略该键（传 NORMAL/EXPEDITE 均 400） */
  | 'MUST_OMIT'

/** 多对象载荷字段（§10A.1-10A.4） */
export type ScopeTargetField = 'orderTargets' | 'taskTargets' | 'changedResourceIds' | 'none'

export interface TriggerRunSpec {
  runType: 'LOCAL_RESCHEDULE' | 'MANUAL_RESCHEDULE' | 'INSERT_ORDER_WHATIF'
  purpose: 'MANUAL_ADJUSTMENT' | 'INSERT_RESCHEDULE' | 'CTP' | 'INSERT_IMPACT_ANALYSIS'
  priorityModeRule: PriorityModeRule
  targetField: ScopeTargetField
  /** §10A 五入口之一（前端提供独立中文入口） */
  isBusinessEntry: boolean
  /** 入口标题（§10A 五入口直接用） */
  entryTitle: string
}

/**
 * 八码 ↔ RunType×Purpose 冻结矩阵
 * @see ScopeJsonV2Validator.cs:34-45（映射）+ :61-86（PriorityMode 轴）
 */
export const TRIGGER_RUN_MATRIX: Record<BusinessTriggerType, TriggerRunSpec> = {
  NEW_ORDER_CTP: {
    runType: 'INSERT_ORDER_WHATIF',
    purpose: 'CTP',
    priorityModeRule: 'FORBID_EXPEDITE',
    targetField: 'orderTargets',
    isBusinessEntry: false,
    entryTitle: '新订单 CTP 评估'
  },
  NEW_ORDER_IMPACT: {
    runType: 'INSERT_ORDER_WHATIF',
    purpose: 'INSERT_IMPACT_ANALYSIS',
    priorityModeRule: 'FORBID_NORMAL',
    targetField: 'orderTargets',
    isBusinessEntry: false,
    entryTitle: '插单影响分析'
  },
  NEW_ORDER_INSERT: {
    runType: 'LOCAL_RESCHEDULE',
    purpose: 'INSERT_RESCHEDULE',
    priorityModeRule: 'FREE',
    targetField: 'orderTargets',
    isBusinessEntry: false,
    entryTitle: '新订单插单'
  },
  EXISTING_ORDER_ADVANCE: {
    runType: 'LOCAL_RESCHEDULE',
    purpose: 'MANUAL_ADJUSTMENT',
    priorityModeRule: 'FREE',
    targetField: 'orderTargets',
    isBusinessEntry: true,
    entryTitle: '已有订单提前'
  },
  GANTT_ADJUSTMENT: {
    runType: 'LOCAL_RESCHEDULE',
    purpose: 'MANUAL_ADJUSTMENT',
    priorityModeRule: 'FORBID_EXPEDITE',
    targetField: 'taskTargets',
    isBusinessEntry: true,
    entryTitle: '甘特图调整'
  },
  EQUIPMENT_FAILURE: {
    runType: 'LOCAL_RESCHEDULE',
    purpose: 'MANUAL_ADJUSTMENT',
    priorityModeRule: 'FORBID_EXPEDITE',
    targetField: 'changedResourceIds',
    isBusinessEntry: true,
    entryTitle: '设备故障后重排'
  },
  RESOURCE_CALENDAR_CHANGE: {
    runType: 'LOCAL_RESCHEDULE',
    purpose: 'MANUAL_ADJUSTMENT',
    priorityModeRule: 'FORBID_EXPEDITE',
    targetField: 'changedResourceIds',
    isBusinessEntry: true,
    entryTitle: '资源日历调整后重排'
  },
  DOMAIN_MANUAL_RESCHEDULE: {
    runType: 'MANUAL_RESCHEDULE',
    purpose: 'MANUAL_ADJUSTMENT',
    priorityModeRule: 'MUST_OMIT',
    targetField: 'none',
    isBusinessEntry: true,
    entryTitle: '整 Domain 人工重排'
  }
}

/** §10A 五业务入口（顺序即 UI 呈现顺序） */
export const BUSINESS_ENTRIES: BusinessTriggerType[] = [
  'EXISTING_ORDER_ADVANCE',
  'GANTT_ADJUSTMENT',
  'EQUIPMENT_FAILURE',
  'RESOURCE_CALENDAR_CHANGE',
  'DOMAIN_MANUAL_RESCHEDULE'
]

/** 草稿：Dialog 收集的原始输入（未校验） */
export interface ScopeDraft {
  priorityMode?: PriorityMode
  orderTargets?: OrderTargetDto[]
  taskTargets?: TaskTargetDto[]
  changedResourceIds?: number[]
}

const isPositiveInt = (v: unknown): v is number =>
  typeof v === 'number' && Number.isInteger(v) && v > 0

/**
 * 前端唯一预校验入口（中文错误数组；空数组 = 通过）
 * 覆盖：PriorityMode 轴 / 三类 targets 非空 + 去重 + 字段必填 / MUST_OMIT 码禁带 targets
 */
export function validateScopeDraft(trigger: BusinessTriggerType, draft: ScopeDraft): string[] {
  const errors: string[] = []
  const spec = TRIGGER_RUN_MATRIX[trigger]

  if (!spec) {
    return [`触发类型非法：${trigger}（不在冻结八码内）`]
  }

  // ① PriorityMode 轴
  if (spec.priorityModeRule === 'FORBID_EXPEDITE' && draft.priorityMode === 'EXPEDITE') {
    errors.push(`${trigger} 固定 NORMAL，不得选择加急（EXPEDITE）`)
  }
  if (spec.priorityModeRule === 'FORBID_NORMAL' && draft.priorityMode === 'NORMAL') {
    errors.push(`${trigger} 固定 EXPEDITE，不得选择普通（NORMAL）`)
  }
  if (spec.priorityModeRule === 'MUST_OMIT' && draft.priorityMode !== undefined) {
    errors.push(`${trigger} 走既有正式优先规则，不得携带优先模式（须省略该字段）`)
  }

  // ② 多对象载荷
  switch (spec.targetField) {
    case 'orderTargets': {
      const list = draft.orderTargets ?? []
      if (list.length === 0) {
        errors.push('请至少选择 1 个订单目标')
      }
      list.forEach((t, i) => {
        if (!isPositiveInt(t.orderCanonicalId)) {
          errors.push(`第 ${i + 1} 行：订单规范 Id（OrderCanonicalId）须为正整数`)
        }
        if (!t.manualTargetDueDate) {
          errors.push(`第 ${i + 1} 行：手工目标交期不能为空`)
        }
      })
      const ids = list.map((t) => t.orderCanonicalId).filter(isPositiveInt)
      if (new Set(ids).size !== ids.length) {
        errors.push('订单目标含重复的 OrderCanonicalId（须去重唯一）')
      }
      if (draft.taskTargets?.length || draft.changedResourceIds?.length) {
        errors.push(`${trigger} 仅接受订单目标，不得携带 Task/资源载荷`)
      }
      break
    }
    case 'taskTargets': {
      const list = draft.taskTargets ?? []
      if (list.length === 0) {
        errors.push('请至少选择 1 个 Task 目标')
      }
      list.forEach((t, i) => {
        if (!isPositiveInt(t.taskId)) {
          errors.push(`第 ${i + 1} 行：Task 主键非法`)
        }
        if (!t.targetTime) {
          errors.push(`第 ${i + 1} 行：软目标时间不能为空`)
        }
      })
      const ids = list.map((t) => t.taskId).filter(isPositiveInt)
      if (new Set(ids).size !== ids.length) {
        errors.push('Task 目标含重复的 taskId（须去重唯一）')
      }
      if (draft.orderTargets?.length || draft.changedResourceIds?.length) {
        errors.push(`${trigger} 仅接受 Task 目标，不得携带订单/资源载荷`)
      }
      break
    }
    case 'changedResourceIds': {
      const list = draft.changedResourceIds ?? []
      if (list.length === 0) {
        errors.push('请至少选择 1 个资源')
      }
      if (list.some((id) => !isPositiveInt(id))) {
        errors.push('资源 Id 须为正整数')
      }
      if (new Set(list).size !== list.length) {
        errors.push('资源 Id 含重复项（须去重唯一）')
      }
      if (draft.orderTargets?.length || draft.taskTargets?.length) {
        errors.push(`${trigger} 仅接受资源载荷，不得携带订单/Task 载荷`)
      }
      break
    }
    case 'none': {
      if (
        draft.orderTargets?.length ||
        draft.taskTargets?.length ||
        draft.changedResourceIds?.length
      ) {
        errors.push(`${trigger} 不接受多对象载荷（整 Domain 范围）`)
      }
      break
    }
  }

  return errors
}

/**
 * 草稿 → ScopeJsonV2 载荷
 * - MUST_OMIT 码 **必须 delete priorityMode 键**（传 null 亦 400）
 * - 空数组省略（WhenWritingNull/CamelCase 由后端序列化配置承担，前端不发冗余键）
 */
export function buildScope(trigger: BusinessTriggerType, draft: ScopeDraft): ScopeJsonV2 {
  const spec = TRIGGER_RUN_MATRIX[trigger]
  const scope: ScopeJsonV2 = { trigger }

  if (spec.priorityModeRule !== 'MUST_OMIT' && draft.priorityMode !== undefined) {
    scope.priorityMode = draft.priorityMode
  }

  if (spec.targetField === 'orderTargets' && draft.orderTargets?.length) {
    scope.orderTargets = draft.orderTargets.map((t) => ({
      orderCanonicalId: Number(t.orderCanonicalId),
      manualTargetDueDate: t.manualTargetDueDate
    }))
  }
  if (spec.targetField === 'taskTargets' && draft.taskTargets?.length) {
    scope.taskTargets = draft.taskTargets.map((t) => ({
      taskId: Number(t.taskId),
      targetTime: t.targetTime
    }))
  }
  if (spec.targetField === 'changedResourceIds' && draft.changedResourceIds?.length) {
    scope.changedResourceIds = Array.from(new Set(draft.changedResourceIds))
  }

  return scope
}

export interface TriggerBusinessEntryInput {
  trigger: BusinessTriggerType
  domainKey: DomainKey
  draft: ScopeDraft
  /** 触发人（必填，审计） */
  actor: string
  basePlanVersionId?: number
  dataCutoffTime?: IsoDateTime
}

/** Dialog → 父组件提交载荷（Dialog 只收集，父组件持 loading 并调 triggerBusinessEntry） */
export interface BusinessEntrySubmission {
  trigger: BusinessTriggerType
  domainKey: DomainKey
  draft: ScopeDraft
}

/**
 * §10A 五业务入口统一出口：校验 → 构建 scope → 触发候选运行
 * 校验失败直接抛错（Dialog 应先行 validateScopeDraft 展示逐条中文错误）
 */
export async function triggerBusinessEntry(
  input: TriggerBusinessEntryInput
): Promise<TriggerRescheduleResult> {
  const errors = validateScopeDraft(input.trigger, input.draft)
  if (errors.length > 0) {
    throw new Error(`自定义范围校验未通过：\n${errors.join('\n')}`)
  }

  const spec = TRIGGER_RUN_MATRIX[input.trigger]

  return runApi.triggerReschedule({
    runType: spec.runType,
    purpose: spec.purpose,
    domainKey: input.domainKey,
    actor: input.actor,
    basePlanVersionId: input.basePlanVersionId,
    dataCutoffTime: input.dataCutoffTime,
    scope: buildScope(input.trigger, input.draft)
  })
}
