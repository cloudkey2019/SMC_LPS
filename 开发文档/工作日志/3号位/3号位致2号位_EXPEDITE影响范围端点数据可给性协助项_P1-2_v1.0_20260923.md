# 3号位 致 2号位 — EXPEDITE 影响范围端点数据可给性协助项（P1-2，D1/D2）

> **发送人**：3号位（Candidate / RunLifecycle 查询 Owner）
> **接收人**：2号位（Data / 排程结果持久化 Owner）
> **抄送**：4号位（备查，前端消费方）
> **日期**：2026-09-23
> **性质**：**协助项提报** —— 随《3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9》带去；非裁决函，一条回执（可/不可派生 + 需否投影）即可闭合
> **关联**：0号位 Q4-B §六 影响范围输出示例；P0-1(a) 前端「EXPEDITE 影响范围」UI

---

## 〇、速览

```
背景   P0-1(a) 拍板 = 新增端点 GET /api/candidate/{id}/expedite-impact 供前端渲染影响范围
端点   契约归 3号位（已起草 v0.9），数据可给性归 2号位（本函 2 项）
请求   D1 重排前后日期可给出性 + D2 Reason 归因可区分性；确认后契约定版
```

---

## 一、背景

P0-1(a) 已由 3号位 拍板：Candidate 页新增「EXPEDITE 影响范围」展示（0号位 Q4-B §六 已裁「V1 输出影响范围供业务确认」）。

3号位 已起草端点契约草案（[3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9](3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9_20260923.md)）。契约字段能否从**排程结果**可靠派生 → **2号位 数据能力**，故提 2 项协助项，确认后契约定版、再由 2号位 落代码实现。

---

## 二、协助项

**端点**（草案 §一）：

```
GET /api/candidate/{candidateId}/expedite-impact
```

**契约字段**（草案 §二）：

```jsonc
{
  "expeditedOrder": { "orderCanonicalId": "10001", "originalDate": "...", "newDate": "..." },
  "affectedOrders": [ { "orderCanonicalId": "10002", "originalDate": "...", "newDate": "...", "reason": "EXPEDITE_RESOURCE_CONTENTION" } ],
  "protectedOrders": [ { "orderCanonicalId": "10004", "originalDate": "...", "newDate": "...", "reason": "PROTECTED_BOUNDARY" } ],
  "summary": { "affectedCount": 2, "protectedCount": 1, "totalDelayDays": 4 }
}
```

请 2号位 确认以下 2 点：

| # | 数据可给性 | 说明 |
|---|---|---|
| **D1** | `originalDate / newDate`（重排前→重排后计划日期）能否从排程产物可靠取到 | 需对比「重排前计划」与「重排后计划」；是否已有 Base PlanVersion / 快照可回溯原日期 |
| **D2** | 能否在结果层面区分「被 EXPEDITE 推移」与「正常排程移位」 | 决定 `reason` 列（`EXPEDITE_RESOURCE_CONTENTION` vs `PROTECTED_BOUNDARY`）是否可信；若不可区分，降级为「仅列受影响单，不判归因」 |

---

## 三、回执方式

- 一条回执即可闭合：告知 **D1/D2 可给 / 不可给 + 需否投影承载**。
  - D1/D2 可给 → 3号位 定版契约，2号位 落 `/expedite-impact` 实现，返 4号位 对接。
  - D1/D2 不可给 / 需投影 → 3号位 调整契约（降级字段 or 另定投影契约），再返实现。

> 本项为协助项，**非请求 2号位 裁决**业务（业务已由 0号位 Q4-B 冻结）；仅确认 2号位 排程产物能否支撑派生。

---

## 四、能力边界（3号位 不越界）

- ❌ 3号位 **不修改** `lps/**` 排程/持久化层代码（`PeggingOrchestrator` 等 2号位 职责）
- ❌ 3号位 **不发包** dev / test 环境
- ✅ 3号位 提供：端点契约草案（v0.9）+ D1/D2 数据可给性确认点
- ✅ 2号位 回执后可：契约定版 + 前端 DTO 类型契约提交 4号位

---

## 五、附档

- 契约草案：[3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9_20260923.md](3号位致4号位_EXPEDITE影响范围端点契约草案_P1-2_v0.9_20260923.md)
- 回执拍板：[3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md](3号位致4号位_EXPEDITE影响回执拍板_v1.0_20260923.md)
- 0号位 EXPEDITE 裁决：[Q4-B：PriorityMode=EXPEDITE强度定义裁决.md](Q4-B：PriorityMode=EXPEDITE强度定义裁决.md)

---

**发送人**：3号位 ｜ **接收人**：2号位（主）｜ **抄送**：4号位（备查）｜ **日期**：2026-09-23
**待办**：① 2号位 回执 D1/D2 数据可给性 ② 3号位 定版契约 ③ 2号位 落实现 ④ 4号位 对接渲染