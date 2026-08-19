# APS 架构终极修复完全体总结 v2.4

**修复日期**：2026-03-11  
**审查人**：第三方架构师（三号架构师 + AI架构师）  
**修复人**：Cascade AI  
**修复级别**：🔴 P0（7项）+ 🟡 P1（5项）

---

## 📋 修复概览

基于第三方架构师的四轮深度审查，对《APS数据架构与防腐层设计方案》进行了**七大P0级SQL硬伤**和**五大P1级架构洁癖**的终极修复完全体，解决了SQL Server语法兼容性、DDL执行时序、视图契约错位、幽灵物料、跨库寻址、一物多仓MERGE冲突、BOMNO重复膨胀、API契约撕裂等问题。

---

## 🔴 P0-7: sp_SyncMaterialMapping的MERGE条件重构（一物多仓精确制导）

### **问题诊断**：
SQL Server的MERGE语句有一个极其严苛的物理限制——**目标表（Target）的一行记录，在一次MERGE操作中绝对不允许被匹配并更新多次**。既然我们已经承认了同一个MaterialCode在ERP里会有多个仓库（Warehouse）对应不同的记录，如果ON条件只写MaterialCode，遇到一物多仓的情况，SQL Server引擎会直接抛出著名的异常：

```
The MERGE statement attempted to UPDATE or DELETE the same row more than once.
```

**灾难场景**：
1. ERP中物料A在WH-01仓库的MasterID是100001
2. ERP中物料A在WH-02仓库的MasterID是100002
3. MERGE语句的ON条件只匹配MaterialCode，没有匹配Warehouse
4. SQL Server发现物料A的同一条记录被匹配了两次（WH-01和WH-02）
5. 抛出异常，MERGE失败，物料映射同步中断

### **手术方案**：
在ON条件中，把"仓库（Warehouse）"这个维度加进去，实现绝对的精确制导。

### **最终DDL修正**：
```sql
-- 修复前（会导致"同一行被多次更新"异常）
MERGE INTO MaterialMapping AS target
USING (...) AS source
ON target.MaterialCode = source.MaterialCode 
   AND target.Source = 'ERP' 
   AND target.IsCurrent = 1
-- 问题：一物多仓时，同一个MaterialCode会匹配多次

-- 修复后（精确制导）
MERGE INTO MaterialMapping AS target
USING (...) AS source
ON target.MaterialCode = source.MaterialCode 
   AND target.Source = 'ERP' 
   AND target.IsCurrent = 1
   AND ISNULL(target.ERP_Warehouse, '') = ISNULL(source.Warehouse, '')  -- ⚠️ 加入仓库维度
-- 解决：每个仓库的映射独立匹配，不会冲突
```

### **修复影响**：
| 场景 | 修复前 | 修复后 |
|------|--------|--------|
| 一物单仓 | 正常 | 正常 |
| 一物多仓 | MERGE异常，同步失败 | 正常，每个仓库独立同步 |

### **修改文件**：
- `APS_数据库表结构设计_v2.0.sql`（第1106-1136行）

---

## 🟡 P1-4: MES_API_BOM_Request_Detail增加去重约束（防止BOMNO重复膨胀）

### **问题诊断**：
如果1000个订单都指向同一个基础BOMNO，你们把这1000个重复的BOMNO推进明细表，CTE展开时就会把这棵树重复计算并输出1000遍。原本350万行的结果，会瞬间膨胀到35亿行，把ODS的硬盘和内存直接撑爆。

**灾难场景**：
1. 1000个订单都使用同一个BOMNO（例如"BOM-COMMON-001"）
2. SqlBulkCopy推送时没有去重，1000个重复的BOMNO全部插入明细表
3. CTE展开时，每个BOMNO都会展开一次，结果重复1000遍
4. 原本350万行的结果膨胀到35亿行
5. ODS库硬盘和内存直接撑爆，数据库崩溃

### **手术方案**：
双管齐下。数据库层加唯一索引防呆，C#应用层在推送前强制Distinct()去重。

### **最终DDL修正**：
```sql
-- 数据库层：强制要求同一批次内的BOMNO绝对唯一
CREATE TABLE MES_API_BOM_Request_Detail (
    Id BIGINT PRIMARY KEY IDENTITY(1,1),
    BatchNo NVARCHAR(50) NOT NULL,
    BOMNO NVARCHAR(50) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    FOREIGN KEY (BatchNo) REFERENCES MES_API_BOM_Request(BatchNo),
    
    -- ⚠️ P1-4修复：强制要求同一批次内的BOMNO绝对唯一
    CONSTRAINT UQ_BOMRequestDetail_BatchBOMNO UNIQUE (BatchNo, BOMNO)
);
```

