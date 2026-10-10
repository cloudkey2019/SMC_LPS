# 4 号位 → 0 号位：《APS_V1_ResourceCalendar资源日历能力补充方案_v1.0》影响分析回执——4 项 P0 阻塞 + 5 项 P1 边界确认

> **发送人**：4 号位（前端）
> **接收人**：0 号位（项目权威 / 文档归属裁决）
> **抄送**：5 号位（资源能力事实 Owner，本函主要承接方）、2 号位（API / DB 实现，备查）、3 号位（治理 / 规则引擎，备查——本方案明确 3 号位不负责 Resource Calendar）
> **日期**：2026-09-22
> **触发**：0 号位 发《APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md》要求各号位通读
> **性质**：**影响分析回执**（4 号位 只读 lps，不改后端；前端代码暂不擅自开工，等 0 号位 Q1-Q4 + 5 号位 / 2 号位 配套答复）
> **关联**：v1.4 §十A.4（资源日历调整后重排 RESOURCE_CALENDAR_CHANGE，L595-672）、v1.5 Setup 换型专项冻结对齐版、本函 §四（实施包版本归属）

---

## 〇、一句话

**总体无冲突**——ResourceType 4 值复用、不入规则引擎、不动 Routing / Setup / OperationPlanningMode，与现有 v1.4 + v1.5 完全一致；4 号位 现有 `ResourceRescheduleDialog.vue`（§10A.4 触发重排）注释需后续修订。**但有 4 项 P0 阻塞（页面范围 / API 契约 / Slot 形式 / 数据源）必须 0 号位 拍板后才能开工**，5 项 P1 边界可随主流程推进一并确认。请 0 号位 在本周内（09-26 前）就 Q1-Q4 给定论，5 号位 / 2 号位 据此配套响应。

---

## 一、总体确认（方案与现有前端无冲突清单）

| # | 方案要点 | 现有 v1.4 / v1.5 / 代码现状 | 是否冲突 |
|---|---|---|---|
| 1 | 设备 / 人工统一 Resource 模型 | 现有 `lps/LPS.APS.Core/Entities/Aps/Task.cs` Resource 主键体系、`src/api/aps-v1/types/schedule.ts:39-154` GanttResourceDto | ✅ 兼容 |
| 2 | ResourceType 4 值 MACHINE / LINE / MANUAL_STATION / HUMAN | 现有 `v1.5 Setup §2 ResourceType 支持 HUMAN` 已扩；lps Resource 表 ResourceType 字段已落库 | ✅ 复用，不变 |
| 3 | 不入规则引擎（不属于 RuleSet） | 现有 v1.4 §十八 5 块参数（Lock / Supply / Procurement / SolverStrategy / CandidateGuardrail）**未含 Resource Calendar**，SolverStrategy 块不动 | ✅ 无需改 Rules.vue |
| 4 | 不动 Routing / OperationPlanningMode / Setup 模型 | 现有 `SetupExactDialog.vue` / `SetupDefaultDialog.vue` / `setupUncovered.ts` 不受影响 | ✅ 无需改 Setup 页 |
| 5 | 不建设人员技能 / HR 接口 / 人工能力矩阵 | 现有前端无相关 UI，符合 | ✅ 无需新增 |
| 6 | Resource Calendar 不属 MES 事实（属 APS 资源能力事实） | 与现有 setupUncovered / setup-mock 自治口径一致 | ✅ 无冲突 |
| 7 | 5 号位 治理 + 业务数据 / 2 号位 DB+API / 3 号位 不负责 | 与 v1.4 §六 职责划分一致 | ✅ 边界一致 |
| 8 | 设备和人工均按"资源什么时候可用"统一表达 | 现有 GanttResourceDto 无 ResourceCalendarSlot 字段；Gantt.vue 未读 Calendar 数据 | ⚠️ **范围新增**（见 §二 Q1 / Q2 / B2 / B3） |

---

## 二、4 项 P0 阻塞（必须 0 号位 拍板，4 号位 才能开工）

### Q1：页面范围——4 号位 新建几个页？

