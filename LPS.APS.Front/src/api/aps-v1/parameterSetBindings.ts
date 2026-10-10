/**
 * APS V1 4号位 — ParameterSet / RuleSet 真实 key 绑定表（B 设计稿 §5 Step D）
 *
 * 目的：
 *  - 用真实后端 key → 元数据（sensitive / editable / type / alias）映射覆盖 §2.2 heuristic
 *  - 用户在浏览器抓两份 response.json（RS + PS）→ 把 key 列填到 bindings.json
 *  - import 此 bindings.json → register() → 后续 isSensitive/isEditable/inferType 自动优先用 binding
 *
 * 用法：
 *   import { registerParameterSetBindings, lookupBinding } from '@/api/aps-v1/parameterSetBindings'
 *
 *   // 1. 用户从 docs/parameter-set-bindings.sample.json 拷到 src/api/aps-v1/parameter-set.bindings.json
 *   import bindings from './parameter-set.bindings.json'
 *   registerParameterSetBindings(bindings)
 *
 *   // 2. flattenBlocks 时自动优先 lookupBinding，缺则 fallback 到 heuristic
 *
 * 联调触发 Step A-F 详见 docs/4号位-2026-09-21-§5联调触发操作手册-给用户.md
 */

/** 单条绑定（覆盖 heuristic） */
export interface ParameterBinding {
  /** 真实后端 key（如 'planningYield' / 'policies.priority.segments'） */
  keyPath: string
  /** 用户可读的 parameterName（前端 UI 展示用） */
  parameterName?: string
  /** 是否敏感（true 时 UI 显示"敏感"标签） */
  sensitive?: boolean
  /** 是否可编辑（false 时 UI 禁用控件） */
  editable?: boolean
  /** 显式类型（缺则 fallback 到 heuristic inferType） */
  type?: 'NUMBER' | 'BOOLEAN' | 'STRING' | 'PERCENT' | 'DURATION'
  /** 业务说明（detail 列展示） */
  description?: string
}

/** 绑定来源（按 block 分组） */
export interface BlockBindings {
  lock?: ParameterBinding[]
  supply?: ParameterBinding[]
  procurement?: ParameterBinding[]
  solverStrategy?: ParameterBinding[]
  candidateGuardrail?: ParameterBinding[]
}

/** 完整绑定集（RS + PS 各一份） */
export interface ParameterSetBindingsConfig {
  parameterSet: BlockBindings
  ruleSet?: BlockBindings
}

/* ===== 全局注册表 ===== */

/** keyPath → binding 的快查（点分路径） */
const globalRegistry = new Map<string, ParameterBinding>()

/** 把一份 bindings 注册到全局表 */
export function registerParameterSetBindings(config: ParameterSetBindingsConfig): void {
  const blocks: Array<[string, ParameterBinding[] | undefined]> = [
    ['lock', config.parameterSet.lock],
    ['supply', config.parameterSet.supply],
    ['procurement', config.parameterSet.procurement],
    ['solverStrategy', config.parameterSet.solverStrategy],
    ['candidateGuardrail', config.parameterSet.candidateGuardrail]
  ]
  for (const [, arr] of blocks) {
    if (!arr) continue
    for (const b of arr) {
      globalRegistry.set(b.keyPath, b)
    }
  }
  if (config.ruleSet) {
    for (const [, arr] of [
      ['ruleSet.lock', config.ruleSet.lock],
      ['ruleSet.supply', config.ruleSet.supply],
      ['ruleSet.procurement', config.ruleSet.procurement],
      ['ruleSet.solverStrategy', config.ruleSet.solverStrategy],
      ['ruleSet.candidateGuardrail', config.ruleSet.candidateGuardrail]
    ] as Array<[string, ParameterBinding[] | undefined]>) {
      if (!arr) continue
      for (const b of arr) {
        globalRegistry.set(b.keyPath, b)
      }
    }
  }
}

/** 查询 binding（缺则返回 undefined — 由调用方 fallback 到 heuristic） */
export function lookupBinding(keyPath: string): ParameterBinding | undefined {
  return globalRegistry.get(keyPath)
}

/** 测试/调试用：清空注册表 */
export function clearBindings(): void {
  globalRegistry.clear()
}

/** 测试/调试用：查询当前注册数 */
export function bindingCount(): number {
  return globalRegistry.size
}

/* ===== 集成入口（draftBuffer.ts 使用） ===== */
/**
 * 检查 key 是否有真实绑定（覆盖 heuristic 的入口）
 * 注：实际集成时由 draftBuffer.ts 的 isSensitive/isEditable/inferType 优先 lookup
 */
export function hasBinding(keyPath: string): boolean {
  return globalRegistry.has(keyPath)
}
