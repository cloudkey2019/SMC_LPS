# 回执：剩余 3 失败逐一定性 —— 2号位 致 1号位

> 收件：1号位／抄送：0号位
> 依据：1号位《回归现状汇总 v1.0 20260921》
> 日期：2026-09-21

---

## 一、v5.1.2 完整排程 FinalTasks=0 —— 根因 = M101 配品首工序缺资格（非 2号位 代码）

根因已坐实（P 段唯一剩余卡点）：成品首工序「配品」M101（`EquipNo=NULL`、`IfhasEquips=0`）**无设备天然零资格**，1号位 `PhaseTwoInitialScheduler` 任一工序零合格资源即 `return empty` → FinalTasks=0。`ScheduleExplanationFact INSERT 8 条` 正是 8 个需求全判 Unscheduled 的兜底登记。

**非 2号位 代码问题**。PM 0921 已裁决「无设备工序三分类」（OperationPlanningMode：FINITE_RESOURCE / UNCONSTRAINED / WAIT_ONLY），但落地需：3号位 出 OperationPlanningMode 实体 + 业务确认配品分类 + **1号位 扩展 Solver 支持「无资源有工时」**。落地前 v5.1.2 会一直 0 产出，属预期内，不阻塞。

## 二、独立接货 IntakePerformed=False —— 测试数据问题（无 READY 批次）

测试库无 READY 状态的 BOM 批次（`MES_API_BOM_Request` 里 READY 批次已被前几次接货标 CONSUMED）。该测试依赖 ODS 侧存在 READY 批次，**非每日可跑**。

处置建议：该测试标记为「依赖真实批次，按需跑」；若要每日回归，需每次预置 READY 批次（sqlcmd 把某批 CONSUMED→READY），或改测「接货服务自身逻辑」不依赖批次状态。

## 三、双跑对照·保护阈值10单 耗时 20 分钟 —— 旧 DFS 指数爆炸（已知，主链已切 BFS）

根因 = 旧 DFS（`RunDfsLoop`/`TraverseBomNode`）路径级 visited 无记忆化，真实 BOM（3.6M 边、51% 共享子件、单装配最大出度 72K）下共享子件指数重遍历，10 单 20 分钟不收敛（台账 09-18 已记：SQL sleeping + testhost 烧 CPU/2.68GB 工作集）。

**主链已切新 BFS**（`RunBfsLoop`，11,659 单 4.5s，2026-09-18 用户裁决「弃旧计算」直接切，非等双跑）。双跑对照的旧 DFS 侧仅作新旧一致性**辅助验证**（非切主链门槛），慢是预期的。建议：该测试标记「已知慢、非每日跑」或跳过旧 DFS 侧（对照已出结论，见台账「BOM爆炸根因实证」）。

---

## 回执表

| 项 | 2号位 定性 |
|---|---|
| v5.1.2 0 产出 | M101 配品缺资格，等 PM 无设备三分类落地，非 2号位 |
| 独立接货 | 测试数据（无 READY 批次），非每日可跑 |
| 双跑对照慢 | 旧 DFS 指数爆炸，主链已切 BFS，慢是预期 |

**发送人**：2号位 ｜ **日期**：2026-09-21