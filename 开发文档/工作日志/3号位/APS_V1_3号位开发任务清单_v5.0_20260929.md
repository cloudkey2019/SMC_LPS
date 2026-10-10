# APS V1 3号位开发任务清单 v5.0（2026-09-29 · 收录 P1-03 2号位 裁定）

**版本**：v5.0
**日期**：2026-09-29
**适用对象**：3号位（及其开发 AI）——仅列 3号位 亲自执行/负责 的任务。
**性质**：在 v4.0（2026-09-23 裁决后更新）基础上，收录《APS_V1_P1-03失败恢复接线_2号位回执_v1.0_20260929(4).md》（v1.3）三裁定 + 4 项附带发现，重组任务清单。

---

## 〇、本轮更新要点（2026-09-29）

1. **P1-03 解除阻塞**：2号位 三裁定已给出权威取值（回执 §一～§四），2号位 侧动作 1~4 已完成（编译 0 错误 / Unit 363/363 / 真跑 Run 819 通过 5m23s），P1-03 转「**3号位 可接线**」。
2. **新增 3号位 待接线/定夺 4 项**（动作 5~8，见 §一）。
3. **新增 P2 缺陷 1 项**：插单 `CreateCandidateRunAsync` 第10步无 try/catch → 插单 Run 永久卡 `RUNNING`（回执 v1.3 §5.4），两条补救通道均够不着；✅ 已定夺方案①（try/catch→FailAsync）并落码（见 §二）。

---

## 一、P1-03 恢复接线（2号位 三裁定 · 可开工）

| # | 裁定点 | 2号位 权威裁定 |
|---|---|---|
| 1 | 恢复收口 seam | 不新增、不扩 `RunSchedulingAndFinalizeAsync`；抽 `RunSchedulingAutoAsync` 既有「按 Run 多域 + 一次性收口」主体为 `ExecuteRunAsync(scheduleRunId, ct)`，夜间发令枪与恢复共用（**2号位 已完成**）。恢复侧 3号位 建好壳后直调 `ExecuteRunAsync(newRunId)` |
| 2 | 恢复 PlanVersion 壳 | **3号位 建**，与 Run 同事务原子写；⚠️ 关键修正：**建 N 个壳（一域一壳），非 1 个** |
| 3 | 恢复壳词表 | `VersionCategory='RECOVERY'`（新取值，已核全仓 7 处消费点全为等值判定、零 DDL）；`Status` 初值 `'Created'`（3号位 写）、终值 `Computed`/`ComputeFailed`（2号位 执行侧写，3号位 不写） |

**壳字段权威取值**（回执 §3.3）：`VersionCode=RECOVERY_{yyyyMMddHHmmss}_{DomainKey}`、`DomainKey`=ExpectedDomainKeysJson 每 key、`Status='Created'`、`ComputeMode='FULL'`、`SourceScheduleRunId=新 Run Id`、`PlanHorizonStart/End`=继承失败 Run 既有 PlanVersion 窗口（缺失兜底今天~+90天）、`CreatedBy`=操作人/`'Recover'`、`BatchNo=NULL`。**建壳须幂等**（执行侧 `TOP 1 ORDER BY Id DESC` 认领，多余壳永久停 Created）。

### 3号位 待接线/定夺（动作 5~8）

| # | 任务 | 状态 |
|---|---|---|
| **P1-03-5** | 建 N 个恢复壳（一域一壳）+ Run 同事务原子写 + `ExecuteRunAsync(newRunId)` | ✅ 已落（`InsertForRecoveryWithShellsAsync`） |
| **P1-03-6** | 拒绝非 FULL_SCHEDULE 恢复（回执 §5.1 fail-closed） | ✅ 已落（`RecoverFailedRunAsync` 显式拒绝非 FULL） |
| **P1-03-7** | `DataCutoffTime` 语义 = `GETDATE()` 最新数据重试（回执 §5.2） | ✅ 已定（代码内注释） |
| **P1-03-8** | 超时预算：内联 `ExecuteRunAsync`（与候选路径一致，N×5.4min 已备注） | ✅ 已定（内联执行） |

> 落地顺序建议：先定 6/7/8 → 按 5 接线 → 联调。

---

## 二、插单 RUNNING 孤儿（回执 §5.4 · P2 · ✅ 方案① 已落码）

`RunLifecycleService.CreateCandidateRunAsync` 第10步 `_schedulingOrchestrator.RunSchedulingAndFinalizeAsync(...)` **无 try/catch**；同步耗时（单域真跑 5m23s）期间应用池回收/发版/OOM/客户端断连 → Run 永久卡 `RUNNING`、壳 `Created`。**两条补救通道均够不着**（发令枪领取 SQL 写死 `RunType='FULL_SCHEDULE'`；恢复端点只收 `FAILED`、拒 `RUNNING`）。

**已定夺并落码方案①（最小）**（`RunLifecycleService.cs:572-584`）：第10步包 `try/catch`，异常 `LogError` + `FailAsync(runId, 0, ex.Message, CancellationToken.None)` → Run 落 `FAILED` 即被恢复通道接住（正是 P1-03 恢复功能存在的理由，覆盖面从夜间扩到插单）。

**放弃方案②**（候选端点改「排队模式」+ 巡检兜超龄 `RUNNING`）——留作「插单孤儿实际发生且属硬杀型」时的 follow-up，本次不上。3号位 已回执 2号位。

---

## 三、v4.0 遗留（2026-09-29 复核：6 项已收口，零开放）

### 已收口
| # | 任务 | 复核结果 |
|---|---|---|
| T1 | S2/S3 OPM 治理 API | ✅ 已交付（09-23 落码 + 单测 12/12，契约致 4号位） |
| T2 | 05 §7.0 RunScope 消费语义注释 | ✅ 已落稿（09-24 落稿回函 + 1号位 回执） |
| T5 | 旧件收口《EXPEDITE强度裁决知会 v1.0》 | ✅ 已收口（09-24 收口确认） |
| C-2 | R3 fork 端点联调测试 | ✅ 已联调完成（09-24 联调完成回执） |
| C-1 | S5 缺口清单 | ✅ 收口（本轮查清：被 R4'~R9 收口覆盖，无需独立成文） |
| T6 | OPM 三值数据维护 | ✅ 收口（本期了结：数据库已赋默认值；真实三值维护留待业务拍板后续触发） |

### 仍开放
无（v4.0 六项已全部收口）

---

## 四、红线与不越位

- **红线 #5**：接口即契约——本次 2号位 新增 `ISchedulingOrchestrator.ExecuteRunAsync`，既有端点零改动。
- **红线 #6**：DB 结构变更 2号位 专属——本项 `VersionCategory='RECOVERY'` 为无约束 NVARCHAR 新取值，**零 DDL**。
- **不越位**：投影/排程落库归 2号位、消费壳实现归 1号位；3号位 只做治理侧（建壳、运行治理口径、维护入口、知会）。
- **字段三来源原则（裁决七）**：治理配置类字段勿登记 ODS 透列/MERGE。

---

## 五、一句话交付

> 3号位 待办：P1-03 动作 5~8（已落码 ✅）、插单 RUNNING 孤儿补救（✅ 方案① 已落码）；v4.0 遗留六项（T1/T2/T5/T6/C-1/C-2）已于 09-23~09-29 全部收口；全程不越位。

---

**发送人**：3号位 ｜ **日期**：2026-09-29 ｜ **依据**：2号位《P1-03 失败恢复接线回执 v1.3 20260929(4)》