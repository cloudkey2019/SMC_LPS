/**
 * 异常与原因解释 DTO（按 5号位 2026-10-09 函件 R1 回执整改）
 *
 * @see 4号位文档第 11-12 节（页面 6：异常与原因解释 + 设备故障）
 * @see 5号位-2026-10-09-Explanation页面一致性-回执-给4号位.md（§ 一 关键纠正 1/2/3 + § 五 R1 承诺）
 * @see 5号位 lps/LPS.APS.Web/Controllers/ExplanationController.cs（L41-94 list + summary 2 端点）
 * @see 5号位 lps/LPS.APS.Core/Dto/ExplanationDtos.cs（ExplanationDto + ExplanationSummaryDto + ReasonCodeCountDto）
 * @see 5号位 lps/LPS.APS.Core/Dto/BusinessFactIssueDto.cs（业务事实问题 DTO）
 *
 * 2026-10-09 改造（按 5号位 回执）：
 *  - R1 ReasonCode 权威源 = v5.1.10 §八.1 15 码（@see 冻结文档/07_APS_数据库字段说明文档_v5.1.10_20261008_BatchPolicy字段语义_PI数量候选版.md :4863）
 *    0号位《1号位代码第15轮审核报告》P1-03「6 码为正式冻结要求」属辅助，与最新冻结文档碰撞时**以最新冻结文档为准**
 *  - FROZEN_ZONE_LOCK：15 码唯一锁码；FIRM / FROZEN / 其它执行锁统一并入；细分走 EvidenceJson.evidenceType = LOCK
 *  - ObjectType 值域 = ORDER / TASK / RESOURCE / STAGE / DOMAIN（v5.1.10 :4858，5 码；前端原 PI/PO 占位已撤）
 *  - Severity = INFO / WARN / ERROR（v5.1.10 :4864；CRITICAL 仅 BOM Issues 表用；前端 WARNING → WARN）
 *  - 撤 5号位 关键纠正 3 占位 interface（后端无对应实体）
 *    改用 ExplanationSummaryDto.errorCount / warnCount / infoCount 三 KPI
 *  - 端点契约改造：summary 端点返 ExplanationSummaryDto（含 items[]）；前端 store 取 items 字段而非 data 字段
 *
 * R2 关键路径 / R3 failureSeverity 跨号位（1号位 求解 + 2号位 落盘 + 0号位 裁决 V1 范围）—— **前端不动**
 *  - 4号位 端不发函（@see feedback_no_redundant_ack_letters：仅"已知悉"不发；R2/R3 跨号位由 5号位 中转）
 *  - verify [E] 段新增 E11/E12 skip 占位（@see dp-freeze DP06/07 模式）
 *
 * 2026-10-10 收口：business-fact-issues 分页 + total + severity 排序（前端先上线）
 *  - ExplanationViewDto 增 businessFactIssuesTotal/PageIndex/PageSize 3 字段（@see frontNew/src/api/aps-v1/types/common.ts PageResult<T>）
 *  - store 层做缓存（版本切换保留，仅"刷新"按钮失效）+ 累积分页 + 本地 severity desc 排序兜底
 *  - 函件 4号位-2026-10-10-business-fact-issues分页severity排序total-给5号位.md 建议 ORDER BY 加 severity + DTO 改 PageResult
 */

import type { IsoDateTime } from './common'

/* ===== ReasonCode 权威枚举（v5.1.10 §八.1 :4863 15 码全集） ===== */
/** 15 码权威枚举（与后端 DTO ReasonCode 字段 1:1 对齐；前端字典改用全集，**勿按 §85 6 码白改**） */
export const REASON_CODES = [
  'RESOURCE_CAPACITY_WAIT',
  'MATERIAL_SHORTAGE',
  'PRECEDENCE_WAIT',
  'FROZEN_ZONE_LOCK',
  'ROUTING_FALLBACK',
  'STAGE_LEADTIME_FALLBACK',
  'BOM_DEGRADE',
  'CROSS_ORG_HANDOFF',
  'PRIORITY_LOWER_THAN_OTHERS',
  'DUE_DATE_RISK',
  'LOGISTICS_DELAY',
  'PRIORITY_INHERITANCE',
  'CROSS_DOMAIN_VERSION_MISMATCH_RISK',
  'MANUAL_COMPLETED_SHORT',
  'EQUIPMENT_BREAKDOWN_RISK'
] as const
export type ReasonCode = (typeof REASON_CODES)[number]

