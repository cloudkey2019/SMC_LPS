# APS V1 3号位 DDL 变更申请：版本表增加发布内容快照列

> 版本：v1.0
> 日期：2026-08-22
> 申请方：3号位
> 执行方：2号位（数据库责任方，红线 #6：DB schema 变更由 2号位 专属执行）
> 依据：0号位《第三轮正式复审报告_a5741a7_Commit冻结版(1).md》§12.3（**批准方案 A** 作为最小技术 DDL 修正）+ §5.2 / §5.6 / §9.1
> 关联契约：《05_3号位和1号位对外契约.md》§6.10.5（本申请对应的契约语义）

---

## 一、申请内容（最小变更，2 处新增列）

| 表 | 新增列 | 类型 | 说明 |
|---|---|---|---|
| `RuleSetVersion` | `ContentSnapshotJson` | `NVARCHAR(MAX) NULL` | 该版本发布时的完整规则内容快照（可重放载体） |
| `ParameterSetVersion` | `ContentSnapshotJson` | `NVARCHAR(MAX) NULL` | 该版本发布时的完整参数内容快照（可重放载体） |

**无删列、无改列、无索引变更**。仅新增 2 列，`NULL` 允许（发布前为空，发布时填充）。

## 二、变更依据（0号位 已正式裁决）

1. **业务必要性**：冻结业务要求"已发布规则/参数版本不可变，且历史 Run 可按 StrategyProfileVersionId 恢复当时完整规则参数"（冻结接口 2↔3）；当前冻结 DDL v5.1.2 缺少"发布内容可重放"物理载体（0号位 报告 §5.1）。
2. **0号位 裁决**（§12.3）：批准方案 A——每张版本表只增加**一个通用发布内容快照字段**；不新增 Snapshot 表；不新增版本体系；不新增主题专用列；不改变业务；由数据库责任方执行正式 DDL 同步。
3. **方案 B/C 已否决**：B（主题表+版本外键）新增多表、DDL 改动大；C（保持各主题 JSON 列）持续扩大主题专用列、固化代码与冻结 DDL 漂移（§5.4/§5.5）。

## 三、变更边界（红线，不越界）

- ✅ 仅新增 2 列，**不新增表**（无 Snapshot 表）
- ✅ **不新增版本号体系**
- ✅ **不新增主题专用列**（DemandPriorityJson/LockJson/SupplyJson/ProcurementJson 等**不再**补进 DDL）
- ✅ **不新增 ChangeReason 等审计字段**（沿用既有 `GovernanceAuditLog`，0号位 §9.2）
- ✅ `StrategyProfileVersion` 表结构**不动**（仍仅组合 RuleSetVersionId + ParameterSetVersionId）
- ✅ 不新增 RuleCondition/RuleAction/DSL/插件表

## 四、变更影响（3号位 侧对齐计划）

执行方（2号位）落地该 2 列后，3号位 侧自动对齐项：

| # | 3号位 对齐 | 状态 |
|---|---|---|
| 1 | Core 实体 `RuleSetVersion`/`ParameterSetVersion` 增加 `ContentSnapshotJson` 属性 | 待 DDL 落地后随 P0-01b 执行 |
| 2 | Repository SQL 对齐新 DDL（INSERT/UPDATE 写 ContentSnapshotJson） | 同左 |
| 3 | Provider 六块从 ContentSnapshotJson 反序列化（真实来源） | 随 P0-02a 执行 |
| 4 | 集成测试环境验证（原 6 跳过项依赖列就位） | 自动转绿 |

## 五、请求

1. 请 2号位 按上表在冻结 DDL v5.1.2 基础上执行最小变更（新增 2 列）；
2. 变更后回执 3号位（含 DDL 语句 / 变更后表结构 / 是否同步更新线上 APS_Production 库）；
3. 3号位 收到回执后执行 P0-01b/P0-02a 代码对齐并跑通集成测试。

> 本申请为 3号位 提交的正式变更申请（红线 #6 流程），不涉及 3号位 自行 ALTER。
