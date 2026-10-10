# §10A.1 OrderAdvanceDialog 改造实施计划 — OrderCanonicalId 自动带入（B4 已落地触发）

> **触发**：5 号位 2026-09-23 [P1-3 回执 + B4 已落地](../proj-aps-aps-production-new/frontNew/docs/5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md) → P1-3-c「落地即支持」触发
> **4 号位 实施包原 §四.5**："待 5 号位 补列后改为自动带入（保留手填兜底）"——**触发条件已满足**
> **冻结依据**：
> 1. **集成接口设计 v1.33 §5.3 + §6.5**：`OrderTargets[].OrderCanonicalId` 必须字段；与 `OrderCanonicalIds` 一致性约束（不一致必须拒绝）
> 2. **v1.4 §十A.1 L595-672（EXISTING_ORDER_ADVANCE）**：LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode 可选 NORMAL/EXPEDITE；载荷 `orderTargets[]{orderCanonicalId(long), manualTargetDueDate}`
> 3. **5 号位 B4 落地清单**：OrderListItemDto 新增 OrderCanonicalId（long?）+ OrderQueryRepository 两处 SQL 补列（build 0 Error）
> **性质**：§10A.1 二次改造——手填兜底 → 自动带入（保留兜底）
> **关联**：
> - 5 号位 P1-3 回执：[5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md](5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md)
> - 4 号位 P1-3 直通函：[4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)
> - 4 号位 合并回执给 3 号位：[4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md](4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md) §二.5
> - 冻结文档：[集成接口设计 v1.33 §5.3 + §6.5](../冻结文档/APS_集成接口设计_v1.33_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.md)

---

## 〇、现状 vs 目标

### 现状（手填兜底模式）

| 位置 | 现状 |
|---|---|
| `src/api/aps-v1/types/order.ts:44-66` OrderBasicInfo | ❌ **无 `orderCanonicalId` 字段** |
| `src/views/Aps/Order.vue:297-306` advanceCandidates | ❌ 只取 5 字段（orderId/orderNo/materialCode/customerName/customerDueDate）|
| `src/views/Aps/components/OrderAdvanceDialog.vue:35-41` OrderAdvanceCandidate | ❌ **无 `orderCanonicalId` 字段** |
| `src/views/Aps/components/OrderAdvanceDialog.vue:188-197`「订单规范 Id」列 | ⚠️ 纯手填 ElInput（type="number"，占位提示 orderNo）|
| `src/views/Aps/components/OrderAdvanceDialog.vue:218-223` ElAlert | ⚠️ "订单规范 Id 手填（5号位 待补列）" |
| `src/api/aps-v1/__mocks__/fixtures.ts` mockOrderList | ❌ 无 `orderCanonicalId` 字段 |

### 目标（自动带入 + 保留兜底）

| 位置 | 目标 |
|---|---|
| `OrderBasicInfo` | ✅ 新增 `orderCanonicalId?: number`（B4 已落地）|
| `Order.vue advanceCandidates` | ✅ 透传 `orderCanonicalId` 字段 |
| `OrderAdvanceDialog.vue OrderAdvanceCandidate` | ✅ 新增 `orderCanonicalId?: number` |
| Dialog 「订单规范 Id」列 | ✅ 自动带入（默认 = `row.orderCanonicalId`）；ElInput 保留手填兜底作兼容回退 |
| Dialog ElAlert 文案 | ✅ "订单规范 Id 自动带入（B4 已落地）"+ "保留手填兜底作兼容回退" |
| `mockOrderList` | ✅ mock 数据补 `orderCanonicalId` 字段（与 5 号位 B4 DTO 对齐） |

---

## 一、冻结契约对齐（v1.33 + v1.4 §十A.1）

### 1.1 字段语义（v1.33 §5.3 + 5 号位 P1-3-a 回执）

| 字段 | 类型 | ID 空间 | 关系 |
|---|---|---|---|
| `orderId` | number | `[Order].Id`（按 PlanVersion 分区）| 订单在某 PlanVersion 的实例 |
| `orderCanonicalId` | number | `Order_Canonical.Id`（规范化主档，v5.0.34 增）| 跨版本/Domain 稳定的规范化身份（upsert 键 = SourceSystem + SourceOrderId）|

→ 两者**非同一 ID 空间**，但 EXPEDITE §10A.1 业务入口契约用的就是 `OrderCanonicalId`。

### 1.2 一致性约束（v1.33 §5.3 §544-552）

