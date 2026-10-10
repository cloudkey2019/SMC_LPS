/**
 * APS V1 4号位 — 策略配置 API（12 端点）
 *
 * @owner 3号位（后端已就绪，4号位 只接 API）
 * @see lps/LPS.APS.Web/Controllers/GovernanceController.cs（策略段 L393-510）
 * @see frontNew/src/api/aps-v1/types/strategyProfile.ts
 *
 * 12 端点清单（按 GovernanceController 实测路径）：
 *   1.  GET    /api/governance/strategy-profiles                       列表
 *   2.  GET    /api/governance/strategy-profile/{id}/versions          版本列表
 *   3.  GET    /api/governance/strategy-profile/version/{id}           单版本详情
 *   4.  POST   /api/governance/strategy-profile/version                创建 DRAFT
 *   5.  PUT    /api/governance/strategy-profile/version/{id}           更新 DRAFT
 *   6.  POST   /api/governance/strategy-profile/version/{id}/publish   发布
 *   7.  POST   /api/governance/strategy-profile/version/{id}/disable   退役
 *   8.  GET    /api/governance/strategy-profile/version/{id}/validate  发布前校验
 *   9.  GET    /api/governance/strategy-profile/version/diff           版本 Diff
 *   10. GET    /api/governance/strategy-profile/{id}/published-version 当前 Published 直达
 *   11. GET    /api/governance/strategy-profile/default                默认版本解析
 *   12. GET    /api/governance/strategy-profile/version/{id}/trace     审计追溯
 *
 * 设计要点：
 *  - mock 模式：读端点返回 3 Profile + 5 Version 极小 fixture；写端点统一抛 503 防伪成功
 *  - 前端不维护 status/createdAt/createdBy/publishedAt/publishedBy 等后端权威字段
 *    （POST /version 后端会强制 Status=DRAFT 并忽略入参 Status）
 *  - IsDefault 通过 PUT /version/{id} 提前置，POST /publish 时由后端自动清同 Profile 旧默认
 *  - 上层组件调 strategyProfileApi.* 即可，store + page 不必关心 unwrap / mock fallback
 */

import { apsHttp, APS_USE_MOCK, ApiError } from './http'
import type {
  StrategyProfileDto,
  StrategyProfileVersionDto,
  StrategyVersionDraftInput,
  PublishStrategyVersionRequest,
  DisableStrategyVersionRequest,
  PublishValidationResultDto,
  StrategyVersionDiffDto,
  StrategyRunTraceDto,
  RuleSetDto,
  RuleSetVersionDto,
  ParameterSetDto,
  ParameterSetVersionDto
} from './types/strategyProfile'

/* ==================== Mock Fixtures（仅离线 UI 演示） ==================== */

const now = (): string => new Date().toISOString()
const fixedDate = (offset: number): string =>
  new Date(Date.now() - offset * 86400_000).toISOString()

const MOCK_PROFILES: StrategyProfileDto[] = [
  {
    id: 1,
    strategyProfileCode: 'FULL_DEFAULT',
    strategyProfileName: '默认全量排产策略',
    description: '完整排产场景（覆盖 PLAN/CTA/批产）的默认策略包',
    runType: 'FULL_SCHEDULE',
    isActive: true,
    createdAt: fixedDate(365),
    createdBy: 'admin'
  },
  {
    id: 2,
    strategyProfileCode: 'LOCAL_RESCHEDULE',
    strategyProfileName: '局部插单策略',
    description: '局部插单场景（车间调度）',
    runType: 'LOCAL_RESCHEDULE',
    isActive: true,
    createdAt: fixedDate(180),
    createdBy: 'admin'
  },
  {
    id: 3,
    strategyProfileCode: 'SIMULATION',
    strategyProfileName: '仿真演练策略',
    description: '干跑（不实际下发），供策略对比',
    runType: 'SIMULATION',
    isActive: true,
    createdAt: fixedDate(60),
    createdBy: 'admin'
  }
]

