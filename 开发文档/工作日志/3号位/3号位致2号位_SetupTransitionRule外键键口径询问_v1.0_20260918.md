# 致 2号位：SetupTransitionRule 外键键口径询问

> **收件**：2号位（数据引擎）
> **发件**：3号位（Setup 规则治理）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-18
> **性质**：跨号位数据键口径协助请求（非催办）

---

## 一、背景

3号位 已按 0号位 2026-09-17 裁决（「Setup 规则治理归 3号位，DDL 按业务 Owner」）落地 `SetupTransitionRule` 表 + 实体（DDL 补丁 `v5.1.7→v5.1.8`）。表已建成，但 DDL 补丁备注明示：

> `ResourceId / FromMaterialId / ToMaterialId 的 FKs 到 Resource/Material 主键口径待 2号位 确认后补。`

即当前表内**只有 `FK_SetupTransitionRule_RuleSetVersion`（→ RuleSetVersion）一条外键**，下方 4 个指向主数据的列**暂未加 FK**，等待 2号位 给键口径后补。

---

## 二、询问事项（请逐列回执：目标表名 + 主键列名 + 键类型）

| # | 列（当前定义） | 语义 | 待 2号位 回执 |
|---|---|---|---|
| 1 | `ResourceId INT NOT NULL` | 当前设备（候选/实际 Resource） | 目标表？主键列名？键类型 INT/BIGINT？ |
| 2 | `FromMaterialId INT NULL` | 前产品 | 目标表（Material / Product）？主键列名？键类型？ |
| 3 | `ToMaterialId INT NULL` | 后产品 | 同上 |
| 4 | `ProductionDepartmentId INT NOT NULL` | 生产部门（业务范围） | 目标表？主键列名？键类型？ |

**关键落点**：若目标主键为 `BIGINT`，则除「补 FK」外还需将本表对应列 `INT → BIGINT` 对齐（属改列）。请一并说明是否需要类型对齐。

---

## 三、当前表结构（供对照，勿改）

```sql
CREATE TABLE SetupTransitionRule (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetVersionId       BIGINT        NOT NULL,   -- FK → RuleSetVersion（已建）
    ProductionDepartmentId INT           NOT NULL,   -- ← 待 FK
    StageCode              NVARCHAR(50)  NOT NULL,
    OperationCode          NVARCHAR(50)  NOT NULL,
    ResourceId             INT           NOT NULL,   -- ← 待 FK
    FromMaterialId         INT           NULL,       -- ← 待 FK
    ToMaterialId           INT           NULL,       -- ← 待 FK
    RuleType               NVARCHAR(20)  NOT NULL,
    SetupMinutes           DECIMAL(18,4) NOT NULL,
    IsActive               BIT           NOT NULL DEFAULT 1,
    -- ...时间戳列
    CONSTRAINT FK_SetupTransitionRule_RuleSetVersion FOREIGN KEY (RuleSetVersionId) REFERENCES RuleSetVersion(Id)
);
```

---

## 四、耗时与影响

- 本询问**不阻塞端点开发**（换型规则唯一性靠 3号位 发布前冲突校验器，FK 属引用完整性兜底，可后补）。
- 但需在排期内预留：拿到键口径后，3号位 需回改 DDL 补 4 条 FK（含可能 INT→BIGINT），故请 2号位 于 **09-22 前**回执。

---

## 五、回执格式建议

```
#1 ResourceId: → Resource(Id, BIGINT) | Resource(Id, INT) | 其它表（写明）
#2 FromMaterialId: → Material(Id, BIGINT) | Product(Id, INT) | 其它
#3 ToMaterialId:   → 同 #2
#4 ProductionDepartmentId: → Department(Id, INT) | Workshop(Id, BIGINT) | 其它
类型对齐: 需要 / 不需要（若需，写明哪几列 INT→BIGINT）
```

---

**发送人**：3号位
**日期**：2026-09-18