### **C#研发纪律**：
```csharp
// APS在执行SqlBulkCopy推送前，必须在内存中执行去重
var activeBOMNOs = orders
    .Select(o => o.BOMNO)
    .Distinct()  // ⚠️ 强制去重
    .ToList();

// 然后再推送到数据库
await SqlBulkCopyAsync(activeBOMNOs, "MES_API_BOM_Request_Detail");
```

### **修复影响**：
| 场景 | 修复前 | 修复后 |
|------|--------|--------|
| 1000个订单，1000个不同BOMNO | 正常 | 正常 |
| 1000个订单，1个相同BOMNO | 展开结果膨胀1000倍（35亿行） | 去重后只展开1次（350万行） |

### **修改文件**：
- `APS_数据库表结构设计_v2.0.sql`（第68-88行）

---

## 🟡 P1-5: 物料映射API返回结构重构（单对象→列表）

### **问题诊断**：
既然底层数据库是一物多仓（多条记录），API却返回一个单对象`{ ... }`，前端和内部调用者拿到的永远是一笔糊涂账。接口契约与物理模型严重撕裂。

**灾难场景**：
1. 数据库中物料A有两条记录（WH-01和WH-02）
2. API返回单对象，只能返回其中一条（例如WH-01）
3. 调用者不知道还有WH-02的映射存在
4. 库存扣减时只查询WH-01，导致WH-02的库存无法使用
5. 明明有库存，却提示缺料，影响排程结果

### **手术方案**：
将单对象返回，强制修改为**列表（Array）**返回。

### **修正后的API契约**：
```json
// 修复前（单对象）
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialCode": "RAW-STEEL-001",
    "erpMasterID": 100001,
    "erpWarehouse": "WH-01",
    ...
  }
}

// 修复后（列表）
{
  "code": 200,
  "message": "Success",
  "data": [
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100001,
      "erpWarehouse": "WH-01",
      ...
    },
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100002,
      "erpWarehouse": "WH-02",
      ...
    }
  ]
}
```

### **业务逻辑端必须配合《库存双源汇聚与优先级判定表》进行二次筛查**：
```csharp
// 获取物料映射列表
var mappings = await GetMaterialMappingAsync("RAW-STEEL-001");

// 根据库存优先级判定表，选择合适的仓库
var selectedMapping = mappings
    .OrderBy(m => GetWarehousePriority(m.erpWarehouse))  // 按优先级排序
    .FirstOrDefault();

// 使用选定的MasterID去扣库存
await DeductInventoryAsync(selectedMapping.erpMasterID, selectedMapping.erpWarehouse, quantity);
```

### **扩展查询参数**：
如果只需要单个仓库的映射，可以使用`warehouse`参数精确查询：
```bash
GET /api/internal/v1/material-mapping/RAW-STEEL-001?warehouse=WH-01
```

### **修改文件**：
- `APS_应用层API接口规范_v2.0.md`（第411-482行）

---

## 📊 v2.4新增修复影响评估

### **数据库表结构变更**：
| 表名 | 变更类型 | 影响 |
|------|---------|------|
| MES_API_BOM_Request_Detail | 新增唯一约束 | UQ_BOMRequestDetail_BatchBOMNO |

### **存储过程变更**：
| 存储过程 | 变更类型 | 影响 |
|----------|---------|------|
| sp_SyncMaterialMapping | 修改ON条件 | 加入Warehouse维度，避免一物多仓MERGE冲突 |

### **API接口变更**：
| 接口 | 变更类型 | 影响 |
|------|---------|------|
| GET /api/internal/v1/material-mapping/{materialCode} | 返回结构 | 从单对象改为列表 |

### **新增场景覆盖**：
| 场景 | 修复前 | 修复后 |
|------|--------|--------|
| 一物多仓MERGE | MERGE异常 | 正常同步 |
| BOMNO重复推送 | 展开结果膨胀1000倍 | 去重后正常 |
| API返回一物多仓 | 只返回一条，信息丢失 | 返回所有仓库映射 |

---

## 🎯 开发团队行动清单（v2.4新增）

