# 4 号位 B 设计稿：ParameterSet 写维护 —— 5 JSON 缓冲 + diff 算法 + 6 态映射

> **状态**：B 设计（实施前）；已审过 Round 1（5 个严重 Issue 已闭环）→ 等用户审 → 进 A
> **更新**：Round 1 修订（2026-09-21 当日）—— 见 § 0 标 🆕 的决策行；5 项严重 Issue 闭环依据：
> - Issue 1：两入口（看 DRAFT vs 建草稿）→ § 1.3 5 时机
> - Issue 2：RS/PS 关系 → § 0 + § 1.2 双轨 + § 5 步骤 1 加 RS 端点
> - Issue 5：dirty 联动 → § 2.4 store actions 包装
> - Issue 6：buildPutBody 显式 DRAFT → § 2.1 算法 3
> - Issue 9：fork 状态矩阵 → § 3.4
>
> **依据**：《4号位-2026-09-21-ParameterSet写维护前端对接-给3号位.md》Q1-Q5 + 《3号位致4号位_ParameterSet写维护对接回执_v1.0_20260921.md》Q1-Q5 答复
> **生效范围**：步骤 1-8 实施全周期；改动集中在 `src/views/Aps/Rules.vue` + `src/api/aps-v1/rule.ts` + `src/store/modules/aps/rules.ts`

---

## § 0. 结论先放

| 议题 | 设计结论 |
|---|---|
| 🆕 **双轨对象模型** | RuleSet 与 ParameterSet 是 **1:1 关联**（每条 RuleSet 含一条对应的 ParameterSet，通过 RuleSetVersion.strategyProfile.parameterset 字段引用，详见 § 1.1 补注）；store **同时持两份 buffer**（`ruleSetBuffer` + `parameterSetBuffer`），并行编辑互不干扰 |
| 🆕 **两入口分立** | 看 DRAFT（点列表卡片）= `loadDetail(draftId)` 走 GET 一发；建草稿（[+ 新建] 按钮）= `forkDraft(PUBLISHED)` 走 GET → POST 两步；入口明确，避免数据流向混淆 |
| **5 JSON 缓冲形态** | 每条对象（RS + PS）三态 `{originalBlocks, workingBlocks, dirty}`；治理字段 `shallowRef + readonly` 防 Vue reactive 透冻失效（详见 § 1.2 补充） |
| **diff 算法** | path-based local mutation（仅修改 `{block, path}` 指向的字段），但 PUT 是 full-object（5 JSON 全量回传） |
| 🆕 **dirty 联动** | applyEdit / applyEditArray 是 **pure functions**；dirty 设置在 **store action 包装层**（`onCellEdit` → 内部调 applyEdit + 联动 dirty），避免 pure 漏调（详见 § 2.4） |
| 🆕 **buildPutBody 显式 DRAFT** | 算法 3 spread governance 后**强制 `status = 'DRAFT'`**；不依赖后端 service 入参忽略（前端代码自证 + 防后端改动连锁） |
| 🆕 **fork 状态矩阵** | 仅 PUBLISHED + DISABLED 允许 [+ 新建草稿]（详见 § 3.4）；SUBMITTED / APPROVED / ARCHIVED 不允许 fork；规则实施在 `canCreateDraftForStatus()` 函数 |
| **扁平化参数表** | `flattenBlocks()` 通用 walk（不依赖硬编码 mock 的 5 key）—— GET 一次真实 DRAFT 对齐后，落 `bindRulesForParameterSet()` 映射函数 |
| **元数据 heuristic** | sensitive / editable / type 字段，因后端 DTO 未提供，按 key 名 + value 类型启发式推断；联调时填实值 |
| **6 态对齐** | `STATUS_LABEL/TAG` 扩 6 态（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）；**RETIRED → DISABLED 归一**（前端 DTO 兼容层处理） |
| **按钮可见性** | UI 仅暴露 4 流转按钮（详见 § 3.2 修订）；SUBMITTED/APPROVED/ARCHIVED 只读不暴露流转 |
| **双 publish** | `publishRuleSet()` + `publishParameterSet()` 串行（顺序：先 RuleSet 再 ParameterSet；任一失败即停） |
| **风险主点** | Q4 元数据：sensitive/editable 启发式；嵌套对象编辑暂不开；并发编辑不做（后端乐观锁兜底）；RS/PS 双轨同步一致性 |

---

## § 1. 5 JSON 缓冲数据结构

### 1.1 后端实体形状（`ParameterSetVersion.cs` 实测）

```ts
// 治理字段（PUT 时一律冻结取自原记录，service 强制置 Status=DRAFT 入参忽略）
interface ParameterSetVersionGovernance {
  id: number
  parameterSetId: number
  versionCode: string
  status: 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'PUBLISHED' | 'DISABLED' | 'ARCHIVED'
  effectiveFrom?: string
  effectiveTo?: string
  publishedAt?: string
  publishedBy?: string
  approvedAt?: string
  approvedBy?: string
  createdAt: string
  createdBy?: string
  remarks?: string
}
// 5 主题 JSON 字段（字符串，存库时序列化；前端取出来反序列化）
interface ParameterSetVersionBlocks {
  lockJson?: string
  supplyJson?: string
  procurementJson?: string
  solverStrategyJson?: string
  candidateGuardrailJson?: string
  contentSnapshotJson?: string // 发版落库的全量快照（前端可读不写）
}
```

