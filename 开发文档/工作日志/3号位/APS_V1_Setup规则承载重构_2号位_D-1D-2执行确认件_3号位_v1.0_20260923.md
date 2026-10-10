# APS V1 Setup 规则承载重构——2号位 D-1/D-2 执行确认件

**文档性质**：跨号位执行确认件（2号位 收）——Setup 规则承载重构后，物理表清理（D-1，红线 #6 专属）+ 快照列容量确认（D-2）。
**发送方**：3号位 ｜ **日期**：2026-09-23
**依据**：重构方案 `APS_V1_SetupTransitionRule重构方案_复用RuleSetVersion快照承载_3号位_v1.0_20260922.md`（S-1~S-9 已全绿：构建 0 错误、350/350 单测）；契约登记件 `APS_V1_Setup规则承载契约登记件_3号位_v1.0_20260923.md`；反向对象清单 = `开发文档\2026-09-17文档更新\APS_数据库_v5.1.7到v5.1.8_Setup换型规则_升级补丁.sql`
**抄送**：0号位（备查）

---

## 一、结论

**2号位 执行项 = D-1（DROP 表）+ D-2（容量回执），无新增代码开发。** 仓储/DI 已由 S-3 撤销（物理表访问清零），第⑦块投影已改道 3号位 `FrozenStrategySnapshotProvider`（S-4）——2号位 不再承担 Setup 投影链（原 v1.6 文档 A1/A2 条目已随契约登记件撤销/改道）。

## 二、D-1：DROP `SetupTransitionRule` 物理表（红线 #6 专属）

**前置条件**：S 系列（S-1~S-9）已就绪并验收（✅ 全绿）。**勿在 S 系列就绪前执行**，避免治理/装载断链。

### 2.1 DROP 对象清单（反向执行 2026-09-17 升级补丁）

| # | 对象 | 类型 | 补丁位置 |
|---|---|---|---|
| 1 | `SetupTransitionRule` | 表 | 补丁 :20-46 |
| 2 | `UQ_SetupTransitionRule_EXACT` | 筛选唯一索引（EXACT 七元组 + IsActive=1） | 补丁 :52-58 |
| 3 | `UQ_SetupTransitionRule_DEFAULT` | 筛选唯一索引（DEFAULT 五元组 + IsActive=1） | 补丁 :63-68 |
| 4 | `IX_SetupTransitionRule_RuleSetVersion` | 普通索引（RuleSetVersionId, IsActive, RuleType） | 补丁 :73-74 |
| 5 | `FK_SetupTransitionRule_RuleSetVersion` | 外键（→ RuleSetVersion.Id） | 补丁 :36-37 |
| 6 | `CK_SetupTransitionRule_RuleType` | 检查约束（EXACT/DEFAULT） | 补丁 :38-39 |
| 7 | `CK_SetupTransitionRule_SetupMinutes` | 检查约束（>=0） | 补丁 :40-41 |
| 8 | `CK_SetupTransitionRule_EXACT_Materials` | 检查约束（EXACT 必须 From/To 均非空） | 补丁 :42-43 |
| 9 | `CK_SetupTransitionRule_DEFAULT_Materials` | 检查约束（DEFAULT 必须 From/To 均空） | 补丁 :44-45 |

### 2.2 建议 DROP 脚本（顺序：索引 → FK/CK → 表）

```sql
USE [APS_Production];
GO
DROP INDEX IF EXISTS UQ_SetupTransitionRule_EXACT ON SetupTransitionRule;
DROP INDEX IF EXISTS UQ_SetupTransitionRule_DEFAULT ON SetupTransitionRule;
DROP INDEX IF EXISTS IX_SetupTransitionRule_RuleSetVersion ON SetupTransitionRule;
ALTER TABLE SetupTransitionRule DROP CONSTRAINT IF EXISTS FK_SetupTransitionRule_RuleSetVersion;
ALTER TABLE SetupTransitionRule DROP CONSTRAINT IF EXISTS CK_SetupTransitionRule_RuleType;
ALTER TABLE SetupTransitionRule DROP CONSTRAINT IF EXISTS CK_SetupTransitionRule_SetupMinutes;
ALTER TABLE SetupTransitionRule DROP CONSTRAINT IF EXISTS CK_SetupTransitionRule_EXACT_Materials;
ALTER TABLE SetupTransitionRule DROP CONSTRAINT IF EXISTS CK_SetupTransitionRule_DEFAULT_Materials;
DROP TABLE IF EXISTS SetupTransitionRule;
GO
```

### 2.3 seed 清理核实（已确认，无需数据迁移）

`APS_Setup_dev_seed_20260920.sql` 段 A（角色绑定）/ 段 B（DRAFT RuleSetVersion）/ 段 C（COMPLETED ScheduleRun）**已核实均无 `SetupTransitionRule` 表插入**——本表为越位建表，seed 从未落数据。故 D-1 **无需 seed 数据迁移**，仅反向执行建表补丁 + DROP 后复核即可。

### 2.4 DROP 后复核项

- [ ] `SELECT COUNT(*) FROM sys.tables WHERE name='SetupTransitionRule'` = 0
- [ ] 全仓 Grep `SetupTransitionRule`（物理表访问）仅剩文档与实体 XML 注释（S-3 已删仓储/DI，S-5 已删 PeggingOrchestrator 读取）
- [ ] 不得再注册 `SetupTransitionRuleRepository` / `ISetupTransitionRuleRepository`（S-3 已删除，勿恢复）

## 三、D-2：确认 `ContentSnapshotJson` 容量承载 Setup 子块

`RuleSetVersion.ContentSnapshotJson` 为 NVARCHAR(MAX)（≤2GB），Setup 子块为 JSON 数组（每条 ≈ 0.5~1KB，含审计字段），按典型规则量（百~千条）占用 ≤ 数 MB，远低于上限。**请 2号位 回执确认容量口径可行**（理论无上限，按规则量评估）。

## 四、回执栏（2号位 填写）

| 项 | 确认 |
|---|---|
| D-1 已执行 DROP（表+索引+约束，顺序脚本 2.2） | ☐ 已执行 |
| D-1 后复核项（2.4）通过 | ☐ 通过 |
| D-2 `ContentSnapshotJson` 容量可行 | ☐ 确认 |
| 备注 |  |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：2号位 D-1 执行 + D-2 回执 → 契约登记件状态转 ✅（物理表清理闭环）
