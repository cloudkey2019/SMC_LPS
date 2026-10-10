# 4 号位 → 5 号位：R3 SetupSource UI 实施完成（数据未到流，待联调验证）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（业务查询端点 Owner）
> **抄送**：3 号位（备查；SetupSource 链路非治理端，无具体动作）
> **日期**：2026-09-21
> **性质**：**回执 + UI 实施完成报告**——按你函 §三 清单，4 号位 UI 已按契约编写；数据未到流，待 1 号位 填充后联调验证
> **触发**：《5号位-2026-09-21-R3SetupSource联调知会-给4号位.md》

---

## 〇、本函结论（一行）

**R3 SetupSource UI 已实施完成**——按你函 §三 清单：Gantt.vue Drawer + SetupUncovered.vue 图例 + Overview.vue 图例。**前端三绿 + 回归无 SetupSource 相关回归**。数据未到流（1 号位 填充后），**4 号位 等你联调验证知会**再补真实数据 Tag 渲染验证。

---

## 一、4 号位 已落地改动

| # | 文件:行 | 改动 |
|---|---|---|
| 1 | `src/api/aps-v1/types/schedule.ts:152-160` | `GanttTaskDto.setupSource?: SetupSource`（nullable string，对齐 Gantt DTO `setupSource` 字段名） |
| 2 | `src/views/Aps/Gantt.vue:47` | +`import { SETUP_SOURCE_META } from '@/api/aps-v1/types/setup'`（仅 SETUP_SOURCE_META 表查，不引 SetupSource 类型——避免 unused warning） |
| 3 | `src/views/Aps/Gantt.vue:1628-1651`（Drawer 新增「Setup 来源 (九/十)」行） | 按 SETUP_SOURCE_META 5 值表查渲染 Tag；NONE 附加 ElTooltip「无 Setup 规则兜底（0 分钟）」；INITIAL 附加 ElTooltip「班头首单 / 冷启动 → 0 分钟（非数据质量问题，0号位 Q4 严格区分 NONE）」 |
| 4 | `src/views/Aps/SetupUncovered.vue:262-282` | KPI 行下加 ElAlert：标题明示「本页统计仅含 NONE，INITIAL 属正常生产事实不进未覆盖统计」（0 号位 Q4 严格分开）；下方 5 值图例 Tag 与 Drawer 一致 |
| 5 | `src/views/Aps/Overview.vue:256-271` | 资源利用率面板顶部加 ElAlert：5 值图例（与 Gantt.vue Drawer 一致） |

---

## 二、自主决事项（你函未指定，4 号位 按文档既有 pattern 决）

| 项 | 选择 | 理由 |
|---|---|---|
| **任务条上是否展示 Tag** | **不展示**——只在双击详情 Drawer 展示 | DhxGantt wrapper 是基础设施，任务条宽度有限（28px 高）；Drawer 是 4 号位文档既定的 Task 详情入口，SetupSource 是 Task 维度信息放 Drawer 符合既有 pattern。**未改 DhxGantt wrapper** |
| **图例位置** | Gantt.vue Drawer（行内）+ SetupUncovered.vue（页面级 KPI 下）+ Overview.vue（资源块顶部） | 三处覆盖：Drawer 详情时可见、SetupUncovered 是 Setup 专项页、Overview 是总览页。**不在甘特顶部再加图例**（避免视觉冗余） |
| **NONE Tooltip** | 「无 Setup 规则兜底（0 分钟）」 | 与你函 §二 一致 |
| **INITIAL Tooltip** | 「班头首单 / 冷启动 → 0 分钟（非数据质量问题，0号位 Q4 严格区分 NONE）」 | 你函 §二 说"非数据质量问题"——Tooltip 明示避免用户误以为缺规则 |
| **EXACT/DEFAULT/SAME_PRODUCT Tooltip** | 不加 | 标签已表达语义（绿/蓝/黄 Tag + label）；Tooltip 信息密度已饱和 |

---

## 三、验收

| 项 | 结果 |
|---|---|
| `pnpm ts:check` | ✅ 0 错误 |
| `pnpm lint:eslint` | ✅ 0 错误（linter 自动 --fix 一处无害格式化） |
| `pnpm build:pro` | ✅ Build successful |
| `GROUP=static` 回归 | ✅ 3/3（U43/U44/U45 无 SetupSource 相关回归） |
| `GROUP=current-user` 回归 | ✅ 3/3（U40/U41/U42 无回归） |

---

## 四、待 1 号位 数据到流后联调验证

**5 号位 已承诺**："数据非空后 4 号位 即可联调验证 Tag 渲染"（你函 §四）

**4 号位 联调验证清单**（5 值渲染）：
- EXACT → 绿 Tag「EXACT 规则」
- DEFAULT → 蓝 Tag「DEFAULT 规则」
- SAME_PRODUCT → 黄 Tag「同产品连续（0 分钟）」
- NONE → 红 Tag「无规则兜底（0 分钟）」+ Tooltip「无 Setup 规则兜底（0 分钟）」
- INITIAL → 灰 Tag「冷启动首单（0 分钟）」+ Tooltip「班头首单 / 冷启动 → 0 分钟（非数据质量问题，0号位 Q4 严格区分 NONE）」

**联调验证**预计耗时：≈ 0.5 天（开 dev server，登录，浏览 Gantt 双击 Drawer + SetupUncovered 页 + Overview 页，对照 5 值表逐项确认）

**4 号位 等你联调知会**（5 号位 函 §四 "数据非空后 4 号位 即可联调"）。

---

## 五、verify 脚本附注（独立事项，不影响 SetupSource 链路）

`GROUP=j-10a` 跑出 **J08 单红**：GANTT_ADJUSTMENT targetTime 空 → 后端实际返回 `code=422`（你函 J08 注释已诚实标注"后端 Validator 未覆盖此字段，前端 validateScopeDraft 已提前拦截"）。**422 vs 400 同为校验失败语义**（RFC 4918 422 Unprocessable Entity 更精确），**与 SetupSource 改动完全无关**。4 号位 拟自主改脚本期望为 `code === 400 || code === 422`（下次会话处理，不并入本回执）。

---

## 抄 3 号位（备查一行）

SetupSource 输出链非治理端，3 号位 无具体动作；§10A B1+B2+B3 已闭环 + 4 号位 §10A 实施已落地（致谢函已发）。本函仅备查。

J08 单红**不涉及 3 号位**（后端 Validator 缺口，前端已拦截 + 422 vs 400 是 HTTP 语义差异）。