#### 1.1.1 🆕 RuleSet ↔ ParameterSet 1:1 关联（Issue 2 闭环）

经 lps `RuleSetVersion.cs` + `ParameterSetVersion.cs` 实测：

- 一条 `RuleSetVersion.id` 对应一条 `ParameterSetVersion.id`，**通过 `RuleSetVersion.parameterSetVersionId` 字段引用**
- 反之 `ParameterSetVersion` **未持有** RuleSetVersion 反向引用（单向 1:1）
- 含义：
  - 进入页面（RuleSet 列表选中）→ store **同时持 RS + PS 两套 buffer**（互不干扰）
  - 改 RS → 只走 RuleSetVersion PUT + ParameterSetVersion PUT（两份独立）
  - 改 PS → 只走 ParameterSetVersion PUT（RuleSet 不变）
  - **发布 RuleSet 时连带发布其引用的 ParameterSet**（如果 ParameterSet 也有 DRAFT 一并发布）—— 这是隐含的业务规则，由 0 号位 第 3 号位 拍板（实施时通过 Step A 联调摸排确认）

#### 1.1.2 🆕 RuleSetVersion 自己的字段（非引用字段）

```ts
interface RuleSetVersionGovernance extends ParameterSetVersionGovernance {
  // 继承 parameterSetVersionId 之外的字段，再加：
  ruleSetId: number  // 与 PS 的 parameterSetId 对应
  parameterSetVersionId: number  // 引用 PS
  // RuleSetVersion 也有 5 JSON 字段（lockJson 等），但语义是"规则策略"非"参数"
  // 当前 mock 未拆分；实施时先按 PS 同 shape 处理
}
```

### 1.2 store 缓冲形态（**关键**） — 🆕 Round 1 双轨修订

```
┌──────────────────────────────────────────────────────────────────────┐
│  store state: 双轨（RS + PS）+ 各自三态 ref                             │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  ┌─ RuleSet 轨 ──────────────────────┐  ┌─ ParameterSet 轨 ────────┐  │
│  │ ruleSetVersionId: number          │  │ parameterSetVersionId   │  │
│  │ governance: shallowRef + readonly │  │ governance: 同左        │  │
│  │ + Object.freeze                    │  │                         │  │
│  │                                    │  │                         │  │
│  │ workingBlocks: {                  │  │ workingBlocks: {         │  │
│  │   lock / supply / procurement /   │  │  lock / supply /         │  │
│  │   solverStrategy / guardrail      │  │  procurement / ...       │  │
│  │ } (Vue ref, 可变)                 │  │ } (Vue ref, 可变)       │  │
│  │                                    │  │                         │  │
│  │ originalBlocks: 同 shape           │  │ originalBlocks: 同      │  │
│  │ (shallowRef, save 后重置)         │  │                         │  │
│  │                                    │  │                         │  │
│  │ dirty: {                           │  │ dirty: {                │  │
│  │   lock/supply/procurement/         │  │  lock/supply/...         │  │
│  │   solverStrategy/guardrail: bool   │  │  : bool                 │  │
│  │ }                                  │  │ }                       │  │
│  └────────────────────────────────────┘  └─────────────────────────┘  │
│                                                                      │
│  crossBuffer 关联：rsBuffer.parameterSetVersionId === psBuffer.id     │
└──────────────────────────────────────────────────────────────────────┘
```

**约束**（🆕 Round 1 修订）：
- `governance` 用 **`shallowRef + readonly + Object.freeze`**（不用普通 ref）
  - 原因：Vue 3 reactive ref 会把对象包成 Proxy；`Object.freeze` 对 Proxy 浅冻是冻引用不冻值；用 `shallowRef` + `Object.freeze` 才能真冻到对象本身
- `originalBlocks` 同样 `shallowRef + Object.freeze`，每次 save/load 后整对象替换（不是 structuredClone 原对象）
- `workingBlocks` 是 **普通 ref**（可变）；dirty 任何字段触发只改 working
- 用户点"重置未保存" = `workingBlocks = JSON.parse(JSON.stringify(originalBlocks))`（深拷，不用 structuredClone，因 Vue ref 深拷有坑）
- 用户点"保存草稿" = `JSON.stringify(workingBlocks)` → PUT → OK 后 `originalBlocks = Object.freeze(JSON.parse(JSON.stringify(workingBlocks)))`
- **重要**：spread 解构 governance 的字段值会丢失 frozen 保护，**buildPutBody 内要 spread 后逐字段再 freeze**（强制 DRAFT 在此）——见 § 2.1 算法 3

