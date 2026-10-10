/**
 * APS V1 4号位 — 规则与参数维护 API
 *
 * @owner 3号位（RuleSet / ParameterSet / StrategyProfile / Diff / 发布 — 治理类）
 * @see 审核报告 §十六.4↔3 + §二十
 *
 * @see 4号位文档第 14 节（页面 8：规则与参数维护）
 * 验收：U21 / U22
 *
 * 设计要点：
 *  - 前端不能直接改 PUBLISHED 字段（4号位文档第 14 节）
 *  - U21：Priority Segment 仅展示 segmentName + orderInSegment，永不出现 PriorityScore
 *  - U22：新 Version 生成，历史不覆盖（publish 返回 historicalVersions）
 *  - 发布前调后端校验接口，前端只发请求不直接写库
 *  - DRAFT 可改，PUBLISHED 不可改，RETIRED 只读
 *
 * 权限矩阵（前端按钮显隐）：
 *  - VIEWER / PMC        只读
 *  - RULE_ADMIN          可创建/编辑 DRAFT、提交校验
 *  - RULE_PUBLISHER      可发布 / 退役（通常同时是 RULE_ADMIN）
 *  - SYSTEM_ADMIN        全部
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type {
  RuleSetParameterStubDto,
  PrioritySegmentDto,
  PublishRuleInput,
  PublishRuleResult,
  RetireRuleInput,
  RetireRuleResult,
  RuleDiffDto,
  RuleSetDetailDto,
  RuleSetSummaryDto,
  RuleVersionHistoryDto,
  RuleSetStrategyStubDto,
  RoleKey,
  RuleSetVersion,
  ParameterSetVersion,
  CreateRuleSetDraftInput,
  UpdateRuleSetDraftInput,
  CreateParameterSetDraftInput,
  UpdateParameterSetDraftInput,
  PublishGovernanceInput,
  PublishGovernanceResult,
  ForkDraftResult
} from './types'

/* ===== Rule Set List ===== */

const mockRuleSets: RuleSetSummaryDto[] = [
  {
    ruleSetId: 1,
    ruleSetCode: 'RS-DEFAULT',
    ruleSetName: '默认规则集',
    domainKey: 'FAMILY_INJECTION',
    currentVersion: 12,
    draftVersion: 13,
    status: 'DRAFT',
    lastChangeReason: '调整 Demand Protection 阈值 0.15 → 0.20',
    createdAt: new Date(Date.now() - 86400_000).toISOString(),
    publishedAt: new Date(Date.now() - 30 * 86400_000).toISOString(),
    validationStatus: 'PENDING',
    validationMessage: '草稿校验未执行'
  },
  {
    ruleSetId: 2,
    ruleSetCode: 'RS-HOLIDAY',
    ruleSetName: '节假日规则',
    domainKey: 'ALL',
    currentVersion: 3,
    status: 'PUBLISHED',
    createdAt: new Date(Date.now() - 30 * 86400_000).toISOString(),
    publishedAt: new Date(Date.now() - 30 * 86400_000).toISOString(),
    validationStatus: 'PASSED'
  },
  {
    ruleSetId: 3,
    ruleSetCode: 'RS-CROSS-DOMAIN',
    ruleSetName: '跨 Domain 共享设备规则',
    domainKey: 'ALL',
    currentVersion: 5,
    status: 'PUBLISHED',
    createdAt: new Date(Date.now() - 60 * 86400_000).toISOString(),
    publishedAt: new Date(Date.now() - 60 * 86400_000).toISOString(),
    validationStatus: 'PASSED'
  },
  {
    ruleSetId: 4,
    ruleSetCode: 'RS-LEGACY-V1',
    ruleSetName: '旧版本规则集（已退役）',
    domainKey: 'FAMILY_INJECTION',
    currentVersion: 8,
    status: 'DISABLED',
    createdAt: new Date(Date.now() - 180 * 86400_000).toISOString(),
    publishedAt: new Date(Date.now() - 90 * 86400_000).toISOString(),
    retiredAt: new Date(Date.now() - 7 * 86400_000).toISOString(),
    validationStatus: 'PASSED'
  }
]