### **2号位（技术负责人）**：
- [ ] 修改sp_SyncMaterialMapping存储过程，ON条件加入Warehouse维度
- [ ] 修改MES_API_BOM_Request_Detail表结构，增加唯一约束
- [ ] 修改物料映射API，返回结构从单对象改为列表
- [ ] 验证一物多仓场景下MERGE是否正常

### **3号位（调度编排器）**：
- [ ] 修改SqlBulkCopy推送逻辑，增加Distinct()去重
- [ ] 修改物料映射API调用逻辑，适配列表返回结构
- [ ] 配合《库存双源汇聚与优先级判定表》进行二次筛查

### **5号位（业务规则引擎）**：
- [ ] 修改库存扣减逻辑，支持多仓库映射
- [ ] 实现仓库优先级判定逻辑
- [ ] 验证一物多仓场景下库存扣减是否正确

### **测试团队**：
- [ ] 测试一物多仓场景：同一个MaterialCode在多个仓库的映射同步
- [ ] 测试BOMNO去重：1000个订单指向同一个BOMNO，验证去重是否生效
- [ ] 测试API返回结构：验证返回列表是否包含所有仓库映射
- [ ] 压力测试：验证去重后展开结果是否正常（350万行而非35亿行）

---

## 🛡️ 新增架构红线（累计15条）

**红线15**：严禁在MERGE的ON条件中遗漏关键维度（如Warehouse）  
- **违规后果**：一物多仓时触发"同一行被多次更新"异常，MERGE失败
- **正确做法**：ON条件必须包含所有唯一性维度（MaterialCode + Source + Warehouse）

---

## 📁 修改文件清单（v2.4新增）

| 序号 | 文件名 | 修改内容 | 状态 |
|------|--------|---------|------|
| 1 | `APS_数据库表结构设计_v2.0.sql` | P0-7：MERGE条件重构 | ✅ 已完成 |
| 2 | `APS_数据库表结构设计_v2.0.sql` | P1-4：BOM明细表去重约束 | ✅ 已完成 |
| 3 | `APS_应用层API接口规范_v2.0.md` | P1-5：物料映射API返回结构重构 | ✅ 已完成 |
| 4 | `APS_架构终极修复完全体总结_v2.4.md` | v2.4修复总结 | ✅ 已完成 |

---

## ✅ 验收标准（v2.4新增）

### **P0-7验收**：
- [ ] 一物多仓场景下，MERGE不再抛出"同一行被多次更新"异常
- [ ] 每个仓库的映射独立同步，互不干扰
- [ ] 物料A在WH-01和WH-02的映射都能正常同步

### **P1-4验收**：
- [ ] 同一批次内，重复的BOMNO无法插入（唯一约束生效）
- [ ] C#代码在推送前执行Distinct()去重
- [ ] 1000个订单指向同一个BOMNO，展开结果只有350万行（而非35亿行）

### **P1-5验收**：
- [ ] 物料映射API返回列表而非单对象
- [ ] 一物多仓场景下，API返回所有仓库的映射
- [ ] 业务逻辑端能够根据优先级判定表选择合适的仓库

---

## 📌 总结

本次v2.4终极修复完全体解决了**三个关键的P0/P1级问题**：

### **修复亮点**：
1. **一物多仓精确制导**：MERGE条件加入Warehouse维度，避免"同一行被多次更新"异常
2. **BOMNO去重防呆**：数据库唯一约束 + C#应用层去重，双重保障
3. **API契约修正**：返回结构从单对象改为列表，支持一物多仓场景

### **累计修复**：
- **v2.1**：首轮修复（4个P0 + 1个P1）
- **v2.2**：终极修复（4个P0 + 3个P1）
- **v2.3**：终极修复补完（6个P0 + 3个P1）
- **v2.4**：终极修复完全体（**7个P0** + **5个P1**）

### **下一步行动**：
1. ✅ 2号位修改sp_SyncMaterialMapping存储过程
2. ✅ 2号位修改MES_API_BOM_Request_Detail表结构
3. ✅ 2号位修改物料映射API
4. ✅ 3号位修改SqlBulkCopy推送逻辑
5. ✅ 5号位修改库存扣减逻辑
6. ✅ 测试团队验证所有场景

---

**编制人**：Cascade AI  
**审核人**：第三方架构师（三号架构师 + AI架构师）  
**批准日期**：2026年3月11日

---

## 附录A：一物多仓MERGE场景演示

### **场景：物料A在两个仓库的映射同步**