### 1.3 数据流（🆕 5 个时机，含两入口分立） — Round 1 修订

**入口 A：loadDetail（看已有 DRAFT）**

```
A1. 用户点列表卡片 → selectRuleSet(ruleSetId)
   store.reset() + activeTab = 'parameters'
   ↓
A2. 并发发两 GET：
      GET /api/governance/rule-set/version/{ruleSetId}/draft-version  ← 取 RS 的 DRAFT
      GET /api/governance/parameter-set/version/{paramSetId}/{draftVersionId}  ← 取 PS 的 DRAFT
   或按 § 1.1.1 引用图：仅 GET 一发 `RS DRAFT`，从 response 取出 parameterSetVersionId 再 GET 二发
   ↓
A3. 解析 response：
      rsBuffer.governance = Object.freeze(governance)
      rsBuffer.workingBlocks = JSON.parse(governance.{lock|supply|...}Json)
      rsBuffer.originalBlocks = Object.freeze(deepClone(rsBuffer.workingBlocks))
      rsBuffer.dirty.* = false
      psBuffer 同左
```

**入口 B：forkDraft（基于 PUBLISHED 建新草稿）**

```
B1. 用户点 [+ 新建草稿] → openCreateDraftDialog()
   ↓
B2. 选 PUBLISHED RuleSet + 填 versionCode（如 "v13"） + 填 changeReason
   ↓
B3. 先调 GET /api/governance/rule-set/version/{publishedVersionId}
   拿到 RS 的 5 JSON 字符串（governance.status = PUBLISHED）
   ↓
B4. 复制对象：
      newRsGovernance = { ...oldGovernance, versionCode: 'v13', status: 'DRAFT' /* 后端会强制覆盖 */, remarks: changeReason, createdAt: nowISO, createdBy: actor }
   ↓
B5. 调 POST /api/governance/rule-set/version → 创建 RS DRAFT，返回新 versionId
   ↓
B6. 立即调 GET /api/governance/parameter-set/version/{oldPSPublishedVersionId} → 复制改 VersionCode → POST PS DRAFT
   ↓
B7. 把两份新 DRAFT 装进 store（与入口 A 相同），dirty 全部 false
   ↓
B8. 跳到该新 DRAFT 详情
```

**3 个编辑时机（C/D/E，双轨都走）**

```
C. 用户在"参数维护" Tab 改值
   ↓ flattenBlocks() 拿到的 row 对象
   onCellEdit(row, newValue)  ← store action，内部调 applyEdit + dirty.set  (见 § 2.4)
   ↓
   workingBuffer.blocks[row.block][...row.path] = newValue
   dirty[row.block] = true
   dirtyIndicatorComponent 立即显示 ● 未保存（≤100ms）

D. 用户点"保存草稿"
   ↓ buildPutBody(workingBuffer, governance, 'DRAFT' /* 显式 */) → 整对象 PUT
   governance 全字段（含治理冻结字段原值回传）+ 5 JSON 字符串新值 + status: 'DRAFT'
   ↓ 后端 PUT 200 OK
   originalBlocks = Object.freeze(deepClone(workingBlocks))
   dirty 全部 false
   ↓
   （若 dirty 涉及 RS + PS 双轨，串行 PUT 两发；先 PS 后 RS；若 RS 成功 PS 失败 → 状态不一致风险）

E. 用户点"取消未保存" / 切走有 dirty 提示
   workingBlocks = deepClone(originalBlocks)
   dirty 全部 false
```

**Q2 全量回传的实战细节**（双轨 + RS/PS 各 5 JSON）：
- 即使 dirty 只有 procurement，PUT body 仍包含 5 JSON 全部序列化（从 workingBlocks 现行副本拿）——保证后端整对象替换一致
- 后端 service `EnsureParameterSetNormalized`（L1466）会把 5 JSON 单独项**整对象替换回** ContentSnapshotJson——空缺字段如果 workingBlocks 漏写，会丢内容
- **双轨陷阱**：当 `dirty.rs && dirty.ps` 时，必须**先 PS 后 RS**（含义：PS 是 RS 的"软依赖"，RS 失败可回滚 PS；反之不行——业务规则见 § 1.1.1）

---

## § 2. diff 算法设计

### 2.1 核心算法 3 函数（实施时落 `rules.ts` 或新 `ruleDraft.ts`）