/* ===== Rule Set Detail (含 Parameters / Segments / Strategy) ===== */

const mockRuleSetDetail = (ruleSetId: number): RuleSetDetailDto => {
  const s = mockRuleSets.find((r) => r.ruleSetId === ruleSetId)
  return {
    summary: s ?? {
      ruleSetId,
      ruleSetCode: `RS-${ruleSetId}`,
      ruleSetName: 'Unknown',
      domainKey: 'ALL',
      currentVersion: 1,
      status: 'DRAFT',
      createdAt: new Date().toISOString(),
      validationStatus: 'PENDING'
    },
    domainKey: s?.domainKey ?? 'ALL',
    parameterSet: mockParametersFor(ruleSetId),
    prioritySegments: mockSegments,
    strategyProfile: mockStrategyFor(ruleSetId),
    history: mockHistoryFor(ruleSetId)
  }
}

const mockParametersFor = (ruleSetId: number): RuleSetParameterStubDto => {
  const isLegacy = ruleSetId === 4
  return {
    parameterSetId: ruleSetId * 10,
    parameterSetCode: `PS-${ruleSetId}`,
    parameterSetName: `参数集 #${ruleSetId}`,
    version: isLegacy ? 8 : 13,
    status: isLegacy ? 'DISABLED' : ruleSetId === 1 ? 'DRAFT' : 'PUBLISHED',
    parameters: [
      {
        parameterKey: 'defaultPurchaseLT',
        parameterName: '默认采购前置期',
        parameterType: 'NUMBER',
        value: ruleSetId === 1 ? 8 : 7,
        defaultValue: 7,
        unit: '天',
        sensitive: false,
        editable: !isLegacy && ruleSetId === 1,
        description: '未指定物料的默认采购前置期'
      },
      {
        parameterKey: 'demandProtectionThreshold',
        parameterName: 'Demand Protection 阈值',
        parameterType: 'PERCENT',
        value: ruleSetId === 1 ? 0.2 : 0.15,
        defaultValue: 0.15,
        unit: '%',
        sensitive: false,
        editable: !isLegacy && ruleSetId === 1,
        description: '已确认订单的需求保护比例（U03/U14）'
      },
      {
        parameterKey: 'planningYield',
        parameterName: '计划良率',
        parameterType: 'PERCENT',
        value: 0.95,
        defaultValue: 0.95,
        unit: '%',
        sensitive: false,
        editable: !isLegacy && ruleSetId === 1,
        description: '排产时考虑的良率补偿'
      },
      {
        parameterKey: 'crossDomainBlockBuffer',
        parameterName: '跨域共享设备缓冲',
        parameterType: 'DURATION',
        value: 4,
        defaultValue: 4,
        unit: 'h',
        sensitive: true,
        editable: !isLegacy && ruleSetId === 1,
        description: '跨 Domain 共享设备切换的额外缓冲时间（敏感字段）'
      },
      {
        parameterKey: 'planningPurchasePlaceholderEnabled',
        parameterName: '启用采购占位（PLANNING_PURCHASE_PLACEHOLDER）',
        parameterType: 'BOOLEAN',
        value: true,
        defaultValue: true,
        sensitive: false,
        editable: !isLegacy && ruleSetId === 1,
        description: 'U07：未确认的采购供应用占位时间呈现'
      }
    ],
    changeReason: ruleSetId === 1 ? '调整 Demand Protection 阈值 + 新增跨域缓冲' : '保持现行配置',
    createdAt: new Date(Date.now() - 86400_000).toISOString(),
    publishedAt: ruleSetId === 1 ? undefined : new Date(Date.now() - 30 * 86400_000).toISOString()
  }
}

