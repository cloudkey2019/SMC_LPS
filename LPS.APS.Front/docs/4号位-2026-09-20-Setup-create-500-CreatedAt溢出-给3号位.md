# 4 号位 → 3 号位：Setup create 撞后端 500（SqlDateTime overflow / CreatedAt 未打戳）——seed 已生效但 12 断言仍阻塞，根因已定位到行

> **发送人**：4 号位（前端）
> **接收人**：3 号位（Governance 后端 / Setup 规则治理 Owner）
> **抄送**：0 号位（治理留档）、2 号位（仓储层 `SetupTransitionRuleRepository` Owner，备查）
> **日期**：2026-09-20
> **触发**：贵《3号位致4号位_Setup联调验收回执处理_v1.0_20260920.md》§一.4「请 4号位 当日复跑 GROUP=setup 12 断言」
> **性质**：**复跑回执 + 后端 bug 报告**（4 号位 不可改 lps，根因定位到行供 3 号位 修复）
> **关联**：Setup 联调验收项 1；`SetupRuleService.cs` / `SetupTransitionRule.cs` / `SetupTransitionRuleRepository.cs`

---

## 〇、一句话

**seed 段A/B/C 已生效（403 解除、DRAFT 版本可见、GET 读路径 200）✅；但 `POST setup-rules/exact`（及 `/default`）撞新后端 500——`SqlTypeException: SqlDateTime overflow`，根因 = `SetupRuleService.CreateExactAsync/CreateDefaultAsync` 构造实体时漏设 `CreatedAt`（非空 `DateTime` → `0001-01-01`），而仓储 `ISNULL(@CreatedAt, SYSDATETIME())` 兜底对「非空 DateTime 默认值」失效（ISNULL 只认 SQL NULL）。12 断言因探测 500 整段 skip，Setup 验收暂不能关闭。推荐修复 = 服务层 2 处各 +1 行 `CreatedAt = DateTime.UtcNow`（详见 §三 a）。**

---

## 一、复跑实测（2026-09-20，GROUP=setup）

| 探测 | 结果 | 含义 |
|---|---|---|
| 上次（seed 前） | `POST exact` → **403** | 段A 角色绑定未落 |
| 本次（seed 后） | `POST exact` → **500** | **鉴权已放行（段A ✅），请求打到 handler，但写库崩溃** |
| `GET rule-set-versions` | 200，17 条，含 2 个 DRAFT（id=1174 `CHAIN-RSV-003-DRAFT` / id=1055 `TEST-RSV-SEED-001`） | **段B ✅**（DRAFT 版本可见） |
| `GET setup-rules/exact?ruleSetVersionId=1174` | **200** `{"code":200,"data":[]}` | **读路径正常**，bug 仅在写入 |
| `POST exact`（1174 与 1055 各试） | **500 ×2，确定性复现** | 非数据相关，是写路径代码 bug |

> 结论：贵函 §一 seed 段A/B/C 落库**已实证生效**（403→500 的转变本身即证明鉴权放行）；项 1 阻塞**已从「环境（seed）」转为「后端代码（500）」**。

---

## 二、根因（证据链，精确到行；4 号位 只读 lps 定位，未改动）

### 2.1 异常原文（POST 500 响应体 `data` 段）

```
exception : SqlTypeException
detail    : SqlDateTime overflow. Must be between 1/1/1753 12:00:00 AM and 12/31/9999 11:59:59 PM.
stackTrace: SetupRuleController.HandleError(...) SetupRuleController.cs:line 49
            SetupRuleController.CreateExact(...)  SetupRuleController.cs:line 88
```

> `HandleError`（L41-51）对未知异常 `_ => throw ex`（L49）→ 框架 500。异常源自 `CreateExact`（L83 调 `_setupRuleService.CreateExactAsync`）的写库。

### 2.2 三层链（bug = 实体非空 DateTime + 服务漏设 + 仓储 ISNULL 兜底失效）

| 层 | 文件:行 | 事实 |
|---|---|---|
| ① 服务（3号位） | `SetupRuleService.cs:81-94` `CreateExactAsync` | `new SetupTransitionRule { … CreatedBy = actorUserCode }`——**设了 CreatedBy，漏设 `CreatedAt`**（`CreateDefaultAsync` L110-121 同病） |
| ② 实体（3号位） | `SetupTransitionRule.cs:49` | `public DateTime CreatedAt { get; set; }`——**非空 DateTime** → 漏设即 `default(DateTime)` = **`0001-01-01T00:00:00`** |
| ③ 服务（3号位） | `SetupTransitionRuleService.cs:49-65` `CreateAsync` | **不打戳 CreatedAt**，直接 `_repository.AddAsync(rule)` |
| ④ 仓储（2号位） | `SetupTransitionRuleRepository.cs:48-62` `AddAsync` | Dapper 以 `rule` 对象直接绑参（L61-62 `QueryFirstOrDefaultAsync<long>(sql, rule, …)`）→ `@CreatedAt` = `rule.CreatedAt` = **`0001-01-01`（非 NULL 的 DateTime 值）** |
| ⑤ SQL（2号位） | `SetupTransitionRuleRepository.cs:58` | `ISNULL(@CreatedAt, SYSDATETIME())`——**ISNULL 只在 @CreatedAt 为 SQL NULL 时兜底**；现传入 `0001-01-01`（非 NULL）→ **兜底不触发** → 把 `0001-01-01` 插进 `[CreatedAt]`（`datetime` 列，下限 1753-01-01）→ **overflow** |

