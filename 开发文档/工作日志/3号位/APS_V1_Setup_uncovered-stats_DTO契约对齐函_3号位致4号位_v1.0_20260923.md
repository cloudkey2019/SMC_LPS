# APS V1 Setup —— #10 uncovered-stats 端点将实现——DTO 契约对齐函

**文档性质**：跨号位契约对齐（3号位 → 4号位）——确认实现 #10 uncovered-stats 后端端点前，需对齐返回值 `SetupUncoveredStatDto` 精确字段，避免返工。
**发送方**：3号位 ｜ **接收方**：4号位（前端）｜ **日期**：2026-09-23
**依据**：4号位 F-1 第三轮回执 `4号位-2026-09-23-F-1第三轮回执-uncovered-stats真实情况-给3号位(1).md` 选项 (d)；3号位 已裁决「Setup 治理域实现 + 对齐前端 DTO + 四级下钻全部实现」
**抄送**：0号位（备查）

---

## 一、背景

4号位 第三轮回执确认：前端 `types/setup.ts` **已有** `SetupUncoveredStatDto` 定义（随 mock 数据长期运行），但后端 `SetupRuleController.cs` **从未实现**该端点。3号位 将补齐后端实现（路由 `GET /api/governance/setup-rules/uncovered-stats`），数据源 = 已落盘 `[Task].SetupSource='SETUP_RULE_MISSING_ZERO_FALLBACK'` 的命中频次聚合（0 分钟规则缺失兜底场景）。

## 二、请 4号位 提供前端 DTO 精确定义

请回复以下任一形式（最省事：直接贴出 `types/setup.ts` 中 `SetupUncoveredStatDto` 的 TypeScript 定义原文）：

```
export interface SetupUncoveredStatDto {
  // 请贴出完整字段定义（字段名 / 类型 / 注释）
}
```

## 三、后端初拟字段（供对照，若前端一致即可直接采用；不一致请标注差异）

| 字段 | 类型 | 说明 |
|---|---|---|
| `scheduleRunId` | number | 排程 Run Id（COMPLETED ScheduleRun）|
| `departmentId` | number | 生产部门主键 |
| `departmentCode` | string | 部门编码（DeptCode 回带）|
| `operationCode` | string | 工序编码 |
| `resourceId` | number | 资源主键 |
| `resourceCode` | string | 资源编码（ResourceCode 回带）|
| `materialId` | number | 物料主键 |
| `materialCode` | string | 物料编码（MaterialCode 回带）|
| `hitCount` | number | 该维度命中频次（Task 计数）|
| `sampleTaskId` | number\|null | 首条命中 Task Id（下钻定位用）|

四级下钻语义：**runId → 部门 → 工序 → 设备 → 产品**，每级按当前过滤维度返回聚合行。

## 四、回执栏（4号位 填写）

| 项 | 确认 |
|---|---|
| 前端 `SetupUncoveredStatDto` 精确字段定义（或确认 §三 初拟一致） | ☐ |
| 四级下钻（runId→部门→工序→设备→产品）是否符合前端 SetupUncovered 页交互 | ☐ |
| 是否需要 `fromMaterialCode`/`toMaterialCode`（转换物料细分）？若否，§三 已无此项 | ☐ |
| `scheduleRunId` 是否前端必传（未传默认取最近 COMPLETED Run）| ☐ |
| 备注 | |

---

**发送方**：3号位 ｜ **日期**：2026-09-23
**待办**：4号位 回执 DTO 定义 → 3号位 按契约实现端点 → 联调