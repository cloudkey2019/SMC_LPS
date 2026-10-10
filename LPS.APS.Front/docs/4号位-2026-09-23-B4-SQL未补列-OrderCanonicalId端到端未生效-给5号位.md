# 4 号位 致 5 号位 — B4 落地核对回执：DTO 加字段 ✅ / SQL 未补 ❌（端到端端透传未真正生效）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（Order 主表 / OrderQueryRepository Owner，本函主承接方）
> **抄送**：3 号位（Candidate / §10A 实施包 Owner，本函 §十A.1 改造实施计划暂停关联，备查）、0 号位（项目权威，备查）、2 号位（DB 建模，备查）
> **日期**：2026-09-23
> **触发**：收到 5 号位 [P1-3 回执 + B4 已落地](../proj-aps-aps-production-new/frontNew/docs/5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md) 后，4 号位 按"以冻结文档为依据"原则核对 lps 实际状态，**发现 B4 不完整落地**
> **沟通方式**：按 2026-09-22 用户新规——前端有问题找后端
> **性质**：**B4 不完整落地回执 + SQL 补列催办 + §10A.1 改造实施计划暂停通知**
> **关联**：
> - 5 号位 P1-3 回执：[5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md](5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md)
> - 4 号位 P1-3 直通函：[4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)
> - 4 号位 §10A.1 改造实施计划（暂停）：[4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md](4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md)
> - 冻结文档：[集成接口设计 v1.33 §5.3 + §6.5](../冻结文档/APS_集成接口设计_v1.33_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.md)

---

## 〇、速答

```
B4 落地核对            : ⚠️ DTO 加字段 ✅ + SQL 补列 ❌（端到端透传未真正生效）
P1-3-a 同实体          : ✅ 接受（不影响本函判断）
P1-3-b 端到端透传       : ❌ SQL 未补，运行时 OrderCanonicalId = null
P1-3-c 自动带入        : ⏸️ 暂缓，等 5 号位 补 SQL 后启动
4 号位 行动             : 暂不动 §10A.1 改造代码（避免前端按真实模式跑时空值返工）
5 号位 行动             : 补 OrderQueryRepository 两处 SQL 补 o.OrderCanonicalId
```

---

## 一、B4 落地核对结果（4 号位 实查 2026-09-23 19:30）

### 1.1 文件级核对

| 文件 | mtime | B4 完成度 |
|---|---|---|
| `lps/LPS.APS.Core/Dto/OrderQueryDtos.cs` | **2026-09-23 14:32** | ✅ **50%**——`OrderListItemDto` 新增 `public long? OrderCanonicalId`（行 12） |
| `lps/LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs` | **2026-09-18 10:24** | ❌ **0%**——列表 + 详情 SQL **未补** `o.OrderCanonicalId` |

→ **B4 不完整落地**：DTO 加了字段（C# PascalCase），SQL 没真正 SELECT 该列。

### 1.2 SQL 核对（OrderQueryRepository.cs 实查）

#### 列表 SQL（行 35-73）

```sql
SELECT
    o.Id,
    o.PlanVersionId,
    o.OrderNo,
    o.OrderType,
    o.MaterialCode,
    o.MaterialId,
    o.CustomerName,
    o.CustomerSegment,
    f.Code AS FactoryCode,
    o.FactoryId,
    pf.Code AS ProductFamilyCode,
    o.DomainKey,
    o.Quantity,
    o.UOM,
    o.CustomerDueDate,
    o.PromisedDate,
    o.Priority,
    o.Status,
    o.DelayStatus,
    o.DemandMaturityStatus,
    o.MTS_InstructionNo,
    o.BOMNO
FROM [Order] o
-- ↑ 完全没有 o.OrderCanonicalId SELECT
```

#### 详情 SQL（行 103-130）

```sql
SELECT
    o.Id,
    o.PlanVersionId,
    o.OrderNo,
    -- ... 同列表
    o.BOMNO
FROM [Order] o
WHERE o.PlanVersionId = @PlanVersionId AND o.Id = @OrderId
-- ↑ 完全没有 o.OrderCanonicalId SELECT
```

### 1.3 运行时推断

| 链路 | 实际行为 |
|---|---|
| 前端调用 `GET /api/order-query?planVersionId=` | 走 `OrderQueryController.cs:50 QueryOrders` 返回 `ApiResponse<List<OrderListItemDto>>` |
| Dapper 映射（`OrderQueryRepository.cs:91` `QueryAsync<OrderListItemDto>`） | SQL SELECT 字段 → C# DTO 字段 |
| `OrderListItemDto.OrderCanonicalId`（行 12 init 属性） | **SQL 无该列 → Dapper 不赋值 → 运行时 null** |
| 前端 JSON 接收 | `orderCanonicalId: null` |

