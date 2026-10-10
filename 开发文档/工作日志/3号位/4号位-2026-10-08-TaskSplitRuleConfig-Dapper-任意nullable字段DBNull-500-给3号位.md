# 4号位 → 3号位 — TaskSplitRuleConfig POST 500 跟进（Dapper 任意 nullable 字段 = DBNull 抛 NotSupportedException）

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 发件 | 4号位（前端 owner） |
| 收件 | 3号位（规则参数治理 owner / 后端） |
| 抄送 | 2号位（DDL 红线 #6 owner，仅信息同步） |
| 主题 | TaskSplitRuleConfig POST/PUT 500 — 任意 nullable 字段为 null 时 Dapper 编译失败 |
| 优先级 | **P0**（业务能力 100% 阻断：V1 新增/编辑 路径全堵；前端无法绕行） |
| 关联 | [4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md](4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md)（上次函件 — 10-07 16:47 — 至今未修复落地） |

---

## 一、问题升级

[前置函件](4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md) 仅覆盖 `EnsureNotDuplicateAsync` 的 `@Dept IS NULL` 单点。10-08 联调发现：

- **任意 nullable 字段为 null 时，POST/PUT 都抛 500**（不仅是 Dept=null）
- Dapper 报错信息会"漂移"到其它字段名（如本函件的 `MaxOptimizationSplitCount`），实际根因是同一 Dapper 编译失败
- EnsureNotDuplicateAsync 的 `@Dept IS NULL` 也仍未修复（10-08 13:00 读 Service.cs:357-375 仍为原 SQL）

> 这是一个**系统性问题**，不是单点 bug。

---

## 二、复现命令（10-08 13:00 已实测，dev 库）

### 2.1 触发 A：仅 6 个 optional 字段为 null（生产部门给非 null 有效值）

```bash
TOKEN=$(curl -sk -X POST "https://localhost:7044/api/auth/login" \
  -H "Content-Type: application/json" \
  --data-raw '{"userCode":"admin","password":"Admin@123456"}' \
  | grep -oE '"accessToken":"[^"]+"' | head -1 | cut -d'"' -f4)

curl -sk -X POST "https://localhost:7044/api/governance/task-split-rule-configs" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "materialId": 5346847,
    "productionDepartmentId": 1114,
    "minExecutionBatchQty": 1,
    "maxExecutionBatchQty": 2000,
    "preferredBatchQty": 3000,
    "allowSplit": true,
    "allowMerge": true,
    "maxOptimizationSplitCount": null,
    "maxBatchCandidates": null,
    "bottleneckSplitStrategy": null,
    "nonBottleneckStrategy": null,
    "effectiveFrom": null,
    "effectiveTo": null
  }'
```

### 2.2 实际响应（HTTP 500）

```json
{
  "code": 500,
  "message": "服务器内部错误",
  "data": {
    "exception": "NotSupportedException",
    "detail": "The member MaxOptimizationSplitCount of type System.DBNull cannot be used as a parameter value",
    "stackTrace": "at LPS.APS.Web.Controllers.TaskSplitRuleConfigController.HandleError(...) line 51  at LPS.APS.Web.Controllers.TaskSplitRuleConfigController.Create(...) line 87 ..."
  },
  "traceId": "0HNP4PUKMSCFO:00000001",
  "timestamp": "2026-10-08T12:10:47.0030443+08:00"
}
```

### 2.3 触发 B：productionDepartmentId=null（前置函件场景，至今仍触发）

```bash
curl -sk -X POST "https://localhost:7044/api/governance/task-split-rule-configs" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "materialId": 5346846,
    "productionDepartmentId": null,
    "minExecutionBatchQty": 10,
    "maxExecutionBatchQty": 200,
    "preferredBatchQty": 50,
    "allowSplit": true,
    "allowMerge": true,
    "maxOptimizationSplitCount": 3,
    "maxBatchCandidates": 5,
    "bottleneckSplitStrategy": "PREFER_SPLIT",
    "nonBottleneckStrategy": "PREFER_LARGE_BATCH",
    "effectiveFrom": "2026-10-01T00:00:00Z",
    "effectiveTo": null
  }'
```

> 此场景虽然不命中本函件的 NotSupportedException，但同样 500（前一次函件 10-07 提的 `@Dept IS NULL` 仍未修复）。

---

## 三、根因（4号位 已读 Service.cs 完整定位）

### 3.1 异常抛出位置（10-08 13:00 读源文件）

`lps/LPS.APS.Application/Services/TaskSplitRuleConfigGovernanceService.cs` 4 处 DynamicParameters 全部命中同一 Dapper 缺陷：

| # | 方法 | 行号 | 参数模式 | Dapper 报错字段名（漂移） |
|---|---|---|---|---|
| 1 | `EnsureNotDuplicateAsync` | L367 | `p.Add("Dept", (object?)productionDepartmentId ?? DBNull.Value)` | `@Dept` |
| 2 | `CreateAsync` | L82-93 | `p.Add("X", (object?)input.X ?? DBNull.Value)` × 11 字段 | `MaxOptimizationSplitCount`（漂移） |
| 3 | `UpdateAsync` | L167-178 | `p.Add("X", (object?)input.X ?? DBNull.Value)` × 11 字段 | 同上 |
| 4 | `EnsureNotDuplicateAsync`（Update 调用） | L367（同一方法） | 同 1 | `@Dept` |

### 3.2 Dapper 已知缺陷（升级版根因）