> 如果 `OrderCanonicalIds` 与 `OrderTargets[]` 同时存在：
> `OrderCanonicalIds` = `OrderTargets[].OrderCanonicalId` 去重投影
> **不一致必须拒绝**。

→ 前端提交前必须保证 `OrderTargets[].OrderCanonicalId` 与 `OrderCanonicalIds` 一致（前端现不传 `OrderCanonicalIds`，仅传 `OrderTargets[]`，满足一致性）。

### 1.3 业务触发器契约（v1.33 §6.5）

| 业务触发器 | RunType | Purpose | PriorityMode |
|---|---|---|---|
| **EXISTING_ORDER_ADVANCE** | LOCAL_RESCHEDULE | MANUAL_ADJUSTMENT | **NORMAL 或 EXPEDITE** |

载荷：`orderTargets[]{ orderCanonicalId(long), manualTargetDueDate }`

### 1.4 B4 落地（5 号位 已落地，build 0 Error）

| 后端文件 | 改动 |
|---|---|
| `LPS.APS.Core/Dto/OrderQueryDtos.cs` | `OrderListItemDto` 新增 `OrderCanonicalId`（long?）|
| `LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs` | 列表 + 详情 2 处 SQL 补 `o.OrderCanonicalId` |

> ⚠️ 联调前请确认 dev 库 `[Order]` 表确有 `OrderCanonicalId` 列（v5.0.34 已加，若 dev 未同步需建列）

---

## 二、文件清单

### 改动（5 文件）

| 路径 | 改动 | 工作量 |
|---|---|---|
| `src/api/aps-v1/types/order.ts` | `OrderBasicInfo` 接口新增 `orderCanonicalId?: number`（带 v1.33 + B4 注释）| 0.05 人天 |
| `src/api/aps-v1/__mocks__/fixtures.ts` | `mockOrderList` 每条 mock 数据新增 `orderCanonicalId` 字段（long 类型）| 0.1 人天 |
| `src/views/Aps/Order.vue` | `advanceCandidates` 计算属性透传 `orderCanonicalId` | 0.05 人天 |
| `src/views/Aps/components/OrderAdvanceDialog.vue` | OrderAdvanceCandidate 接口 + Dialog 自动带入 + ElAlert 文案更新 + 占位提示逻辑 | 0.4 人天 |
| `scripts/verify-acceptance.mjs` | 新增 **[M] 段**（group `m-10a1`）+ main 分发 + 顶部覆盖注释 | 0.3 人天 |
| **合计** | — | **~0.9 人天** |

### 不动（4 文件）

| 路径 | 不动理由 |
|---|---|
| `src/api/aps-v1/order.ts` | `orderApi.list` 已返回 `OrderBasicInfo`，**自动透传**新字段，无需改 |
| `src/store/modules/aps/order.ts` | Store 透传 `OrderBasicInfo`，**自动透传**新字段，无需改 |
| `src/views/Aps/Order.vue:951` OrderAdvanceDialog 调用 | props 名（`orders`）不变，无需改 |
| `src/api/aps-v1/runScope.ts` | 验证矩阵（orderTargets 已含 `orderCanonicalId`）不变 |

### 只读参照（不动）

| 路径 | 用途 |
|---|---|
| `src/views/Aps/components/StrategyDraftDialog.vue` | Dialog 范式参照（`props{visible, submitting}` + `emit('update:visible')` + `emit('submit')`） |
| `src/api/aps-v1/types/run.ts` | ScopeJsonV2 / OrderTargetDto 类型契约 |
| `lps/LPS.APS.Core/Dto/OrderQueryDtos.cs`（只读）| 后端 OrderListItemDto 定义（5 号位 B4 已改） |
| `lps/LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs`（只读）| 后端 SQL 改动（5 号位 B4 已改） |
| 冻结文档：[集成接口设计 v1.33 §5.3 + §6.5](../冻结文档/APS_集成接口设计_v1.33_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.md) | 契约真源 |

---

## 三、关键实现要点

### 3.1 types/order.ts OrderBasicInfo 新增字段

```typescript
/** 4号位文档第 5 节：订单基础信息（v1.33 §5.3 规范 Id；B4 落地） */
export interface OrderBasicInfo {
  orderId: number
  orderNo: string
  /** 订单规范化 Id（v5.0.34 增列；与 orderId 非同一 ID 空间；EXPEDITE §10A.1 契约输入）
   *  5号位 B4 已落地（OrderQueryRepository 列表+详情 2 处 SQL 补 o.OrderCanonicalId）
   *  v1.33 §5.3 一致性约束：与 OrderCanonicalIds 必须一致（前端现不传 OrderCanonicalIds）
   */
  orderCanonicalId?: number
  // ... 其他字段不变
}
```

