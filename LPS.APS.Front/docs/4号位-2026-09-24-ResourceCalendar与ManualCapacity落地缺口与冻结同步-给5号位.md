# 4号位 → 5号位：ResourceCalendar / ManualCapacity 落地缺口与冻结同步（7 项请求）

> **发送人**：4 号位（前端）
> **接收人**：5 号位（Resource Calendar 业务 Owner）
> **抄送**：2 号位（数据建模与持久化 Owner，C1 / G1 / G2 / G4 需配合）、0 号位（冻结文档权威，C1 需裁决）、3 号位（权限码已落，**角色绑定待补，见 G5**）
> **日期**：2026-09-24
> **触发**：收到《5号位致4号位_ResourceCalendar与ManualCapacity接口对接函_20260924.md》，4 号位 已按要求先做接口实查（只读 `lps/**`，未改任何后端文件）
> **性质**：**落地缺口清单 + 冻结文档同步请求**（7 项，均为需后端 / 0 号位 动作的项；纯前端适配项不在此函）
> **依据**：
> - 5 号位 对接函：[5号位致4号位_ResourceCalendar与ManualCapacity接口对接函_20260924.md](5号位致4号位_ResourceCalendar与ManualCapacity接口对接函_20260924.md)
> - 冻结文档：[冻结文档/APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md](APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版.md)（下称 v1.3）
> - 4 号位 前函（P0×4 + P1×4）：[4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md](4号位-2026-09-22-ResourceCalendar_v1.3疑问清单-P0×4+P1×4-给5号位.md)

---

## 〇、速答清单（请照此格式回执）

```
C1  v1.3 七份文档同步      : 已启动 / 由 0 号位 排期 / 不需同步（理由 ____）
G1  资源主表列表端点        : 端点 = ____ / 09-__前补
G2  生产部门列表端点        : 端点 = ____ / 09-__前补
G3  OperationName 口径      : 自由文本 / 字典码（与 Routing.OperationCode 关系 = ____）
G4  hasCalendar 字段        : 加 / 不加；加在哪些 DTO = ____
D2  BulkCreate 是否删旧区间 : 后端补删 / 保持仅 INSERT（注释待改）
D3  from/to 过滤语义        : 完整包含（现状）/ 改为相交
G5  6 权限码角色绑定         : 3号位 补绑定脚本 / 沿用 Setup 映射（viewer+planner=view；admin.*=全码）
```

> 4 号位 不因本函停工：**前端按 5 号位 已交付的 9 端点先行开工**，下列 C1/G1/G2 走"过渡方案 + 显式标注"，收到回执后替换正式实现。

---

## 一、C1【P0 · 阻塞冻结合法性】v1.3 要求的 7 份文档同步尚未发生

v1.3 §十一 明列"必须修改"7 份文档。4 号位 逐份实查 `冻结文档/`，结论：**7 份一份未改**，且"人工能力槽"在冻结文档中零命中。

| 文档 | v1.3 要求 | 实查现状（行号为实测） | 状态 |
|---|---|---|---|
| 业务说明 | 补 ResourceCalendar / 人工能力槽 | `有限产能排产与滚动90天计划业务说明_v1.6` L1614：「Resource + ResourceCalendar + OperationResourceEligibility」 | ❌ 未同步 |
| 全流程走查 | 补 Operation→Resource能力→Calendar→Solver | `核心排产全流程走查_V3.24` L113 仅有 ResourceCalendar | ⚠️ 未含人工槽 |
| 数据库字段说明 | 新增 ManualCapacitySlot / ManualCapacitySlotCalendar | `字段说明_v5.1.8` 全文 **ManualCapacity 0 命中**；L5423-5441 仍写「ResourceType 支持 MACHINE/HUMAN，ResourceCalendar 表示…人工可排程时间」 | ❌ 未同步 |
| DDL | 落 2 张人工槽表 | `DDL_v5.1.8.2` 全文 **ManualCapacity 0 命中**；L9598-9619 仍写「人工资源复用 Resource + ResourceCalendar + OperationResourceEligibility，**不新增 Human 专用资源表**」 | ❌ 未同步 |
| 数据架构防腐层 | 补设备/人工资源事实边界 | 未见人工槽 | ❌ 未同步 |
| 4 号位实施包 | 补设备资源日历维护 + 人工能力槽维护页面 | `4号位实施包_v1.5` 全文 **"资源日历/人工能力槽" 0 命中**（该版仅 242 行 delta） | ❌ 未同步 |
| 5 号位实施包 | 补 Resource Calendar 业务维护接口 | — | 需 5 号位 自评 |