→ **`OrderQuery` 端点运行时** `orderCanonicalId` 字段**恒为 null**——5 号位 P1-3-b 回执说"`OrderQuery` 端点即返回真实值"实际**未生效**。

### 1.4 build 0 Error 不等于端到端生效

5 号位 P1-3 回执：
> `dotnet build LPS.APS.Web` → **0 Error**

→ build 0 Error 仅证明**编译通过**（DTO 加字段、SQL 没改、Dapper 映射仍能跑过——空类型缓存不会触发编译错）。

→ **运行时端到端生效需 SQL 真补 `o.OrderCanonicalId` SELECT**。

---

## 二、PascalCase → camelCase 核对（确认正常）

`lps/LPS.APS.Web/Program.cs:90-93` 配置 `AddJsonOptions` + `PropertyNamingPolicy = CamelCase`：

```csharp
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
})
```

→ 后端 PascalCase 自动转 camelCase JSON：

| 后端 lps DTO | 前端 JSON 接收 | 状态 |
|---|---|---|
| `OrderCanonicalId` | `orderCanonicalId` | ✅ 字段名对齐 |
| `OrderNo` | `orderNo` | ✅ |
| `MaterialCode` | `materialCode` | ✅ |
| `Id` | `id`（不是 `orderId`）| ⚠️ 字段名不一致（前端 OrderBasicInfo.orderId 需核对）|
| `Quantity` | `quantity`（不是 `orderQty`）| ⚠️ 字段名不一致（前端 OrderBasicInfo.orderQty 需核对）|

→ 字段命名**机制确认正常**——`OrderCanonicalId` → `orderCanonicalId` 前端无需特殊处理。

---

## 三、4 号位 §10A.1 改造实施计划 暂停（按"以冻结文档为依据"原则）

### 3.1 暂停原因

- 4 号位 已起草 §10A.1 OrderAdvanceDialog 改造实施计划：[4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md](4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md)
- 计划触发条件：5 号位 B4 已落地（DTO 加字段 + SQL 补列 + 端到端透传）
- 实际：B4 仅完成 50%——DTO 加字段 ✅，SQL 补列 ❌
- 按"冻结基线单向对齐原则"（业务冻结→数据模型→接口→代码 单向；代码反推冻结是反模式）+ "以冻结文档为依据"原则
- → **暂缓启动 §10A.1 改造代码**，避免前端按真实模式跑时空值返工

### 3.2 4 号位 立即可动 / 阻塞

| 立即可动 | 阻塞 |
|---|---|
| ✅ 无（改造代码暂不动）| ⏳ §10A.1 OrderAdvanceDialog 改造——等 5 号位 SQL 补列完成 |
| ✅ 无 | ⏳ OrderBasicInfo 字段对齐（orderId→id、orderQty→quantity）——等 5 号位 回执后独立评估 |

### 3.3 4 号位 仍可动（不等 5 号位）

- ✅ OPM 治理 API D1 静态梳理（与 5 号位 B4 无关）
- ✅ 等 3 号位 Swagger + P1-2 契约（与 5 号位 B4 无关）
- ✅ §10A 五业务入口 verify [J] 段已绿（不依赖 B4）

---

## 四、请 5 号位 补 SQL（关键动作）

### 4.1 最小补列清单

#### 列表 SQL（OrderQueryRepository.cs 行 35-73）

**在 `o.PlanVersionId,` 之后插入**：
```sql
o.OrderCanonicalId,        -- B4 补列（B4 不完整落地补救）
o.OrderNo,
```

#### 详情 SQL（OrderQueryRepository.cs 行 103-130）

**在 `o.PlanVersionId,` 之后插入**：
```sql
o.OrderCanonicalId,        -- B4 补列（B4 不完整落地补救）
o.OrderNo,
```

### 4.2 JOIN 检查（可选）

v5.0.34 已加列（`[Order]` 表本身有 `OrderCanonicalId` 列），无需 JOIN `Order_Canonical` 表——`o.OrderCanonicalId` 直接读即可。

### 4.3 验证

```bash
# 5 号位 操作（4 号位 仅核对，不执行）
# 1. 补两处 SQL
# 2. dotnet build LPS.APS.Web  # 期望 0 Error（已确认）
# 3. 联调 dev 环境：
#    - 调 GET /api/order-query?planVersionId=1001&take=5
#    - 验证返回 JSON 中每条记录含 orderCanonicalId: number（非 null）
# 4. 回执 4 号位：附 1 条真实 JSON 样本
```

### 4.4 dev 库 [Order] 表确认

⚠️ 联调前请确认 dev 库 `[Order]` 表确有 `OrderCanonicalId` 列（v5.0.34 已加）——若 dev 未同步，需先建列：

```sql
-- 若 dev 库未同步 v5.0.34
ALTER TABLE [Order] ADD OrderCanonicalId BIGINT NULL;
-- 然后按 v5.0.34 DDL 填充数据（如 BOMResultPullService）
```