const mockSegments: PrioritySegmentDto[] = [
  {
    segmentId: 1,
    segmentCode: 'P1',
    segmentName: '客户关键订单',
    orderInSegment: 1,
    description: '客户指定交期/Top 客户/红线订单'
  },
  {
    segmentId: 2,
    segmentCode: 'P2',
    segmentName: '常规订单',
    orderInSegment: 2,
    description: '按下单顺序 + 交期'
  },
  {
    segmentId: 3,
    segmentCode: 'P3',
    segmentName: '库存补货',
    orderInSegment: 3,
    description: '备库型生产'
  }
]

const mockStrategyFor = (ruleSetId: number): RuleSetStrategyStubDto => {
  const isLegacy = ruleSetId === 4
  return {
    strategyProfileId: ruleSetId * 100,
    strategyProfileCode: `SP-${ruleSetId}`,
    strategyProfileName: `Solver 策略 #${ruleSetId}`,
    version: isLegacy ? 8 : 5,
    status: isLegacy ? 'DISABLED' : ruleSetId === 1 ? 'DRAFT' : 'PUBLISHED',
    solverStrategy: { approach: 'PRIORITY_FIRST', splitAllowed: !isLegacy, maxSplitCount: 3 },
    features: isLegacy
      ? ['SPLIT']
      : (['SPLIT', 'SETUP', 'OVERLAP'] as Array<'SPLIT' | 'SETUP' | 'OVERLAP'>),
    changeReason: ruleSetId === 1 ? '为草稿调整 Solver 配置' : undefined,
    createdAt: new Date(Date.now() - 7 * 86400_000).toISOString(),
    publishedAt: ruleSetId === 1 ? undefined : new Date(Date.now() - 7 * 86400_000).toISOString()
  }
}

const mockHistoryFor = (ruleSetId: number): RuleVersionHistoryDto[] => {
  const isLegacy = ruleSetId === 4
  const current = isLegacy ? 8 : ruleSetId === 1 ? 13 : ruleSetId === 2 ? 3 : 5
  return Array.from({ length: 3 }, (_, idx) => {
    const v = current - idx
    // v1.4 §二十：mock versionId = ruleSetId * 100 + version（与真实后端 ID 形态对齐）
    const versionId = ruleSetId * 100 + v
    return {
      ruleSetId,
      version: v,
      versionId,
      status: v === current && ruleSetId === 1 ? 'DRAFT' : v < current ? 'PUBLISHED' : 'PUBLISHED',
      changeReason:
        idx === 0
          ? ruleSetId === 1
            ? '调整 Demand Protection 阈值'
            : '最近一次发布'
          : `历史版本 v${v}`,
      publishedAt: new Date(Date.now() - (v * 7 + 1) * 86400_000).toISOString(),
      publishedBy: idx === 0 ? 'rule-admin' : `rule-admin-${v}`,
      isCurrent: idx === 0 && !isLegacy && ruleSetId !== 1
    }
  })
}

/* ===== Diff ===== */

const mockDiff = (ruleSetId: number, base: number, target: number): RuleDiffDto => {
  if (ruleSetId === 1) {
    return {
      baseVersion: base,
      targetVersion: target,
      changedParameters: [
        {
          parameterKey: 'demandProtectionThreshold',
          oldValue: 0.15,
          newValue: 0.2,
          oldDisplay: '15%',
          newDisplay: '20%'
        },
        {
          parameterKey: 'defaultPurchaseLT',
          oldValue: 7,
          newValue: 8,
          oldDisplay: '7 天',
          newDisplay: '8 天'
        }
      ],
      summary: { addedCount: 0, modifiedCount: 2, removedCount: 0 }
    }
  }
  return {
    baseVersion: base,
    targetVersion: target,
    changedParameters: [],
    summary: { addedCount: 0, modifiedCount: 0, removedCount: 0 }
  }
}