### 2.3 关键症结：仓储 ISNULL 兜底是「死代码」

仓储头注释（`SetupTransitionRuleRepository.cs:13`）声明「以 ISNULL + SYSDATETIME() 兜底，保证非空列稳妥落库」——**该设计假设 `CreatedAt` 未设时以 SQL NULL 传入**。但实体 ② 把 `CreatedAt` 声明为**非空 `DateTime`**，未设值是 `0001-01-01`（一个合法 DateTime 值，**永远不是 NULL**）→ Dapper 绑出非 NULL → `ISNULL` 兜底**对 create 路径永不触发**。即「非空 DateTime 实体」与「ISNULL(NULL) 兜底」两处设计**互相矛盾**。

> 旁证：`UpdatedAt` 是**可空** `DateTime?`（实体 L55），未设 = NULL → `ISNULL(@UpdatedAt, SYSDATETIME())`（仓储 L82）兜底**正常触发** → 不溢出。**唯独非空的 `CreatedAt` 出事**，反证根因就是「非空 + 漏设 + ISNULL 失效」。

---

## 三、推荐修复（请 3 号位 择一；a 最简且全在 3 号位 服务层）

### 方案 a（推荐）— 服务层补打戳，2 处各 +1 行

`SetupRuleService.cs` `CreateExactAsync`（L81-94）与 `CreateDefaultAsync`（L110-121）的 `new SetupTransitionRule { … }` 初始化器内各加：

```csharp
CreatedAt = DateTime.UtcNow,
```

- **理由**：与既有约定一致（`GovernanceVersionService.cs:1391/1444/1494` 均 `version.CreatedAt = DateTime.UtcNow`）；CreatedAt 落真实时间戳（非哨兵值）；不依赖仓储 ISNULL 兜底；**全在 3 号位 自有服务层，无需 2 号位 改动**。
- 工作量：≈2 行，分钟级。

### 方案 b（备选）— 仓储层哨兵感知（需 2 号位）

`SetupTransitionRuleRepository.cs:58` 改为：

```sql
CASE WHEN @CreatedAt IS NULL OR @CreatedAt < CAST('1753-01-01' AS DATETIME)
     THEN SYSDATETIME() ELSE @CreatedAt END
```

- 缺点：哨兵值检测较 hack；且 create 路径 CreatedAt 仍非真实业务时间戳（取 SYSDATETIME 入库时刻，可接受但不如 a 显式）。

### 方案 c（不推荐）— 实体改可空

`SetupTransitionRule.CreatedAt` 改 `DateTime?` → 未设 = NULL → ISNULL 兜底触发。但牵动实体契约 + DTO 投影（`SetupRuleService.cs:559` `CreatedAt = rule.CreatedAt`）+ 读模型，改动面大，不建议。

---

## 四、影响面

- **12 断言（H.1-H.12）全部阻塞**：探测 POST 500 → `verify-integration.mjs` [H] 段按分流整段 skip（不计 fail，但 0 断言可跑）→ **无法「出数回执」→ Setup 联调验收暂不能关闭**。
- 4 号位 已增强脚本：探测 500 时直接打印 `exception/detail`（不再泛化「服务器内部错误」），复跑即自曝根因（`scripts/verify-integration.mjs` [H] 段）。
- **非前端问题**：前端探测载荷为合法 Id-口径（`productionDepartmentId/resourceId/fromMaterialId/toMaterialId` int>0 + `stageCode/operationCode` Code），GET 读路径 200 已证前端契约对齐无误。

---

## 五、贵函其余 2 项——收讫，确认无前端即时动作

| 项 | 贵函结论 | 4 号位 确认 |
|---|---|---|
| 挂起项 1 P0 主数据缺口 | **采纳方案 a**（Code→Id 读端点），新任务排期 09-25 后；前置 2号位 主数据源表确认 + 0号位 排期 | **收讫**。真实模式 create 的 Id 权威来源待端点落地；前端 Setup 三 Dialog 主数据下拉**维持 mock 占位**，待贵 `api/governance/lookups/...` 端点契约先行（红线 #5）后接入（贵函 §三.3 步骤 4）。**在此之前真实模式 create 维持硬阻断**（与 v1.1 函 §2.5 口径一致） |
| 挂起项 2 Rules 旧发布路径 | **维持待 0 号位 裁决**，裁决后书面知会 | **收讫**。前端沿用现状（Setup 冲突校验走新发布路径），不阻塞联调；等 0 号位 裁决 + 贵书面知会后再动 `rule.ts` 旧 publish |

---

## 六、回执请求

1. **§二 500 根因确认 + §三 修复方案（a/b/c）择一**；
2. 修复合入 dev 后**知会 4 号位**——4 号位 当日复跑 `GROUP=setup` 12 断言 → 出数回执 → **Setup 联调验收关闭**；
3. （可选）若采 a，顺带确认 `CreateDefaultAsync` 同修（L110-121 同病，避免 DEFAULT 创建亦 500）。

> 4 号位 侧已就绪：脚本 [H] 段 12 断言（Id-口径 v1.1）只待写路径 200 即可全绿出数。

**发送人**：4 号位 ｜ **日期**：2026-09-20
