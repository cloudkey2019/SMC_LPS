/**
 * APS V1 4号位 — Domain 定义 API（v1.2 Domain专项 Pkg-2 / Pkg-3）
 *
 * @owner 3号位（DomainDefinition 治理接口 — 增删改查 / 启停用）
 * @see 冻结文档《APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md》§四
 * @see 4号位开发包 v1.2 §Domain专项 1-9
 *
 * 用途：
 *  - Domain 维护页（v1.2 Pkg-3）CRUD：list/detail/create/update/enable/disable
 *  - 顶部 Domain 切换器 / CTP / Candidate / Run / PI 页面共用 domainKey 字典
 *  - 3号位 governance 流程：FAMILY ↔ FACTORY_FAMILY 切换 / 工厂扩展
 *
 * 注意：
 *  - V1 mock 数据只覆盖 FAMILY 三键（INJECTION/ASSEMBLY/TEST）
 *  - FACTORY_FAMILY 由 3号位治理接口落地后接入（预留键 BJ_FAMILY_INJECTION）
 *  - Domain 列表由 3号位治理后下发到 4号位；4号位不维护 DomainDefinition
 */

import { apsHttp, ApiError, APS_USE_MOCK } from './http'
import type {
  DomainDefinitionDto,
  DomainListQuery,
  FactorySummaryDto,
  ProductFamilySummaryDto
} from './types'

/**
 * 标识「接口未实现」（HTTP 404 / 业务 404）
 *  - 由 domainApi.list() 在真实后端 /api/governance/domain-definition 不可用时抛出
 *  - store 捕获后设 fetchStatus='unavailable'，UI 用 banner 提示用户
 *  - 与 mock 模式无关：mock 模式下根本不发请求
 */
export class EndpointUnavailableError extends ApiError {
  constructor(message: string, traceId?: string, timestamp?: string) {
    super(message, 404, traceId, timestamp)
    this.name = 'EndpointUnavailableError'
  }
}

/* ==================== Mock Fixtures ==================== */

const now = (): string => new Date().toISOString()

/**
 * V1 mock 产品族字典（domain 维护页「产品族」下拉数据源）
 *
 * 字段变更（2026-09-16，3号位契约收口）：
 *  - productFamily: string code → productFamilyId: number FK
 *  - id 与代码对应：1='INJECTION', 2='ASSEMBLY', 3='TEST'
 */
export const MOCK_PRODUCT_FAMILY_FIXTURES: ProductFamilySummaryDto[] = [
  { id: 1, code: 'INJECTION', name: '注塑' },
  { id: 2, code: 'ASSEMBLY', name: '装配' },
  { id: 3, code: 'TEST', name: '测试' }
]

/**
 * V1 mock 工厂字典（domain 维护页「工厂」下拉数据源，FACTORY_FAMILY 必填）
 *
 * id 与代码对应：1='F-BJ-01', 2='F-SUZ-01'
 */
export const MOCK_FACTORY_FIXTURES: FactorySummaryDto[] = [
  { id: 1, code: 'F-BJ-01', name: '北京一厂' },
  { id: 2, code: 'F-SUZ-01', name: '苏州一厂' }
]

/**
 * V1 mock Domain 字典（与 MOCK_DOMAIN_KEYS 严格一致）
 *
 * 替代旧的 'INJECTION' / 'ASSEMBLY' / 'TEST' 字面量：
 *  - 'INJECTION'   → FAMILY_INJECTION
 *  - 'ASSEMBLY'    → FAMILY_ASSEMBLY
 *  - 'TEST'        → FAMILY_TEST
 *
 * 字段变更（2026-09-16，3号位契约收口）：
 *  - productFamily: string  → productFamilyId: number（FK 到 MOCK_PRODUCT_FAMILY_FIXTURES.id）
 *  - factory?: string      → factoryId?: number（FK 到 MOCK_FACTORY_FIXTURES.id）
 */