**ERP数据**：
```sql
-- 物料A在WH-01仓库
MaterialCode: RAW-STEEL-001, MasterID: 100001, Warehouse: WH-01

-- 物料A在WH-02仓库
MaterialCode: RAW-STEEL-001, MasterID: 100002, Warehouse: WH-02
```

**修复前的MERGE（会失败）**：
```sql
MERGE INTO MaterialMapping AS target
USING (...) AS source
ON target.MaterialCode = source.MaterialCode 
   AND target.Source = 'ERP' 
   AND target.IsCurrent = 1
-- 问题：物料A会匹配两次（WH-01和WH-02），触发异常
```

**修复后的MERGE（正常）**：
```sql
MERGE INTO MaterialMapping AS target
USING (...) AS source
ON target.MaterialCode = source.MaterialCode 
   AND target.Source = 'ERP' 
   AND target.IsCurrent = 1
   AND ISNULL(target.ERP_Warehouse, '') = ISNULL(source.Warehouse, '')
-- 解决：WH-01和WH-02分别匹配，互不干扰
```

---

## 附录B：BOMNO去重场景演示

### **场景：1000个订单指向同一个BOMNO**

**订单数据**：
```csharp
var orders = new List<Order>
{
    new Order { OrderNo = "ORD-001", BOMNO = "BOM-COMMON-001" },
    new Order { OrderNo = "ORD-002", BOMNO = "BOM-COMMON-001" },
    // ... 998 more orders with the same BOMNO
    new Order { OrderNo = "ORD-1000", BOMNO = "BOM-COMMON-001" }
};
```

**修复前（会导致展开结果膨胀）**：
```csharp
// 直接推送，1000个重复的BOMNO全部插入
await SqlBulkCopyAsync(orders.Select(o => o.BOMNO), "MES_API_BOM_Request_Detail");

// CTE展开时，每个BOMNO都会展开一次
// 结果：350万行 × 1000 = 35亿行（硬盘和内存撑爆）
```

**修复后（去重）**：
```csharp
// 先去重，只推送1个BOMNO
var activeBOMNOs = orders
    .Select(o => o.BOMNO)
    .Distinct()  // ⚠️ 去重
    .ToList();

await SqlBulkCopyAsync(activeBOMNOs, "MES_API_BOM_Request_Detail");

// CTE展开时，只展开1次
// 结果：350万行（正常）
```

---

## 附录C：物料映射API返回结构对比

### **修复前（单对象）**：
```json
{
  "code": 200,
  "message": "Success",
  "data": {
    "materialCode": "RAW-STEEL-001",
    "erpMasterID": 100001,
    "erpWarehouse": "WH-01",
    "source": "ERP"
  }
}
```

**问题**：只返回WH-01的映射，WH-02的映射丢失

### **修复后（列表）**：
```json
{
  "code": 200,
  "message": "Success",
  "data": [
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100001,
      "erpWarehouse": "WH-01",
      "source": "ERP"
    },
    {
      "materialCode": "RAW-STEEL-001",
      "erpMasterID": 100002,
      "erpWarehouse": "WH-02",
      "source": "ERP"
    }
  ]
}
```

**解决**：返回所有仓库的映射，调用者可以根据优先级判定表选择合适的仓库

---

## 附录D：C#研发纪律（强制执行）

---

### 🚫 **研发红线 1：不要让数据库替你"擦屁股"（关于 SqlBulkCopy）**

**背景**：ODS库的`MES_API_BOM_Request_Detail`表虽然加上了唯一约束来防呆，但这绝不是你们在C#里偷懒的理由！

**灾难场景**：
- 如果你们把几万个重复的BOMNO直接塞进SqlBulkCopy
- 不仅会导致整批写入因为主键冲突而当场报错回滚
- 还会引发极其恐怖的**事务日志膨胀**

**铁律**：
> 在调用BulkCopy砸向数据库之前，必须在应用层内存里老老实实地调用`.Distinct()`或使用`HashSet`去重！只有绝对干净、唯一的名单，才允许过网线！

**代码示例**：
```csharp
// ❌ 错误写法（会导致BOMNO重复，触发主键冲突，事务日志膨胀）
var bomnos = orders.Select(o => o.BOMNO).ToList();
await SqlBulkCopyAsync(bomnos, "MES_API_BOM_Request_Detail");

// ✅ 正确写法（应用层去重，只推送干净数据）
var bomnos = orders.Select(o => o.BOMNO).Distinct().ToList();
await SqlBulkCopyAsync(bomnos, "MES_API_BOM_Request_Detail");

// ✅ 更优写法（使用HashSet，性能更好）
var bomnos = new HashSet<string>(orders.Select(o => o.BOMNO));
await SqlBulkCopyAsync(bomnos.ToList(), "MES_API_BOM_Request_Detail");
```

