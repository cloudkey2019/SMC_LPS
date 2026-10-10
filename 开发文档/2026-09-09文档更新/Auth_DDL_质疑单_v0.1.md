# Auth 数据库 DDL 质疑单

> 版本：v0.1
> 提交方：3号位（Auth / RBAC / Scope 治理）
> 提交日期：2026-09-09
> 裁决方：0号位
> 对象：`开发文档/2026-09-07文档更新/APS_Auth数据库DDL_v1.2_20260907冻结对齐版.sql`
> 状态：待裁决

---

## 一、总览

| 编号 | 事项 | 性质 | 请求 |
|---|---|---|---|
| D-1 | `RolePermission` / `RoleDataScope` 缺 `AssignedBy`（行级溯源不对称） | 质疑 → 申请加列 | 加列到 DDL |
| D-2 | 主数据表软删除口径缺失/不一致 | 质疑 → 申请加列 | 加列到 DDL |
| D-3 | 联合主键 vs 代理 Id（`UserRole`/`RolePermission` 实体残留 `Id`） | 维持项（不质疑） | 不改 DDL，实体对齐收口 |

---

## 二、D-1：`RolePermission` / `RoleDataScope` 缺 `AssignedBy`

### 现状

四张分配关联表中，**「用户维度」有 `AssignedBy`、「角色维度」没有**：

| 分配动作 | 关联表 | DDL 有 `AssignedBy`? | 代码写 `AssignedBy`? |
|---|---|---|---|
| 用户 ← 角色 | `UserRole` | ✅（`DDL:130`） | ✅（`RbacManagementService:183`） |
| 用户 ← 范围 | `UserDataScope` | ✅（`DDL:193`） | ✅（`RbacManagementService:456`） |
| 角色 ← 权限 | `RolePermission` | ❌ | ❌（`RbacManagementService:292` 只写 `RoleId,PermissionId`） |
| 角色 ← 范围 | `RoleDataScope` | ❌ | ❌（`RbacManagementService:481` 只写 `RoleId,ScopePolicyId`） |

### 理由

1. **提权/降权是最高敏感动作，却唯一缺行级证据。** 角色 ← 权限是 RBAC 核心授权链路，`AssignedAt`（何时）有、`AssignedBy`（谁）没有，只能靠 AuditLog 反查，缺行级兜底。
2. **这是「漏列」而非「有意设计」。** 同一套 RBAC 分配，用户维度两支有、角色维度两支没有，是字段收口时漏对齐。补齐成本 = 一个 `INT NULL` + 一条 FK，无新语义。
3. **与事件级审计互补。** `WriteAuditAsync("AssignPermissions", ...)`（`RbacManagementService:300`）记的是「动作概要」；`AssignedBy` 记的是「这一行是谁配的」，防审计日志单点丢失/未写。

### 请求

- DDL：`RolePermission`、`RoleDataScope` 各补一列
  `AssignedBy INT NULL` + `FOREIGN KEY (AssignedBy) REFERENCES [User](Id)`。
- 代码（裁决通过后）：`INSERT` 补齐 `@OperatorId`（完全对齐 `RbacManagementService:183` / `:456` 已有写法）。

---

## 三、D-2：软删除口径缺失/不一致

### 现状（修正口径：不是「每张表都没有」，而是「仅 User 有真软删，其余口径断裂」）

| 类别 | 表 | 软删现状 |
|---|---|---|
| 主数据 | `User` | ✅ `IsDeleted`（`DDL:55`） |
| 主数据 | `Role` | ⚠️ 仅 `IsActive` 启停，无 `IsDeleted`（`DDL:85`） |
| 主数据 | `Permission` | ⚠️ 仅 `IsActive`，无 `IsDeleted`（`DDL:109`） |
| 主数据 | `DataScopePolicy` | ❌ 无任何软删/启停（`DDL:167-181`） |
| 关联表 | `UserRole` / `RolePermission` / `UserDataScope` / `RoleDataScope` | ❌ 硬删（`DELETE FROM` 全量替换） |
| 审批 | `ApprovalFlow` / `ApprovalNode` / `ApprovalRule` | 靠 `Status` / `IsEnabled` 生命周期，无 `IsDeleted` |
| 追加型 | `AuditLog` / `ApprovalRecord` | 追加日志，语义上不需软删 |

### 理由