```ts
// ========================================
// 算法 1: flattenBlocks — 通用 walk
// ========================================
type BlockKey = 'lock' | 'supply' | 'procurement' | 'solverStrategy' | 'candidateGuardrail'
type JsonValue = string | number | boolean | null | JsonObject | JsonValue[]
interface JsonObject { [k: string]: JsonValue }

interface ParameterRow {
  block: BlockKey
  path: string[] // e.g. ['planningYield'] 或 ['sourcing', 'defaultDays']
  parameterKey: string // path.join('.')
  parameterName: string // 待联调后端提供别名（暂用 path 显示）
  parameterType: 'NUMBER' | 'BOOLEAN' | 'STRING' | 'PERCENT' | 'DURATION'
  value: JsonValue
  sensitive: boolean
  editable: boolean
}

function flattenBlocks(blocks: Record<BlockKey, JsonObject>): ParameterRow[] {
  const rows: ParameterRow[] = []
  for (const blockKey of Object.keys(blocks) as BlockKey[]) {
    rows.push(...walkBlock(blocks[blockKey], blockKey, []))
  }
  return rows
}

function walkBlock(obj: JsonObject, blockKey: BlockKey, prefix: string[]): ParameterRow[] {
  const rows: ParameterRow[] = []
  for (const [key, value] of Object.entries(obj)) {
    const path = [...prefix, key]
    if (isPrimitive(value)) {
      rows.push({
        block: blockKey,
        path,
        parameterKey: path.join('.'),
        parameterName: path.join('.'), // TODO 联调时换 backend alias
        parameterType: inferType(key, value),
        value,
        sensitive: isSensitive(key),
        editable: isEditable(key)
      })
    } else if (Array.isArray(value)) {
      // 数组不展开（编辑边界）—— 表象化显示但不进编辑
      rows.push({
        block: blockKey,
        path,
        parameterKey: path.join('.'),
        parameterName: path.join('.') + ` (length: ${value.length})`,
        parameterType: 'STRING', // 数组显示为 JSON 字符串只读
        value: JSON.stringify(value),
        sensitive: isSensitive(key),
        editable: false
      })
    } else if (typeof value === 'object' && value !== null) {
      // 嵌套对象：递归 walk
      rows.push(...walkBlock(value as JsonObject, blockKey, path))
    }
  }
  return rows
}

// ========================================
// 算法 2: applyEdit — path-based local mutation
// ========================================
function applyEdit(
  blocks: Record<BlockKey, JsonObject>,
  row: ParameterRow,
  newValue: JsonValue
): void {
  // 边界
  if (row.path.length === 0) throw new Error('invalid row.path')
  if (!row.editable) throw new Error('row not editable')

  // 沿 path 钻到末级父节点
  let target: JsonObject = blocks[row.block]
  for (let i = 0; i < row.path.length - 1; i++) {
    const key = row.path[i]
    if (typeof target[key] !== 'object' || target[key] === null) {
      // 中间节点缺失 → 创建
      target[key] = {}
    }
    target = target[key] as JsonObject
  }
  const lastKey = row.path[row.path.length - 1]
  target[lastKey] = newValue
}

// ========================================
// 算法 3: buildPutBody — full-object PUT（🆕 Round 1 显式 DRAFT）
// ========================================
function buildPutBody(
  governance: Readonly<ParameterSetVersionGovernance>,
  blocks: Record<BlockKey, JsonObject>
): ParameterSetVersion {
  return {
    ...governance,
    // 🆕 Round 1：spread 完显式置 DRAFT，不依赖后端 service 入参忽略
    // 原因：(1) 代码自证加可读性 (2) 防后端策略改动连锁
    status: 'DRAFT' as GovernanceStatus,
    // 5 JSON 字段：可能为空（governance 原始 undefined 也维持 undefined 不报错）
    lockJson: blocks.lock ? JSON.stringify(blocks.lock) : undefined,
    supplyJson: blocks.supply ? JSON.stringify(blocks.supply) : undefined,
    procurementJson: blocks.procurement ? JSON.stringify(blocks.procurement) : undefined,
    solverStrategyJson: blocks.solverStrategy ? JSON.stringify(blocks.solverStrategy) : undefined,
    candidateGuardrailJson: blocks.candidateGuardrail ? JSON.stringify(blocks.candidateGuardrail) : undefined
    // contentSnapshotJson 不传（后端 service 自行序列化）
  }
}
```

### 2.2 heuristic 元数据（不依赖后端 DTO）

```ts
function isSensitive(key: string): boolean {
  const s = key.toLowerCase()
  return s.includes('buffer') || s.includes('secret') || s.includes('confidential')
  // 已知 sensitive: crossDomainBlockBuffer / crossDomainBuffer 等含 "Buffer" 的
}

function isEditable(key: string): boolean {
  // 默认都可编辑；"default*" 开头的不可编辑（出厂锁定）
  return !key.toLowerCase().startsWith('default')
  // 已知 editable=false: defaultPurchaseLT
}

function inferType(key: string, value: JsonValue): 'NUMBER' | 'BOOLEAN' | 'STRING' | 'PERCENT' | 'DURATION' {
  if (typeof value === 'boolean') return 'BOOLEAN'
  if (typeof value === 'string') return 'STRING'
  if (typeof value === 'number') {
    if (key.toLowerCase().includes('yield') || key.toLowerCase().includes('threshold')) return 'PERCENT'
    if (key.toLowerCase().includes('buffer') || key.toLowerCase().includes('leadtime') || key.toLowerCase().includes('duration')) return 'DURATION'
    return 'NUMBER'
  }
  return 'STRING'
}
```