/** ReasonCode → UI 中文标签（前端字典，非后端契约） */
export const REASON_CODE_LABELS: Record<ReasonCode, string> = {
  RESOURCE_CAPACITY_WAIT: '资源产能等待',
  MATERIAL_SHORTAGE: '物料短缺',
  PRECEDENCE_WAIT: '前序等待',
  FROZEN_ZONE_LOCK: '冻结区锁定',
  ROUTING_FALLBACK: '工艺路线回退',
  STAGE_LEADTIME_FALLBACK: '阶段前置时间回退',
  BOM_DEGRADE: 'BOM 降级',
  CROSS_ORG_HANDOFF: '跨组织交接',
  PRIORITY_LOWER_THAN_OTHERS: '优先级低于其他',
  DUE_DATE_RISK: '交期风险',
  LOGISTICS_DELAY: '物流延期',
  PRIORITY_INHERITANCE: '优先级继承',
  CROSS_DOMAIN_VERSION_MISMATCH_RISK: '跨域版本不一致风险',
  MANUAL_COMPLETED_SHORT: '手工完成不足',
  EQUIPMENT_BREAKDOWN_RISK: '设备故障风险'
}

/* ===== ObjectType 值域（v5.1.10 :4858 5 码） ===== */
export const EXPLANATION_OBJECT_TYPES = ['ORDER', 'TASK', 'RESOURCE', 'STAGE', 'DOMAIN'] as const
export type ExplanationObjectType = (typeof EXPLANATION_OBJECT_TYPES)[number]

/** ObjectType → UI 中文标签 */
export const OBJECT_TYPE_LABELS: Record<ExplanationObjectType, string> = {
  ORDER: '订单',
  TASK: '任务',
  RESOURCE: '资源',
  STAGE: '工段',
  DOMAIN: '分域'
}

/* ===== Severity 值域（v5.1.10 :4864 3 码） ===== */
export const EXPLANATION_SEVERITIES = ['INFO', 'WARN', 'ERROR'] as const
export type ExplanationSeverity = (typeof EXPLANATION_SEVERITIES)[number]

/** Severity → UI 中文标签（CRITICAL 仅 BOM Issues 表用，ScheduleExplanationFact 不含） */
export const EXPLANATION_SEVERITY_LABELS: Record<ExplanationSeverity, string> = {
  INFO: '提示',
  WARN: '警告',
  ERROR: '错误'
}

/** Severity → ElTag type */
export const EXPLANATION_SEVERITY_TAG: Record<ExplanationSeverity, 'info' | 'warning' | 'danger'> =
  {
    INFO: 'info',
    WARN: 'warning',
    ERROR: 'danger'
  }

/* ===== 排程解释事实（ScheduleExplanationFact，对齐 5号位 lps/LPS.APS.Core/Dto/ExplanationDtos.cs :9-43） ===== */
export interface ScheduleExplanationFact {
  factId: number
  planVersionId: number
  scheduleRunId?: number
  objectType: ExplanationObjectType
  /** 聚合展示用：基于 OrderId / TaskId / ResourceId / StageCode 派生（store 层聚合） */
  objectRef: string
  /** 权威枚举（v5.1.10 :4863 15 码全集） */
  reasonCode: ReasonCode
  severity: ExplanationSeverity
  /** 影响小时数（v5.1.10 :4865；可为 0） */
  impactHours?: number
  /** 证据 JSON（外壳 evidenceType/summary/details；v5.1.10 :4866；细分走 evidenceType） */
  evidenceJson?: string
  occurredAt: IsoDateTime
}

/* ===== 业务事实问题（BusinessFactIssue，对齐 5号位 lps/LPS.APS.Core/Dto/BusinessFactIssueDto.cs :9-80）
 *  数据源：5号位 `GET /api/business-fact-issues`（独立于 Summary 端点聚合）
 *  @see lps/LPS.APS.Web/Controllers/BusinessFactIssueController.cs :60-95
 *  冻结基线对齐：4号位 v1.6 § 八 + 01 基线 v1.10 §85 ReasonCode 隐含「业务事实层」===== */
/** Issue 来源（@see BusinessFactIssueDto.cs:12「BOM_WORKSET / MATERIAL_STAGE_CONTEXT / PI_POSITION / MANUAL_ETA 等」） */
export type BusinessFactSource =
  | 'BOM_WORKSET'
  | 'MATERIAL_STAGE_CONTEXT'
  | 'PI_POSITION'
  | 'MANUAL_ETA'
  | string