**现状**（4 号位 已 grep 确认）：

- ❌ 无 `src/views/Aps/Resource.vue` 资源主表维护页
- ❌ 无 `src/views/Aps/ResourceCalendar.vue` Calendar Slot 维护页
- ❌ 路由表 `src/router/modules/aps.ts` 无 `/aps/resource` 与 `/aps/resource-calendar` 路径
- ✅ 仅有 `src/views/Aps/components/ResourceRescheduleDialog.vue`（§10A.4 **触发重排** Dialog，非维护功能）

**方案要求 4 号位「页面维护 / 查询 / 编辑」**，但**资源主表**与**Slot 维护**是 2 个独立职责。请 0 号位 拍板：

- (a) **仅做 Slot 维护**（资源主表由 5 号位 现有页面维护，4 号位 只引用）→ 新建 `/aps/resource-calendar` 单页
- (b) **资源主表 + Slot 维护** 双页（资源主表由 4 号位 一并维护，含「小工序 / 能力槽」新建）→ 新建 `/aps/resource` + `/aps/resource-calendar` 双页
- (c) **资源主表内双 Tab**（资源主表 + Slot 在同一页 Tab 切换）→ 新建 `/aps/resource` 单页含 2 Tab

> 4 号位 倾向 **(c)**：资源与 Slot 是 1:N 强绑定，单页双 Tab 减少跨页跳转、避免误编辑他人资源；但需 5 号位 确认资源主表目前是否已落库在 5 号位 现有页面。

### Q2：API 契约——何时冻结？

**方案 §四**仅给数据模型（`Resource` 5 字段 + `ResourceCalendarSlot` 4 字段），**缺 DTO / 路径 / 行为**。4 号位 必须有契约才能写 `src/api/aps-v1/resourceCalendar.ts` 与 `types/resourceCalendar.ts`。

**最小可用契约清单**（请 2 号位 09-26 前冻结）：

| # | 端点 | 行为 | 用途 |
|---|---|---|---|
| 1 | `GET /api/resources?productionDepartmentId=&operationCode=&resourceType=&page=&pageSize=` | 资源主表分页 | 资源列表 / Slot 选择器 |
| 2 | `GET /api/resource-calendar/slots?resourceId=&from=&to=` | Slot 区间查询（按 from~to） | Slot 列表 / Gantt 不可用窗口显示 |
| 3 | `POST /api/resource-calendar/slots` body 单条 / 批量？批量上限？ | 新增 Slot | 维护页提交 |
| 4 | `PUT /api/resource-calendar/slots/{id}` | 改 Slot（含 AvailableFlag 切换） | 维护页编辑 |
| 5 | `DELETE /api/resource-calendar/slots/{id}` 硬删 / 软删？ | 删 Slot | 维护页删除 |

**字段疑问**：

- `ResourceCalendarSlot` 是否含 `Reason`（不可用原因：设备故障 / 维修 / 培训 / 调休 / 加班 /...）？现有 `ResourceRescheduleDialog.vue:37` `unavailableWindows: Array<{ from, to, reason }>` mock 字段已隐含需求
- `AvailableFlag` 是 bool 还是 enum（如 AVAILABLE / UNAVAILABLE / OVERTIME 三态）？
- 时间精度 ISO 8601 带时区？还是本地时间？

**请 0 号位**：

- 确认 Q2 5 项端点 + 3 字段疑问由 **2 号位 09-26 前冻结**（4 号位 据此同步起草 `src/api/aps-v1/resourceCalendar.ts` + types）
- 5 号位 同步给 1~2 个**真实 Slot 样本**（精修01 白班 08-12 / 13-17 / 周末休；装配02 夜班 20-08 / 周三维护停机），4 号位 据此设计 UI

### Q3：Slot 是「离散条目」还是「周班次模板」？

**方案 §四**字段是 `StartTime / EndTime / AvailableFlag`——**没说 Slot 是单条区间还是周期模板**。两种 UI 与工作量差 1 倍：