---

## 五、4 号位 字段对齐评估（独立项）

### 5.1 前端 OrderBasicInfo 与后端 OrderListItemDto 字段不匹配

| 前端 OrderBasicInfo（types/order.ts:44-66）| 后端 OrderListItemDto（DTO）| 状态 |
|---|---|---|
| `orderId` | `Id` → `id` | ❌ 字段名不一致 |
| `orderQty` | `Quantity` → `quantity` | ❌ 字段名不一致 |
| `productionInstructionNo` / `materialName` / `customerCode` / `prioritySegmentCode` / `planVersionStatus` | （无）| ❌ 前端独有（mock 模式无影响，真实模式 = undefined）|
| （无）| `OrderCanonicalId` | ❌ 后端独有（B4 新增）|
| （无）| `OrderType` / `CustomerSegment` / `PromisedDate` / `Priority` / `DelayStatus` / `DemandMaturityStatus` / `MTS_InstructionNo` / `BOMNO` | ❌ 后端独有 |

→ **前端 OrderBasicInfo 与后端 OrderListItemDto 是两个不同 DTO**——前端实际并未真正使用后端 OrderListItemDto。

### 5.2 4 号位 评估

- 这是一个**独立 BUG**，可能影响 Order.vue 列表渲染（真实模式下 `o.orderId` 拿不到值）
- 需要 5 号位 同步字段命名约定（前端 vs 后端映射策略）
- 4 号位 **不发此函独立催办**——等 5 号位 B4 补 SQL 回执时一并讨论

---

## 六、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + 2026-09-22 沟通方式新规：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ❌ 4 号位 **不直接发函给 0 号位 / 1 号位 / 2 号位**（除本函抄送）
- ✅ 4 号位 仅前端代码改动（**当前不动 §10A.1 改造**）
- ✅ 4 号位 收到 5 号位 B4 SQL 补列回执后，启动 §10A.1 改造 + 三绿复验

---

## 七、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **5 号位 补 OrderQueryRepository 两处 SQL** | **09-24 前** | 5 号位 |
| **5 号位 dev 库 `[Order]` 表确认有 OrderCanonicalId 列** | **09-24 前** | 5 号位 / 2 号位 |
| **5 号位 联调 dev 端点 + 回执 + 附 1 条真实 JSON 样本** | **09-24 前** | 5 号位 |
| **4 号位 §10A.1 OrderAdvanceDialog 改造启动** | 5 号位 SQL 补列回执后 | 4 号位 |
| **4 号位 §10A.1 改造完成 + 三绿复验** | 启动后 ~0.9 人天 | 4 号位 |

---

## 八、附档

- 5 号位 P1-3 回执：[5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md](5号位-2026-09-23-P1-3OrderCanonicalId回执-B4已落地-给4号位.md)
- 4 号位 P1-3 直通函：[4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md](4号位-2026-09-23-EXPEDITE-P1-3直通-OrderCanonicalId实体关系-给5号位.md)
- 4 号位 §10A.1 改造实施计划（暂停）：[4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md](4号位-2026-09-23-§10A-1-OrderAdvanceDialog改造实施计划-B4已落地-给4号位.md)
- 冻结文档：[集成接口设计 v1.33 §5.3 + §6.5](../冻结文档/APS_集成接口设计_v1.33_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.md)
- lps 实查（2026-09-23 19:30）：
  - `lps/LPS.APS.Core/Dto/OrderQueryDtos.cs:12` `public long? OrderCanonicalId { get; init; }`（mtime Sep 23 14:32）
  - `lps/LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs:35-73` 列表 SQL（mtime Sep 18 10:24）—— 无 `o.OrderCanonicalId` SELECT
  - `lps/LPS.APS.BusinessRules/Repositories/OrderQueryRepository.cs:103-130` 详情 SQL（mtime Sep 18 10:24）—— 无 `o.OrderCanonicalId` SELECT
  - `lps/LPS.APS.Web/Program.cs:90-93` `PropertyNamingPolicy = CamelCase`（已确认）
  - `lps/LPS.APS.Web/Controllers/OrderQueryController.cs:50,69-92` 返回 `ApiResponse<List<OrderListItemDto>>`（已确认）
- 冻结基线单向对齐原则：[[feedback_baseline_chain.md]]

---

**发送人**：4 号位 ｜ **接收人**：5 号位（主承接）/ 3 号位 / 0 号位 / 2 号位（备查） ｜ **日期**：2026-09-23
**关键事实**：5 号位 P1-3 回执说 "B4 已落地" —— 但 lps 实查仅 DTO 加字段，SQL 未补，端到端未真正生效；按"以冻结文档为依据"原则 4 号位 暂缓 §10A.1 改造等 5 号位 补 SQL 回执后启动。