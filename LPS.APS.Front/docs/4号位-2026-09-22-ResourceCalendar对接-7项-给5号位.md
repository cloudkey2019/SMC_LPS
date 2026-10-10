# 4 号位 → 5 号位：Resource Calendar 资源日历能力补充方案——4 号位 配套需求清单 7 项（F1-F7，P0×4 + P1×3）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（APS 资源能力事实 Owner + 资源日历业务数据治理方，本方案主承接方）
> **抄送**：0 号位（项目权威 / 已转交 5 号位 主答）、2 号位（API 端点契约冻结将同步推进，备查）
> **日期**：2026-09-22
> **触发**：0 号位 发《APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md》要求各号位通读；4 号位 已发回执给 0 号位（[4号位-2026-09-22-ResourceCalendar补充方案回执-4P0+5P1-给0号位.md](4号位-2026-09-22-ResourceCalendar补充方案回执-4P0+5P1-给0号位.md)），**0 号位 转交 5 号位 主答**。
> **性质**：**对接函 + 请求清单**（7 项，均为 5 号位 主答范围；不引入新诉求）
> **关联**：v1.4 §十A.4（资源日历调整后重排 RESOURCE_CALENDAR_CHANGE，L595-672）、v1.5 Setup 换型专项冻结对齐版、方案 §六 职责划分（5 号位 治理 + 业务数据 / 4 号位 页面维护 / 2 号位 API）

---

## 〇、速答清单（请照此格式回执）

```
F1 真实 Slot 样本       : 已给 / 否，09-__前补
F2 Slot 维护形式        : 离散条目 / 周班次模板 / 混合（a / b / c）
F3 小工序数据源         : Domain 治理 / 新维护 / 等同 Routing.OperationCode（a / b / c）
F4 现有资源主表查询端点  : 端点路径 = ____；DTO 是否含「人工能力槽」字段 = ____
F5 删 Slot 软删 / 硬删  : 软删保留 __ 天 / 硬删；归档策略 = ____
F6 页面范围             : 4 号位 仅做 Calendar / 资源+Calendar 双页 / 资源页内双 Tab（a / b / c）
F7 5 号位 业务数据治理范围: 含 ResourceCalendarSlot / 仅 Resource 主表；4 号位 只读 / 可写 = ____
```

---

## 一、F1【P0】真实 Slot 样本（最优先）

**为什么必须先给**：F2 Slot 形式（a/b/c）、F4 现有 DTO 是否够用、4 号位 UI 形态判断——**全部依赖真实样本**。没样本，4 号位 无法起草 types/resourceCalendar.ts。

**请 5 号位 给 1~3 个 ResourceCalendarSlot 样本**，覆盖：

| # | 资源类型 | 场景 | 期望字段示例 |
|---|---|---|---|
| 1 | 人工能力槽 | 精修01 白班 | 周一~周五 08:00-12:00 / 13:00-17:00，周末休 |
| 2 | 人工能力槽 | 装配02 夜班 | 周一~周五 20:00-08:00（含跨天），周三 02:00-04:00 临时维护停机 |
| 3 | 设备 | 注塑机03 三班轮换 | 早 06-14 / 中 14-22 / 夜 22-06 |

**期望字段**（按方案 §四 + 4 号位 推测）：
```
ResourceCalendarSlot {
  slotId?: number              // 主键（编辑用）
  resourceId: number           // 关联 Resource
  startTime: string            // ISO 8601
  endTime: string
  availableFlag: boolean       // true=可用 / false=不可用
  reason?: string              // 「夜班」「维护」「国庆加班」等
  source?: 'WEEKLY_TEMPLATE' | 'EXCEPTION' | 'AD_HOC'  // 5 号位 是否分类型？
  effectiveFrom?: string       // 模板生效起
  effectiveTo?: string         // 模板生效止（永久=null）
  createdBy?: string
  createdAt?: string
}
```

→ 5 号位 请按 5 号位 真实字段定义补全 / 校正。

---