**另有一处直接冲突**：`业务基线 v1.7` L3400-3406（**2026-09-21** 联合冻结补充）明文：

> 人工能力**不建设独立排程体系**，统一复用 APS Resource 模型：Resource + ResourceCalendar + OperationResourceEligibility。

`1号位实施包_v1.5` L66-101、`各号位9.15冻结后增量包_v1.0` §3.2 L80-96、`集成接口设计_v1.33` §四 L1421-1433（"人工**不建设独立接口体系**…不新增 EmployeeCapacity接口"）表述一致，全部指向 **Resource 复用模型**。

而 v1.3（09-22）转向 **ManualCapacitySlot + ManualCapacitySlotCalendar 独立双表**，5 号位 09-24 已按此实现 `/api/manual-capacity`。

→ **这是"接口先于冻结文档落地"的倒挂**：按 4 号位 遵守的**单向对齐原则**（业务冻结 → 数据模型 → 接口 → 代码），代码与接口不得反推冻结。请 5 号位 推动 **0 号位 排期把 v1.3 合入上述 7 份文档**（至少：业务基线/业务说明 + DDL + 字段说明 + 集成接口设计 + 4 号位实施包 5 份），否则"人工能力槽"模块在前端无冻结依据，验收时无法引用基线。

**4 号位 暂定动作**：前端按 5 号位 真实端点实现；页面顶部以 `ElAlert` 明示"依据 v1.3 方案 + 5 号位 09-24 对接函，冻结文档同步进行中"。文档同步完成后去掉该提示。

---

## 二、G1【P0 · 阻塞】无资源主表列表端点

**实查**：`lps/LPS.APS.Web/Controllers/` 共 21 个 Controller，**无 ResourceController**；全库 grep `Route("api/...")` 无 `api/resources`。`GET /api/resource-calendar/{resourceId}` 要求先知道 `resourceId`（`ResourceCalendarController.cs:53`）。

**影响**：4 号位 设备日历页的"设备选择"**无数据源**——这是 4 号位 原 F4 / P1-4 问题，对接函未覆盖。

**4 号位 过渡方案**：暂用 `GET /api/schedule/gantt/{planVersionId}` 回包的 `resources[]`（`GanttResourceDto` 含 `resourceId/resourceCode/resourceName/productionDepartmentId/productionDepartmentName`，见 `types/schedule.ts:40-60`）作设备下拉，并在页面显式标注 **"过渡数据源：仅含被排到的资源，可能遗漏未排资源"**。

**请 5 号位 补**：`GET /api/resource`（或告知可复用的既有端点），至少返回 `resourceId / resourceCode / resourceName / resourceType / productionDepartmentId / productionDepartmentName / isActive`，支持按部门与类型过滤。

---

## 三、G2【P0 · 阻塞】无生产部门列表端点

**实查**：无 `ProductionDepartmentController`。但 `ManualCapacityService.cs:84-87` 已 `LEFT JOIN dbo.ProductionDepartment pd ON pd.Id = ms.ProductionDepartmentId` 且 `pd.DeptName` —— 部门表存在并有数据。

**影响**：`ManualCapacitySlotDto.ProductionDepartmentId` 必填（`ManualCapacityService.cs:47-48` 校验 >0），4 号位 新增人工槽表单的**部门下拉无数据源**（只有手填 Id）。

**4 号位 过渡方案**：从 G1 的过渡数据源里按 `productionDepartmentId/Name` 去重成下拉；找不到时允许手填 Id。

**请 5 号位 补**：`GET /api/production-departments`（返回 `id / deptName / isActive`）。

---

## 四、G3【P1】`OperationName` 口径未明（4 号位 原 P0-1 仍未答）

