# 答复：Position5 Scope 校验 —— Department 维度真源与“是否新建部门字段”

> 收件：5号位（业务事实/控制器）、PM（存档）
> 发件：3号位（认证 + 角色 + 功能权限 + 业务范围权限）
> 依据：5号位 对 `ProcurementManualEtaOverride.DepartmentCode`（问题④）的后续疑问
> 日期：2026-09-14

---

## 一、结论一句话

**不用新建 `DepartmentCode` 字段**——ManualEta 的部门从 `MaterialSupplyContext` 按 (物料 + 仓库) JOIN 派生即可。同时「Department = 生产部门」是我 3号位 在代码注释里的消歧写法，**不是冻结口径**：冻结真源只有 `Department` 这个字符串字面量（权限设计方案 §9.1 + Auth DDL v1.3 `CK_DataScope_Type`），其 ScopeValue 到底取哪套部门编码，从未被文档冻结。

---

## 二、Department 真源答复（三层，无“=生产部门”冻结句）

| 层 | 真源 | 原文 | 出处 |
|---|---|---|---|
| 维度名值域 | 权限设计方案 §9.1 | `Factory / ProductFamily / Department / Domain / Global` | `开发文档/2026-09-07文档更新/APS_权限与审批设计方案_v1.1_20260901冻结对齐版.md:363-371` |
| 数据库强制 | Auth DDL v1.3 `CK_DataScope_Type` | `ScopeType IN ('Factory','ProductFamily','Department','Domain','ResourceOrgGroup','Global')` | `开发文档/2026-09-09文档更新/APS_Auth数据库DDL_v1.3_20260909冻结对齐版.sql:177-178` |
| 等价写法（暗示） | 同设计方案 §2.1 / §18 | `Department / ProductionDepartment` 斜杠并列 | 同上 `:26`、`:701` |

- 三处都只有 `Department` 字面量，**没有任何一处定义 Department 的 ScopeValue 编码源**。
- 代码 `LPS.APS.Core/Authorization/DataScopeTypes.cs:15` 的「部门（生产部门 ProductionDepartment）」是 3号位 的注释消歧，依据是“系统唯一部门字典 = `APS_Production.ProductionDepartment`”（组织部门/成本中心在 v5.0.16 已排除，DDL 注明“不做组织树”）。**属合理推断，非冻结文本**，故 5号位 质疑成立。

---

## 三、不新建字段：部门已能从 MaterialSupplyContext 派生

`ProcurementManualEtaOverride` 已含 `MaterialId` + `ReceivingWarehouse`（`LPS.APS.Core/Dto/ProcurementManualEtaOverride.cs:30、40`），恰是 `MaterialSupplyContext` 的业务键（MaterialCode + WarehouseCode），后者已按供给方式挂好三类责任部门（`LPS.APS.Core/Entities/Aps/MaterialSupplyContext.cs:47-66`）：

| SupplyMode | 责任部门字段 | 语义 |
|---|---|---|
| PURCHASE | `ProcurementDeptCode` | 采购责任部门 |
| MAKE | `DefaultProductionDeptCode` / `DefaultProductionDepartmentId`（FK→`ProductionDepartment.Id`） | 生产责任部门 |
| OUTSOURCE | `OutsourceDeptCode` | 委外责任部门 |

→ 5号位 只需 `JOIN MaterialSupplyContext` 取部门；在 ManualEta 表冗余一个部门码 = 造第二套部门真值，会与上述字段漂移（同 Auth DDL v1.3 禁止“第二套业务范围真值”原则）。

---

## 四、需 5号位/PM 先澄清的一点

`ProcurementManualEta` 是**采购**人工 ETA，语义应对齐 **`ProcurementDeptCode`（采购责任部门）**；而 5号位 举例 `MACHINING_DEPT / ASSEMBLY_DEPT / INJECTION_DEPT` 是**生产工序部门**，属 `DefaultProductionDeptCode` 侧。两个口径对不上。

请 5号位/PM 拍板：

| 问题 | 选项 | 影响 |
|---|---|---|
| ManualEta 的“部门”用于 Scope 校验时应指 | □ 采购责任部门 → 对齐 `ProcurementDeptCode` | 该码是否与 `ProductionDepartment.DeptCode` 同源字典，需另核对 |
| | □ 生产责任部门 → 对齐 `DefaultProductionDeptCode` / `ProductionDepartment` | 走既有 FK `DefaultProductionDepartmentId`，字典明确 |

---

## 五、回执表

| 5号位疑问 | 3号位答复 |
|---|---|
| Department 维度真源从哪来 | 维度名 = 字符串 `Department`（权限设计 §9.1 + Auth DDL `CK_DataScope_Type`）；“=生产部门”是 3号位 注释消歧，非冻结 |
| 能否直接对齐 `DataScopeTypes.Department` 校验 | 不能直接：5号位 的 `_DEPT` 例码 ≠ `ProductionDepartment.DeptCode`（`CN_MACH_DEPT_01`）一套；且采购场景语义对不上生产部门 |
| 是否新建 DepartmentCode 字段 | **不建议**。走 `MaterialSupplyContext` JOIN 派生，避免第二套部门真值 |