# APS V1 业务范围产品族（ProductFamily）/ 部门（Department）维度 —— 已裁执行清单

**版本**：v1.0
**日期**：2026-09-09（北京时间）
**性质**：承接 0号位 2026-09-07 已裁回执（Q1=A；Q1附=同源；Q2=C；并案=否）。本单**不是**新的裁决请求，而是把已裁口径落成 3号位 执行清单。**替代**作废的《判决请求_3号位致0号位_v1.0_20260909.md》。

---

## 一、裁决依据（已裁，不再催办）

| 点 | 0号位 已裁 |
|---|---|
| ProductFamily → DomainKey | A：以 DomainDefinition 为唯一权威源，3号位 自驱（不找 ERP/ODS、不建表） |
| 2号位 归域同源 | 必须同源；异源 = 2号位 代码整改（非待裁决） |
| Department | C：保持正式 Scope，禁止建「Department→Domain」桥；由 5→2 查询链按 ProductionDepartment 事实过滤，3号位 只输出 `Departments[]` |
| 并案 MSC Q1/Q2 | 否（MaterialStageDeptContext 部门码正规化桥 ≠ F-G4 过滤，是两座不同桥） |
| Factory | 退出 `FACTORY_{id}` 权威推导，DomainKey 只认 DomainDefinition |
| G4 查询 Owner | 迁 5号位；3号位 不扩建普通结果查询 |

## 二、执行清单（3号位 侧）

| # | 事项 | 落地动作 | 依据 | 状态 |
|---|---|---|---|---|
| 1 | ProductFamily → DomainKey 自驱 | 范围解析接 DomainDefinition（ScopeType=FAMILY / FACTORY_FAMILY，含 ProductFamilyId↔DomainKey），不再 fail-closed 拒绝 ProductFamily | Q1=A | 非3号位遗留（fail-closed 过渡，正式闭环随 5→2 迁移由 5号位/2号位 按 DomainDefinition 落地） |
| 2 | `/me` 返回完整四维 Scope | AuthController `/api/Auth/me` 补返 Factory/ProductFamily/Department/Domain；DataScopeService 正确输出 `Departments[]` | 代码审核 P1；§六 | ✅ 已完成（2026-09-09 核实） |
| 3 | 退出 `Factory → FACTORY_{id}` | 移除把 Factory 拼 `FACTORY_{id}` 当 DomainKey 的推导；DomainKey 只认 DomainDefinition（FAMILY 域无 FactoryId，Factory 权限不能无损转 DomainKey） | 0号位 补充整改 | ✅ 已完成（2026-09-09 核实） |
| 4 | 与 2号位 核同源 | 上报 2号位 核对单：排程归域是否消费 DomainDefinition；异源由 2号位 整改 | Q1附=同源 | 待上报 |

> 边界说明：G4 普通查询逐步迁 5号位，落地中 #1/#3 若已随迁移移交 5号位，则执行者相应变为 5号位；3号位 始终保留「认证 / RBAC / Business Scope 输出（↓ #2）/ Run 治理」。开工前先定当期 G4 查询 Owner。

## 三、红线声明

3号位 不建 Department→Domain 桥、不并案 MSC、不找 ERP/ODS 建映射表、不改 2/5号位 代码与数据；仅按已裁口径落地自己职责内的解析 / 输出 / 整改与核对上报。

---

## 四、跟踪表

| # | 动作 | 结果 | 备注 |
|---|---|---|---|
| 1 | ProductFamily 接 DomainDefinition（非3号位遗留） | 随 5→2 迁移 | — |
| 2 | `/me` 补四维 Scope | ✅ 完成 | — |
| 3 | 退出 `FACTORY_{id}` | ✅ 完成 | — |
| 4 | 2号位 同源核对 | □ 已回执 □ 待回 | 唯一剩余事项 |

---

以上。四件事均可按已裁口径立即动手，无裁决阻塞。