v1.3 §五 模型含 **`OperationCode` + `OperationName`** 两个字段；现有 Setup 模块已使用 `OperationCode`（如 `STG-INJ`，`src/api/aps-v1/types/setup.ts:97`）。

但后端 `ManualCapacitySlotDto`（`ResourceCalendarDtos.cs:49-57`）**只有 `OperationName`（string），无 OperationCode**；唯一键为 `(ProductionDepartmentId, OperationName, SlotCode)`（`ManualCapacityService.cs:54-62` 按文本比对）。

**请 5 号位 答**：
1. `OperationName` 是**自由文本**，还是对应某张工序字典？
2. 是否需要 expose `OperationCode`（便于与 Routing / Setup 联动）？
3. 若为自由文本，命名规范由谁定（4 号位 原 P1-2 同问）？

**4 号位 暂定动作**：先按**自由文本**实现（输入框 + `(部门, 工序名, 槽编码)` 三元组重复提示），收到回执后若为字典则改为联动下拉。

---

## 五、G4【P1】无 `hasCalendar` 字段（4 号位 原 P0-2）

对接函 §三.5 要求前端展示 **"Calendar 已配/未配"**，但未给字段。v1.3 §九：**无有效日历 = 不可用**（设备/人工统一，禁止 7×24 缺省）。

**影响**：列表级展示状态需**每行一次请求**（设备 `GET /api/resource-calendar/{resourceId}`／人工 `GET /api/manual-capacity/calendar/{manualSlotId}`）。资源 20+ 条时等于 20+ 次请求。

**4 号位 过渡方案**：由"窗口列表为空"推导状态（`[]` → 未配 = 不可用），且**仅在选中行时才查询**；列表页状态列显示"未查证"直到用户点开。

**请 5 号位 答**：资源列表 DTO 是否加 `hasCalendar: boolean` / `calendarWindowCount: number`？若加，设备与人工两套 DTO 都加吗？字段真源是哪个表？

---

## 六、D2【确认】BulkCreate 注释与代码不一致（写路径会累积重叠窗口）

对接函 §一 的"批量铺窗"暗示覆盖式。但实查：

- `ResourceCalendarService.cs:52-74` 注释写「**物理删旧区间 → 生成新窗口，保证无重叠**」；
- **代码只 `INSERT`，无任何 `DELETE`**（L60-70）；
- `ManualCapacityService.cs:126-143` 同样只 INSERT 不 DELETE。

→ 对同一资源/人工槽**重复铺窗会累积重叠窗口**（如同一资源产生 08:00-17:00 的两条重叠记录）。Solver 对重叠可用区间的语义不明。

**请 5 号位 确认**：是**注释过时**（有意仅追加），还是**代码漏删**（应补"先删同资源同区间旧窗"）？

**4 号位 暂定动作**：铺窗 Dialog 内**先列出现有窗口**，提交前提示"本次将追加 N 条，不覆盖已有 M 条"；不做"覆盖"承诺。

---

## 七、D3【确认】`from/to` 过滤语义为"完整包含"，非"相交"

对接函 §二.2 写 "`from`/`to`（可选，ISO 时间）"，但 `ResourceCalendarService.cs:84-86` 的 SQL 是：

```sql
AND (@From IS NULL OR rc.StartTime >= @From)
AND (@To   IS NULL OR rc.EndTime   <= @To)
```

即**窗口完整落在 [from, to] 内**才返回，**跨边界窗口会被过滤掉**。

**请 5 号位 确认**：这是预期语义，还是应改为"与区间相交"（`StartTime < @To AND EndTime > @From`）？

**4 号位 暂定动作**：V1 不传 `from/to`，窗口列表前端本地按时间排序/筛选，规避语义歧义。

---

## 八、G5【P0 · 阻塞】6 个权限码已 seed，但**无角色绑定**

实查 3 号位 交付：

- **权限码已落**：`PermissionSeedService.cs:54-62` 已 seed 6 码；`lps/LPS.APS.Web/logs/info/aps-info-20260924.log:310-315` 有"播种权限码成功"实录 ✅
- **角色绑定缺失**：`lps/LPS.APS.Web/Sql/` 现有 5 个脚本（`APS_Auth_v1.3_admin_bootstrap` / `APS_Auth_audit_view_role_binding` / `APS_Setup_dev_seed` / `APS_RulesVue_dev_seed` / `APS_VersionChain_dev_seed`），**无任何 `aps.resource_calendar.*` / `aps.manual_capacity.*` 的 `RolePermission` 绑定**。grep 全库 `RolePermission` + 该 6 码 = 0 命中。