1. **同一服务、三种删除语义，是典型口径裂缝。** `RbacManagementService`：删 User 走 `IsDeleted=1` 软删（`:160`）；删 Role 仅停用（`:275`，语义是 `IsActive=0`）；删 `DataScopePolicy` 直接物理 `DELETE`（`:435`）。同一「删除」动作，用户可恢复、数据范围不可恢复。
2. **误删不可恢复。** `DataScopePolicy` 物理删除 + 级联清理引用（`:432-435` 显式删除 `UserDataScope`/`RoleDataScope` 后硬删主表），一旦删错 scope，级联清理引用、历史全灭。
3. **「先 DELETE 后 INSERT」全量替换丢失历史。** 四张关联表均先清空再重建（`:179` / `:288` / `:452` / `:477`），天然无法回答「该用户/角色曾经拥有过哪些权限」，合规追溯只能靠「当时审计日志还在不在」。

### 请求（分层，不搞一刀切）

- **主数据表**（`Role` / `Permission` / `DataScopePolicy`）补 `IsDeleted`（可选再加 `DeletedAt` / `DeletedBy`）——这是对象生命周期，最该软删。
- **关联表**不强推软删（避免 SQL Server filtered unique index 与 `(UserId, RoleId)` 唯一约束打架的复杂度）；改为依赖「撤销/重分配已落 AuditLog」兜底（现已落 `:191` / `:300` / `:464` / `:489`）。若要行级，再考虑 `RevokedAt`，非必须。
- **追加型**（`AuditLog` / `ApprovalRecord`）不加。

---

## 四、D-3：联合主键维持现状（不构成质疑）

### 结论

**维持 0号位「联合主键、不设代理 Id」的裁决，不补 `Id`。** 本条不提交质疑，仅作口径说明与实体收口。

### 为何不质疑

1. **联查不会用到关联表自身的 Id**——现有联查全部走 FK 列：
   - `PermissionCodeRepository:25,44`：`join rp on p.Id equals rp.PermissionId`
   - `PermissionRepository:76`：`join rp on p.Id equals rp.PermissionId`
   - `RbacManagementService:533,545`：`JOIN RolePermission rp ON rp.RoleId = r.Id`
   三处 `ON` 均为 FK（`PermissionId`/`RoleId`），`Id` 从未参与。加 Id 是死列，联查便利性不增。
2. **无任何引用**：全仓无 `.UserRole.Id` / `.RolePermission.Id` 读写，无 FK 指向 → 纯死字段。
3. **加 Id 是反向成本**：真以 Id 为主键，须再补 `UNIQUE(UserId, RoleId)` 才能保住「不重复」约束 → 两套键、多索引、零收益。
4. **`AssignedAt`/`AssignedBy` 仅是溯源戳**，不足以把关联行升格为一等实体；联合主键 `(UserId, RoleId)` 本身可寻址（`.Where(...)` / EF `Find(new { x, y })`），不存在「无法定位一行」。

### 实体收口（只改代码，不涉 DDL）

| 文件 | 动作 |
|---|---|
| `LPS.APS.Core/Entities/Auth/UserRole.cs:9` | 删 `public int Id { get; set; }` |
| `LPS.APS.Core/Entities/Auth/RolePermission.cs:9` | 删 `public int Id { get; set; }` |
| `LPS.APS.Core/Entities/Auth/RolePermission.cs:13` | `AssignedBy` 去留 **取决于 D-1 裁决**：批准补列 → 保留；驳回 → 一并删除 |

> 已核对：`UserDataScope` / `RoleDataScope` 实体本就无 `Id`，与 DDL 一致，无需处理。

---

## 五、请 0号位 裁决事项

1. **D-1**：是否批准 `RolePermission` / `RoleDataScope` 补 `AssignedBy`？
2. **D-2**：是否批准主数据表补 `IsDeleted`（是否同时加 `DeletedAt` / `DeletedBy`）？
3. **D-3**：是否认可「联合主键维持现状、实体删残留 `Id`」？

## 六、影响与依赖

- 均为「冻结 v1.2 后的一次性加列」，开发/测试期口径为直接重建 `APS_Auth`（R2=A「不维护 ALTER 链」），无需任何 `ALTER` 迁移脚本。
- 代码侧随裁决结果反向对齐（`AuthDbContext` / 实体 / Repository 收口），不跨号位、不改排程核心库。