> ⚠️ **风险**: 这是 heuristic，联调时需用**真实后端 keys 覆盖**——见 § 5.1。

### 2.3 单元测试用例（落到 `tests/unit/parameterSetDraft.spec.ts`）

```
US1: flattenBlocks 空对象 → []
US2: flattenBlocks 单 key {a:1} → [{path:['a'], value:1, type:'NUMBER'}]
US3: flattenBlocks 嵌套 {a:{b:{c:1}}} → [{path:['a','b','c'], value:1}]
US4: flattenBlocks 数组 {a:[1,2]} → [{path:['a'], value:'[1,2]'只读}]
US5: flattenBlocks 5 块各一 → length === 5
US6: applyEdit 修改顶层 → blocks 改了+dirty=true（由 § 2.4 store action 验证）
US7: applyEdit 修改嵌套 4 层 → 末级父节点自动创建（如未存在则建；存在但 primitive 不覆盖）
US8: applyEdit editable=false 抛错
US9: applyEdit row.path.length===0 抛错
US10: buildPutBody 治理字段冻结值原样回传（不会因 working buffer 改了 PublishedAt 而被覆盖）
US11: buildPutBody 5 JSON 字符串化（undefined 块转为 undefined 不报错）
US12: buildPutBody **显式 status='DRAFT'**（不依赖后端 service 入参忽略）— Round 1 补
US13: 脏标记：onCellEdit 后 dirty=true; 用户 cancel → dirty=false
US14: round-trip: PUT body → GET → flatten → 参数行 = 之前所有用户编辑
US15: round-trip 双轨 RS+PS 同步：modify PS → PUT PS → modify RS → PUT RS → reload → 两边都生效
US16: forkDraft B3-B8：mock GET 返 PUBLISHED → 改 VersionCode → POST 返新 DRAFT → 进 store 可编辑
```

### 2.4 🆕 Round 1：dirty 联动——store action 包装层（Issue 5 闭环）

§ 2.1 算法 1-3 是 **pure functions**（无副作用、不接触 store）。dirty 设置 + workingBuffer 写入在 **store actions 内联**：

```ts
// ========================================
// Store action: onCellEdit — 包装 applyEdit + dirty.set
// ========================================
function onCellEdit(
  row: ParameterRow,
  newValue: JsonValue,
  bufferChoice: 'ruleSetBuffer' | 'parameterSetBuffer'
): void {
  const buf = bufferChoice === 'ruleSetBuffer' ? ruleSetBuffer : parameterSetBuffer

  // 1. 类型校验（Issue 10 暂不修，但占位）
  validateType(row, newValue)

  // 2. 调用 pure function applyEdit
  applyEdit(buf.workingBlocks, row, newValue)

  // 3. 联动 dirty
  buf.dirty[row.block] = true

  // 4. (可选) 触发 reactive 标记
  // Vue ref 直接改属性可能不触发响应式；触发 dirty 字段需显式 set
  // 解决：buf.dirty 是 reactive 对象，属性赋值即可；workingBlocks 内字段同理 Vue 3 自动 Proxy
}

function onCancelDirty(bufferChoice: 'ruleSetBuffer' | 'parameterSetBuffer'): void {
  const buf = bufferChoice === 'ruleSetBuffer' ? ruleSetBuffer : parameterSetBuffer
  buf.workingBlocks = JSON.parse(JSON.stringify(buf.originalBlocks))
  buf.dirty = { lock: false, supply: false, procurement: false, solverStrategy: false, candidateGuardrail: false }
}

async function onSaveDraft(
  changeReason: string,
  bufferChoice: 'ruleSetBuffer' | 'parameterSetBuffer'
): Promise<void> {
  const buf = bufferChoice === 'ruleSetBuffer' ? ruleSetBuffer : parameterSetBuffer
  const putBody = buildPutBody(buf.governance, buf.workingBlocks)
  putBody.remarks = changeReason
  // 双轨守卫：先 PS 后 RS（具体见 § 1.3 数据流 D）
  if (bufferChoice === 'parameterSetBuffer' && ruleSetBuffer.dirty && ruleSetBuffer.dirty.hasAny) {
    throw new Error('Must save RuleSet DRAFT first (or batch save in order)')
  }
  await ruleApi.updateDraft({ versionId: buf.governance.id, body: putBody })
  // 成功后刷新 originalBlocks + 清 dirty
  buf.originalBlocks = Object.freeze(JSON.parse(JSON.stringify(buf.workingBlocks)))
  buf.dirty = { lock: false, supply: false, procurement: false, solverStrategy: false, candidateGuardrail: false }
}
```

**架构原则**：
- 算法 1-3 是 pure，便于单测（§ 2.3）
- dirty / workingBuffer 在 store 层（reactive 友好）
- 用户 UI 事件（@cellEdit） → 调 onCellEdit action → 自动触发 reactive re-render
- 表格的 ● 未保存 标签直接绑定 `dirty[block]` 显隐（watcher reactive）

---