Dapper 对 `DynamicParameters.Add(string name, object value)` 的实现：
- 当 `value` 是 `null` 时，Dapper 内部走 `DBNull.Value` 路径
- Dapper 需要根据 C# 类型推断 SQL 类型，但 boxed DBNull 没有类型信息
- 抛 `NotSupportedException: The member {paramName} of type System.DBNull cannot be used as a parameter value`

> 这是 Dapper 的设计选择：**要求 nullable 参数必须显式提供 DbType**。

### 3.3 字段名"漂移"原因

Dapper 的参数编译是按 SQL 字符串缓存的。`EnsureNotDuplicateAsync` 和 `CreateAsync` 是两条不同的 SQL，各自有自己的缓存键。当 SQL 编译失败时，错误消息中的字段名取自 DynamicParameters 中**最后一个被引用的参数名**或**第一次失败的参数名**，但用户看到的报错字段名常常对应不上实际触发位置。这是已知 Dapper 行为，不代表"只在 MaxOptimizationSplitCount 这一字段出错"。

---

## 四、修复方案（请 3号位 决策；4号位 仅提供选项）

### 方案 A（推荐，根治）— 全部 nullable 参数加 DbType 显式声明

把 4 处 DynamicParameters 的所有 nullable 参数改为 3 参 `Add(string, object?, DbType)` 重载：

```csharp
// 之前：
p.Add("MaxOptimizationSplitCount", (object?)input.MaxOptimizationSplitCount ?? DBNull.Value);
// 之后：
p.Add("MaxOptimizationSplitCount", input.MaxOptimizationSplitCount, DbType.Int32);
```

完整 11 字段表（CreateAsync L82-93）：

| 字段 | C# 类型 | DbType |
|---|---|---|
| `ProductionDepartmentId` | `int?` | `DbType.Int32` |
| `MinExecutionBatchQty` | `decimal?` | `DbType.Decimal` |
| `MaxExecutionBatchQty` | `decimal?` | `DbType.Decimal` |
| `PreferredBatchQty` | `decimal?` | `DbType.Decimal` |
| `MaxOptimizationSplitCount` | `int?` | `DbType.Int32` |
| `MaxBatchCandidates` | `int?` | `DbType.Int32` |
| `BottleneckSplitStrategy` | `string?` | `DbType.String` |
| `NonBottleneckStrategy` | `string?` | `DbType.String` |
| `EffectiveFrom` | `DateTime?` | `DbType.DateTime` |
| `EffectiveTo` | `DateTime?` | `DbType.DateTime` |

`EnsureNotDuplicateAsync` L367 的 `@Dept` 同样改：`DbType.Int32`（这是上次函件方案 C 的扩展）。

- **优点**：一次性根治，不依赖 SQL 形态；4 处 11 字段 22 行改动
- **缺点**：需手工对照类型表（漏一个字段仍会出错）
- **范围**：Service.cs 4 处 DynamicParameters
- **验证**：完整 11 字段 null 测试 + productionDepartmentId=null 测试

### 方案 B（备选）— 仅修 INSERT/UPDATE SQL，去掉 null 处理

把 `CreateAsync` 的 INSERT SQL 拆成动态拼接（类似 `EnsureNotDuplicateAsync` 的方案 B）。代码量翻倍，**不推荐**。

### 方案 C（折中）— 统一封装 helper

```csharp
private static DynamicParameters AddNullable(DynamicParameters p, string name, object? value, DbType dbType)
{
    p.Add(name, value, dbType);
    return p;
}
```

之后所有 nullable 字段改用 `AddNullable(p, "X", input.X, DbType.Int32)`。与方案 A 效果相同，可读性更好。

### 4号位 不下结论

按 [不从前端反推后端 DTO](不从前端反推后端 DTO) 原则，4号位 仅指认 bug + 提供选项，**修复选型由 3号位 决策**。

---

## 五、建议同步修复（前置函件遗留）

[前置函件](4号位-2026-10-07-TaskSplitRuleConfig-Dapper-Dept-DBNull-500-给3号位.md) 的 `EnsureNotDuplicateAsync` `@Dept IS NULL` 至今未修复。**强烈建议**：方案 A 落地时同步改 `EnsureNotDuplicateAsync` 的 SQL 为 `ISNULL(ProductionDepartmentId, -1) = ISNULL(@Dept, -1)`（前置函件方案 A），否则即使参数化修了，`@Dept IS NULL` 仍可能命中 Dapper 其它路径的编译错误。

---

## 六、4号位 前端后续（不依赖 3号位 修复）

| 阶段 | 4号位 动作 |
|---|---|
| 当前 | 在 Drawer 顶部加红警「非必填字段不填也会报服务器内部错误，请全部填写后再提交」+ optional 字段 placeholder 加「（建议填）」提示 |
| 3号位 修复后 | 移除红警 + placeholder 改回「（留空 = 不启用）」；恢复契约语义 |
| 复跑 verify | 新增 TSRC14 动态断言：POST `productionDepartmentId=null` + 6 字段 null → code=201 |
| 浏览器走查 | V1 全场景可创建 / 编辑（Material 级默认、永久有效、有限窗口等） |

> 注：4号位 暂不在前端代码层注入哨兵值（如 MaxOptimizationSplitCount=999999），避免污染业务数据。

---

## 七、不在请求范围

- 后端代码（4号位 硬约束：lps/ 只读）
- 数据契约（v1.0 契约已锁定 nullable 语义）
- 冻结文档（业务冻结：nullable 字段可空是契约要求）
- 修复选型（方案 A/B/C 由 3号位 决策）

---

> 按 4号位 → 后端中转沟通方式，本函件仅发 3号位 + 抄送 2号位（DDL 红线 #6 owner，仅信息同步，不请 2号位 决策）。