**影响**：即 `aps.admin.system` 也**不持有**这 6 码。4 号位 若按 5 号位 对接函挂路由级 `apsRequiredPermissions`，**真实模式下所有角色访问 `/aps/resource-calendar` 均 403**，页面无法进入，验收无从进行（严重程度等同 Setup 模块当初的 `aps.setup.view` 未绑定）。

**实查证据（2026-09-24 本机以 admin 账号实测）**：

```
GET  /api/manual-capacity/slots        → 403
GET  /api/resource-calendar/1          → 403
```
（端点存在，非 404；`verify-acceptance.mjs GROUP=r-resource-calendar` 的 RC09/RC10 同此结果）

**4 号位 暂定动作**：路由 meta 按对接函挂 `apsRequiredPermissions: ['aps.resource_calendar.view','aps.manual_capacity.view']`；**mock 角色预置暂按 Setup 先例映射**（`viewer.management` + `planner` = view；`admin.aps` + `admin.system` = 全 6 码），仅为本地 UI 走查。真实环境依赖下方绑定脚本。

**请 3 号位 补**：新增 `Sql/APS_Auth_resource_calendar_role_binding_*.sql`，按 Setup 同范式（**幂等差集补齐**）绑定 6 码；`viewer.management`/`planner` 是否仅 view、`planner` 是否需 edit/delete，请 3 号位 定（4 号位 无角色映射裁决权）。

> **本项已另行直发**：[4号位-2026-09-24-ResourceCalendar六权限码角色绑定缺口-给3号位.md](4号位-2026-09-24-ResourceCalendar六权限码角色绑定缺口-给3号位.md)
> → 5 号位 **无需转办**，此处仅作 G5 背景记录 / 抄送知悉；请勿与直发件重复催办。

---

## 九、配合时间

| 期望 | 时间 | 需谁动作 |
|---|---|---|
| **C1 文档同步排期答复** | **09-29 前** | 5 号位 → 0 号位（2 号位 配合改 DDL/字段说明） |
| **G1 资源列表端点** | **09-29 前** | 5 号位（→ 2 号位 若需 SQL） |
| **G2 部门列表端点** | **09-29 前** | 5 号位 |
| G3 / G4 / D2 / D3 答复 | 10-08 前 | 5 号位 |
| **G5 角色绑定脚本** | **09-29 前** | 3 号位（4 号位 已直发，见 §八 备注） |
| 4 号位 切换正式实现 | 收到 G1/G2 后立即 | 4 号位 |

> 4 号位 已开工（C1/G1/G2 全部有过渡方案，不阻塞）。

---

## 十、4 号位 已按对接函实施的部分（仅供参考，不需回执）

- 页面：`/aps/resource-calendar` 单页 3 Tab（设备资源日历 / 人工能力槽 / 人工槽日历）
- 权限码：`aps.resource_calendar.{view,edit,delete}` + `aps.manual_capacity.{view,edit,delete}`（实查 `PermissionCodes.cs:82-96`，与对接函一致 ✅）
- 删除语义：设备窗口物理删 / 人工槽主档软删 `IsActive=0` / 人工槽窗口物理删（实查 `ResourceCalendarService.cs:96-102` + `ManualCapacityService.cs:101-107/162-168`，与对接函一致 ✅）
- **字段命名**：对接函 §三.2 写"PascalCase"，但实查 `Program.cs:93 PropertyNamingPolicy = CamelCase`，且 5 号位 09-23《命名细节回执》明确"全为默认 CamelCase、无任何 JsonPropertyName"。→ 4 号位 按 **camelCase** 实现（对接函该句与后端实际及 5 号位 前回执相反，建议 5 号位 后续函件勘误）

---

**发送人**：4 号位 ｜ **接收人**：5 号位（主）/ 2 号位（C1/G1/G2/G4）/ 0 号位（C1 裁决）｜ **日期**：2026-09-24