## § 3. 6 态映射表

### 3.1 STATUS_LABEL 6 态 + Tag 颜色

```ts
// rule.ts 替换原 3 态定义
export type RuleStatus = 'DRAFT' | 'SUBMITTED' | 'APPROVED' | 'PUBLISHED' | 'DISABLED' | 'ARCHIVED'

export const STATUS_LABEL: Record<RuleStatus, string> = {
  DRAFT: '草稿',
  SUBMITTED: '已提交',
  APPROVED: '已审批',
  PUBLISHED: '已发布',
  DISABLED: '已停用',   // 含原 'RETIRED' 归一
  ARCHIVED: '已归档'
}

export const STATUS_TAG: Record<RuleStatus, 'success' | 'warning' | 'info' | 'primary' | 'danger'> = {
  DRAFT: 'warning',      // 黄 — 待编辑
  SUBMITTED: 'primary',  // 蓝 — 中转
  APPROVED: 'info',      // 灰蓝 — 中转
  PUBLISHED: 'success',  // 绿 — 生效
  DISABLED: 'info',      // 灰 — 失效
  ARCHIVED: 'info'       // 灰 — 历史
}
```

### 3.2 按钮可见性矩阵

| 当前状态 | [+ 新建草稿] | [编辑 / 保存草稿] | [校验] | [发布] | [退役 / disable] |
|---|---|---|---|---|---|
| **DRAFT** | ✅ | ✅ | ✅ | ✅ | ❌ |
| **SUBMITTED** | ✅ | ❌ 只读 | ❌ | ❌ | ❌ |
| **APPROVED** | ✅ | ❌ 只读 | ❌ | ❌ | ❌ |
| **PUBLISHED** | ✅ | ❌ 只读 | ❌ | ❌ | ✅ |
| **DISABLED** | ✅ | ❌ 只读 | ❌ | ❌ | ❌ |
| **ARCHIVED** | ✅ | ❌ 只读 | ❌ | ❌ | ❌ |
| **无当前选中** | ✅ | — | — | — | — |

**实现**：
```ts
// store getter — 替代原 canPublish / canRetire 二元判断
const actions = computed<ActionAvailability>(() => {
  const s = currentStatus.value
  return {
    canCreateDraft: apsAuth.has('aps.rule.edit'),
    canEdit: s === 'DRAFT' && apsAuth.has('aps.rule.edit'),
    canValidate: s === 'DRAFT' && apsAuth.has('aps.rule.publish'),
    canPublish: s === 'DRAFT' && apsAuth.has('aps.rule.publish'),
    canRetire: s === 'PUBLISHED' && apsAuth.has('aps.rule.publish')
  }
})
```

### 3.3 RETIRED → DISABLED 归一（兼容层）

后端不再返 `RETIRED`（审核报告已冻结），但**前端可能从 mock 或老数据拿到 RETIRED**。在 store receive 处增加：

```ts
// 实际调用位置：`rules.ts` 内 `loadDetail()` 第 N 行 `summary.status = normalize(summary.status)`
// 同步 normalize parameterSetVersion 的 status（双轨一致）
const normalize = (s: string): RuleStatus => {
  if (s === 'RETIRED' || s.toLowerCase() === 'retired') return 'DISABLED'
  // 老 3 态向上兼容
  if (s in STATUS_LABEL) return s as RuleStatus
  // 未知态 → DISABLED 兜底（不抛错，避免页面打不开）
  console.warn(`Unknown status from backend: ${s}, falling back to DISABLED`)
  return 'DISABLED'
}
```

**好处**：前端 mock 删 RETIRED 字段前不会破；老 v1.0 数据进来也能扛。

### 3.4 🆕 Round 1：fork 状态矩阵（Issue 9 闭环）

`canCreateDraftForStatus()` 函数决定源 RuleSet 处于某状态时，[+ 新建草稿] 按钮是否点亮：

| 源状态 | 允许 fork 新 DRAFT | 业务理由 |
|---|---|---|
| **PUBLISHED** | ✅ | 主用例 + v1.4 §10 文档要求 |
| **DISABLED** | ✅ | 重启治理中（已退役但未归档，治理想复活） |
| **DRAFT** | ⚠️ **特殊**：不允许再建新草稿 | DRAFT 已有"当前编辑"，再建会冲突 |
| **DRAFT**（基另一源） | ❌ | 见下方 "二次 fork" 边界 |
| **SUBMITTED** | ❌ | 流程中，改需先撤回 |
| **APPROVED** | ❌ | 已审批就等发布，不应该分叉 |
| **ARCHIVED** | ❌ | 归档文件不修改 |
| **未知态** | ❌ | 兜底禁掉（提示"请联系管理员"） |

**实施函数**：

