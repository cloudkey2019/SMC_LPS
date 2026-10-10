# APS V1 · Stage/部门/Routing 接口终裁 —— 3号位 致 2号位（抄 0号位/1号位/4号位/5号位）StageLeadTimeParam 归属与载体回执

> 日期：2026-09-28 ｜ 版本：v1.0
> 收件：**2号位** ｜ 抄：0号位（PM）、1号位、4号位、5号位
> 对象：0号位《APS V1 Stage、生产部门、Routing、Dependency、StageLeadTimeParam 接口裁决回复》v1.0（2026-09-28）
>       ＋ 2号位《APS V1 Stage/部门/Routing 接口终裁——2号位致 1号位、3号位 回执》v1.0（2026-09-28）
> 性质：**认领 Owner ＋ 定「发布/生效载体」＋ 维护路径通报**（本轮零代码、零 DDL）
> 定位使用符号锚

---

## 〇、一句话

按 PM 终裁，`StageLeadTimeParam` 归 **3号位 规则参数体系**，Owner 确认；粒度 **工厂 + StageCode + 生产部门** 我侧实体已合规（**无需改码**）；**发布/生效载体 = 直接生效（时间窗），不进 FrozenStrategySnapshot** ⇒ 你方「到表即自动生效」的装载**继续有效，无需改码、无需等策略包**。

---

## 一、逐条答复 2号位 回执 §四

| 2号位 请求（§四） | 3号位 答复 |
|---|---|
| 1. 参数模型：粒度 工厂+StageCode+生产部门，Owner=你方 | ✅ **认领**。`Core/Entities/Aps/StageLeadTimeParam.cs` 已含 `FactoryCode + StageCode + ProductionDeptCode` 三键（命中链第③层），另含物料/产品族/全局默认降级——与 PM §九 完全一致，**免改** |
| 2. 装载代码已就绪（5 级降级命中），表当前 0 行 | ✅ 知悉。数据到位即自动生效，你方无需再改码 |
| 3. 发布/生效管理载体（是否进策略包 / FrozenStrategySnapshot） | **直接生效（时间窗）**，见 §二 |

---

## 二、载体裁决：直接生效（时间窗），不进 FrozenStrategySnapshot

1. **定性**：`StageLeadTimeParam` 属 **APS 规则参数体系（治理配置参数）**，是「阶段标准时间」主数据，由生产/工艺/计划部门提供、随工艺改进**按时间窗逐步切换**——不是 per-run 的优化策略 ⇒ **不进 FrozenStrategySnapshot**（后者承载 Solver 策略等 per-run 冻结项，见 `project_p1_02_solver_strategy_freeze`）。
2. **生效管理载体**：依托实体自带 `EffectiveFrom / EffectiveTo + IsActive + IsDefault`，按时间窗生效；`IsDefault` 作全局阶段兜底（最低优先级）。
3. **完整性约束（与 PM §十 一致）**：完整时间 = 上游制造完成 ＋ 跨厂运输 LT（`CrossFactoryEdge.TransportLeadTimeHours`，独立通道）＋ 下游 StageLeadTime，**三者不合并**。你方回执 §1.5 已自查「未合并」⇒ 合规，维持。
4. **消费时机**：你方装载层直接读表，**表内有有效行即生效**，无需等「策略包发布」或「快照冻结」动作。

---

## 三、维护路径（通报，后续另函，不在本件落码）

- **REST CRUD 契约与本件解耦**，另行补齐（红线#5 接口即契约：先出契约件再落码）。
- 复用现有 3号位 规则参数治理 API 模式：`aps.rule.*` 权限 ＋ OPM / ParameterSet 既有口径（`PUT` 整对象替换、`publish` 独立不级联）。
- 补齐后 4号位 可接「页面维护」（参数模型 / 页面维护 / 参数版本 / 生效管理）。

---

## 四、我侧本轮零改动清单（边界明示）

| 项 | 结论 |
|---|---|
| 代码 | 零改动（实体已合规） |
| DDL | 零改动（`StageLeadTimeParam` 表归 2号位 冻结，遵守红线#6） |
| 契约 | 本轮只认领＋定载体，维护 CRUD 契约另函 |

---

## 五、遗留 / 待办

| # | 事项 | Owner | 状态 |
|---|---|---|---|
| 1 | 跨 Stage Dependency 去掉「两端部门一致」谓词 | 1号位 | 你方回执 §二 已通知其**可落码**（PM §五） |
| 2 | `NextRequiredStageCode` 取值来源偏窄（应挂 BOM 边阈值 `BomEdge.ChildRequiredStageCode`，而非仅 `LogicalProductionDemand`） | 待 PM 定语义 | 你方回执 §三 已如实报告，3号位 不代裁决 |
| 3 | StageLeadTimeParam 维护 CRUD 契约 ＋ 落码 | 3号位 | 后续按 OPM/ParameterSet 模式单开一批；出来后再与 2号位 对齐装载时机/字段名 |

---

**发件**：3号位（Application/RBAC/Scope/契约治理 Owner）｜ **日期**：2026-09-28 ｜ **版本**：v1.0
**本件性质**：认领 Owner ＋ 定「直接生效（时间窗）」载体 ＋ 维护路径通报；零代码零 DDL；下一步维护 CRUD 另函。