| 形式 | 描述 | UI 范式 | 工作量 |
|---|---|---|---|
| (a) **离散条目** | 每条 Slot 一区间（不可累积，无限增长） | 列表 + 区间编辑 + 删除 | 1~2 人天 |
| (b) **周班次模板** | 每周一~日 × 时段，可复用（精修01 每周复制） | 周视图 + 拖拽时段 + 模板保存 | 3~4 人天 |
| (c) **混合：周模板 + 例外条目** | 模板为基线，单日可加例外（国庆加班 / 临时停机） | 双 Tab：周模板 + 例外列表 | 4~5 人天 |

**请 0 号位 拍板用 (a) / (b) / (c) 哪一种**，并请 5 号位 给真实样本对得上。

> 4 号位 倾向 **(c)**：业务方既要规律班次，也要例外处理（设备临时故障 / 临时加班）；但若 Q1 选 (a) 则方案 (c) 落地复杂，需重排。

### Q4：「生产部门 + 小工序」数据源

**现有数据**（已 grep）：

- `productionDepartmentId / productionDepartmentName` 已存在于 `src/api/aps-v1/__mocks__/fixtures.ts:48-114`（注塑车间 / 装配车间 / 测试车间 3 部门）
- `OperationCode` 已用于 Setup 模块（`src/api/aps-v1/types/setup.ts:97`），但语义是「Routing 中的工序」而非「能力槽小工序」
- **方案 §二 要求**：人工能力槽 = **生产部门 + 小工序 + 资源槽名称**（如 A部门 / 精修 / 精修01）

**未澄清**：

- 「小工序」（精修 / 粗车 / 抛光）字典是来自 Domain 治理？是新维护？还是从 Routing.OperationCode 引用？
- 不同部门可存在同名小工序（如 A部门精修 / B部门精修）—— 这是数据层 `UNIQUE(productionDepartmentId, operationCode, slotName)` 还是 UI 层允许同名？
- 「资源槽名称」（精修01 / 02 / 03）是资源级命名，还是槽位（pool）级命名？

**请 0 号位**：

- (a) 「小工序」从 Domain 治理取（前端只读，无新维护页）
- (b) 「小工序」新维护（4 号位 在资源页内附带给「小工序字典」CRUD）
- (c) 「小工序」直接 = Routing.OperationCode（不另建字典）

> 4 号位 倾向 **(a)**：保持「资源主表」与「Routing 工序」解耦，避免双向耦合；但需 0 号位 确认 Domain 治理能覆盖「精修 / 粗车」这类人员能力描述。

---

## 三、5 项 P1 边界（不阻塞开工，请 0 号位 随主流程一并答复）

| # | 边界 | 影响 | 4 号位 默认倾向 |
|---|---|---|---|
| **B1** | **历史 Slot / 软删策略**——排产追溯依赖 Slot 快照；删 Slot 硬删 / 软删？ | 决定是否要「历史 Slot 查询」Tab | **软删 + 历史 Tab**（保留 90 天，与 Run 历史对齐） |
| **B2** | **§10A.4 闭环**——`ResourceRescheduleDialog.vue:37` 的 `unavailableWindows` 字段目前是 mock（来自前端 fixture）；真实数据要调 Calendar API 拉「最近 N 天已变更窗口」 | 4 号位 内闭环，等 Q2 API 后改 | 4 号位 改 Dialog props 改为调 Q2-2 API |
| **B3** | **Gantt.vue 是否画 Calendar 不可用窗口阴影** | 方案未提；如果要，扩 Gantt 渲染层（虚线 / 阴影 / 锁图标） | **暂不画**（避免与现有 `ProductionDepartmentName` lane 视觉冲突；如业务要可加） |
| **B4** | **SolverStrategy 块是否新增「按 Calendar 班次切分」开关** | 方案 §一明文「Calendar 不属规则引擎」 | **不加**（规则引擎不持有 Calendar 数据；如 Solver 需读 Calendar 应由 2 号位 直连 DB 而非走规则） |
| **B5** | **实施包版本归属**——v1.4 已冻结、v1.5 是 Setup 换型专项；Resource Calendar 是 v1.5 addendum？新立 v1.6？还是 v2？ | 决定 v1.4 §十A.4 L10「前端 V1 不提供 Calendar 维护功能」注释修订时机 | 4 号位 倾向**新立 v1.6**（v1.5 已专攻 Setup；新能力独立版本便于追溯） |

