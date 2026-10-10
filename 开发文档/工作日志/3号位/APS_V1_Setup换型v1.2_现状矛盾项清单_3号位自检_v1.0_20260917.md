# Setup换型 v1.2 —— 现状影响与矛盾项清单自检（3号位）

> 发件：3号位（规则/参数/运行生命周期治理）／抄 0号位、1号位、2号位、4号位
> 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》
> 日期：2026-09-17
> 性质：重读 v1.2 后对照当前代码的「影响 + 矛盾」逐条自检。矛盾 = 当前代码/实体与 v1.2 口径直接冲突之处，每条附冲突原因与处置。
> 术语约定：英文契约词一律「中文名称（英文）」。
> 锚点约定：文档引「§符号锚」，代码引 `文件:行`。

---

## 〇 结论摘要

1. **影响**：1/2/3/4 号位均有改动（换型口径翻转），详见《影响对照与整改清单 v1.0》；本文只列**矛盾项**。
2. **矛盾**：共 **10 处**「代码 ↔ v1.2」直接冲突，按归属分 3号位 / 1号位 / 2号位 / 文档四组。
3. **关键定性**：换型优化器（SetupOptimizer）当前是**死代码**（其类注释自认「从未被调用」）。即 Setup 的「真实占用 + 生产日有界序列优化」在 V1 是从 0 实现，不是换字段。

---

## 一、3号位 所属矛盾（我可直接处置）

| # | 现状（代码锚） | v1.2 口径 | 冲突原因 | 处置 |
|---|---|---|---|---|
| M1 | 换型参数块（SetupParams）仍挂 `DefaultSetupMinutes=30`、`SetupLookAheadSize=5`（`FrozenStrategySnapshot.cs:233-234`；`Dimensions` 已删） | 规则进规则集版本（RuleSetVersion）、算法预算进参数集版本（ParameterSetVersion）；「全公司统一 30 分钟」废止（§五 第三优先） | 冻结快照的求解策略块（SolverStrategyBlock.Setup）仍混着两个已废止语义的旧算法值 | 待 0号位 定快照承载方式（裁决项 2）后，把这俩字段迁出 |
| M2 | 求解策略校验器（SolverStrategyValidator）仍校验「默认换型分钟（DefaultSetupMinutes）必须 >0」+「前瞻窗口（SetupLookAheadSize）≥0」（`SolverStrategyValidator.cs:66-78`）；测试 `E4_SetupDefaultSetupMinutes_非正_拒绝` 设 0 即拒（`SolverStrategyValidatorTests.cs:97-109`） | 新口径校验应为「发布前规则冲突阻止（§十）」+「无规则 0 分钟兜底统计（§十八）」；且无规则时 Setup=0 分钟是**合法结果**（§五 第三优先） | 「必须为正」与「无规则 = 0 分钟」口径相左；校验器在治理两个即将失效的字段，而对真正的新校验一无所知 | 删旧校验；增「明确（EXACT）/默认（DEFAULT）唯一键冲突」发布前校验 |
| M3 | 产品转换换型规则（SetupTransitionRule）无实体、无仓存储、无表（Grep 全仓库仅文档/注释出现） | 需最小字段实体 + EXACT/DEFAULT 唯一键（§九、§十） | 无实体 → 规则无可承载、冲突无可校验 | 3号位 建实体与治理 + 提 DDL 变更申请（建表归 2号位） |
| M4 | 三预算参数——有界搜索预算 / 最大邻域尝试次数 / 单生产日保护阈值——无任何字段 | 归参数集版本（ParameterSetVersion），纯算法数值（§8.2） | 参数无处落 → 「冻结算法参数」被卡死 | 待 0号位 定值（裁决项 1）后落 ParameterSetVersion |

---

## 二、1号位 所属矛盾（我列影响、不碰其代码）

| # | 现状（代码锚） | v1.2 口径 | 冲突原因 | 处置（1号位） |
|---|---|---|---|---|
| M5 | 排程任务（SchedulingTask）.换型属性（SetupAttribute）字段仍在（`SchedulingContext.cs:231`） | 废止模具/刀具/颜色/材质属性式（§1.1） | 已废止概念仍是字段 | 删字段 |
| M6 | 换型优化器（SetupOptimizer）整类按「换型属性相等」判型 + 默认换型 30 分钟（`SetupOptimizer.cs:46,63`） | 规则查表 + 真实资源占用（§二、§十一、§19.4） | 类自带注释「未启用」= 死代码，却仍签名为旧口径；`=30` 违「30 分钟废止」 | 从 0 实现为「前/当前产品 → 换型分钟」查表 + 真实占用 |
| M7 | 排产策略配置（StrategyConfig）仍含 `DefaultSetupMinutes=30`、`SetupLookAheadSize=5`（`SchedulingContext.cs:318,328`） | 全局 30 分钟废止；固定前瞻 → 有界邻域搜索（§8.2） | 策略配置里埋着两个废止值 | 移除/迁移，与 2号位 装载对齐 |
| M8 | 排程任务（SchedulingTask）无「前产品（FromMaterial）/ 当前产品（ToMaterial）」字段，也无设备「上一产品状态」追踪与「初始设备状态」解释（Explanation）承载 | 换型键 = 当前工序 + 设备 + 前产品 + 当前产品（§2.1）；无上一产品 → Setup=0 + Explanation（§14.3、§0.1） | 模型无字段 → 规则查表无输入；初始设备状态无落点 | 补字段 + 补上一产品状态与 Explanation |

