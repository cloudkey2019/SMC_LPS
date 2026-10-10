# 3号位 → 4号位：StageLeadTimeParam 维护接口契约（v1.0）

> 发件：3号位（规则参数治理 Owner）｜ 致：4号位（前端）｜ 抄：0号位、1号位、2号位、5号位
> 日期：2026-09-28 ｜ 版本：v1.0
> 依据：0号位《APS V1 Stage…StageLeadTimeParam 接口裁决回复》v1.0（2026-09-28）= StageLeadTimeParam 归 3号位 规则参数体系、载体「直接生效（时间窗）」
> 性质：红线 #5 接口即契约——**契约先行**，后端落码与前端对接据此执行

---

## 〇、速答

```
归属        : 3号位 规则参数体系（非 Routing、非 5号位）
粒度        : 工厂(FactoryCode) + StageCode + 生产部门(ProductionDeptCode)（物料/产品族为可选更细降级层）
载体        : 直接生效（时间窗），不进 FrozenStrategySnapshot —— CRUD 即最终态
权限        : 查看 aps.rule.view；维护 aps.rule.edit（复用 Rule 码，零权限种子变更）
停用        : DELETE 软删 IsActive=0（不物理删，保留历史与生效窗口）
DDL         : 零改动（表已在 2号位 冻结 v5.1.8.2）
```

---

## 一、端点（前缀 /api/governance）

| # | 方法 | 路由 | 鉴权 | 语义 |
|---|---|---|---|---|
| 1 | GET | `/stage-lead-time-params` | aps.rule.view | 列表（可过滤） |
| 2 | POST | `/stage-lead-time-params` | aps.rule.edit | 新增 |
| 3 | PUT | `/stage-lead-time-params/{id}` | aps.rule.edit | 编辑（整对象替换） |
| 4 | DELETE | `/stage-lead-time-params/{id}` | aps.rule.edit | 停用（IsActive=0） |

响应统一 `ApiResponse<T>`（`{ code, message, data, traceId, timestamp }`）。

---

## 二、查询参数与请求体

### 2.1 GET 列表查询参数（全部可选）

`factoryCode` / `stageCode` / `productionDeptCode`（字符串精确匹配）/ `isActive`（bool?）

排序：`FactoryCode, StageCode, Priority, Id`。

### 2.2 POST / PUT 请求体 `SaveStageLeadTimeParamRequest`

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| FactoryCode | string | ✅ | 工厂编码 |
| StageCode | string | ✅ | 大工艺阶段码（如 TJ_OUTS/BJ_SURF） |
| ProductionDeptCode | string? | 否 | 生产部门编码（主粒度键） |
| MaterialCode | string? | 否 | 物料级精确匹配（降级层） |
| ProductFamilyCode | string? | 否 | 产品族级匹配（降级层） |
| LeadTimeDays | decimal | — | 提前期（天） |
| LeadTimeHours | decimal | — | 提前期（小时，更细） |
| Priority | int | — | 命中优先级（越小越先） |
| EffectiveFrom | DateTime | ✅ | 生效起始 |
| EffectiveTo | DateTime? | 否 | 生效截止（null=永久） |
| IsDefault | bool | — | 全局阶段默认值（兜底） |

### 2.3 响应体 `StageLeadTimeParamDto` = 请求体 + `Id / IsActive / CreatedAt / UpdatedAt`

---

## 三、校验与错误（fail-fast）

| 场景 | 状态码 |
|---|---|
| FactoryCode / StageCode 空 | 400 |
| LeadTimeDays/LeadTimeHours 为负 | 422 |
| LeadTimeDays 与 LeadTimeHours 同时为 0 | 422 |
| Priority < 0 | 422 |
| EffectiveTo < EffectiveFrom | 422 |
| 业务键重复（全等 工厂+阶段+部门+物料+族） | 422 |
| id 不存在 | 404 |
| 未授权 | 403 |

---

## 四、审计

写 / 停用均落统一 `AuditLog`（EntityType=`StageLeadTimeParam`，ActionCode=Create/Update/Deactivate），带真实 actorUserId/actorUserCode。

---

## 五、漂移说明（如实报告，不掩盖）

- 实体 `Core/Entities/Aps/StageLeadTimeParam.cs` 把 `LeadTimeDays/LeadTimeHours/EffectiveFrom` 声明为**非空**，而冻结 DDL v5.1.8.2 这三列是 **NULL 可空**。
- **本契约口径**＝按实体（业务语义）执行：治理侧一律写**非空**值（LeadTime 须 >0、EffectiveFrom 必填），完全兼容 DDL 可空列；读侧若遇历史 NULL，Dapper 回落默认 0。
- 是否需将 DDL 收紧为 NOT NULL，属 2号位 DDL 变更（红线 #6），本件不发起，仅留痕备查。

---

## 六、前端对接提示（4号位）

- StageLeadTimeParam 维护页数据源即本 4 端点，**无需 mock**（表当前 0 行，新增后即见）。
- 排序/分页由前端决定是否需要；后端现返全量（规则参数行数有限，未分页）。

---

**发件**：3号位 ｜ 日期：2026-09-28 ｜ 版本：v1.0
**本件性质**：红线 #5 契约先行件——后端已同步落码，请 4号位 按 §一～§三 对接。