---

## 四、4 号位 现有代码需后续修订

| 文件:行 | 现状 | 待修订 |
|---|---|---|
| `src/views/Aps/components/ResourceRescheduleDialog.vue:10` | 注释「前端 V1 不提供 Calendar 维护功能，日历变更由外部系统落库后在此引用」 | 等 v1.6 落地后改为「Calendar 维护由 `/aps/resource-calendar` 页承担，本 Dialog 调 Calendar API 读取已变更窗口」 |
| `src/views/Aps/components/ResourceRescheduleDialog.vue:37` | mock 字段 `unavailableWindows: Array<{ from, to, reason }>` | 等 Q2-2 API 冻结后改 `props.resources[].unavailableWindows` 类型 + 加 API 拉取 |
| `src/api/aps-v1/types/schedule.ts:39-154` GanttResourceDto | 无 ResourceCalendarSlot 字段 | 等 Q2-2 API 冻结后决定是否需增 `unavailableWindows` 字段 |
| `src/views/Aps/Gantt.vue` | 未读 Calendar 数据 | 视 B3 决定 |

---

## 五、能力边界（4 号位 不越界）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md)：

- ❌ 4 号位 **不修改** `lps/**` 任何文件（Resource Calendar 表结构 / API 路由 / 行为均归 2 号位）
- ❌ 4 号位 **不发包** dev / test 环境
- ✅ 4 号位 可提供：详细 UI 草案（草图 + 字段映射）+ types/resourceCalendar.ts 类型契约 + verify-acceptance.mjs `[K]` 段脚本（Resource / ResourceCalendar 端到端用例，等 Q2 后起草）
- ✅ 4 号位 可协助：与 5 号位 对齐「小工序字典」是否真从 Domain 治理取

---

## 六、配合时间

| 期望 | 时间 | 接收方 |
|---|---|---|
| **Q1 页面范围拍板** | **本周内（09-26 前）** | 0 号位 |
| **Q2 API 契约冻结**（5 端点 + 3 字段疑问） | **09-26 前** | 2 号位（抄送 4 号位 / 5 号位） |
| **Q3 Slot 形式拍板** + **真实 Slot 样本**（1~2 例覆盖白班/夜班/休息） | **09-26 前** | 0 号位 拍板 + 5 号位 给样本 |
| **Q4 「小工序」数据源拍板** | **09-26 前** | 0 号位（抄送 5 号位） |
| **B1-B5 P1 边界** | 09-30 前一并答复 | 0 号位 |
| **4 号位 开工** | Q1-Q4 全到位 + Q2 API 冻结后立即起草资源页与 Calendar 页 | 4 号位 |

→ Q1-Q4 全到位后 4 号位 立即开工；任一项缺位则停工等回执。

---

## 七、附档

- 0 号位 原方案：[APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md](APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md)
- v1.4 §十A.4（资源日历调整后重排）：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
- v1.5 Setup 换型专项：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.5_20260916_Setup换型规则专项冻结对齐版.md](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.5_20260916_Setup换型规则专项冻结对齐版.md)
- 现有 §10A.4 Dialog：[src/views/Aps/components/ResourceRescheduleDialog.vue](../src/views/Aps/components/ResourceRescheduleDialog.vue)
- 路由表（无 resource / resource-calendar 路径）：[src/router/modules/aps.ts](../src/router/modules/aps.ts)
- v1.4 收口授权（4 号位 不动 lps）：见 [[project_v1_4_execution_plan.md]] + [[feedback_no_backend_modification.md]]

---

**发送人**：4 号位 ｜ **接收人**：0 号位（主）/ 5 号位（业务数据）/ 2 号位（API）/ 3 号位（备查） ｜ **日期**：2026-09-22