---

## 三、2号位 所属矛盾（我列影响、不碰其代码/DDL）

| # | 现状（代码锚） | v1.2 口径 | 冲突原因 | 处置（2号位） |
|---|---|---|---|---|
| M9 | 域求解请求（DomainSolveRequest）未含 Setup 规则查找结构 | 按本 Run 冻结快照装载规则 → 内存快速查找 → 传域求解请求（§19.3） | 1号位 拿不到规则 → M5～M8 无法落地 | 建 `Resource+Operation+FromMaterial+ToMaterial → SetupMinutes` 与 `Resource+Operation → DefaultSetupMinutes` 查找 |
| M10 | 换型转换规则表（SetupTransitionRule）DDL 未建；工序换型时间兼容列（RoutingOperation.SetupTime）未标退役 | 建表（唯一键 §十）；旧列退役、新代码禁读、不 fallback（§1.2、§21） | 无表=规则无物理落点；旧列仍在=有被误读风险 | 建表 + 退役旧列 |

---

## 四、文档级矛盾（非代码）

| # | 现状 | v1.2 口径 | 冲突原因 | 处置 |
|---|---|---|---|---|
| D1 | 11 份正式基线文档仍按旧口径（属性式 + MES 换型时间 + 前小工序/后小工序） | §21 列出必改清单；仅保留一个工序维度（OperationCode）、禁出现前工序/后工序（FromOperationCode/ToOperationCode） | 正式基线未回写 → 新旧两套口径并行，实施包对不上 | 各号位按 §21 增量回写正式基线 |

---

## 五、处置节奏

- **3号位 现在可动**：M2 删旧校验 + 换「冲突校验」；M3 建实体与 DDL 申请（表交 2号位）。
- **0号位 裁决已全部收口（裁决项 1/2/3/4）**：M1 承载=裁决项2（FrozenStrategySnapshot 载荷一次性交付）；M4 三参数=裁决项1（1号位 标定、3号位 ParameterSetVersion 治理）；生产日识别=裁决项3（Q1·Resource Calendar 最大连续可用窗口，不新增 ProductionDay 字段）；初始设备状态=裁决项4（Q4·Setup=0 + ExplainTrace `INITIAL_SETUP_STATE` 分支，不新增 ReasonCode）。
- **1号位**：已接《致 1号位 换型口径回执》开工，SetupOptimizer 骨架完成（新增 17 测试全绿）；数值按裁决项1 自行标定、不再等 0号位 定值。
- **2号位**：M9 域求解请求（DomainSolveRequest）装载头号阻塞，先动；M10 建表随 3号位 DDL 申请。

（工艺约定：术语一律「中文名称（英文）」；文档述引「§符号锚」不 cite 行号，代码引 `文件:行`。）

---

## 六、§19.1 职责对账（3号位 八项职责 vs 现状）

> 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》§十九·19.1。
> 逐项对当前代码，标「现状 / 影响原因 / 解决方法」。红线：**3号位 不负责 Pegging 和 Solver 序列计算**（§19.1 末句）。
> 更新：0号位 已于 2026-09-17 发《Setup换型 v1.2 悬置项正式回复 v1.0》，裁决项 1（三参数）/ 2（快照承载）/ 3（Q1 生产日）/ 4（Q4 Explanation 码）**全部收口**，本文据其改判。