const MOCK_VERSIONS: StrategyProfileVersionDto[] = [
  {
    id: 1001,
    strategyProfileId: 1,
    versionCode: 'v1.0.0',
    ruleSetVersionId: 2001,
    parameterSetVersionId: 3001,
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(30),
    isDefault: true,
    publishedAt: fixedDate(30),
    publishedBy: 'admin',
    approvedAt: fixedDate(30),
    approvedBy: 'admin',
    createdAt: fixedDate(35),
    createdBy: 'admin'
  },
  {
    id: 1002,
    strategyProfileId: 1,
    versionCode: 'v1.1.0-draft',
    ruleSetVersionId: 2002,
    parameterSetVersionId: 3002,
    status: 'DRAFT',
    isDefault: false,
    createdAt: now(),
    createdBy: 'admin'
  },
  {
    id: 1003,
    strategyProfileId: 2,
    versionCode: 'v2.0.0',
    ruleSetVersionId: 2001,
    parameterSetVersionId: 3001,
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(20),
    isDefault: true,
    publishedAt: fixedDate(20),
    publishedBy: 'admin',
    approvedAt: fixedDate(20),
    approvedBy: 'admin',
    createdAt: fixedDate(25),
    createdBy: 'admin'
  },
  {
    id: 1004,
    strategyProfileId: 3,
    versionCode: 'v3.0.0',
    ruleSetVersionId: 2001,
    parameterSetVersionId: 3001,
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(10),
    isDefault: true,
    publishedAt: fixedDate(10),
    publishedBy: 'admin',
    createdAt: fixedDate(15),
    createdBy: 'admin'
  },
  {
    id: 1005,
    strategyProfileId: 1,
    versionCode: 'v0.9.0-archived',
    ruleSetVersionId: 2000,
    parameterSetVersionId: 3000,
    status: 'ARCHIVED',
    isDefault: false,
    publishedAt: fixedDate(60),
    publishedBy: 'admin',
    createdAt: fixedDate(70),
    createdBy: 'admin'
  }
]

/* ---- RuleSet / ParameterSet 列表（mock 演示）---- */
const MOCK_RULE_SETS: RuleSetDto[] = [
  {
    id: 200,
    ruleSetCode: 'RS_PLAN_DEFAULT',
    ruleSetName: 'PLAN 场景默认规则集',
    description: '完整排产场景规则集（含优先级/瓶颈/换型）',
    isActive: true,
    createdAt: fixedDate(200),
    createdBy: 'admin'
  },
  {
    id: 201,
    ruleSetCode: 'RS_CTA_FAST',
    ruleSetName: 'CTA 快速响应规则集',
    description: 'CTP 试算/插单评估专用',
    isActive: true,
    createdAt: fixedDate(150),
    createdBy: 'admin'
  }
]

const MOCK_RULE_SET_VERSIONS: RuleSetVersionDto[] = [
  {
    id: 2001,
    ruleSetId: 200,
    versionCode: 'rs-v1.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(60),
    publishedAt: fixedDate(60),
    publishedBy: 'admin',
    createdAt: fixedDate(65)
  },
  {
    id: 2002,
    ruleSetId: 200,
    versionCode: 'rs-v1.1.0-draft',
    status: 'DRAFT',
    createdAt: now()
  },
  {
    id: 2010,
    ruleSetId: 201,
    versionCode: 'rs-v1.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(40),
    publishedAt: fixedDate(40),
    publishedBy: 'admin',
    createdAt: fixedDate(45)
  }
]

const MOCK_PARAMETER_SETS: ParameterSetDto[] = [
  {
    id: 300,
    parameterSetCode: 'PS_PLAN_DEFAULT',
    parameterSetName: 'PLAN 场景默认参数集',
    description: '完整排产场景参数集（Lock/Supply/Procurement/SolverStrategy）',
    isActive: true,
    createdAt: fixedDate(200),
    createdBy: 'admin'
  },
  {
    id: 301,
    parameterSetCode: 'PS_CTA_FAST',
    parameterSetName: 'CTA 快速响应参数集',
    description: 'CTP 试算/插单评估专用参数集',
    isActive: true,
    createdAt: fixedDate(150),
    createdBy: 'admin'
  }
]

