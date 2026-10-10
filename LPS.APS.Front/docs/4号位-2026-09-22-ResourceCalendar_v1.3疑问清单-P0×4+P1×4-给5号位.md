# 4 号位 致 5 号位 — Resource Calendar v1.3 修订版影响分析 + 4 号位 新疑问清单（P0×4 + P1×4）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（Resource Calendar 业务 Owner，人工能力槽业务维护方，本函主承接方）
> **抄送**：2 号位（数据建模与持久化 Owner，本函 P0-1/P0-2/P1-4 需 2 号位 配合）、1 号位（Solver 消费 Owner，本函 P0-3 需 1 号位 拍板 RESOURCE_CALENDAR_CHANGE 触发）、0 号位（项目权威，本函经 5 号位 中转，备查）
> **日期**：2026-09-22
> **触发**：收到 0 号位《APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md》（在 `冻结文档/` 下，正式冻结版）
> **沟通方式说明**：按用户 2026-09-22 明确指示——**前端有问题找后端（5 号位），后端在中间给协调，不直接跟 0 号位 对接**。本函所有疑问由 5 号位 中转 0 号位 / 1 号位 / 2 号位，4 号位 不越级。
> **性质**：**v1.3 影响分析回执**（替代 v1.0 旧函）+ **新疑问清单**（v1.3 未澄清 4 项 P0 + 4 项 P1）
> **关联**：
> - 0 号位 v1.3 冻结版：[冻结文档/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md](../冻结文档/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md)
> - v1.0 旧方案（已替代）：[APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md](APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md)
> - 4 号位 上一封对接函（F1-F7）：[4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md](4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md)（本函下发后作废）
> - v1.4 §十A.4 RESOURCE_CALENDAR_CHANGE：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A.4](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672

---

## 〇、沟通方式（重要）

按用户 2026-09-22 明确指示：

> **前端有问题找后端（5 号位 / 2 号位 / 3 号位 等实施包拥有方），后端在中间给协调；以后的沟通都是这种方式——不直接跟 0 号位 对接。**

本函所有疑问由 **5 号位 主承接**，按需中转：

- P0-1 / P0-2 / P1-4 → 5 号位 中转 **2 号位**（数据建模 Owner）
- P0-3 → 5 号位 中转 **1 号位**（Solver 消费 Owner）+ **0 号位**（Candidate 触发规则裁决）
- P0-4 → 5 号位 中转 **2 号位** + **0 号位**（缺省语义回溯影响）
- P1-1 / P1-2 / P1-3 → **5 号位**自答（业务 UI 组织）

→ 4 号位 后续不再直接发函给 0 号位，所有疑问汇总给 5 号位 / 2 号位 / 3 号位 等实施包拥有方，由其实施包 Owner 中转裁决。

---

## 一、v1.0 → v1.3 关键变化（4 号位 影响分析）

### 1.1 资源模型拆分（关键变化）

| 维度 | v1.0 | **v1.3（冻结版）** |
|---|---|---|
| **设备资源** | Resource（MES/EAM 同步）| **Resource + ResourceCalendarSlot**（v1.3 §三，与 v1.0 一致）|
| **人工能力** | Resource（建议「生产部门 + 小工序 + 资源槽名称」）| **ManualCapacitySlot + ManualCapacitySlotCalendar**（v1.3 §五，独立表！非 Resource）|
| **数据来源** | 设备 MES/EAM，人工未明 | 设备 Resource 来自 MES/EAM；人工 ManualCapacitySlot **5 号位 业务维护** |
| **缺省语义** | 未明 | **无有效日历 = 不可用**（设备/人工统一，禁止 7×24 默认，v1.3 §九）|
| **职责 Owner** | 仅 5/4/2/3 | **新增 1 号位 Solver 消费 Owner**（v1.3 §八）|

### 1.2 4 号位 之前的疑问已部分澄清

| 之前疑问 | v1.3 答复 |
|---|---|
| **Q1 页面范围** | ✅ **3 类对象 3 个页**：① 设备资源日历维护 ② 人工能力槽维护 ③ 人工能力槽日历维护（v1.3 §八） |
| **Q4 小工序数据源** | ⚠️ **v1.3 §五 明文 ManualCapacitySlot 6 字段**，但 OperationCode 与 Routing.OperationCode 关系仍未明（→ P0-1）|
| **缺省语义** | ✅ **无日历 = 不可用**（v1.3 §九）—— UI 必须显式标识「Calendar 已配/未配」状态 |
| **实施包版本** | ✅ **v1.3 落在 冻结文档/，正式冻结**——v1.4 §十A.4 之前「前端 V1 不提供 Calendar 维护」注释待 v1.6 修订 |

### 1.3 4 号位 上一封对接函（F1-F7）作废声明

[4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md](4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md) 中的 F1-F7 大部分已由 v1.3 澄清或需重写：

| F 项 | 状态 |
|---|---|
| F1 真实 Slot 样本 | ⚠️ **改**：现在需 3 类样本（设备 Calendar / 人工槽 / 人工槽日历）|
| F2 Slot 维护形式 | ❌ 仍待 5 号位 答（业务方维护习惯）|
| F3 小工序数据源 | ⚠️ **改**：v1.3 §五 明文 ManualCapacitySlot.OperationCode，但与 Setup.OperationCode 关系 → P0-1 |
| F4 现有资源主表查询端点 | ⚠️ **改**：现需 5 号位 答 ManualCapacitySlot 端点（→ P1-4）|
| F5 删 Slot 软删 / 硬删 | ❌ 仍待 5 号位 答 |
| F6 页面范围 | ✅ v1.3 已明（3 类对象 3 个页）→ 仍待 P1-1 确认组织形式 |
| F7 5 号位 治理范围 | ⚠️ **改**：v1.3 §八 明确 5 号位 业务 Owner + 2 号位 数据建模 Owner 分工 |

---

## 二、P0 必答疑问（4 项，09-26 前）

### P0-1【P0】ManualCapacitySlot.OperationCode 与 Setup 模块 OperationCode 关系

**疑问**：v1.3 §五 ManualCapacitySlot 6 字段含 `OperationCode` + `OperationName`；现有 Setup 模块 `src/api/aps-v1/types/setup.ts:97` 已用 `OperationCode`（如 `STG-INJ`/`STG-ASM`）。**两者是同一字典，还是两套独立字段**？

**影响**：
- 若是同一字典：4 号位 资源页可直接复用 Setup 下拉（如 `productionDepartmentId + operationCode` 联动）
- 若是两套独立：4 号位 资源页需新增「小工序」下拉，与 Setup 模块并列维护

**请 5 号位 中转 2 号位**（数据建模 Owner）确认：ManualCapacitySlot.OperationCode 是否与 Routing.OperationCode 同一字典？DDL v5.1.8.2 是否已含 ManualCapacitySlot 表？

### P0-2【P0】资源列表是否需「Calendar 状态」字段

**v1.3 §九**：无有效日历 = 不可用（设备/人工统一）。

**影响**：4 号位 资源列表必须显式展示「Calendar 已配/未配」状态，否则用户不知道哪些资源能排、哪些不能排。

**请 5 号位 答**：
- 资源列表 DTO 是否新增 `hasCalendar: boolean` 字段（或 `latestCalendarAt: IsoDateTime`）？
- 若是设备 + 人工槽两套表，是否两套 DTO 都加？
- 字段真源是哪个表？是否需新增端点？

### P0-3【P0】§10A.4 RESOURCE_CALENDAR_CHANGE 触发是否仍有效

**冲突点**：
- v1.4 §十A.4 冻结：`trigger: RESOURCE_CALENDAR_CHANGE`（资源日历调整后重排）作为 §10A 5 入口之一，前端已实现 `ResourceRescheduleDialog.vue`
- v1.3 §十明文：「**具体 Candidate 触发规则属于独立裁决，不在本文展开**」

**疑问**：RESOURCE_CALENDAR_CHANGE trigger 在 v1.3 落地后是否仍可用？还是 Calendar 变化后的 Candidate 触发要走单独的「运行治理」裁决（如新增 `GOVERNANCE_REVIEW` trigger 类型）？

**请 5 号位 中转 1 号位 + 0 号位**：
- 1 号位 Solver 消费 Calendar 变化时，Candidate 触发类型是否仍是 RESOURCE_CALENDAR_CHANGE？
- 0 号位 是否已就 v1.3 §十「Candidate 触发规则独立裁决」发过专门裁决？是否影响 §10A.4 现状？

### P0-4【P0】缺省语义「无日历=不可用」是否回溯历史

**疑问**：v1.3 §九明文无有效日历 = 不可用。但：
- 现有设备资源（MES/EAM 同步）大量未配 Calendar
- 现有 ManualCapacitySlot 新建，人工槽不一定立即有 Calendar

**回溯影响**：
- 是否**立即生效**（即所有未配 Calendar 的资源从 v1.3 发布日起不可用）？
- 还是**宽限期**（如 30 天，宽限期内按 7×24 兜底，宽限期后切严格）？
- 还是**仅新增生效**（历史资源不溯及既往）？

**请 5 号位 中转 2 号位 + 0 号位**：
- 2 号位 数据迁移计划：是否有 DDL 兜底（如 `IsActive=0` 默认值）
- 0 号位 业务裁决：缺省语义生效时点（立即 / 宽限 / 仅新增）

---

## 三、P1 待澄清疑问（4 项，09-30 前）

### P1-1【P1】3 个新页是单页双 Tab / 3 独立页 / 资源页内 3 Tab

**v1.3 §八 4 号位 维护 3 类对象**：
1. 设备资源日历维护（ResourceCalendarSlot）
2. 人工能力槽维护（ManualCapacitySlot）
3. 人工能力槽日历维护（ManualCapacitySlotCalendar）

**3 种组织形式**：

| 形式 | 含义 | 4 号位 工作量 |
|---|---|---|
| (a) **3 独立页** | `/aps/resource-calendar`（设备） + `/aps/manual-capacity-slot`（人工槽） + `/aps/manual-capacity-slot-calendar`（人工槽日历）| 3 页 / 5~6 人天 |
| (b) **单页 3 Tab**（4 号位 倾向）| `/aps/resource-calendar` 单页含 3 Tab（设备日历 / 人工槽 / 人工槽日历）| 1 页 / 3~4 人天 |
| (c) **资源页内 3 Tab** | 嵌入 `/aps/resource` 主资源页 Tab | 1 页 / 4~5 人天 |

**请 5 号位 答**：业务方（生产部门 / 车间人员）期望哪种入口组织？

### P1-2【P1】SlotCode 命名规范由谁定

**v1.3 §五**：ManualCapacitySlot.SlotCode（如精修01 / 精修02）。

**疑问**：
- SlotCode 命名规则（命名空间 / 前缀 / 长度限制）由谁定？5 号位 业务方还是 4 号位 协助建议？
- 是否需校验 SlotCode 在 `(ProductionDepartmentId, OperationCode)` 下 UNIQUE（v1.3 §四"不同生产部门、不同小工序的能力不可直接混用"暗示 UNIQUE）？

**请 5 号位 答**：SlotCode 命名规范是否已冻结？后端是否有 UNIQUE 约束？

### P1-3【P1】设备 Calendar 与人工 Calendar 是否需分开菜单

**v1.3 §三 设备**：`Resource + ResourceCalendarSlot`
**v1.3 §五 人工**：`ManualCapacitySlot + ManualCapacitySlotCalendar`

**疑问**：两套独立表 + 两套独立 ID 体系，UI 入口是否分开（避免用户混淆设备与人工）？

| 方案 | 含义 |
|---|---|
| (a) **分开菜单** | 左侧导航：「设备日历」「人工能力槽」「人工槽日历」3 个独立菜单项 |
| (b) **统一入口** | 左侧导航：「资源日历」单菜单，Tab 区分设备/人工 |
| (c) **按生产部门聚合** | 左侧导航：「按部门管理」（精修 / 装配 / 测试 各一菜单项），Tab 区分设备/人工 |

**请 5 号位 答**：业务方（车间人员）期望哪种入口？是否需要按部门聚合？

### P1-4【P1】5 号位 现有 ManualCapacitySlot 端点是否已存在

**v1.3 §十一**：补充 5 号位 实施包「Resource Calendar 业务维护接口」+「人工能力事实查询接口」。

**疑问**：5 号位 当前**是否已存在** ManualCapacitySlot 相关端点？还是 v1.3 落地后才新建？

**请 5 号位 答**：
- 现有 ManualCapacitySlot CRUD 端点路径（如 `GET/POST /api/manual-capacity-slots`）
- 现有 ManualCapacitySlotCalendar CRUD 端点路径
- 是否含分页 / 过滤（按 productionDepartmentId / operationCode）

---

## 四、配合时间（按新规矩由 5 号位 中转）

| 期望 | 时间 | 中转方 |
|---|---|---|
| **P0-1 OperationCode 字典关系** | **09-26 前** | 5 号位 → 2 号位 |
| **P0-2 Calendar 状态字段** | **09-26 前** | 5 号位（业务主答）+ 2 号位（DTO 字段落点）|
| **P0-3 §10A.4 触发是否仍有效** | **09-26 前** | 5 号位 → 1 号位 + 0 号位 |
| **P0-4 缺省语义回溯生效时点** | **09-26 前** | 5 号位 → 2 号位 + 0 号位 |
| P1-1 页面组织形式 | 09-30 前 | 5 号位（业务主答）|
| P1-2 SlotCode 命名规范 | 09-30 前 | 5 号位 |
| P1-3 入口菜单组织 | 09-30 前 | 5 号位 |
| P1-4 ManualCapacitySlot 端点现状 | 09-30 前 | 5 号位（自答）|
| **4 号位 开工** | P0-1~4 + P1-4 全部到位 + 2 号位 API 冻结后立即起草资源日历页 | 4 号位 |

→ **P0 4 项任一项缺位则停工等回执**；5 号位 中转回执可逐项答复。

---

## 五、能力边界（4 号位 不越界 + 不越级）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md) + **2026-09-22 用户沟通方式新规**：

- ❌ 4 号位 **不修改** `lps/**` 任何文件
- ❌ 4 号位 **不发包** dev / test 环境
- ❌ 4 号位 **不直接发函给 0 号位**（按新规矩，前端疑问→5 号位→0 号位）
- ❌ 4 号位 **不直接发函给 1 号位 / 2 号位**（按新规矩，5 号位 中转）
- ✅ 4 号位 仅前端代码改动（types/resourceCalendar.ts + ResourceCalendar.vue + ManualCapacitySlot*.vue）
- ✅ 4 号位 收到 5 号位 中转回执后，三绿复验（ts:check / lint:eslint / build:pro）
- ✅ 4 号位 可提供：详细 UI 草案（草图 + 字段映射）+ types/resourceCalendar.ts 类型契约（待 P0-1 + P0-2 + P1-4 到位后）

---

## 六、附档

- 0 号位 v1.3 冻结版：[冻结文档/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md](../冻结文档/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md)
- v1.0 旧方案（已替代）：[APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md](APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md)
- 4 号位 上一封对接函（F1-F7，本函下发后作废）：[4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md](4号位-2026-09-22-ResourceCalendar对接-7项-给5号位.md)
- v1.4 §十A.4 RESOURCE_CALENDAR_CHANGE：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4 §十A.4](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
- 现有 §10A.4 Dialog：[src/views/Aps/components/ResourceRescheduleDialog.vue](../src/views/Aps/components/ResourceRescheduleDialog.vue)
- 路由表（无 resource / resource-calendar 路径）：[src/router/modules/aps.ts](../src/router/modules/aps.ts)
- Setup 模块 OperationCode 现状：[src/api/aps-v1/types/setup.ts:97](../src/api/aps-v1/types/setup.ts)
- v1.4 收口授权（4 号位 不动 lps）：见 [[project_v1_4_execution_plan.md]] + [[feedback_no_backend_modification.md]]

---

**发送人**：4 号位 ｜ **接收人**：5 号位（主承接/中转）/ 2 号位 / 1 号位 / 0 号位（经 5 号位 中转，备查） ｜ **日期**：2026-09-22