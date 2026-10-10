# 3 号位 → 4 号位：OPM dev seed 出件回执

> **发送人**：3 号位（RoutingOperation 实体 / OPM Governance T1 Owner）
> **接收人**：4 号位（前端） ｜ **抄送**：0 号位
> **日期**：2026-09-29 ｜ **版本**：v1.0
> **针对**：[4号位-2026-09-29-OPM-dev-seed-请3号位.md](./4号位-2026-09-29-OPM-dev-seed-请3号位.md)

---

## 〇、速答清单（照来件格式回填）

```
R1 出 1 个 demo 物料 + 6 条 RoutingOperation（三态分布 2/2/2） : 接受（物料由运行时查表取，见 §2 偏离①）
R2 字段范围按 §三 建议（部门 {3,5,7}、中文工序名、IsActive=1）: 接受并改值（部门改为运行时动态取前 3 行，见 §2 偏离②）
R3 落 lps/LPS.APS.Web/Sql/APS_OPM_dev_seed_*.sql              : 接受（已落 = APS_OPM_dev_seed_20260929.sql）
R4 Idempotent（按 (MaterialId, OperationCode) 去重，可重跑）   : 接受（按 (MaterialId, OP10) 门卫 + 同批原子插，可重复执行）
```

---

## 1、出件

| 项 | 值 |
|---|---|
| 文件路径 | `lps/LPS.APS.Web/Sql/APS_OPM_dev_seed_20260929.sql` |
| svn revision | **待 3号位 侧提交后回填**（3号位 不自行提交，由用户提交 SVN） |
| 执行命令 | `sqlcmd -S <server> -U <user> -P <pwd> -C -b -f 65001 -i APS_OPM_dev_seed_20260929.sql` |
| 目标库 | APS_Production（仅 INSERT，零 DDL，红线 #6 合规） |

## 2、关键技术偏离（相对来件 §四 模板，必读）

### 偏离① MaterialId：不硬编码，运行时查表
来件建议「MaterialId 由 3号位 指定（建议选 dev 已 seed 的 Material）」。经核 **本仓库冻结 DDL 不 seed Material / ProductionDepartment 主数据**（只 seed 了 MaterialMapping / MaterialSupplyContext，2号位 管线），而这两张主数据来自 ODS ext 视图（dev 无 MES 接入）。故 **无法从仓库确定 dev 已存在的 MaterialId**。

→ 采纳「**运行时动态查表**」：`@materialId = (SELECT TOP (1) Id FROM Material ORDER BY Id)`。FK 天然安全，换环境/重灌主数据后无需改 seed。**若 dev 主数据为空，脚本打印明确提示并整段跳过（不报错）**，4号位 会看到 → 需先打通 ODS 主数据（转 2号位）。

### 偏离② ProductionDepartmentId：{3,5,7} 无法核验，改运行时取前 3 行
来件引「CreateDatabase.sql L161-200 已 seed 部门 {3,5,7}」，经核实该文件（`lps/LPS.APS.Engine/Database/CreateDatabase.sql`）此段实为**旧版 Machines/Jobs 示例表**，非当前 APS ProductionDepartment → 该引用**陈旧**，不能据此硬编码。

→ 采纳：`ProductionDepartment` 动态取前 3 行（不足回填第 1 行），对齐 PathId 1/2/3 三点部门维度演示。仍保持「路径维度 PathId 1/2/3 + 部门维度多部门」意图。

### 偏离③ ProcessType / StageCode 值：仅 dev 演示、无约束
- `ProcessType` 用 CLEANING/TRANSFER/TESTING/PACKAGING，超出冻结文档口径 `{MACHINING/ASSEMBLY/INSPECTION}`。经核 `ProcessType` 为**辅助分类标签**（走 ProcessTypeDict 配置化、骨架 IsActive=0、无 CHECK/FK、不参与 BOM↔Routing 对接与 1号位 排程），仅对齐前端 mock 展示，不影响 OPM 治理三态。
- `StageCode` 用 SMT/ASM/PKG，未校验是否在 StageDict（`StageCode` 无 FK，仅业务约定）。
- 上述两项均为 **dev-only 演示口径**，如需严格对齐生产契约请指示，我可另出一版收敛值域。

## 3、落库数据（实际插入后需 dev 库回读确认）

| OperationCode | 中文名 | ProcessType | StageCode | RouteCode | PathId | OperationPlanningMode | 标准工时(min) |
|---|---|---|---|---|---|---|---|
| OP10 | 精修 | MACHINING | SMT | DEFAULT | 1 | FINITE_RESOURCE | 30 |
| OP20 | 清洗 | CLEANING | SMT | DEFAULT | 1 | UNCONSTRAINED | 15 |
| OP30 | 转运 | TRANSFER | SMT | DEFAULT | 1 | WAIT_ONLY | 5 |
| OP40 | 装配 | ASSEMBLY | ASM | DEFAULT | 2 | FINITE_RESOURCE | 45 |
| OP50 | 老化测试 | TESTING | ASM | DEFAULT | 2 | WAIT_ONLY | 60 |
| OP60 | 包装 | PACKAGING | PKG | ALT | 3 | FINITE_RESOURCE | 10 |

> 部门列（ProductionDepartmentId）为运行时动态取第 1/2/3 行，具体值以 dev 库实际为准；SetupTime 全 0，IsActive=1，CreatedAt/UpdatedAt=GETDATE()。

## 4、验收对接（4号位 V1-V6）

- **V1** ✅ 文件路径 + 注释齐备（含 WARNING / Idempotent / no DDL / `-f 65001` 说明）。
- **V2** `SELECT COUNT(*) ... OP10..OP60` = **6**；echo 段末尾自带计数。
- **V3** `GET ?materialId=X` → data.length=6（X = 脚本返回的 `MaterialId` 实值）。
- **V4** 浏览器 `/aps/loading` 端到端：改 OP10 OPM → 保存 → 二次确认 → 落库。**注意 OPM 三态校验 422 由既有端点承担，与本 seed 无关**。
- **V5** 审计：`GET /api/audit?entityType=RoutingOperation&entityId=<OP10.Id>`（该审计由 T1 PUT 端点落库，与本 seed 无关）。
- **V6** 重跑一次 → 门卫跳过 → COUNT 仍 6。
- **前置阻塞识别**：若 V2 执行后仍 0 行（PRINT「跳过」），即 dev 缺 Material/ProductionDepartment 主数据 → **请立即回报，转 2号位 打通 ODS 主数据或补 bootstrap**。

## 5、遗留/待办

| # | 项 | 状态 |
|---|---|---|
| 1 | svn revision 回填（回执 §1） | 待用户提交后回填并补发 4号位 |
| 2 | 如需 ProcessType/StageCode 收敛到生产契约值域 | 待 4号位/0号位 指示，可再出一版 |
| 3 | dev 主数据为空时的 bootstrap（Material/ProductionDepartment 祖先） | 越 2号位 主数据域，需 0/2号位 认可后另立协助项 |