### 3.2 mocks/fixtures.ts mockOrderList 补字段

```typescript
// mock 数据补 orderCanonicalId（与 B4 DTO 对齐）
{
  orderId: 1001,
  orderNo: 'SO10001-1',
  orderCanonicalId: 50001,  // 新增（B4 对齐）
  // ... 其他字段不变
}
```

### 3.3 Order.vue advanceCandidates 透传

```typescript
/** Dialog 候选行（仅取所需字段；OrderCanonicalId 自动带入由 Dialog watch 处理） */
const advanceCandidates = computed(() =>
  list.value.map((o) => ({
    orderId: o.orderId,
    orderNo: o.orderNo,
    materialCode: o.materialCode,
    customerName: o.customerName,
    customerDueDate: o.customerDueDate,
    orderCanonicalId: o.orderCanonicalId  // 新增透传（B4 已落地）
  }))
)
```

### 3.4 OrderAdvanceDialog.vue 关键改动

#### 3.4.1 OrderAdvanceCandidate 接口新增字段

```typescript
interface OrderAdvanceCandidate {
  orderId: number
  orderNo: string
  materialCode?: string
  customerName?: string
  customerDueDate?: string
  /** 订单规范化 Id（5号位 B4 已落地；自动带入可空由 Dialog watch 处理） */
  orderCanonicalId?: number
}

interface Row extends OrderAdvanceCandidate {
  /** 手填兜底（兼容回退；正常情况 = orderCanonicalId 自动带入） */
  canonicalId?: number
  manualTargetDueDate?: string
}
```

#### 3.4.2 watch visible 时自动带入

```typescript
watch(
  () => props.visible,
  async (vis) => {
    if (!vis) return
    rows.value = props.orders.map((o) => ({
      ...o,
      // 自动带入（B4 已落地）：orderCanonicalId 存在则带入 canonicalId
      canonicalId: o.orderCanonicalId
    }))
    selected.value = []
    errors.value = []
    priorityMode.value = 'NORMAL'
    domainKey.value = props.defaultDomainKey ?? props.domainOptions[0]
    await nextTick()
    const pre = new Set(props.preselectedOrderIds ?? [])
    if (pre.size > 0 && tableRef.value) {
      rows.value.forEach((r) => {
        if (pre.has(r.orderId)) tableRef.value?.toggleRowSelection(r, true)
      })
    }
  },
  { immediate: true }
)
```

#### 3.4.3「订单规范 Id」列保留手填兜底

```html
<ElTableColumn label="订单规范 Id" width="180">
  <template #default="{ row }">
    <ElInput
      v-model="row.canonicalId"
      size="small"
      type="number"
      :placeholder="row.orderCanonicalId
        ? `已带入 ${row.orderCanonicalId}（可覆盖）`
        : `手填（提示 ${row.orderNo}）`"
    />
  </template>
</ElTableColumn>
```

#### 3.4.4 ElAlert 文案更新

```html
<ElAlert type="success" :closable="false" show-icon class="d-alert">
  <template #title>订单规范 Id 自动带入（5号位 B4 已落地）</template>
  列表 DTO 已返回 <code>OrderCanonicalId</code>（<code>Order_Canonical.Id</code>，与列表
  <code>orderId</code> 非同一 ID 空间）；本 Dialog <strong>自动带入</strong>该字段。
  手填框保留作兼容回退（异常数据 / 历史订单）。手工目标交期**不修改正式 DueDate**。
</ElAlert>
```

#### 3.4.5 文件头注释更新

```typescript
/**
 * APS V1 4号位 — §10A.1 已有订单提前（EXISTING_ORDER_ADVANCE）
 *
 * 冻结依据：《APS_V1_4号位页面与业务操作开发实施包 v1.4》§十A（L595-672）
 * 契约：LOCAL_RESCHEDULE × MANUAL_ADJUSTMENT；PriorityMode 可选 NORMAL / EXPEDITE
 * 载荷：orderTargets[]{ orderCanonicalId(long), manualTargetDueDate }
 * 集成接口设计 v1.33 §5.3 + §6.5：OrderCanonicalId 必须字段；与 OrderCanonicalIds 一致性约束
 *
 * 2026-09-23 B4 已落地：OrderCanonicalId 由后端 OrderQuery 真实返回；Dialog 改为自动带入
 * + 保留手填兜底作兼容回退（异常数据 / 历史订单）。
 */
```

