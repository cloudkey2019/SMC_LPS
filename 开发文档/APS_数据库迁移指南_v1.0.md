# APS 数据库迁移指南

**版本**：v1.0  
**日期**：2026-03-31  
**适用人员**：2号位（唯一有权执行数据库变更的人）  
**数据库**：SQL Server 2019

---

## 🚨 核心原则

1. **只有2号位有权修改数据库结构**（红线4）
2. **每次变更必须有迁移脚本 + 回滚脚本**
3. **生产环境变更前必须在 UAT 验证**
4. **变更前必须备份数据库**
5. **排程结果主表禁止 UPDATE/DELETE，只允许 Append-Only**

---

## 📁 迁移脚本管理

### **目录结构**

```
database/
├── schema/                          # 基线DDL
│   ├── APS_数据库表结构设计_v2.0.sql
│   └── APS_Auth数据库DDL_v1.0.sql
├── migrations/                      # 增量迁移脚本
│   ├── V001__initial_schema.sql
│   ├── V002__add_permission_tables.sql
│   ├── V003__add_inventory_balance.sql
│   └── ...
├── rollbacks/                       # 回滚脚本
│   ├── V001__rollback.sql
│   ├── V002__rollback.sql
│   └── ...
├── seeds/                           # 初始化数据
│   ├── seed_roles_permissions.sql
│   └── seed_test_data.sql
└── migration_log.md                 # 迁移日志
```

### **脚本命名规范**

```
V{序号}__{简短描述}.sql
```

**示例**：
- `V001__initial_schema.sql`
- `V002__add_permission_tables.sql`
- `V003__add_inventory_balance_index.sql`
- `V004__alter_planversion_add_userid.sql`

### **脚本模板**

```sql
-- =============================================================================
-- 迁移脚本：V00X__描述
-- 日期：2026-XX-XX
-- 作者：2号位
-- 目的：XXX
-- 影响表：XXX
-- 预计执行时间：X分钟
-- 回滚脚本：V00X__rollback.sql
-- =============================================================================

-- 前置检查
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TableName') AND name = 'NewColumn')
BEGIN
    -- 执行变更
    ALTER TABLE TableName ADD NewColumn NVARCHAR(50) NULL;

    PRINT '✅ 迁移 V00X 执行成功';
END
ELSE
BEGIN
    PRINT '⚠️ 迁移 V00X 已执行过，跳过';
END
GO
```

### **回滚脚本模板**

```sql
-- =============================================================================
-- 回滚脚本：V00X__rollback
-- 日期：2026-XX-XX
-- 作者：2号位
-- 目的：回滚 V00X 的变更
-- =============================================================================

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('TableName') AND name = 'NewColumn')
BEGIN
    ALTER TABLE TableName DROP COLUMN NewColumn;
    PRINT '✅ 回滚 V00X 执行成功';
END
GO
```

---

## 🔄 迁移执行流程

### **步骤1：编写迁移脚本**

```
1. 在 database/migrations/ 下创建新脚本
2. 在 database/rollbacks/ 下创建对应回滚脚本
3. 更新 database/migration_log.md
```

### **步骤2：DEV 环境验证**

```sql
-- 在本地开发数据库执行
-- 1. 备份
BACKUP DATABASE APS_Production TO DISK = 'D:\Backup\APS_Production_before_V00X.bak';

-- 2. 执行迁移
:r database\migrations\V00X__description.sql

-- 3. 验证
-- 检查表结构是否正确
-- 运行应用确认功能正常
-- 运行单元测试确认通过

-- 4. 测试回滚
:r database\rollbacks\V00X__rollback.sql

-- 5. 再次执行迁移（验证可重复执行）
:r database\migrations\V00X__description.sql
```

### **步骤3：UAT 环境验证**

```
1. 在 UAT 数据库执行迁移脚本
2. 部署对应版本的应用代码
3. 执行验收测试
4. 如果失败，执行回滚脚本
```

### **步骤4：PROD 环境执行**

```
1. 获得部署审批
2. 备份生产数据库（完整备份）
3. 在维护窗口执行迁移脚本
4. 部署对应版本的应用代码
5. 验证核心功能
6. 如果失败，执行回滚方案
```

---

## 📊 常见变更类型与模板

### **类型1：新增表**

```sql
-- 迁移
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('NewTable'))
BEGIN
    CREATE TABLE NewTable (
        Id BIGINT PRIMARY KEY IDENTITY(1,1),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL DEFAULT 1,
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETDATE()
    );

    CREATE UNIQUE INDEX IX_NewTable_Code ON NewTable(Code);
END
GO

-- 回滚
DROP TABLE IF EXISTS NewTable;
GO
```

### **类型2：新增列**

```sql
-- 迁移
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ExistingTable') AND name = 'NewColumn')
BEGIN
    ALTER TABLE ExistingTable ADD NewColumn NVARCHAR(100) NULL;
END
GO

-- 回滚
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ExistingTable') AND name = 'NewColumn')
BEGIN
    ALTER TABLE ExistingTable DROP COLUMN NewColumn;
END
GO
```

### **类型3：新增索引**

```sql
-- 迁移
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Table_Column')
BEGIN
    CREATE INDEX IX_Table_Column ON TableName(ColumnName);
END
GO

-- 回滚
DROP INDEX IF EXISTS IX_Table_Column ON TableName;
GO
```

### **类型4：修改存储过程**

```sql
-- 迁移（存储过程始终用 CREATE OR ALTER）
CREATE OR ALTER PROCEDURE sp_ProcedureName
    @Param1 NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    -- 新逻辑
END
GO

-- 回滚（恢复旧版本）
CREATE OR ALTER PROCEDURE sp_ProcedureName
    @Param1 NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;
    -- 旧逻辑
END
GO
```

---

## 🚨 变更申请流程

### **其他号位需要数据库变更时**

```
1. 填写变更申请（在团队群中）：
   --------------------------------
   【数据库变更申请】
   申请人：X号位
   变更类型：新增列/新增表/修改存储过程/...
   影响表：XXX
   变更原因：XXX
   期望完成时间：XXX
   --------------------------------

2. 2号位评审变更：
   - 是否符合设计规范
   - 是否影响现有功能
   - 是否需要数据迁移
   - 性能影响评估

3. 2号位编写迁移脚本并执行

4. 2号位更新相关文档：
   - DDL 文件（APS_数据库表结构设计_v2.0.sql）
   - 字段说明文档
   - 防腐层文档（如涉及）
```

---

## ✅ 迁移检查清单

**编写脚本时**：
- [ ] 脚本有前置检查（幂等性）
- [ ] 有对应的回滚脚本
- [ ] 脚本头部有注释说明
- [ ] 已更新 migration_log.md

**执行前**：
- [ ] 已在 DEV 环境验证
- [ ] 已在 UAT 环境验证
- [ ] 已备份数据库
- [ ] 已通知相关人员

**执行后**：
- [ ] 表结构符合预期
- [ ] 应用功能正常
- [ ] 测试通过
- [ ] 已更新文档（DDL + 字段说明）

---

**维护责任人**：2号位  
**最后更新**：2026-03-31