## 二、F2【P1】Slot 维护形式（业务方实际维护习惯）

**三种形式对比**：

| 形式 | 描述 | UI | 4 号位 工作量 |
|---|---|---|---|
| (a) **离散条目** | 每条 Slot 一区间（无规律，自由组合）| 列表 + 区间编辑 + 删除 | 1~2 人天 |
| (b) **周班次模板** | 每周一~日 × 时段，可整体复用 | 周视图 + 拖拽时段 + 模板保存 | 3~4 人天 |
| (c) **混合**（推荐）| 周模板为基线 + 单日例外（国庆加班 / 临时停机） | 双 Tab：周模板 + 例外列表 | 4~5 人天 |

**请 5 号位 答**：生产部门（资源维护方）当前是哪种？是否计划推行 (c) 混合？**4 号位 据此决定 UI 与工作量**。

---

## 四、F3【P0】「小工序」数据源

**方案 §二 要求**：人工能力槽 = **生产部门 + 小工序 + 资源槽名称**（如 A部门 / 精修 / 精修01）。其中：

- **生产部门**：现有 `productionDepartmentId / productionDepartmentName`（fixture 注塑 / 装配 / 测试 3 车间）—— **数据源已存在**
- **小工序**（精修 / 粗车 / 抛光）：**数据源未明**
- **资源槽名称**（精修01 / 02 / 03）：**4 号位 拟作为 Resource.ResourceName**

**请 5 号位 答**：

| 选项 | 含义 | 4 号位 动作 |
|---|---|---|
| (a) **从 Domain 治理取** | 5 号位 治理端点已落库「小工序」字典 | 4 号位 只读引用，不另维护 |
| (b) **新维护** | 5 号位 治理端点无「小工序」字典，需新建 | 4 号位 在资源页内附带给「小工序字典」CRUD |
| (c) **等同 Routing.OperationCode** | 「小工序」= Routing 中的工序，不另建字典 | 4 号位 只读 Routing 数据 |

→ 5 号位 当前是否已有「小工序」主数据？落库在哪？4 号位 通过哪个端点取？

---

## 五、F4【P0】现有资源主表查询端点

**5 号位 现有 Resource / 资源主表查询端点是什么？** 4 号位 必须先知道：

| 字段 | 期望 |
|---|---|
| 端点路径 | 例：`GET /api/resources?productionDepartmentId=&operationCode=&resourceType=&page=&pageSize=` |
| 是否含 `productionDepartmentId` / `productionDepartmentName` | ✅ / ❌ |
| 是否含 `operationCode`（小工序） | ✅ / ❌ |
| 是否含 `resourceType`（MACHINE / LINE / MANUAL_STATION / HUMAN） | ✅ / ❌ |
| 是否含 `availableCapacity`（人工能力槽数量） | ✅ / ❌ |

→ 5 号位 答端点路径 + DTO 字段清单；如现有端点缺字段，5 号位 是否新增 / 2 号位 何时补？

---

## 六、F5【P1】删 Slot 软删 / 硬删

**请 5 号位 答**：

- 删 Slot 是**硬删**（DELETE 即消失）还是**软删**（标记 IsActive=0）？
- 软删保留多久？（90 天？永久？）
- 排产追溯依赖历史 Slot，是否要**归档**（迁出主表，转历史表）？
- 4 号位 是否需要「历史 Slot 查询」Tab？

→ 决定 4 号位 维护页是否要 `GET /api/resource-calendar/slots/history?resourceId=` 端点 + UI 是否要「已归档」筛选。

---

## 七、F6【P0】Q1 页面范围（资源主表 + Slot 维护）

**4 号位 现状**（已 grep 确认）：

- ❌ 无 `/aps/resource` 资源主表维护页
- ❌ 无 `/aps/resource-calendar` Calendar Slot 维护页
- ✅ 仅有 `ResourceRescheduleDialog.vue`（§10A.4 **触发重排** Dialog）

**三种页面方案**：