### 3.5 不动（按 v1.33 §5.3 §544-552 一致性约束）

前端**不传** `OrderCanonicalIds` 字段——仅传 `OrderTargets[]`，与 v1.33 §5.3 一致性约束兼容（"如果 OrderCanonicalIds 与 OrderTargets[] 同时存在" 的前置条件不成立）。

---

## 四、verify [M] 段设计（`GROUP=m-10a1`）

- **M01 静态断言**：
  - `types/order.ts` OrderBasicInfo 含 `orderCanonicalId?: number` 字段
  - `OrderAdvanceDialog.vue` OrderAdvanceCandidate 含 `orderCanonicalId?: number` 字段
  - `OrderAdvanceDialog.vue` ElAlert 文案含"自动带入"+"兼容回退"字样
  - `OrderAdvanceDialog.vue` 不再含"5号位 待补列"字样（旧的兜底提示语）
  - `Order.vue advanceCandidates` 透传 `orderCanonicalId`
  - `mocks/fixtures.ts` mockOrderList 含 `orderCanonicalId` 字段
- **M02 接受类（默认关闭）**：`VERIFY_ALLOW_WRITE=1` 时执行（需 Order 页已选订单 + Dialog 自动带入 + 提交跳转 Candidate）
  - 验证 Row.canonicalId 默认值 = OrderBasicInfo.orderCanonicalId
  - 验证手填覆盖后提交载荷正确（orderTargets[].orderCanonicalId = 覆盖值）

---

## 五、验证清单

| # | 项 | 预期 |
|---|---|---|
| 1 | `pnpm ts:check` | 三绿 |
| 2 | `pnpm lint:eslint` | 三绿 |
| 3 | `pnpm build:pro` | 三绿 |
| 4 | `GROUP=m-10a1 node scripts/verify-acceptance.mjs` | M01 全绿；M02 默认 skip |
| 5 | `GROUP=static` 复跑 | 无回归（U41/U43-U45 仍绿）|
| 6 | `GROUP=j-10a` 复跑 | 无回归（§10A 五业务入口验证仍绿）|
| 7 | 浏览器走查（用户执行）| Order 页 → 选订单 → 打开 OrderAdvanceDialog → 「订单规范 Id」列自动带入 → 改优先级为 EXPEDITE → 提交 → 跳转 Candidate |
| 8 | dev 库 `[Order]` 表联调（用户执行）| `OrderQuery` 端点返回真实 `orderCanonicalId`（B4 已 build 0 Error）|

---

## 六、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **4 号位 实施** | 本日内 | 4 号位 |
| **3 绿复验** | 实施后 0.5 人天内 | 4 号位 |
| **dev 库 `[Order]` 表确认有 OrderCanonicalId 列** | 实施前 | 5 号位 / 2 号位 |
| **B4 端点联调** | 实施后 | 4 号位 + 5 号位 |

---

## 七、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + 2026-09-22 沟通方式新规：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ✅ 4 号位 仅前端代码改动（5 文件，~0.9 人天）
- ✅ 4 号位 实施后三绿复验 + verify [M] 段 M01 全绿

---

## 八、附档

- 5 号位 P1-3 回执：[5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md](5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md)
- 4 号位 P1-3 直通函：[4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)
- 4 号位 合并回执给 3 号位：[4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md](4号位-2026-09-23-OPM-API与EXPEDITE拍板合并回执-给3号位.md) §二.5
- 冻结文档：[集成接口设计 v1.33 §5.3 + §6.5](../冻结文档/APS_集成接口设计_v1.33_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.md)
- 4 号位 实施包原 §十A.1 L595-672（仓库中无 v1.4 文档；引用见 `OrderAdvanceDialog.vue:2-10`）
- 4 号位 v1.5 实施包 §四.5："待 5 号位 补列后改为自动带入（保留手填兜底）"——触发条件已满足
- 前端现状：OrderAdvanceDialog.vue:35-47（OrderAdvanceCandidate / Row 接口）+ 188-197（手填列）+ 218-223（ElAlert）+ Order.vue:297-306（advanceCandidates）
- 冻结基线单向对齐原则：[[feedback_baseline_chain.md]]

---

**发送人**：4 号位 ｜ **日期**：2026-09-23
**触发条件**：5 号位 B4 已落地 + P1-3-c 触发
**工作量**：~0.9 人天
**风险**：低（前端 Dialog 字段级改造 + mock 补字段，不改后端）