**违规后果**：
- 整批写入失败，回滚
- 事务日志膨胀，影响数据库性能
- 可能导致ODS库磁盘空间不足

---

### 🚫 **研发红线 2：抛弃"一物一码"的单线条思维（关于 Mapping 数组）**

**背景**：由于ERP物理真相的妥协，同一个物料在不同仓库会有不同的映射。物料映射API现在返回的是一个**列表（List/Array）**，不再是单对象！

**灾难场景**：
- 业务代码里出现`var mapping = api.Get(code).First();`这种闭着眼睛瞎拿的代码
- 默认拿第一条，可能拿到错误的仓库映射
- 导致库存扣减错误，制造车间账本的灾难

**铁律**：
> 业务代码里绝对不允许出现`var mapping = api.Get(code).First();`这种闭着眼睛瞎拿的代码。在执行库存扣减、对账时，你们必须结合`InventorySourcePriority`（库存优先级/例外仓库配置），从这个List中精准挑出符合当前上下文的那条映射记录。谁敢默认拿第一条，谁就在制造车间账本的灾难！

**代码示例**：
```csharp
// ❌ 错误写法（闭着眼睛瞎拿第一条，可能拿到错误的仓库）
var mapping = await GetMaterialMappingAsync("RAW-STEEL-001");
var firstMapping = mapping.First();  // 危险！
await DeductInventoryAsync(firstMapping.erpMasterID, quantity);

// ❌ 更危险的写法（假设返回单对象）
var mapping = await GetMaterialMappingAsync("RAW-STEEL-001");
await DeductInventoryAsync(mapping.erpMasterID, quantity);  // 编译错误！

// ✅ 正确写法（根据优先级判定表选择合适的仓库）
var mappings = await GetMaterialMappingAsync("RAW-STEEL-001");
var selectedMapping = mappings
    .OrderBy(m => GetWarehousePriority(m.erpWarehouse))
    .FirstOrDefault();

if (selectedMapping == null)
{
    throw new Exception($"物料 {materialCode} 没有可用的映射");
}

await DeductInventoryAsync(
    selectedMapping.erpMasterID, 
    selectedMapping.erpWarehouse, 
    quantity
);

// ✅ 更严谨的写法（结合业务上下文，例如指定仓库）
var mappings = await GetMaterialMappingAsync("RAW-STEEL-001");
var selectedMapping = mappings
    .Where(m => m.erpWarehouse == currentWarehouse)  // 根据当前业务上下文
    .FirstOrDefault();

if (selectedMapping == null)
{
    // 降级策略：使用优先级最高的仓库
    selectedMapping = mappings
        .OrderBy(m => GetWarehousePriority(m.erpWarehouse))
        .FirstOrDefault();
}

await DeductInventoryAsync(
    selectedMapping.erpMasterID, 
    selectedMapping.erpWarehouse, 
    quantity
);
```

**违规后果**：
- 库存扣减错误，账实不符
- 车间账本混乱，影响排程结果
- 可能导致明明有库存却提示缺料

---

### **3. 仓库优先级判定表示例**：
```csharp
private int GetWarehousePriority(string warehouse)
{
    // 优先级：WH-01 > WH-02 > WH-03
    // 根据实际业务规则配置，可以从配置文件或数据库读取
    return warehouse switch
    {
        "WH-01" => 1,  // 主仓库，优先级最高
        "WH-02" => 2,  // 备用仓库
        "WH-03" => 3,  // 临时仓库
        _ => 999       // 未知仓库，最低优先级
    };
}
```

---

### **4. 研发纪律检查清单**：

在代码审查（Code Review）时，必须检查以下项目：

- [ ] 所有`SqlBulkCopy`调用前是否执行了`.Distinct()`或使用`HashSet`去重
- [ ] 所有物料映射API调用是否正确处理了列表返回结构
- [ ] 是否存在`.First()`、`.FirstOrDefault()`直接获取映射的危险代码
- [ ] 是否根据业务上下文（如当前仓库）选择合适的映射
- [ ] 是否实现了降级策略（如优先级判定表）
- [ ] 是否有完善的异常处理（如映射不存在时的处理）