| 方案 | 含义 | 4 号位 工作量 |
|---|---|---|
| (a) **仅 Calendar 页** | 资源主表由 5 号位 治理，4 号位 只做 Slot | 1 页 / 2~3 人天 |
| (b) **资源 + Calendar 双页** | 4 号位 一并维护资源主表（含「小工序 / 能力槽」新建）| 2 页 / 4~5 人天 |
| (c) **单页双 Tab**（4 号位 倾向）| 资源主表 + Slot 在同一页 Tab 切换 | 1 页 / 3~4 人天 |

**请 5 号位 答**：资源主表 5 号位 **是否已有页面**？5 号位 治理端点是否含「人工能力槽」CRUD？4 号位 倾向 (c)，但需 5 号位 确认资源主表是否已落库 4 号位 可直接读。

→ 5 号位 答后，4 号位 + 0 号位 一并拍板。

---

## 八、F7【P1】5 号位 业务数据治理范围

**请 5 号位 答**：

- 「APS 资源能力事实治理」是否含 ResourceCalendarSlot？还是仅 Resource 主表？
- 5 号位 是否同时是**治理方**（CRUD UI 提供）+ **数据提供方**（API 数据源）？
- 4 号位 角色是 **只读**（Slot 维护由 5 号位 治理端点承担，4 号位 只在 ResourceRescheduleDialog 等处引用）还是**可写**（4 号位 也提供 Slot 维护 UI）？

→ 影响 4 号位 是否要建完整的 Slot CRUD 页（含 RBAC），还是仅资源列表 + 引用查询页。

---

## 九、配合时间

| 期望 | 时间 |
|---|---|
| **F1 真实 Slot 样本** | **09-26 前**（最优先，无样本 4 号位 无法起草 types） |
| **F3 小工序数据源答复** | **09-26 前** |
| **F4 现有资源主表查询端点** | **09-26 前** |
| **F6 页面范围**（5 号位 初判 + 0 号位 最终拍板） | **09-26 前** |
| F2 Slot 形式 + F5 软删 + F7 治理范围 | 09-30 前一并答复 |
| **4 号位 开工** | F1+F3+F4+F6 全到位 + 2 号位 API 冻结后立即 |

→ 4 号位 收到 F1-F7 答复后立即起草 `src/api/aps-v1/types/resourceCalendar.ts` + `/aps/resource-view` 页面。

---

## 十、能力边界（4 号位 不越界）

按 [CLAUDE.md memory: 不修改后端](../CLAUDE.md)：

- ❌ 4 号位 **不修改** `lps/**` 任何文件（Resource / ResourceCalendarSlot 表结构 / API 路由 / 行为均归 2 号位）
- ❌ 4 号位 **不发包** dev / test 环境
- ✅ 4 号位 可提供：详细 UI 草案（草图 + 字段映射）+ types/resourceCalendar.ts 类型契约（待 F1 样本到位后）+ verify-acceptance.mjs `[K]` 段脚本（待 Q2 API 冻结后起草）

---

## 十一、附档

- 0 号位 原方案：[APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md](APS_V1_ResourceCalendar资源日历能力补充方案_v1.0.md)
- 4 号位 → 0 号位 回执（已转交 5 号位 主答）：[4号位-2026-09-22-ResourceCalendar补充方案回执-4P0+5P1-给0号位.md](4号位-2026-09-22-ResourceCalendar补充方案回执-4P0+5P1-给0号位.md)
- v1.4 §十A.4：[冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md](../冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.4_20260915_白天业务场景_Candidate人工调整冻结对齐版.md) L595-672
- 现有 §10A.4 Dialog：[src/views/Aps/components/ResourceRescheduleDialog.vue](../src/views/Aps/components/ResourceRescheduleDialog.vue)
- 路由表（无 resource / resource-calendar 路径）：[src/router/modules/aps.ts](../src/router/modules/aps.ts)

---

**发送人**：4 号位 ｜ **接收人**：5 号位（主）/ 0 号位（转交留档）/ 2 号位（API 备查） ｜ **日期**：2026-09-22