export const MOCK_DOMAIN_FIXTURES: DomainDefinitionDto[] = [
  {
    id: 1,
    domainKey: 'FAMILY_INJECTION',
    domainName: '注塑域',
    scopeType: 'FAMILY',
    productFamilyId: 1,
    isActive: true,
    sortOrder: 10,
    updatedBy: 'system',
    updatedAt: now()
  },
  {
    id: 2,
    domainKey: 'FAMILY_ASSEMBLY',
    domainName: '装配域',
    scopeType: 'FAMILY',
    productFamilyId: 2,
    isActive: true,
    sortOrder: 20,
    updatedBy: 'system',
    updatedAt: now()
  },
  {
    id: 3,
    domainKey: 'FAMILY_TEST',
    domainName: '测试域',
    scopeType: 'FAMILY',
    productFamilyId: 3,
    isActive: true,
    sortOrder: 30,
    updatedBy: 'system',
    updatedAt: now()
  },
  // v1.2 Domain专项 §四：FACTORY_FAMILY 实例（同 INJECTION 产品族，按工厂切片）
  // 演示"按工厂+产品族"独立排程边界（BJ 注塑 vs SUZ 注塑）
  {
    id: 4,
    domainKey: 'BJ_FAMILY_INJECTION',
    domainName: '北京注塑域',
    scopeType: 'FACTORY_FAMILY',
    productFamilyId: 1,
    factoryId: 1,
    isActive: true,
    sortOrder: 11,
    updatedBy: 'system',
    updatedAt: now()
  }
]

/* ==================== API ==================== */

/** 真实后端端点前缀（与 3 号位 GovernanceController 对齐 — G-D01~G-D16）*/
const DOMAIN_API_BASE = '/api/governance/domain-definition'

