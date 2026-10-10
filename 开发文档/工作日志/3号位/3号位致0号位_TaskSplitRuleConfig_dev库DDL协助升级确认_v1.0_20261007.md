# ⚠️ 已答复 · 由 0号位 裁决取代 —— 3号位 → 0号位：TaskSplitRuleConfig dev 库 DDL 协助升级 · 请确认拍板（v1.0）

**本件已由 0号位 正式裁决答复**（`未命名的Markdown文件 (4).md`，2026-10-07）：授权 3号位 作为 Batch Policy Owner 在 dev 直接执行正式 DDL 既有幂等升级脚本（不新增字段/索引语义）；test 随测试部署同步、prod 按发布流程。本件目的达成，不再生效。

> 发件：3号位（规则参数治理 / 契约发件方）｜ 致：0号位（PM / 仲裁）｜ 抄：4号位
> 日期：2026-10-07 ｜ 优先级：P2（阻塞 4号位 前端真实联调）
> 背景：原拟致 **2号位**（DDL 红线 #6 owner）的协助申请，因 **2号位 今日不在**，升级 0号位 拍板。
> 详载：`3号位致2号位_TaskSplitRuleConfig_dev库DDL协助申请_v1.0_20261007.md`（本件只列待裁要点）

---

## 一、阻塞现状

4号位 实测 `GET /api/governance/task-split-rule-configs` 返 500：

```
列名 'ProductionDepartmentId' / 'MinExecutionBatchQty' / 'MaxExecutionBatchQty' /
     'PreferredBatchQty' / 'AllowSplit' 无效。
```

dev 库 `TaskSplitRuleConfig` 表**缺 5 列**（+ 可能索引需重建），前端真实联调被卡（P2，不阻塞其 types/api 起草）。

## 二、字段真源（已核实，非 4号位 提出）

- 冻结 **DDL v5.1.8.3**：`开发文档/2026-10-07文档更新/APS_数据库表结构设计_DDL_v5.1.8.3_20261005_真实RoutePath_TaskMES_BatchPolicy最小物理回写版.sql`
- 全量建表 L4460–4481 已含 18 列；**最小物理回写 L9779–9786** 共 8 条幂等 `IF COL_LENGTH IS NULL ALTER … ADD`（dev 缺前 5 条，后 3 条守卫自动跳过）。
- 后端实体/Service SELECT 照冻结 DDL 写 18 列（冻结顺序=业务→数据模型→接口→代码），非反推代码、非 4号位 提。

## 三、请 0号位 拍板（三项）

| # | 待裁点 | 选项 |
|---|---|---|
| 1 | **dev 库今日是否落地** L9779–9786 幂等 ALTER（+ 索引 `IX_TaskSplitRuleConfig_Query` 重建核对） | 立即落地 / 顺延至 2号位 返岗 |
| 2 | **由谁执行**（2号位 今日不在） | 0号位 临时指定执行人 / 临时授权 3号位 / 2号位 返岗后补跑 |
| 3 | **test / pro 是否同步**（口径） | 同步 test / 仅 dev / 按部署节奏 |

## 四、红线 gating（不越位）

DDL = 红线 #6 = **2号位专属**；3号位 不自行执行，仅升级请裁。后端代码 + 契约 + 单测均已就绪，**只欠 dev schema**，无其它阻塞项。

---

**发件**：3号位 ｜ **日期**：2026-10-07 ｜ **版本**：v1.0
**本件性质**：因 2号位 缺席的跨号位升级确认件——请 0号位 对 dev 库 DDL 落地②谁执行③环境范围拍板。