```ts
function canCreateDraftForStatus(
  sourceStatus: RuleStatus,
  existingDraft?: RuleSetVersion | null
): { allowed: boolean; reason?: string } {
  // DRAFT 状态：返回"先编辑现有草稿"的提示
  if (sourceStatus === 'DRAFT') {
    return { allowed: false, reason: '当前 RuleSet 已有 DRAFT；请直接编辑现有草稿' }
  }
  // PUBLISHED 是允许基
  if (sourceStatus === 'PUBLISHED') {
    return { allowed: true }
  }
  // DISABLED 是允许基（已退役但启用的 fork）
  if (sourceStatus === 'DISABLED') {
    return { allowed: true, reason: '基于已停用 RuleSet fork；新 DRAFT 将重新进入 PUBLISHED 流程' }
  }
  // 其他一律禁掉
  return { allowed: false, reason: `当前状态 ${sourceStatus} 不允许 fork` }
}
```

**Dialog 行为**：
- 用户在 PUBLISHED RuleSet 卡片菜单点 [+ 新建草稿] → Dialog 标题「新建 DRAFT（基于 RS-DEFAULT v12 → v13）」
- 用户在 DRAFT 卡片菜单**不显示** [+ 新建草稿] 入口（如 UI 实在要在菜单项上，禁用并 tooltip 解释）
- 用户在 DISABLED 卡片菜单点 → Dialog 标题「新建 DRAFT（基于 RS-LEGACY-V1 v8 → v9，重新启用）」+ 文案提示「重启流程」

**修订 § 3.2 矩阵** ([+ 新建草稿] 列)：

| 当前状态 | [+ 新建草稿]（基于自己 fork） |
|---|---|
| **DRAFT** | ❌（应编辑现有的） |
| **SUBMITTED** | ❌（流程中） |
| **APPROVED** | ❌（等发布） |
| **PUBLISHED** | ✅ |
| **DISABLED** | ✅（重启） |
| **ARCHIVED** | ❌（归档不动） |

**3.2 其他 4 列（编辑/校验/发布/退役）维持原不变**。

---

## § 4. 风险与未决项

| # | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R1 | **Q4 元数据缺失**：sensitive / editable 后端 DTO 未提供 | 编辑"敏感/只读字段"会误改；显示"是否可编辑"完全靠 key 名启发 | 联调时 Q4 真实 key + 联调后端补 schema（如不能补，至少 stable `bindRulesForParameterSet()` 映射表固化到代码） |
| R2 | **嵌套对象编辑边界**：array 类型不让编辑 | 用户遇到 array 字段只读；与 §4 现状不一致 | §1 R1 临时 readonly；后续若必需编辑 array，需另发函 + 加 PATCH 端点 |
| R3 | **并发编辑无锁**：两个用户同时改一个 DRAFT → 后端覆盖 | 后端报 409；前端需刷新 | 不做；后端 OptimisticConcurrency 兜底；前端接 409 → 弹"已被他人修改，请刷新" |
| R4 | **5 JSON 字符串丢失精度**：JSON.stringify/reverse 时精度/类型丢失 | 罕见；number→JSON 能保；BigInt 失 | 不处理（治理参数都是 plain number/string/boolean） |
| R5 | **HEURISTIC 误判**：依赖 key 名匹配，真实后端 key 改了 heuristic 就失效 | 老 key 被改名后误显示 editable=true/false | 第 1 次联调后改 schema-binds-真值；heuristic 仅过渡用 |
| R6 | **Q3 publish 双调用顺序**：先 RuleSet 还是先 ParameterSet？ | 业务上无依赖；万一某条 400，另一条无回滚 | 顺序：先 RuleSet（业务约束更紧——释放 cascade），再 ParameterSet；任一失败即停 + 回滚前端 dirty 状态 |

---

## § 5. 实施映射（步骤 1-8 怎么对应到本设计）— 🆕 Round 1 双轨修订

| 步骤 | 文件 | 实施动作 | 引用本设计 |
|---|---|---|---|
| **1** | `rule.ts` | 🆕 **加 7 函数封装**（RS+PS 平行）：<br>`createRuleSetDraft(input)` / `updateRuleSetDraft(input, id)` / `publishRuleSet(id, reason)`<br>`createParameterSetDraft(input)` / `updateParameterSetDraft(input, id)` / `publishParameterSet(id, reason)`<br>`forkDraft(governance)`（包装 RS+PS 两发） | § 1.3 B3-B8 + § 1.1.1 1:1 关联 |
| **2** | `rules.ts` | 🆕 加 store **双轨**：<br>`ruleSetBuffer` + `parameterSetBuffer`（parallel，每个三态）<br>+ actions: `loadDetail`（入口 A）/ `forkDraft`（入口 B）/ `onCellEdit` / `onSaveDraft` / `onCancelDirty` | § 1.2 双轨 + § 1.3 5 时机 + § 2.4 store action 包装 |
|  └ unit test | `tests/unit/parameterSetDraft.spec.ts` | US1-US16（含 US12/US15/US16 三条 Round 1 新增） | § 2.3 |
| **3** | `Rules.vue` 顶部 | [+ 新建草稿] 按钮 + Dialog（基于 RuleSet 单选 + `canCreateDraftForStatus()` 守门） | § 1.3 入口 B + § 3.4 |
| **4** | `Rules.vue:381-420` | 参数维护 Tab 改可编辑 + flatten 双轨渲染 + dirty 显隐 + [保存草稿]/[取消未保存] 按钮 | § 1.3 C/D/E + § 2.4 actions + § 2.2 heuristic |
| **5** | `rule.ts` | STATUS_LABEL / STATUS_TAG 6 态替换 3 态；normalize 兼容层（双轨同步调用） | § 3.1 + § 3.3 |
| **6** | `Rules.vue` Tab templates | 6 态标签替换；按钮可见性用 `actions.canEdit/canPublish/.../canCreateDraftForStatus(...)`；矩阵按 § 3.2 修订 + § 3.4 fork 守门 | § 3.2 + § 3.4 |
| **7** | `verify-acceptance.mjs` 加 [P] 段 | 🆕 双轨 3 断言：createRuleSetDraft + updateParameterSetDraft + publishRuleSet | § 1.3 |
| **8** | — | ts:check / lint:eslint / build:pro / GROUP 回归 | — |

