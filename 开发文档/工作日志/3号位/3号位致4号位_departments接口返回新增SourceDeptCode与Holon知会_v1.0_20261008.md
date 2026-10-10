# 3号位 → 4号位 — departments 下拉接口返回新增 SourceDeptCode / Holon 知会

| 项 | 值 |
|---|---|
| 日期 | 2026-10-08 |
| 发件 | 3号位（规则参数治理 owner / 后端） |
| 收件 | 4号位（前端 owner） |
| 抄送 | 2号位（DDL 红线 #6 owner；dev Holon 列冻结对齐核对，见 §五） |
| 主题 | `GET /api/governance/lookups/departments` 返回值新增 `SourceDeptCode`、`Holon` 两字段（向后兼容） |
| 优先级 | 普通（已落码 + 已验证；4号位 按需消费） |

---

## 一、变更内容

| 文件 | 改动 |
|---|---|
| `LPS.APS.Core/DTOs/Setup/SetupRuleQueryDtos.cs` | `DepartmentLookupItem` **新增** `SourceDeptCode`（string?）、`Holon`（string?）两个可空属性 |
| `LPS.APS.Engine/Repositories/Governance/MasterDataLookupRepository.cs` | `LookupDepartmentsAsync` SQL 追加 `[SourceDeptCode]`、`[Holon]` 两列 |

- 接口签名、既有字段（`MasterId`/`Code`/`StageCode`）、路由、权限码（`aps.setup.view`）**均不变**——纯增字段，向后兼容。
- 红线：接口签名未改（红线 #5 不涉）；只读查询、无 DDL（红线 #6 不涉）。

## 二、返回形态（dev 库实测样例，脱敏）

```json
{
  "masterId": 1114,
  "code": "BJ_ASSY_制造3课751HL",
  "stageCode": "BJ_ASSY",
  "sourceDeptCode": "311333",
  "holon": "751"
}
```

- `SourceDeptCode` = 源系统部门码（6 位数字，如 `311333`）；填充率 **279/287**
- `Holon` = 生产单元 holon 码（3~4 位，如 `751`）；与 `Code` 内嵌 `...751HL` 同源；填充率 **174/287**
- 未填写的行两字段均返回 `null`（不注入哨兵值）

## 三、4号位 动作

| 项 | 说明 |
|---|---|
| 消费 | 可选——`sourceDeptCode`/`holon` 需要展示或过滤时直接取用；不需要可忽略，不影响既有消费 |
| verify | 可选补断言：`lookups/departments` 响应对象含 `sourceDeptCode`/`holon` 键（值为 string 或 null） |
| 不动作 | 本知会无强制前置——前端现状（三 Dialog 用 MasterId/Code/StageCode）零改动继续可用 |

## 四、背景

该字段来源 = `ProductionDepartment` 表直接列（**非** `Resource.SourceProductionDeptCode` 内嵌解析）。`Holon` 列与 2026-09-07 升级案「公司部门码 ↔ 生产单元 holon 缺映射桥」同域——本接口只读透出，不承担建桥职责。

## 五、抄送 2号位 — dev Holon 列冻结对齐核对

- dev 库 `ProductionDepartment` 表**已有 `Holon` 列**，但冻结 DDL（v5.1.2 / v5.1.5）与 `ProductionDepartment` Core 实体（仅到 `SourceDeptCode`）**均无**该列——dev 库漂移。
- 本次仅**只读引用**（`[Holon]` 列透出），未建/未改 DDL，红线 #6 不违。
- 请 2号位 核对：`Holon` 列是否应补进冻结 DDL 与 Core 实体对齐（若为 2号位 已落 DDL，请回填冻结文档）。

---

> 按 3号位 ↔ 4号位 中转沟通方式，本知会仅发 4号位 + 抄送 2号位（冻结对齐核对）。