| # | §19.1 职责 | 现状（代码/契约锚） | 影响原因 | 解决方法 |
|---|---|---|---|---|
| 1 | Setup规则治理 | ✅ 实体 `SetupTransitionRule.cs` + DDL 补丁 v5.1.8（§九字段/§十唯一键 + EXACT/DEFAULT 空值 CHECK）已落地（2026-09-17，DB 红线 #6 豁免自建） | 规则有承载、唯一键有兜底 | ✅ 已落地；剩 ResourceId/FromMaterialId/ToMaterialId 的 FK 主键口径待 2号位 确认 |
| 2 | RuleSetVersion内容 | 冻结快照（FrozenStrategySnapshot）只有 `RuleSetVersionId` 标量锚，无规则载荷 | 规则版本号空转，内容装不进版本 | ✅已裁决（裁决项2）：Setup 规则属 RuleSetVersion、**不新增**第二套版本平台；本 Run 经 FrozenStrategySnapshot/等价一次性 DTO 完整交付 2号位；3号位 提 `SetupTransitionRules` 载荷（类名与 2号位 对齐） |
| 3 | 冲突校验 | 校验器仍查旧「默认换型分钟>0 / 前瞻≥0」（`SolverStrategyValidator.cs:66-78`），无 EXACT/DEFAULT 唯一键冲突校验 | 校验错目标，新唯一键冲突（§十）无人拦（矛盾 M2） | 3号位 **现在可动**：删旧换型校验，换 §十 唯一键冲突校验 |
| 4 | 权限 | 权限码体系（PermissionCodes 34 码）已冻结，Setup 维护端点未建未挂 | 新端点无鉴权码可挂，存在越权风险 | ✅已定：**复用规则治理三码、不新增 Setup 专属码**——查看→`RuleView`(aps.rule.view)、维护→`RuleMaintain`(aps.rule.edit)、发布→`RulePublish`(aps.rule.publish)；#8 建端点时按此挂 `[Authorize(Policy=…)]` |
| 5 | 审计 | 统一审计日志（AuditLog，APS_Auth）已落地（GovernanceAuditLog 已 P0-04 否决「第14张表」+ 5c 收敛删除、代码零引用），但 Setup 规则变更未接入 | 规则增删改/发布无审计痕迹，版本 Diff 无据可查 | ✅已定：复用 `IAuditLogRepository`（AuditLog 表），`EntityType="SetupTransitionRule"`，记 UserId/ActionCode(Create·Update·Delete)/前后值(OldValue·NewValue)/VersionCode(=规则版本码)；代码调用点随 #8 维护服务落地 |
| 6 | Setup算法参数治理 | 三预算参数（有界搜索预算 / 最大邻域尝试次数 / 单生产日保护阈值）无字段（矛盾 M4） | 算法参数无处落 | ✅已裁决（裁决项1）：0号位 不冻 500/50 具体值，归 Solver 内部技术参数，由 **1号位 性能标定提值**，3号位 只做参数集版本（ParameterSetVersion）治理；「单生产日保护阈值」**当前删除**（语义未定义，不造字段） |
| 7 | 本Run规则版本冻结 | 快照有 `RuleSetVersionId` 锚，但规则载荷承载方式未定 | 2号位 无法按本 Run 冻结版本装载规则（§19.3 依赖此） | ✅已裁决：本 Run 实际 Setup 规则内容必须经 FrozenStrategySnapshot 一次性交付 2号位，禁 Solver 逐 Task 查 3号位（同第 2 项） |
| 8 | Setup维护API | 实体+表已建，维护端点（Controller/契约）未出 | 4号位（§19.2 维护界面）无后端可调 | 3号位 出治理端点契约（CRUD + 发布/Diff + 权限 + 审计）；后端实体落库仓储待 2号位 |

### 六.1 红线（§19.1 末句）

**3号位 不负责 Pegging 和 Solver 序列计算**：编排器（PeggingOrchestrator）、阶段一/三求解（PhaseOneConstraintBuilder / PhaseThreeDiagnostics）归 2号位 / 1号位。3号位 只治理规则、参数、版本、冲突校验、权限、审计、维护 API，不碰序列计算。

### 六.2 合计划分

- **3号位 已落地**：① 规则实体 + 建表 DDL（DB 红线 #6 豁免自建）· ②⑦ FrozenStrategySnapshot 增 `SetupTransitionRules` 载荷 · ③ 冲突校验替换 · ④ 权限码（复用 RuleView/RuleMaintain/RulePublish，不新增码）。
- **3号位 现在可动（纯 3号位、零越位、不再被裁决卡）**：⑤ 审计接入（统一 AuditLog 表，复用 `IAuditLogRepository`、`EntityType="SetupTransitionRule"`；调用点随 #8 落地）。
- **等 1号位 提值后才落**：⑥ 有界搜索预算 / 最大邻域尝试次数 → 3号位 做 ParameterSetVersion 治理；「单生产日保护阈值」已删除。
- **跨号位协调（3号位 牵头）**：⑨ ExplainTrace Setup 解释类型——Q4 已裁（0号位：不新增 ReasonCode，`INITIAL_SETUP_STATE` 符号锚 + 与「规则缺失降级」严格分支）；待 2号位 定 ExplainTrace 载体契约（`DomainSolveResult` 现仅 ExplanationFacts）后三方对齐 explanationType 符号。
- **依赖 2号位**：①补 ResourceId/FromMaterialId/ToMaterialId 的 FK 主键口径 · ⑧ 维护 API 后端实体落库仓储 + SetupTransitionRules 快照投影/Domain 裁剪。