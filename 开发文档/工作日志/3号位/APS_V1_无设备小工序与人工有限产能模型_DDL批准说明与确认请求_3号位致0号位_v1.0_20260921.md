# APS V1 无设备小工序与人工有限产能模型 —— DDL 批准说明与确认请求（3号位 致 0号位）

**版本**：v1.0
**日期**：2026-09-21
**致**：0号位（PM）
**来自**：3号位（规则/参数治理）
**性质**：S1 DDL 变更申请（v5.1.9 候选）的**设计说明 + 批准催办**——回顾原有设计要点，请 0号位 批准并裁决 3 项待确认
**前置**：
- 0号位 终版裁决《未命名的Markdown文件 (51)(1).md》（§二 R4'/R6 / §八 / §九）
- 《APS_V1_无设备小工序与人工有限产能模型_DDL变更申请_v5.1.9候选_v1.0_20260921.md》（S1 申请单，2026-09-21 已提交）
- 1号位《无设备工序_1号位致3号位_确认单_v1.0_20260921.md》+ 3号位 回执（消费口径已对齐）

---

## 一、原申请内容回顾（说明原有设计）

S1 申请单提出 **APS_Production 新增 3 张表**，均经 **DomainSolveRequest** 进 1号位、**不进 Frozen 六块**：

| 表 | 定位（裁决口径） | 冻结维度 | 关键设计 |
|---|---|---|---|
| `OperationPlanningParam` | APS 工艺规划属性，**非规则引擎**（R6） | 无（OperationCode + EffectiveVersion） | OperationCode / PlanningMode（三态 CHECK）/ EffectiveVersion / IsActive / 审计四列；唯一索引 DB 兜底（红线 #3） |
| `OperationTransitionRule` | 工序转运规则（R1 参照 SetupTransitionRule） | RuleSetVersionId（FK） | 部门/Stage/From/To 工序 范围可空=兜底；TransferMinutes CHECK≥0；防全空 CHECK |
| `StageTransferTime` | 跨大工艺转运时长（R1） | RuleSetVersionId（FK） | FromStageCode→ToStageCode 必填单向 + TransferMinutes |

**沿用 v5.1.8 SetupTransitionRule 全部规范**：BIGINT Id 自增 PK、RuleSetVersionId 冻结、DECIMAL(18,4) 时长、审计四列、过滤唯一索引仅 DB 兜底、查询辅助索引 IX_*_RuleSetVersion。

**不在本单建表**：人工 Resource Slot（FINISH_SLOT_01~N）+ Resource Calendar——裁决 §五/§六 明确由 **APS 资源主数据侧维护、2号位 加载**，资源/Calendar 表结构不属 3号位 治理范围。

## 二、设计取舍说明（为何如此）

| 取舍 | 理由 |
|---|---|
| OperationPlanningParam **无 RuleSetVersionId** | 裁决 R6 明示非 SetupTransitionRule 类动态规则；版本语义走 EffectiveVersion（同码 IsActive=1 最新） |
| Transition/Transfer **走 RuleSetVersionId 冻结** | 裁决 R1 保留「参照 SetupTransitionRule」治理通道；发布前冲突校验主防线=3号位 |
| 不做复杂规则引擎 | 裁决 §八 明示简化；Transition 用范围+可空工序对表达兜底默认，匹配优先级在 S4 契约文档定义 |
| seed 默认值 30/240 分钟 | 裁决 §八 示例（机加工内部默认转运 30 分钟；机加工→表面处理 4 小时）；业务可改；未命中规则兜底 0 |

## 三、请 0号位 裁决的 3 项确认（S1 §六）

| # | 确认项 | 3号位 建议 |
|---|---|---|
| ① | **落库执行归属**（红线 #6） | 默认 **2号位** 执行；或参照 Setup v5.1.8 先例授权 **3号位 直接落地**（2026-09-17 后 Setup 规则 DDL 已由 3号位 落地，本轮为同类规则新增） |
| ② | OperationCode/StageCode 是否补 FK 到 RoutingOperation/Stage 主链 | **暂不补**，待 2号位 确认主链口径后随 v5.2 再议（对齐 v5.1.8 延期先例） |
| ③ | EffectiveVersion 版本选取规则 | 取同码 **IsActive=1 最新版本**，随 S4 契约文档定案并备案 |

## 四、阻塞说明（为何请尽快批准）

- **S2~S7 全链依赖 DDL 批准**（治理模型 / API / 契约草案 / seed / 集成测试）；
- **1号位 已承诺**「通道数据到位当天开消费代码」（Phase2 分型 + 测试，限 Scheduling 层）——DDL 批准 → 落库 → S2/S4 后 1号位 即可启动，等待成本由 0号位 批准节奏决定。

## 五、回执表

| 问 | 答复 |
|---|---|
| 三表结构与裁决口径是否一致 | （待 0号位） |
| 落库执行归属 ① | （待 0号位） |
| FK 主链口径 ② / 版本规则 ③ | （待 2号位，③ 待 0号位 确认） |

---

*批准后：生成《APS_数据库_v5.1.8到v5.1.9_无设备小工序_升级补丁.sql》→ 落库 dev → S2 治理模型实施。*
