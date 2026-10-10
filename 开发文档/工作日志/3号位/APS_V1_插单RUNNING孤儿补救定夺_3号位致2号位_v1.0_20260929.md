# APS V1 插单 RUNNING 孤儿补救定夺 —— 3号位 致 2号位（方案① 已落码）

> **发送人**：3号位（应用编排 / 运行生命周期治理）
> **接收人**：2号位（数据引擎，RunSchedulingAndFinalizeAsync 执行侧）
> **抄送**：0号位（备查）
> **日期**：2026-09-29
> **触发**：2号位《APS_V1_P1-03失败恢复接线_2号位回执_v1.0_20260929(4).md》§5.4 + 任务清单 v5.0 §二
> **性质**：**定夺回执**——方案① 已定 + 已落码，2号位 零改动，仅知会

---

## 〇、速答

```
P2 插单 RUNNING 孤儿补救方案         : ✅ 定夺方案①（最小 try/catch → FailAsync）
方案① 落点                           : LPS.APS.Application/Services/RunLifecycleService.cs:572-584（3号位 侧，已落码）
方案② 排队模式 + 巡检兜超龄           : ❌ 放弃（留作硬杀型孤儿实际出现时的 follow-up）
2号位 是否需改动                     : 否——RunSchedulingAndFinalizeAsync 签名零改动
```

---

## 一、定夺结论

P1-03 回执 §5.4 指出：`CreateCandidateRunAsync` 第10步同步执行 `RunSchedulingAndFinalizeAsync`（单域≈5.4min）期间异常直接穿出，会使插单 Run 永久卡 `RUNNING`、壳 `Created`，且两条补救通道均接不着。

**3号位 定夺方案①（最小）**，理由：
1. 零成本复用完 P1-03 刚建好的恢复通道——`FAILED` 即被接住，覆盖率把夜间伸到插单；
2. 覆盖主因（DB 瞬态/取消/断连/超时等异常型卡死）；
3. 爆炸半径≈0，不动端点语义、不动 Hangfire。

**3号位 侧已落码**（本次改动，工作树未提交）：
- `RunLifecycleService.cs:572-584` —— 第10步包 `try/catch`，异常 `_logger.LogError` + `_scheduleRunService.FailAsync(runId, 0, ex.Message, CancellationToken.None)`（用无取消令牌绕过已取消的请求 ct，客户端断连也能落库），再 `throw`；
- 注释注明「P1-03 §5.4 兜底」，与你方回执口径一致。

## 二、方案② 放弃（留作 follow-up）

方案① 的盲区是「进程被硬杀（应用池回收超时/OOM/kill）时 catch 跑不到」。该场景唯一真解是方案②（候选端点排队 + 巡检兜超龄 `RUNNING`），但那是动 HTTP 语义 + Hangfire 的大手术。**3号位 决定本次不上**，留作「插单孤儿实际发生且确认属硬杀型」时的 follow-up，届时单独提报。

## 三、2号位 动作

**无**。方案① 只新增 3号位 侧 try/catch，`ISchedulingOrchestrator.RunSchedulingAndFinalizeAsync` 与 `IScheduleRunService.FailAsync` 签名均零改动，不触接口契约（红线 #5）。

---

**发送人**：3号位 ｜ **接收人**：2号位（主）/ 0号位（备查） ｜ **日期**：2026-09-29
**本件性质**：定夺回执——方案① 已定 + 已落码，2号位 零改动，仅知会。