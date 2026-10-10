# 派单：Auth 库 DDL 缺陷与真源裁决 —— 3号位 → 0号位（PM）

> 收件：0号位（PM，拍板 Auth schema 真源 + 物理库对齐路径）
> 发件：3号位（应用编排 / 运行生命周期治理 / 认证权限）
> 抄送：2号位（Pegging + Scheduling，物理库建库方）
> 依据：F-G5 裁决「代码侧=真源」+ 集成测试 0 skip + 2026-09-07 仓库实测
> 日期：2026-09-07
> 性质：请求口径裁决 + 物理库迁移路径授权；红线——3号位 不动物理库、不碰 2号位，仅求定向。

---

## 结论一句话

Auth schema 当前**三源失联**——代码（F-G5 真源）≈ 物理库（2号位 建）≠ 仓库 DDL 文件（3号位 Owner，但漂移）。仓库 `Auth DDL v1.1` 有**字段漂移**、**缺第 14 张 GovernanceAuditLog 表**、**2号位 建表脚本未入库**三处缺陷，导致"没有一份跑完就和代码一致的 Auth DDL 可执行"。请求 0号位 拍板 R1（真源+授权重冻结）与 R2（物理库对齐路径）。

---

## 一、三处缺陷（仓库已核实）

### 缺陷① 字段漂移：DDL 文件 ≠ 代码真源

F-G5 已裁「代码侧=真源」，但 `APS_Auth数据库DDL_v1.1_20260901冻结对齐版.sql` 仍是旧口径。以 User 表为硬例：

| | 仓库 DDL v1.1（10 列） | 代码 `User.cs` 真源（17 字段） |
|---|---|---|
| 登录标识 | `LoginName` | `UserCode` + `UserName` |
| 是否启用 | `IsEnabled` | `Status` |
| 缺列 | — | `FactoryId` / `DepartmentId` / `LastLoginTime` / `LastLoginIP` / `FailedLoginAttempts` / `LockoutEnd` / `RefreshToken` / `RefreshTokenExpiry` |

其余表同理漂移：
- `Role`：DDL 无 `IsActive` / `UpdatedAt`
- `Permission`：DDL 无 `Module` / `Category` / `IsActive` / `UpdatedAt`
- `AuditLog`：DDL 缺 `UserCode` / `Module` / `EntityType` / `EntityId` / `OldValue` / `NewValue` / `IPAddress` / `UserAgent`

**后果**：全新库若执行 v1.1，字段与 `AuthDbContext` 对不上，EF 首次查询即报"列不存在"。

### 缺陷② 缺第 14 张 `GovernanceAuditLog` 表

DDL v1.1 header 明写"不推倒现有 **13 张表**"。但代码有 `GovernanceAuditLog` 实体 + `AuthDbContext` DbSet + Repository，物理库亦已落地（集成测试 0 skip = 复用生效）。该表 `CREATE TABLE` **未进任何仓库 SQL**（全仓库 grep 无 `GovernanceAuditLog_建表_*.sql`）。

### 缺陷③ 2号位 建表脚本未入库，三源失联

现有 APS_Auth 物理库为 **2号位 所建**（含 09-02 未入库的 GovernanceAuditLog 独立脚本）。三线各说各话：**代码（真源）≈ 物理库 ≠ 仓库 DDL 文件**。3号位 已收 Auth Owner，但仓库内没有一份与真源/物理库一致的完整 DDL 可依。

---

## 二、请 PM 拍板两件事

### R1：真源 + 授权重冻结

确认 Auth schema 单向以「代码侧=真源」（F-G5 已裁），并授权 3号位 产出 **`Auth DDL v1.2 代码对齐版`**（字段对齐代码 + 补 GovernanceAuditLog）作为**新冻结版**，废弃 v1.1 漂移口径。

| 选项 | 含义 |
|---|---|
| **A（推荐）** | 代码侧=真源，授权 3号位 出 v1.2 新冻结版（纯 3号位 自驱，不改 2号位、不动物理数据） |
| B | 维持 v1.1 冻结、反向改代码对齐 DDL（与 F-G5 相悖，不建议，会导致登录/刷新/锁定等代码回退） |

### R2：物理库对齐路径

现有 APS_Auth 物理库（2号位 建、已含种子/审计数据）如何对齐 v1.2：

| 选项 | 含义 | 适用 |
|---|---|---|
| **A（推荐）** | 开发/测试期直接重建：drop → 跑 v1.2 → 权限种子靠 `PermissionSeedService` 启动播种 | 干净、无增量 ALTER 漏项 |
| B | 增量 ALTER 补齐差异 | 需"不可重建"约束时；易漏、风险高 |
| C | 暂不动物理库，仅文档先行对齐 | 风险：文档/库/代码继续漂移 |

---

## 三、附带（非独立裁决，随 R1 收口）

GovernanceAuditLog 建表脚本建议由 **3号位 直接并入 v1.2**，不必回头找 2号位 回收入库（避免为一张表再跨号位转一圈）。

---

## 回执表

| # | 请 PM 答复 | 回执 |
|---|---|---|
| R1 | Auth schema 真源 + 授权重冻结 | □ A（代码=真源，3号位 出 v1.2 新冻结版）□ B（改代码对齐 DDL）：____ |
| R2 | 物理库对齐路径 | □ A（开发期重建）□ B（增量 ALTER）□ C（仅文档先行）：____ |
| 附 | GovernanceAuditLog 建表并入 v1.2，由 3号位 补录 | □ 同意 □ 责成 2号位 回收入库 |