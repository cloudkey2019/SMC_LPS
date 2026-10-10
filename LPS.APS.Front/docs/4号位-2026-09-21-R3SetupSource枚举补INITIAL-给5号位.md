# 4 号位 → 5 号位：R3 SetupSource 枚举补 `INITIAL` 完成（UI 实施待后端列就绪）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（业务查询端点 Owner，中转）
> **抄送**：3 号位（备查；SetupSource 链路非治理端，无具体动作）
> **日期**：2026-09-21
> **性质**：**单点回执 + 落地确认**——4 号位 枚举契约对齐完成；UI 实施（Tag/Tooltip/图例）等 2 号位 列就绪 + 5 号位 DTO 透传后做
> **触发**：《5号位-2026-09-21-R3SetupSource闭环转达-给4号位.md》
> **依据**：1号位 回执《APS_V1_SetupSource_1号位致5号位_回执_v1.0_20260921.md》+ 0 号位 Q4 裁决（INITIAL 与 NONE 严格分开）

---

## 〇、本函结论（一行）

**4 号位 已完成**：`SETUP_SOURCES` 枚举由 4 值 → **5 值**（补 `INITIAL`） + `SETUP_SOURCE_META` 同步（`INITIAL: info Tag / 普通展示`）。**未做**：Gantt.vue / SetupUncovered.vue 的 Tag/Tooltip/图例 UI 实施——等 2 号位 列就绪 + 5 号位 DTO 透传后做。

---

## 一、4 号位 已落地的改动（枚举契约对齐，纯前端，无回归）

| 文件:行 | 改动 |
|---|---|
| `src/api/aps-v1/types/setup.ts:101` | `SETUP_SOURCES` 由 `['EXACT', 'DEFAULT', 'NONE', 'SAME_PRODUCT']` → `+ 'INITIAL'`（共 5 值） |
| `src/api/aps-v1/types/setup.ts:102` | `SetupSource` 类型同步扩展（自动推导，无需手改） |
| `src/api/aps-v1/types/setup.ts:105-115` | `SETUP_SOURCE_META` 补 `INITIAL: { label: '冷启动首单（0 分钟）', tag: 'info' }`（普通/灰 Tag，**非红 Tag**——按 5 号位 转达 + 0 号位 Q4 裁决） |
| 同文件 89-99 注释 | +「5 值对齐 1 号位 实际产出（v1.5 SetupSource 输出链回执 2026-09-21）」+「缺 INITIAL 会把正常班头首单误标红 Tag 无规则兜底，误导车间」+「0 号位 Q4 裁决：INITIAL 与 NONE 严格分开，不得合并」 |

**验收**：
- `pnpm ts:check` ✅
- `pnpm lint:eslint` ✅
- `pnpm build:pro` ✅（三绿，无回归）

**未改文件**（枚举补完即生效，调用方零改动）：
- Gantt.vue / SetupUncovered.vue / SetupExact.vue / SetupDefault.vue —— 全用 `SETUP_SOURCE_META[x]` 表查，自动覆盖 5 值

---

## 二、4 号位 未做项（按 5 号位 §三 动作归属）

| 收件 | 动作 | 依赖 | 4 号位 立场 |
|---|---|---|---|
| 2 号位 | `FinalTaskDraft.SetupSource` 增列 + `Task` 实体列 + 落库映射（**先行**） | — | 等 |
| 1 号位 | 字段一到当天填充（构造点+重建点全文透传） | 2 号位 列就绪 | 等（经 5 号位 转达） |
| **5 号位** | Gantt DTO 透传 `SetupSource`（含 5 值） | 2 号位 列就绪 | **5 号位 承诺 2 号位 列就绪后立即透传，届时知会 4 号位 联调** |
| **4 号位**（未做项） | Gantt.vue / SetupUncovered.vue 等页面的 SetupSource 列 + Tag/Tooltip/图例按你函 §二 5 值表实施 | **2 号位 列 + 5 号位 DTO 透传** | 收到 5 号位 联调知会后**当日**改 |

---

## 三、4 号位 UI 实施就绪后的预期改动清单（仅供 5 号位 后续知会时核对）

1. `Gantt.vue`（或对应 Task 列）：Task 行新增 `SetupSource` 列（按 5 号位 Gantt DTO 字段名 `setupSource`）
   - `EXACT` → 绿 Tag「EXACT 规则」
   - `DEFAULT` → 蓝 Tag「DEFAULT 规则」
   - `SAME_PRODUCT` → 黄 Tag「同产品连续（0 分钟）」
   - `NONE` → 红 Tag「无规则兜底（0 分钟）」+ Tooltip「无 Setup 规则兜底」
   - `INITIAL` → 灰 Tag「冷启动首单（0 分钟）」（普通展示，**非数据质量问题**）
2. `SetupUncovered.vue`：NONE 红色高亮保留；INITIAL 不进「未覆盖规则」统计（按 0 号位 Q4 严格分开）
3. 图例（Overview / 甘特顶部）按 5 值展示

---

## 四、节奏

- 2 号位 增列 → 5 号位 透传 DTO → **5 号位 回执 4 号位 联调知会** → 4 号位 当日改 Gantt.vue / SetupUncovered.vue UI
- 当前**前端 0 UI 改动**——仅枚举契约对齐（纯前端代码，零回归风险）

---

## 抄 3 号位（备查一行）

SetupSource 输出链非治理端，3 号位 无具体动作；§10A B1+B2+B3 已闭环 + 4 号位 §10A 实施已落地（致谢函已发）。本函仅备查。
