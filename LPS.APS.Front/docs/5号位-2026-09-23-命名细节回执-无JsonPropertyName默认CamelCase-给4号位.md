# 5号位 → 4号位 — 命名细节回执：无 [JsonPropertyName]，全为默认 CamelCase（2026-09-23）

**发送人**：5号位（Order 主表 / OrderQueryRepository Owner）
**接收人**：4号位
**日期**：2026-09-23
**触发**：回复 [4号位-2026-09-23-B4真实JSON收到-§10A1启动-回执给5号位.md] §四.1 命名细节询问

---

## 〇、速答

```
命名机制      : ✅ 全为默认 PropertyNamingPolicy=CamelCase（Program.cs:90-93），无任何 [JsonPropertyName]
mtS_InstructionNo : 非强制，默认 CamelCase 自然产物（前端用 mtS_InstructionNo）
bomno         : 非强制，默认（前端用 bomno）
uom           : 非强制，默认（前端用 uom）
MTS_InstructionNo 重命名 : 建议不纳入本期（会改 JSON 字段名，破坏对接）
```

---

## 一、实查结论：`OrderQueryDtos.cs` 无任何 [JsonPropertyName]

`grep` 全部 `JsonPropertyName` → **0 命中**。`OrderListItemDto` 全部字段（`Id/OrderCanonicalId/UOM/MTS_InstructionNo/BOMNO` 等）均为**纯 C# PascalCase 属性，无强制重命名属性**。

JSON 输出命名完全由全局 `Program.cs:90-93` `JsonNamingPolicy.CamelCase` 决定。

## 二、四问逐答

| 询问 | 答案 | 依据 |
|---|---|---|
| 1. `mtS_InstructionNo` 是 [JsonPropertyName] 强制？ | **否** | 默认 CamelCase 将 `MTS_InstructionNo` → `mtS_InstructionNo`（首字母小写、下划线不动、大写 T 不切词），无强制属性 |
| 2. `bomno` 强制？ | **否** | `BOMNO` 默认 CamelCase → `bomno` |
| 3. `uom` 强制？ | **否** | `UOM` 默认 CamelCase → `uom` |
| 4. 是否考虑重命名 `MTS_InstructionNo`？ | **建议不纳入本期** | 当前无 JsonPropertyName；若重命名为 `MtsInstructionNo` 会改 JSON 字段名（→`mtsInstructionNo`），**破坏前端现对接**；且 §10A.1 不引用该字段，无当期必要。如需规范化，建议单独立项、前后端协同切，不在 B4/§10A.1 作用域内 |

## 三、对前端类型的结论

- 前端 `OrderBasicInfo` 补 `mtS_InstructionNo?: string`（对齐 dev 实跑命名）✅ 正确；
- `bomno` / `uom` 照默认输出对接，无需特殊处理；
- `orderCanonicalId` 与 dev 实跑一致，§10A.1 直接对接，无影响。

---

**发送人**：5号位
**日期**：2026-09-23
**回执**：§10A.1 可正常启动；前端类型按 `mtS_InstructionNo` 等 dev 实跑命名对齐