### 5.1 联调触发（Q4 真实 key 锚点）— 🆕 加 Q4 双轨

实施步骤 4 完成+三绿后，**不**立刻发致谢函。改走：

```
Step A: 切真实（按本函 § 1.3 操作步骤）
Step B: 浏览器登录 → 进规则与参数维护 → 选已有 DRAFT
Step C: F12 → Network → 双 GET（RS DRAFT + PS DRAFT）
        复制两份 response.json →
          docs/real-rule-set-version.sample.json
          docs/real-parameter-set-version.sample.json
Step D: 双 sample.json 校准 § 2.2 heuristic
        重写 bindRulesForRuleSet() + bindRulesForParameterSet()（基于真 key 名）
Step E: 重跑步骤 8 三绿 → 真实数据可编辑（包括双轨 fork + 双轨 save + 双轨 publish）
Step F: 给 3 号位 发"接入完成回执"+ 双 sample.json 附档
```

---

## § 6. 不在本设计范围

- SolverStrategyJson / CandidateGuardrailJson 详细 schema（参数 Tab 暂按 JSON 字符串展示，不拆字段）
- 字段描述（description）——后端无 DTO；前端 heuristic 显示 `path.join('.')` 替代
- 异步协作 / 多人协同 ——第 4 号位 v1.5 不做
- 操作审计 UI ——后端已有 `audit.view`，前端 AuditLog 页签已闭环

---

## § 7. 收口

- **核心不变量**：每次 PUT 前 working buffer 必须包含**所有 5 块完整对象**（即使未改）——防后端 service 丢失内容
- **不可改字段**：governance 字段一律取原值；store 内 `shallowRef + Object.freeze` 强制（🆕 Round 1：去除 Vue reactive 透冻失效）
- **关键校验**：步骤 4 编辑后表格 dirty 显隐直接来自 § 1.3 dirty 状态（用户视觉反馈不超过 100ms）

---

## § 8. Round 1 修订清单（Issue → 修复位置速查）

| Issue | 严重度 | 修复位置 |
|---|---|---|
| Issue 1 两入口分立 | 🔴 | § 1.3 拆为入口 A（loadDetail）+ 入口 B（forkDraft） |
| Issue 2 RS ↔ PS 1:1 | 🔴 | § 1.1.1 + § 1.2 双轨 + § 5 步骤 1 加 7 函数 |
| Issue 5 dirty 联动位置 | 🔴 | § 2.4 新增 store action 包装层（onCellEdit/onCancelDirty/onSaveDraft） |
| Issue 6 buildPutBody 显式 DRAFT | 🔴 | § 2.1 算法 3 spread 后强制 `status = 'DRAFT'` |
| Issue 9 fork 状态矩阵 | 🔴 | § 3.4 新增 canCreateDraftForStatus() + 修订 § 3.2 矩阵的 [+新建草稿] 列 |

**Round 2 仍待办**（不进 A 前可后移，但建议下次会话先做）：
- Issue 3/4 freeze 模型已在 § 1.2 提示框架；实施时实测 Vue 3 `shallowRef + Object.freeze` 兼容性
- Issue 7 applyEdit 中间节点覆盖 → 需加 "target[key] === undefined 才创建"
- Issue 11 OCC 模型 → 联调 Step C 摸排后端策略
- Issue 14 dirty 拦截（路由切换 + 浏览器 beforeunload）→ 步骤 4 捎带
- Issue 15 canPublish/canRetire 替换影响面 → 步骤 1 起手前 grep 一遍

**实施工时重估**：原估 2.9 天，含 Round 1 双轨复杂化后 **3.2 天**（+RS 端点封装 0.3 + 双轨 store 0.3 + 双轨 fork 流程 0.2 - Round 1 前估错的精简 0.5）。


实施工时重估：**2.9 天**（原估同上未变；新增 unit test 0.3 天，orchestration 复杂度上行 0.2 天）。