export const ruleApi = {
  /** RuleSet 列表（页面 8 入口）
   *  v1.4 §二十：3号位 GovernanceController 列表走 /api/governance/rule-sets
   *  旧 /api/rules 路径已废（lps 无此端点）
   *
   *  真实模式字段映射（B 设计稿 §3 落地后实测）：
   *  - 后端返回精简主表：{id, ruleSetCode, ruleSetName, description, isActive, createdAt, createdBy}
   *  - 前端 RuleSetSummaryDto 期望 13 字段，缺 5 个关键：domainKey / currentVersion / draftVersion /
   *    status / lastChangeReason / validationStatus
   *  - 缺字段在 mapping 时填占位（'ALL' / 0 / 'PENDING' / undefined），让页面先渲染骨架，
   *    详情加载后再回填（store action 负责）
   *  - 临时方案：等 3号位 列表端点扩展后切到完整 DTO（见回执/联调触发手册）
   */
  async listRuleSets(): Promise<RuleSetSummaryDto[]> {
    if (APS_USE_MOCK) return mockRuleSets
    type BackendRuleSet = {
      id: number
      ruleSetCode: string
      ruleSetName: string
      description?: string
      isActive?: boolean
      createdAt?: string
      createdBy?: string
    }
    const raw = await apsHttp.get<BackendRuleSet[]>({ url: '/api/governance/rule-sets' })
    return raw.map((r) => ({
      // 主表直连字段
      ruleSetId: r.id,
      ruleSetCode: r.ruleSetCode,
      ruleSetName: r.ruleSetName,
      createdAt: (r.createdAt ?? new Date().toISOString()) as RuleSetSummaryDto['createdAt'],
      // 占位字段（详情加载后由 store 回填）
      domainKey: 'ALL' as RuleSetSummaryDto['domainKey'],
      currentVersion: 0,
      draftVersion: undefined,
      // isActive=true 视为有发布版本，否则视为 DISABLED（保守；详情加载后校正）
      status: (r.isActive === false ? 'DISABLED' : 'PUBLISHED') as RuleSetSummaryDto['status'],
      lastChangeReason: r.description,
      publishedAt: undefined,
      retiredAt: undefined,
      validationStatus: 'PENDING' as const
    }))
  },

  /** RuleSet 当前已发布版本详情（v1.4 §二十 专用端点）
   *  后端：GET /api/governance/rule-set/{ruleSetId}/published-version
   *  返回 RuleSetVersion（含 ruleSetId/code/name/domainKey/version/status/parameters/segments/strategy）
   *  - 与旧"list versions + 过滤 PUBLISHED + 取 fullVersion"等价，但少 2 次请求
   *  - null 时表示该 RuleSet 暂无已发布版本（404 由调用方处理）
   */
  async getPublishedVersion(ruleSetId: number): Promise<unknown | null> {
    if (APS_USE_MOCK) {
      // mock：从 mockRuleSets 拼一条 RuleSetVersion 形状返回
      const s = mockRuleSets.find((r) => r.ruleSetId === ruleSetId)
      if (!s || s.status === 'DRAFT') return null
      return {
        id: s.ruleSetId * 100 + s.currentVersion,
        ruleSetId: s.ruleSetId,
        ruleSetCode: s.ruleSetCode,
        ruleSetName: s.ruleSetName,
        domainKey: s.domainKey,
        version: s.currentVersion,
        status: s.status,
        createdAt: s.createdAt,
        publishedAt: s.publishedAt,
        parameters: mockParametersFor(ruleSetId).parameters,
        prioritySegments: mockSegments,
        strategyProfile: mockStrategyFor(ruleSetId)
      }
    }
    try {
      return await apsHttp.get<unknown>({
        url: `/api/governance/rule-set/${ruleSetId}/published-version`
      })
    } catch (err) {
      // 404 → 暂无已发布版本（合规返回 null；上层区分"DRAFT 无发布"和"加载失败"）
      if ((err as { code?: number })?.code === 404) return null
      throw err
    }
  },

  /** RuleSet 详情（含参数集/段位/策略/历史）
   *  v1.4 §二十：后端提供 /rule-set/{id}/published-version 专用端点（v1.4 新增）
   *  优先调专用端点（少 2 次请求）；若 404（无已发布版本）再降级到 list+filter
   */
  async getRuleSet(ruleSetId: number): Promise<RuleSetDetailDto> {
    if (APS_USE_MOCK) return mockRuleSetDetail(ruleSetId)
    // 真实模式：先尝试 published-version 专用端点
    let fullVersion: any = null
    try {
      fullVersion = await apsHttp.get<any>({
        url: `/api/governance/rule-set/${ruleSetId}/published-version`
      })
    } catch (err) {
      if ((err as { code?: number })?.code !== 404) throw err
    }
    // 降级：list versions + 选 PUBLISHED（兼容旧 RuleSet 暂无 published-version 的边界）
    if (!fullVersion) {
      const versions = await apsHttp.get<unknown[]>({
        url: `/api/governance/rule-set/${ruleSetId}/versions`
      })
      if (!Array.isArray(versions) || versions.length === 0) {
        throw new Error(`RuleSet #${ruleSetId} 无可用版本`)
      }
      const published: any =
        versions.find((v: any) => v.status === 'PUBLISHED' || v.status === 'Published') ??
        versions[0]
      fullVersion = await apsHttp.get<any>({
        url: `/api/governance/rule-set/version/${published.id ?? published.Id}`
      })
      return {
        summary: {
          ruleSetId,
          ruleSetCode: fullVersion.ruleSetCode ?? fullVersion.RuleSetCode ?? `RS-${ruleSetId}`,
          ruleSetName: fullVersion.ruleSetName ?? fullVersion.RuleSetName ?? '',
          domainKey: fullVersion.domainKey ?? fullVersion.DomainKey ?? 'ALL',
          currentVersion: fullVersion.version ?? fullVersion.Version ?? 1,
          status: (fullVersion.status ?? fullVersion.Status ?? 'PUBLISHED').toUpperCase(),
          createdAt: fullVersion.createdAt ?? fullVersion.CreatedAt ?? new Date().toISOString(),
          publishedAt: fullVersion.publishedAt ?? fullVersion.PublishedAt ?? null,
          validationStatus: fullVersion.validationStatus ?? 'PASSED'
        },
        domainKey: (fullVersion.domainKey ?? fullVersion.DomainKey ?? 'ALL') as any,
        parameterSet: {
          parameterSetId: 0,
          parameterSetCode: '',
          parameterSetName: '见 parameter-set/*',
          version: 0,
          status: 'DRAFT' as const,
          parameters: [],
          createdAt: new Date().toISOString()
        },
        prioritySegments: [],
        strategyProfile: {
          strategyProfileId: 0,
          strategyProfileCode: '',
          strategyProfileName: '',
          version: 0,
          status: 'DRAFT' as const,
          solverStrategy: {},
          features: [],
          createdAt: new Date().toISOString()
        },
        history: versions.map((v: any) => ({
          ruleSetId,
          versionId: v.id ?? v.Id,
          version: v.version ?? v.Version ?? 0,
          status: (v.status ?? v.Status ?? 'DRAFT').toUpperCase() as
            | 'DRAFT'
            | 'SUBMITTED'
            | 'APPROVED'
            | 'PUBLISHED'
            | 'DISABLED'
            | 'ARCHIVED',
          changeReason: v.changeReason ?? v.ChangeReason ?? '',
          publishedAt: v.publishedAt ?? v.PublishedAt ?? null,
          publishedBy: v.publishedBy ?? v.PublishedBy ?? '',
          isCurrent: false
        }))
      }
    }
    // 优先路径：published-version 专用端点直返（少 2 次请求；减少不必要的版本列表）
    return {
      summary: {
        ruleSetId,
        ruleSetCode: fullVersion.ruleSetCode ?? fullVersion.RuleSetCode ?? `RS-${ruleSetId}`,
        ruleSetName: fullVersion.ruleSetName ?? fullVersion.RuleSetName ?? '',
        domainKey: fullVersion.domainKey ?? fullVersion.DomainKey ?? 'ALL',
        currentVersion: fullVersion.version ?? fullVersion.Version ?? 1,
        status: (fullVersion.status ?? fullVersion.Status ?? 'PUBLISHED').toUpperCase(),
        createdAt: fullVersion.createdAt ?? fullVersion.CreatedAt ?? new Date().toISOString(),
        publishedAt: fullVersion.publishedAt ?? fullVersion.PublishedAt ?? null,
        validationStatus: fullVersion.validationStatus ?? 'PASSED'
      },
      domainKey: (fullVersion.domainKey ?? fullVersion.DomainKey ?? 'ALL') as any,
      parameterSet: {
        parameterSetId: 0,
        parameterSetCode: '',
        parameterSetName: '见 parameter-set/*',
        version: 0,
        status: 'DRAFT' as const,
        parameters: [],
        createdAt: new Date().toISOString()
      },
      prioritySegments: [],
      strategyProfile: {
        strategyProfileId: 0,
        strategyProfileCode: '',
        strategyProfileName: '',
        version: 0,
        status: 'DRAFT' as const,
        solverStrategy: {},
        features: [],
        createdAt: new Date().toISOString()
      },
      history: []
    }
  },

  /** Rule Diff（发布前预览）
   *  v1.4 §二十：3号位 后端走 /api/governance/rule-set/version/diff?sourceVersionId=&targetVersionId=
   *  不再需要 ruleSetId（versionId 全局唯一）
   */
  async diffVersions(_ruleSetId: number, base: number, target: number): Promise<RuleDiffDto> {
    if (APS_USE_MOCK) return mockDiff(_ruleSetId, base, target)
    return apsHttp.get<RuleDiffDto>({
      url: '/api/governance/rule-set/version/diff',
      params: { sourceVersionId: base, targetVersionId: target }
    })
  },

  /** 提交 DRAFT 校验（U22：发布前必走）
   *  v1.4 §二十：后端 /api/governance/rule-set/version/{versionId}/validate
   *  调用方需传 versionId 而非 ruleSetId
   */
  async validateDraft(
    versionId: number
  ): Promise<{ status: 'PASSED' | 'FAILED'; message: string }> {
    if (APS_USE_MOCK) {
      return {
        status: 'PASSED',
        message: '校验通过：所有参数在合法范围内；不与已发布规则冲突'
      }
    }
    return apsHttp.get<{ status: 'PASSED' | 'FAILED'; message: string }>({
      url: `/api/governance/rule-set/version/${versionId}/validate`
    })
  },

  /** 发布（仅 RULE_PUBLISHER） — U22 新 Version 生成，历史不覆盖
   *  v1.4 §二十：后端 /api/governance/rule-set/version/{versionId}/publish
   *  调用方需传 versionId
   */
  async publish(input: PublishRuleInput): Promise<PublishRuleResult> {
    if (APS_USE_MOCK) {
      return {
        newVersion: input.draftVersion,
        ruleSetCode:
          mockRuleSets.find((r) => r.ruleSetId === input.ruleSetId)?.ruleSetCode ?? 'RS-?',
        publishedAt: new Date().toISOString(),
        historicalVersions: Array.from({ length: 5 }, (_, i) => input.draftVersion - i - 1),
        actor: input.actor
      }
    }
    const versionId = (input as any).versionId ?? input.draftVersion
    return apsHttp.post<PublishRuleResult>({
      url: `/api/governance/rule-set/version/${versionId}/publish`,
      data: { changeReason: input.changeReason }
    })
  },

  /** 退役（仅 RULE_PUBLISHER）→ v1.4 后端语义改为 disable
   *  v1.4 §二十：/api/governance/rule-set/version/{versionId}/disable
   *  不再使用 /retire 端点
   */
  async retire(input: RetireRuleInput): Promise<RetireRuleResult> {
    if (APS_USE_MOCK) {
      return {
        ruleSetCode:
          mockRuleSets.find((r) => r.ruleSetId === input.ruleSetId)?.ruleSetCode ?? 'RS-?',
        version: mockRuleSets.find((r) => r.ruleSetId === input.ruleSetId)?.currentVersion ?? 0,
        retiredAt: new Date().toISOString(),
        actor: input.actor
      }
    }
    const versionId = (input as any).versionId ?? input.version
    return apsHttp.post<RetireRuleResult>({
      url: `/api/governance/rule-set/version/${versionId}/disable`,
      data: { reason: input.reason }
    })
  },

  /* ====================================================================== */
  /* v1.4 §二十 写维护：RuleSet / ParameterSet DRAFT 全套端点                     */
  /* (B 设计稿 Step 1：7 函数封装 — RS+PS 平行 + fork 复合)                       */
  /* ====================================================================== */

  /** 新建 RuleSet DRAFT（POST /api/governance/rule-set/version）
   *  后端强制置 Status=DRAFT、治理字段清空（CreateRuleSetVersionAsync L1441-1454）
   */
  async createRuleSetDraft(input: CreateRuleSetDraftInput): Promise<RuleSetVersion> {
    if (APS_USE_MOCK) {
      // mock：基于 mockRuleSets 派生一条新的 DRAFT（仅 mock 演示用）
      const base = mockRuleSets.find((r) => r.ruleSetId === input.ruleSetId)
      const nextVersion = (base?.currentVersion ?? 0) + 1
      return {
        id: (base?.ruleSetId ?? input.ruleSetId) * 1000 + nextVersion,
        ruleSetId: input.ruleSetId,
        parameterSetVersionId: input.parameterSetVersionId,
        versionCode: input.versionCode,
        status: 'DRAFT',
        createdAt: new Date().toISOString(),
        remarks: input.remarks
      }
    }
    return apsHttp.post<RuleSetVersion>({
      url: '/api/governance/rule-set/version',
      data: input
    })
  },

  /** 更新 RuleSet DRAFT（PUT /api/governance/rule-set/version/{versionId}）
   *  后端实现：冻结治理字段，其余字段整对象落库（UpdateRuleSetVersionAsync L1457-1477）
   *  前端必须把 RuleSetVersion 完整对象（含全部 5 JSON 字符串）回传
   */
  async updateRuleSetDraft(input: UpdateRuleSetDraftInput): Promise<RuleSetVersion> {
    if (APS_USE_MOCK) {
      // mock：直接回显入参（前端 buffer 即后端真相）
      return { ...input.body, id: input.versionId }
    }
    return apsHttp.put<RuleSetVersion>({
      url: `/api/governance/rule-set/version/${input.versionId}`,
      data: input.body
    })
  },

  /** 发布 RuleSet DRAFT（POST /api/governance/rule-set/version/{versionId}/publish）
   *  独立端点：ParameterSet publish 不级联（Q3 答复）
   */
  async publishRuleSet(input: PublishGovernanceInput): Promise<PublishGovernanceResult> {
    if (APS_USE_MOCK) {
      return {
        newVersion: 99,
        governanceCode: `RS-MOCK-${input.versionId}`,
        publishedAt: new Date().toISOString(),
        actor: input.actor
      }
    }
    return apsHttp.post<PublishGovernanceResult>({
      url: `/api/governance/rule-set/version/${input.versionId}/publish`,
      data: { changeReason: input.changeReason }
    })
  },

  /** 新建 ParameterSet DRAFT（POST /api/governance/parameter-set/version）
   *  与 createRuleSetDraft 平行；同样后端强制置 Status=DRAFT
   */
  async createParameterSetDraft(input: CreateParameterSetDraftInput): Promise<ParameterSetVersion> {
    if (APS_USE_MOCK) {
      const nextVersion = Math.floor(Math.random() * 100) + 100
      return {
        id: input.parameterSetId * 1000 + nextVersion,
        parameterSetId: input.parameterSetId,
        versionCode: input.versionCode,
        status: 'DRAFT',
        createdAt: new Date().toISOString(),
        remarks: input.remarks
      }
    }
    return apsHttp.post<ParameterSetVersion>({
      url: '/api/governance/parameter-set/version',
      data: input
    })
  },

  /** 更新 ParameterSet DRAFT（PUT /api/governance/parameter-set/version/{versionId}）
   *  full-object PUT：5 JSON 字符串必须全量回传（B 设计稿 §2.1 算法 3）
   */
  async updateParameterSetDraft(input: UpdateParameterSetDraftInput): Promise<ParameterSetVersion> {
    if (APS_USE_MOCK) {
      return { ...input.body, id: input.versionId }
    }
    return apsHttp.put<ParameterSetVersion>({
      url: `/api/governance/parameter-set/version/${input.versionId}`,
      data: input.body
    })
  },

  /** 发布 ParameterSet DRAFT（POST /api/governance/parameter-set/version/{versionId}/publish）
   *  独立端点：与 RuleSet publish 不级联（Q3 答复）
   */
  async publishParameterSet(input: PublishGovernanceInput): Promise<PublishGovernanceResult> {
    if (APS_USE_MOCK) {
      return {
        newVersion: 99,
        governanceCode: `PS-MOCK-${input.versionId}`,
        publishedAt: new Date().toISOString(),
        actor: input.actor
      }
    }
    return apsHttp.post<PublishGovernanceResult>({
      url: `/api/governance/parameter-set/version/${input.versionId}/publish`,
      data: { changeReason: input.changeReason }
    })
  },

  /** Fork Draft 复合（B 设计稿 §1.3 入口 B + §1.1.1 RS↔PS 1:1）
   *  前端手工 fork 走 GET → POST 两步：先 GET 源 RS+PS → 改 VersionCode → POST 双发
   *  流程：
   *   1. GET /rule-set/version/{sourceRuleSetVersionId} 取源 RS（含 parameterSetVersionId）
   *   2. GET /parameter-set/version/{parameterSetVersionId} 取源 PS
   *   3. POST /rule-set/version + POST /parameter-set/version 双发
   *   4. 任一失败即停 + 回滚（前端 dirty 状态由 store action 控制）
   *
   *  注：本方法只在真实模式实现；mock 模式由前端 store 直接拼 mock 数据
   */
  async forkDraft(
    sourceRuleSetVersionId: number,
    newVersionCode: string
  ): Promise<ForkDraftResult> {
    if (APS_USE_MOCK) {
      throw new Error('mock 模式下请使用 store forkDraft() 内部分支；本端点仅真实模式')
    }
    // 1. GET 源 RuleSetVersion
    const sourceRs = await apsHttp.get<RuleSetVersion>({
      url: `/api/governance/rule-set/version/${sourceRuleSetVersionId}`
    })
    // 2. GET 源 ParameterSetVersion（通过 sourceRs.parameterSetVersionId）
    const sourcePs = await apsHttp.get<ParameterSetVersion>({
      url: `/api/governance/parameter-set/version/${sourceRs.parameterSetVersionId}`
    })
    // 3. POST 双发
    const [ruleSetDraft, parameterSetDraft] = await Promise.all([
      apsHttp.post<RuleSetVersion>({
        url: '/api/governance/rule-set/version',
        data: {
          ruleSetId: sourceRs.ruleSetId,
          versionCode: newVersionCode,
          sourceRuleSetVersionId,
          parameterSetVersionId: sourceRs.parameterSetVersionId
        }
      }),
      apsHttp.post<ParameterSetVersion>({
        url: '/api/governance/parameter-set/version',
        data: {
          parameterSetId: sourcePs.parameterSetId,
          versionCode: newVersionCode,
          sourceParameterSetVersionId: sourcePs.id
        }
      })
    ])
    return { ruleSetDraft, parameterSetDraft }
  }
}

/** 客户端判断：当前 actor 是否拥有某角色（mock 模式用固定 actor，真实模式由 JWT 注入）
 *  v1.2 DDL：RULE_ADMIN + RULE_PUBLISHER 合并为单一 aps.admin.aps 角色
 */
export const mockActor: { name: string; roles: RoleKey[] } = {
  name: 'rule-admin',
  roles: ['aps.admin.aps']
}
