/**
 * APS V1 4号位 — 排程域（Domain）定义 DTO
 *
 * @see 冻结文档《APS_V1_Domain定义分域计算与运行边界业务裁决_v1.0_20260901.md》
 * @see 4号位开发包 v1.2 §Domain专项 1-9
 *
 * 核心定义：
 *  - Domain 是 APS 独立稳定排程边界（不等于 ProductFamily、不等于 Stage）
 *  - V1 ScopeType 只支持 FAMILY / FACTORY_FAMILY 两种
 *  - DomainDefinition 由 3号位治理、2号位执行真实归域
 *  - 同一 ProductFamily 在不同工厂可拆成 FACTORY_FAMILY（如 BJ_FAMILY_INJECTION）
 *
 * DomainKey 命名规范：
 *  - FAMILY_<Family>              如 FAMILY_INJECTION
 *  - <Factory>_FAMILY_<Family>    如 BJ_FAMILY_INJECTION
 */

import type { DomainKey, IsoDateTime } from './common'

/**
 * Domain 范围类型（V1 仅两种）
 *  - FAMILY           按产品族整体排程（跨工厂）
 *  - FACTORY_FAMILY   按"工厂+产品族"切片排程（如 BJ 注塑 vs SUZ 注塑 独立）
 */
export type DomainScopeType = 'FAMILY' | 'FACTORY_FAMILY'

/** DomainScopeType 中文标签（UI 显示用） */
export const DOMAIN_SCOPE_TYPE_LABELS: Record<DomainScopeType, string> = {
  FAMILY: '按产品族',
  FACTORY_FAMILY: '按工厂+产品族'
}

/**
 * 产品族摘要 DTO（来自 3号位下拉读端点 GET /api/governance/product-families）
 *
 * 4号位场景：
 *  - Domain 维护页「产品族」选择器数据源
 *  - 表格列展示（lookup productFamilyId → name）
 *  - 与 ProductFamilyDto（后端 Core）字段对齐：id + code + name
 */
export interface ProductFamilySummaryDto {
  /** 数据库主键 */
  id: number
  /** 产品族代码（如 'INJECTION'） */
  code: string
  /** 产品族名称（中文） */
  name: string
}

/**
 * 工厂摘要 DTO（来自 3号位下拉读端点 GET /api/governance/factories）
 *
 * 4号位场景：
 *  - Domain 维护页「工厂」选择器数据源（FACTORY_FAMILY 必填）
 *  - 表格列展示（lookup factoryId → name）
 *  - 与 FactoryDto（后端 Core）字段对齐：id + code + name
 */
export interface FactorySummaryDto {
  /** 数据库主键 */
  id: number
  /** 工厂代码（如 'F-BJ-01'） */
  code: string
  /** 工厂名称（中文） */
  name: string
}

/**
 * Domain 定义 DTO（来自 3号位治理接口 GET /api/domain/definitions）
 *
 * 4号位场景：
 *  - CTP/Candidate/Run/PI/Rules 页面展示 domainName + scopeType + productFamilyName
 *  - 顶部 Domain 切换器下拉选项
 *  - Domain 维护页（v1.2 Pkg-3）CRUD 用
 *  - 业务范围（Scope）domainKeys 白名单依据
 *
 * 字段变更（2026-09-16，3号位契约收口）：
 *  - productFamily: string  → productFamilyId: number（FK 到 ProductFamily.Id）
 *  - factory?: string       → factoryId?: number（FK 到 Factory.Id）
 *  - 维护页新增 ProductFamily / Factory 选择器，数据源调 GET /api/governance/product-families + /factories
 */
export interface DomainDefinitionDto {
  /** 数据库主键（后端 DomainDefinition.Id；前端 update/enable/disable 用，detail 按 id 查）
   *  - mock 模式下为模拟值（1/2/3/4）；后端真实下发时由 3 号位 GovernanceController 填入
   *  - 缺省：未拉过 list 或新建尚未返回的临时态
   */
  id?: number
  /** 域标识（系统生成、不可改） */
  domainKey: DomainKey
  /** 域名称（中文，可改） */
  domainName: string
  /** 范围类型 */
  scopeType: DomainScopeType
  /** 关联产品族 ID（FK 到 ProductFamily.Id；维护页通过下拉选） */
  productFamilyId: number
  /** 工厂 ID（FK 到 Factory.Id；FACTORY_FAMILY 必填；FAMILY 为空） */
  factoryId?: number
  /** 是否启用（false 时下拉不可选，CTP/Candidate 不可见） */
  isActive: boolean
  /** 排序权重（升序） */
  sortOrder?: number
  /** 更新人 */
  updatedBy?: string
  /** 更新时间（ISO8601） */
  updatedAt?: IsoDateTime
  /** 创建人（后端 DomainDefinition 实体字段；联调 §D01 已对齐） */
  createdBy?: string
  /** 创建时间（后端实体字段；联调 §D01 已对齐） */
  createdAt?: IsoDateTime
}

/**
 * Domain 列表查询请求
 */
export interface DomainListQuery {
  /** 仅查启用项（默认 true；维护页传 false 含已停用） */
  activeOnly?: boolean
  /** 按产品族 ID 过滤 */
  productFamilyId?: number
  /** 按工厂 ID 过滤 */
  factoryId?: number
}

/* ==================== Mock 规范键（前后端约定） ==================== */

/**
 * Mock 模式 DomainKey 字面量集合（与 fixtures 严格一致）
 *
 * 替换旧的 'INJECTION' / 'ASSEMBLY' / 'TEST' 字面量：
 *  - 旧 'INJECTION'    →  FAMILY_INJECTION
 *  - 旧 'ASSEMBLY'     →  FAMILY_ASSEMBLY
 *  - 旧 'TEST'         →  FAMILY_TEST
 *
 * 注：V1 mock 数据主要展示 FAMILY 场景；FACTORY_FAMILY 留给 3号位治理接口落地后接入
 */
export const MOCK_DOMAIN_KEYS = {
  FAMILY_INJECTION: 'FAMILY_INJECTION',
  FAMILY_ASSEMBLY: 'FAMILY_ASSEMBLY',
  FAMILY_TEST: 'FAMILY_TEST',
  /** 多工厂切片示例（暂时只 mock 出键，不接入业务数据） */
  BJ_FAMILY_INJECTION: 'BJ_FAMILY_INJECTION',
  SUZ_FAMILY_INJECTION: 'SUZ_FAMILY_INJECTION'
} as const satisfies Record<string, DomainKey>

/** Mock 全部 Domain 键数组（按规范键顺序） */
export const MOCK_DOMAIN_KEY_LIST: DomainKey[] = [
  MOCK_DOMAIN_KEYS.FAMILY_INJECTION,
  MOCK_DOMAIN_KEYS.BJ_FAMILY_INJECTION,
  MOCK_DOMAIN_KEYS.FAMILY_ASSEMBLY,
  MOCK_DOMAIN_KEYS.FAMILY_TEST
]