export interface BusinessFactIssue {
  issueId: number
  source: BusinessFactSource
  issueType: string
  /** 严重级别（后端 4 码 INFO/WARN/ERROR/CRITICAL，宽 string 接收；view 无 severity 列渲染，无视觉影响） */
  severity: string
  detail: string
  materialCode?: string
  factoryCode?: string
  documentNo?: string
  stageCode?: string
  affectedQuantity?: number
  degradeAction?: string
  reviewStatus: 'PENDING' | 'CONFIRMED' | 'IGNORED' | 'FIXED'
  reviewedBy?: string
  reviewedAt?: IsoDateTime
  occurredAt: IsoDateTime
}

/* ===== 重排建议 / 设备故障（10-10 冻结基线收口：4号位 v1.6 未列出，撤 UI + DTO）
 *  历史：v1.0/v1.1 前端 DTO 自扩（RescheduleRecommendation / EquipmentFailure）+ 模板 mock 占位
 *  - 4号位 v1.6 § 八「4号位 不重新计算原因」无 UI 块规格
 *  - 01 基线 v1.10 §85 ReasonCode 无 UI 块匹配（只有 ReasonCode 通用枚举）
 *  - 14 5号位 v1.7 § 八输出清单也不含此两类
 *  - lps 整目录无 RescheduleRecommendationDto / EquipmentFailureDto 对应实体
 *  结论：撤掉，等冻结文档 v1.7 增补后再启
 *  为避免影响外部 import 占位，类型保留空壳导出（理由同 # frozen-baseline-rebase-10-10）
 */
// 撤：RescheduleRecommendation
// 撤：EquipmentFailure

/* ===== 按 ReasonCode 分组统计（对齐 5号位 lps/LPS.APS.Core/Dto/ExplanationDtos.cs :71-76 ReasonCodeCountDto） ===== */
export interface ReasonCodeCount {
  reasonCode: ReasonCode
  count: number
  totalImpactHours?: number
}

/* ===== ExplanationViewDto — 异常与原因解释（页面 6） ===== */
/**
 * 5号位 2026-10-09 函件关键纠正 3：后端无对应实体字段；
 *  改用 Summary 端点的 `errorCount / warnCount / infoCount` 三计数。
 *
 *  说明：本 DTO 是 4号位 视图聚合层（与 5号位 lps/LPS.APS.Core/Dto/ExplanationDtos.cs :48 ExplanationSummaryDto 字段对齐 + 增 4号位 业务字段）：
 *  - top 3 KPI 来自 `errorCount / warnCount / infoCount`（Summary 端点权威数）
 *  - 排程解释事实卡片来自 `scheduleExplanationFacts[]`（Summary.items）
 *  - reasonCodeCounts 来自 Summary 端点直接透传
 */
export interface ExplanationViewDto {
  planVersionId: number
  scheduleExplanationFacts: ScheduleExplanationFact[]
  businessFactIssues: BusinessFactIssue[]
  /** 业务事实问题总数（来自后端 PageResult.total；前端视图层"共 M 条"展示） */
  businessFactIssuesTotal: number
  /** 业务事实问题当前页码（首屏 1；翻页累加）
   *  字段名 page 来自后端真值（5号位 2026-10-10 函件已落地 PageResult）
   *  与 LPS.APS.Core.Dto.PageResult<T>.page 一致（RBAC/TaskSplitRuleConfig/ManualETA 同源）
   *  注：本仓通用类型 PageResult<T> 仍用 pageIndex（@see types/common.ts:62-67），
   *     仅 explanation 模块用此 page 字段以对齐后端真值
   */
  businessFactIssuesPage: number
  /** 业务事实问题每页条数（默认 20，对齐 Audit.vue） */
  businessFactIssuesPageSize: number
  /** Summary 端点 errorCount（v5.1.10 字段映射，@see lps/LPS.APS.Core/Dto/ExplanationDtos.cs :57） */
  errorCount: number
  /** Summary 端点 warnCount */
  warnCount: number
  /** Summary 端点 infoCount */
  infoCount: number
  /** Summary 端点 reasonCodeCounts（按 15 码分布） */
  reasonCodeCounts: ReasonCodeCount[]
}