const MOCK_PARAMETER_SET_VERSIONS: ParameterSetVersionDto[] = [
  {
    id: 3001,
    parameterSetId: 300,
    versionCode: 'ps-v1.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(60),
    publishedAt: fixedDate(60),
    publishedBy: 'admin',
    createdAt: fixedDate(65)
  },
  {
    id: 3002,
    parameterSetId: 300,
    versionCode: 'ps-v1.1.0-draft',
    status: 'DRAFT',
    createdAt: now()
  },
  {
    id: 3010,
    parameterSetId: 301,
    versionCode: 'ps-v1.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(40),
    publishedAt: fixedDate(40),
    publishedBy: 'admin',
    createdAt: fixedDate(45)
  }
]

/** mock 模式 write 端点统一抛 503（防伪成功误导；dev APS_USE_MOCK=false 不走 mock） */
const rejectMockWrite = (op: string): Promise<never> =>
  Promise.reject(new ApiError(`mock 模式禁止写策略（${op}）`, 503))

/* ==================== API ==================== */

export const strategyProfileApi = {
  /* ===== 1. Profile 列表 ===== */
  async listProfiles(params?: {
    activeOnly?: boolean
    keyword?: string
    page?: number
    pageSize?: number
  }): Promise<StrategyProfileDto[]> {
    if (APS_USE_MOCK) {
      let arr = MOCK_PROFILES
      if (params?.activeOnly) arr = arr.filter((p) => p.isActive)
      if (params?.keyword) {
        const kw = params.keyword.toLowerCase()
        arr = arr.filter(
          (p) =>
            p.strategyProfileCode.toLowerCase().includes(kw) ||
            p.strategyProfileName.toLowerCase().includes(kw)
        )
      }
      return arr
    }
    return apsHttp.get<StrategyProfileDto[]>({
      url: '/api/governance/strategy-profiles',
      params
    })
  },

  /* ===== 2. 单 Profile 版本列表 ===== */
  async listVersions(profileId: number): Promise<StrategyProfileVersionDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_VERSIONS.filter((v) => v.strategyProfileId === profileId)
    }
    return apsHttp.get<StrategyProfileVersionDto[]>({
      url: `/api/governance/strategy-profile/${profileId}/versions`
    })
  },

  /* ===== 3. 单版本详情 ===== */
  async getVersion(versionId: number): Promise<StrategyProfileVersionDto> {
    if (APS_USE_MOCK) {
      const v = MOCK_VERSIONS.find((x) => x.id === versionId)
      if (!v) throw new ApiError(`mock 无策略版本 ${versionId}`, 404)
      return v
    }
    return apsHttp.get<StrategyProfileVersionDto>({
      url: `/api/governance/strategy-profile/version/${versionId}`
    })
  },

  /* ===== 4. 创建 DRAFT（POST 返回 201 + 完整实体） ===== */
  async createVersion(input: StrategyVersionDraftInput): Promise<StrategyProfileVersionDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createVersion')
    return apsHttp.post<StrategyProfileVersionDto>({
      url: '/api/governance/strategy-profile/version',
      data: input
    })
  },

  /* ===== 5. 更新 DRAFT（可改 IsDefault；后端会校验仅 DRAFT 可改） ===== */
  async updateVersion(
    versionId: number,
    input: StrategyVersionDraftInput
  ): Promise<StrategyProfileVersionDto> {
    if (APS_USE_MOCK) return rejectMockWrite('updateVersion')
    return apsHttp.put<StrategyProfileVersionDto>({
      url: `/api/governance/strategy-profile/version/${versionId}`,
      data: input
    })
  },

  /* ===== 6. 发布（DRAFT/SUBMITTED/APPROVED → PUBLISHED） ===== */
  async publishVersion(versionId: number, request: PublishStrategyVersionRequest): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('publishVersion')
    await apsHttp.post<void>({
      url: `/api/governance/strategy-profile/version/${versionId}/publish`,
      data: request
    })
  },

  /* ===== 7. 退役（仅 PUBLISHED 可 Disable） ===== */
  async disableVersion(versionId: number, request: DisableStrategyVersionRequest): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('disableVersion')
    await apsHttp.post<void>({
      url: `/api/governance/strategy-profile/version/${versionId}/disable`,
      data: request
    })
  },

  /* ===== 8. 发布前校验 ===== */
  async validateVersion(versionId: number): Promise<PublishValidationResultDto> {
    if (APS_USE_MOCK) {
      const v = MOCK_VERSIONS.find((x) => x.id === versionId)
      if (!v) throw new ApiError(`mock 无策略版本 ${versionId}`, 404)
      const canPublish = v.status === 'DRAFT' || v.status === 'SUBMITTED' || v.status === 'APPROVED'
      return {
        isValid: canPublish,
        errors: canPublish
          ? []
          : [
              {
                code: 'INVALID_STATUS',
                message: `版本状态不可发布（当前 ${v.status}）`,
                fieldName: 'status'
              }
            ],
        warnings: [],
        validatedAt: now()
      }
    }
    return apsHttp.get<PublishValidationResultDto>({
      url: `/api/governance/strategy-profile/version/${versionId}/validate`
    })
  },

  /* ===== 9. 版本 Diff ===== */
  async diffVersions(
    sourceVersionId: number,
    targetVersionId: number
  ): Promise<StrategyVersionDiffDto> {
    if (APS_USE_MOCK) {
      const src = MOCK_VERSIONS.find((x) => x.id === sourceVersionId)
      const tgt = MOCK_VERSIONS.find((x) => x.id === targetVersionId)
      if (!src || !tgt) {
        throw new ApiError(`mock 无策略版本 ${!src ? sourceVersionId : targetVersionId}`, 404)
      }
      const fields: Array<{ key: keyof StrategyProfileVersionDto; label: string }> = [
        { key: 'versionCode', label: '版本号' },
        { key: 'ruleSetVersionId', label: '引用的规则集版本' },
        { key: 'parameterSetVersionId', label: '引用的参数集版本' },
        { key: 'isDefault', label: '默认版本' },
        { key: 'effectiveFrom', label: '生效起始' },
        { key: 'effectiveTo', label: '生效截止' }
      ]
      return {
        sourceVersionId,
        targetVersionId,
        sourceVersionCode: src.versionCode,
        targetVersionCode: tgt.versionCode,
        entityType: 'StrategyProfileVersion',
        fieldDiffs: fields.map((f) => {
          const sVal = src[f.key]
          const tVal = tgt[f.key]
          const sStr = sVal == null ? '' : String(sVal)
          const tStr = tVal == null ? '' : String(tVal)
          return {
            fieldName: String(f.key),
            fieldDisplayName: f.label,
            sourceValue: sStr,
            targetValue: tStr,
            isChanged: sStr !== tStr
          }
        }),
        comparedAt: now()
      }
    }
    return apsHttp.get<StrategyVersionDiffDto>({
      url: '/api/governance/strategy-profile/version/diff',
      params: { sourceVersionId, targetVersionId }
    })
  },

  /* ===== 10. 当前 Published 直达（无则 404） ===== */
  async getPublishedVersion(profileId: number): Promise<StrategyProfileVersionDto | null> {
    if (APS_USE_MOCK) {
      const v = MOCK_VERSIONS.find(
        (x) => x.strategyProfileId === profileId && x.status === 'PUBLISHED'
      )
      return v ?? null
    }
    try {
      return await apsHttp.get<StrategyProfileVersionDto>({
        url: `/api/governance/strategy-profile/${profileId}/published-version`
      })
    } catch (err) {
      if (err instanceof ApiError && err.code === 404) return null
      throw err
    }
  },

  /* ===== 11. 默认版本解析（按 RunType + asOf 找唯一 PUBLISHED + IsDefault=1） ===== */
  async resolveDefaultVersion(
    runType: string,
    asOf?: string
  ): Promise<StrategyProfileVersionDto | null> {
    if (APS_USE_MOCK) {
      const v = MOCK_VERSIONS.find(
        (x) =>
          x.status === 'PUBLISHED' &&
          x.isDefault &&
          MOCK_PROFILES.find((p) => p.id === x.strategyProfileId)?.runType === runType
      )
      return v ?? null
    }
    try {
      return await apsHttp.get<StrategyProfileVersionDto>({
        url: '/api/governance/strategy-profile/default',
        params: { runType, asOf }
      })
    } catch (err) {
      if (err instanceof ApiError && err.code === 404) return null
      throw err
    }
  },

  /* ===== 12. Run 引用追溯 ===== */
  async getRunTrace(versionId: number): Promise<StrategyRunTraceDto> {
    if (APS_USE_MOCK) {
      const v = MOCK_VERSIONS.find((x) => x.id === versionId)
      if (!v) throw new ApiError(`mock 无策略版本 ${versionId}`, 404)
      return {
        strategyProfileVersionId: versionId,
        versionCode: v.versionCode,
        status: v.status,
        ruleSetVersionId: v.ruleSetVersionId,
        parameterSetVersionId: v.parameterSetVersionId,
        referencingRuns: []
      }
    }
    return apsHttp.get<StrategyRunTraceDto>({
      url: `/api/governance/strategy-profile/version/${versionId}/trace`
    })
  },

  /* ===== 附 1. RuleSet 主表列表（策略 DRAFT 表单下拉源 #1）=====
   * 后端：GET /api/governance/rule-sets（lps/LPS.APS.Web/Controllers/GovernanceController.cs L674）
   * 权限：aps.rule.view（持 aps.strategy.edit 的用户必然也持 rule.view）
   * 用途：策略 DRAFT 创建/编辑时，第一级下拉选 RuleSet
   */
  async listRuleSets(params?: {
    activeOnly?: boolean
    keyword?: string
    page?: number
    pageSize?: number
  }): Promise<RuleSetDto[]> {
    if (APS_USE_MOCK) {
      let arr = MOCK_RULE_SETS
      if (params?.activeOnly) arr = arr.filter((p) => p.isActive)
      if (params?.keyword) {
        const kw = params.keyword.toLowerCase()
        arr = arr.filter(
          (p) =>
            p.ruleSetCode.toLowerCase().includes(kw) || p.ruleSetName.toLowerCase().includes(kw)
        )
      }
      return arr
    }
    return apsHttp.get<RuleSetDto[]>({
      url: '/api/governance/rule-sets',
      params
    })
  },

  /* ===== 附 2. RuleSetVersion 列表（策略 DRAFT 表单下拉源 #2）=====
   * 后端：GET /api/governance/rule-set/{ruleSetId}/versions（GovernanceController.cs L85）
   * 用途：选完 RuleSet 后加载其下属版本，第二级下拉
   */
  async listRuleSetVersions(ruleSetId: number): Promise<RuleSetVersionDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_RULE_SET_VERSIONS.filter((v) => v.ruleSetId === ruleSetId)
    }
    return apsHttp.get<RuleSetVersionDto[]>({
      url: `/api/governance/rule-set/${ruleSetId}/versions`
    })
  },

  /* ===== 附 3. ParameterSet 主表列表（策略 DRAFT 表单下拉源 #3）=====
   * 后端：GET /api/governance/parameter-sets（GovernanceController.cs L690）
   * 用途：策略 DRAFT 创建/编辑时，第一级下拉选 ParameterSet
   */
  async listParameterSets(params?: {
    activeOnly?: boolean
    keyword?: string
    page?: number
    pageSize?: number
  }): Promise<ParameterSetDto[]> {
    if (APS_USE_MOCK) {
      let arr = MOCK_PARAMETER_SETS
      if (params?.activeOnly) arr = arr.filter((p) => p.isActive)
      if (params?.keyword) {
        const kw = params.keyword.toLowerCase()
        arr = arr.filter(
          (p) =>
            p.parameterSetCode.toLowerCase().includes(kw) ||
            p.parameterSetName.toLowerCase().includes(kw)
        )
      }
      return arr
    }
    return apsHttp.get<ParameterSetDto[]>({
      url: '/api/governance/parameter-sets',
      params
    })
  },

  /* ===== 附 4. ParameterSetVersion 列表（策略 DRAFT 表单下拉源 #4）=====
   * 后端：GET /api/governance/parameter-set/{parameterSetId}/versions（GovernanceController.cs L141）
   * 用途：选完 ParameterSet 后加载其下属版本，第二级下拉
   */
  async listParameterSetVersions(parameterSetId: number): Promise<ParameterSetVersionDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_PARAMETER_SET_VERSIONS.filter((v) => v.parameterSetId === parameterSetId)
    }
    return apsHttp.get<ParameterSetVersionDto[]>({
      url: `/api/governance/parameter-set/${parameterSetId}/versions`
    })
  }
}
