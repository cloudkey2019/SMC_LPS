/**
 * APS V1 4号位 API 总索引
 *
 * 使用：
 *   import { scheduleApi, overviewApi, type GanttDataDto } from '@/api/aps-v1'
 *
 * 所有 API 模块遵循：
 *  - mock 切换：import.meta.env.VITE_USE_MOCK === 'true' → 返回 fixture
 *  - 真实请求：统一走 apsHttp，自动解包 ApiResponse<T>、注入 JWT
 *  - 错误统一抛 ApiError（业务码 + traceId）
 */

export * from './types'
export * from './http'
export { scheduleApi } from './schedule'
export { overviewApi } from './overview'
export { orderApi } from './order'
export { ctpApi } from './ctp'
export { candidateApi } from './candidate'
export { explanationApi } from './explanation'
export { piApi } from './pi'
export { ruleApi, mockActor } from './rule'
export {
  flattenBlocks,
  applyEdit,
  buildPutBody,
  isSensitive,
  isEditable,
  inferType,
  parseJsonSafe,
  parseParameterSetBlocks
} from './draftBuffer'
export type { ParameterRow, JsonObject, JsonArray, JsonValue } from './draftBuffer'
export {
  registerParameterSetBindings,
  lookupBinding,
  clearBindings,
  bindingCount,
  hasBinding
} from './parameterSetBindings'
export type {
  ParameterBinding,
  BlockBindings,
  ParameterSetBindingsConfig
} from './parameterSetBindings'
export { runApi } from './run'
export {
  BUSINESS_ENTRIES,
  TRIGGER_RUN_MATRIX,
  buildScope,
  validateScopeDraft,
  triggerBusinessEntry
} from './runScope'
export type {
  PriorityModeRule,
  ScopeTargetField,
  TriggerRunSpec,
  ScopeDraft,
  BusinessEntrySubmission
} from './runScope'
export { apsAuthApi } from './auth'
export { manualEtaApi, mockManualEtaActor } from './manualEta'
export { demandProtectionApi, mockDpActor } from './demandProtection'
export { domainApi, MOCK_DOMAIN_FIXTURES, EndpointUnavailableError } from './domain'
export { rbacApi } from './rbac'
export { strategyProfileApi } from './strategyProfile'
export { setupApi } from './setup'
export { auditApi } from './audit'
export { opmApi, MOCK_OPM_MATERIAL_ID } from './opm'
export { resourceCalendarApi } from './resourceCalendar'
export type { ResourceCandidateResult, DepartmentCandidate } from './resourceCalendar'
