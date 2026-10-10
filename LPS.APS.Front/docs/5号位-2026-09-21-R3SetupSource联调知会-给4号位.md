# 5号位 → 4号位 — R3 SetupSource 联调知会：DTO 透传就位，值契约已统一（2026-09-21）

**发送人**：5号位（业务查询端点 Owner）
**接收人**：4号位
**日期**：2026-09-21
**触发**：SetupSource 链路 Gantt DTO 透传完成 + 落库值契约三方（1/2/4号位）统一，联调前提就绪
**关联**：4号位《R3SetupSource枚举补INITIAL》/《R1字段结构契约收到》 / 2号位 增列回执 / 1号位 回执

---

## 一、SetupSource 链路已通，联调前提就绪

| 环节 | 状态 |
|---|---|
| `[Task].[SetupSource]` 列 | ✅ 2号位 已落库（`NVARCHAR(50) NULL`） |
| `FinalTaskDraft.SetupSource` + Task 实体列 | ✅ 2号位 已增 |
| **Gantt DTO 透传** | ✅ 5号位 已完成：`GanttTaskDto.SetupSource`（JSON `setupSource`，nullable string），`dotnet build` 0 错 |
| **落库值契约** | ✅ **大写 5 值统一**（1/2/4号位 三方确认）：`EXACT / DEFAULT / SAME_PRODUCT / NONE / INITIAL` |
| 1号位 数据填充 | ⏳ 1号位 将按大写 5 值填充（`SetupOutcome → 大写字符串`） |

## 二、值契约（大写 5 值，最终）

| `setupSource` 值 | 语义 | 前端展示 |
|---|---|---|
| `EXACT` | 精确命中规则 | 绿 Tag「EXACT 规则」 |
| `DEFAULT` | 默认规则兜底 | 蓝 Tag「DEFAULT 规则」 |
| `SAME_PRODUCT` | 同产品相邻 0 分钟 | 黄 Tag「同产品连续（0 分钟）」 |
| `NONE` | 有前后产品但无规则 → 0 分钟兜底 | 红 Tag「无规则兜底（0 分钟）」+ Tooltip |
| `INITIAL` | 班头首单/冷启动 → 0 分钟 | 灰 Tag「冷启动首单（0 分钟）」**非数据质量问题** |

## 三、4号位 可启动 UI（按你 §三 清单，契约已锁）

- `Gantt.vue`：Task 行加 `SetupSource` 列，按上表 5 值 Tag/Tooltip
- `SetupUncovered.vue`：`NONE` 红高亮；`INITIAL` **不进**「未覆盖规则」统计（0号位 Q4 严格分开）
- 图例（Overview / 甘特顶部）按 5 值展示

> 数据值待 1号位 填充后到流；但你 UI 代码现在即可按此契约编写，值与列均已锁，不返工。

## 四、数据填充节奏

- **3号位/1号位**：1号位 按大写 5 值填充 `SetupSource` → 数据到 `[Task].[SetupSource]`
- 5号位 Gantt 查询已透传；数据非空后 4号位 即可联调验证 Tag 渲染

---

**发送人**：5号位
**日期**：2026-09-21