export const domainApi = {
  /**
   * Domain 定义列表（治理 + 业务共用）
   * @param query activeOnly/productFamilyId/factoryId 客户端过滤（后端 list 不支持）
   */
  async list(query: DomainListQuery = {}): Promise<DomainDefinitionDto[]> {
    if (APS_USE_MOCK) {
      let result = MOCK_DOMAIN_FIXTURES.slice()
      if (query.activeOnly !== false) {
        result = result.filter((d) => d.isActive)
      }
      if (query.productFamilyId !== undefined) {
        result = result.filter((d) => d.productFamilyId === query.productFamilyId)
      }
      if (query.factoryId !== undefined) {
        result = result.filter((d) => d.factoryId === query.factoryId)
      }
      return result.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    }
    try {
      // 后端 GET /api/governance/domain-definition 返回全部（含停用）；activeOnly/productFamilyId/factoryId 不支持
      // 客户端按需过滤
      const all = await apsHttp.get<DomainDefinitionDto[]>({ url: DOMAIN_API_BASE })
      let result = all
      if (query.activeOnly !== false) {
        result = result.filter((d) => d.isActive)
      }
      if (query.productFamilyId !== undefined) {
        result = result.filter((d) => d.productFamilyId === query.productFamilyId)
      }
      if (query.factoryId !== undefined) {
        result = result.filter((d) => d.factoryId === query.factoryId)
      }
      return result.sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
    } catch (err) {
      // 真实后端调用失败：
      //  - 404 → 接口未实现（3号位 GovernanceController 路径变更）→ 抛 EndpointUnavailableError
      //    由 store 设 fetchStatus='unavailable'，UI 用 banner 提示；不再静默回退 mock（避免误导）
      //  - 其它错误 → 透传 ApiError
      const apiErr = err as ApiError
      if (apiErr?.code === 404) {
        console.warn(
          `[APS domainApi.list] 后端 ${DOMAIN_API_BASE} 返回 404（接口未实现或路径变更）。` +
            'dev 模式如需使用 mock 数据，请设 VITE_USE_MOCK=true；生产模式请联系 3 号位核对 GovernanceController 路由。'
        )
        throw new EndpointUnavailableError(
          `Domain 字典接口不可用（${DOMAIN_API_BASE} 404）`,
          apiErr.traceId,
          apiErr.timestamp as string | undefined
        )
      }
      throw err
    }
  },

  /** 单个 Domain 定义（按数据库 Id；后端 GET /api/governance/domain-definition/{id}） */
  async detail(id: number): Promise<DomainDefinitionDto | null> {
    if (APS_USE_MOCK) {
      return MOCK_DOMAIN_FIXTURES.find((d) => d.id === id) ?? null
    }
    return apsHttp.get<DomainDefinitionDto>({
      url: `${DOMAIN_API_BASE}/${id}`
    })
  },

  /** 创建 Domain（仅 SYSTEM_ADMIN 角色；前端 Domain 维护页用） */
  async create(
    input: Omit<DomainDefinitionDto, 'id' | 'updatedBy' | 'updatedAt'>
  ): Promise<DomainDefinitionDto> {
    if (APS_USE_MOCK) {
      if (MOCK_DOMAIN_FIXTURES.some((d) => d.domainKey === input.domainKey)) {
        throw new Error(`Domain 已存在：${input.domainKey}`)
      }
      const nextId = Math.max(0, ...MOCK_DOMAIN_FIXTURES.map((d) => d.id ?? 0)) + 1
      const created: DomainDefinitionDto = {
        id: nextId,
        ...input,
        updatedBy: 'rule-admin',
        updatedAt: now()
      }
      MOCK_DOMAIN_FIXTURES.push(created)
      return created
    }
    return apsHttp.post<DomainDefinitionDto>({
      url: DOMAIN_API_BASE,
      data: input
    })
  },

  /** 更新 Domain（按数据库 Id；domainKey 不可改，其它字段可改） */
  async update(
    id: number,
    patch: Partial<Omit<DomainDefinitionDto, 'id' | 'domainKey' | 'updatedBy' | 'updatedAt'>>
  ): Promise<DomainDefinitionDto> {
    if (APS_USE_MOCK) {
      const idx = MOCK_DOMAIN_FIXTURES.findIndex((d) => d.id === id)
      if (idx < 0) throw new Error(`Domain 不存在：id=${id}`)
      const merged: DomainDefinitionDto = {
        ...MOCK_DOMAIN_FIXTURES[idx],
        ...patch,
        id,
        updatedBy: 'rule-admin',
        updatedAt: now()
      }
      MOCK_DOMAIN_FIXTURES[idx] = merged
      return merged
    }
    return apsHttp.put<DomainDefinitionDto>({
      url: `${DOMAIN_API_BASE}/${id}`,
      data: patch
    })
  },

  /** 启用（专用 POST 端点；后端 POST /api/governance/domain-definition/{id}/enable） */
  async enable(id: number): Promise<DomainDefinitionDto> {
    if (APS_USE_MOCK) {
      return this.update(id, { isActive: true })
    }
    return apsHttp.post<DomainDefinitionDto>({
      url: `${DOMAIN_API_BASE}/${id}/enable`,
      data: {}
    })
  },

  /** 停用（停用后下拉不可选、CTP/Candidate 不可见；已有 PlanVersion 不影响）
   *  - 后端 POST /api/governance/domain-definition/{id}/disable
   */
  async disable(id: number): Promise<DomainDefinitionDto> {
    if (APS_USE_MOCK) {
      return this.update(id, { isActive: false })
    }
    return apsHttp.post<DomainDefinitionDto>({
      url: `${DOMAIN_API_BASE}/${id}/disable`,
      data: {}
    })
  },

  /** 产品族字典（下拉读端点；3号位 GET /api/governance/product-families）
   *  - 用于 Domain 维护页「产品族」选择器
   *  - 表格列展示（lookup productFamilyId → name）
   *  - 字段变更（2026-09-16）：替代旧的 string code 选择器
   */
  async listProductFamilies(): Promise<ProductFamilySummaryDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_PRODUCT_FAMILY_FIXTURES.slice()
    }
    return apsHttp.get<ProductFamilySummaryDto[]>({
      url: '/api/governance/product-families'
    })
  },

  /** 工厂字典（下拉读端点；3号位 GET /api/governance/factories）
   *  - 用于 Domain 维护页「工厂」选择器（FACTORY_FAMILY 必填）
   *  - 字段变更（2026-09-16）：替代旧的 string code 选择器
   */
  async listFactories(): Promise<FactorySummaryDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_FACTORY_FIXTURES.slice()
    }
    return apsHttp.get<FactorySummaryDto[]>({
      url: '/api/governance/factories'
    })
  }
}

export default domainApi
