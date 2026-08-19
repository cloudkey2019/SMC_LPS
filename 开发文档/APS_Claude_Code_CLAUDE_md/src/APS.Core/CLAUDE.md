# CLAUDE.md — 1号位排程计算域规则

## 职责

本目录属于 1号位，负责排程计算域。

只允许实现：

- 内存排程算法
- Task 排布
- 资源时间窗计算
- 有限产能计算
- 排程输出对象生成
- ExplanationFactDraft 内存原因事实草稿

## 禁止事项

- 禁止访问数据库。
- 禁止访问文件系统。
- 禁止访问网络。
- 禁止调用 API。
- 禁止直接写 ScheduleExplanationFact。
- 禁止直接修改 PlanVersion / ScheduleRun。
- 禁止读取 ODS / ERP / MES。
- 禁止写业务规则插件。
- 禁止修改 2号位持久化逻辑。

## ScheduleRun 相关规则

1号位不创建 ScheduleRun。
1号位不回填 ScheduleRun.Status。
1号位不激活 PlanVersion。

1号位只接收已经构建好的 ScheduleContext。

## ExplanationFactDraft 规则

1号位可以在计算过程中输出 ExplanationFactDraft。

ExplanationFactDraft 仅为内存对象，必须交给 2号位统一落库。

禁止在本目录中出现数据库写入 ScheduleExplanationFact 的代码。

## 性能规则

- 热点路径避免 LINQ，尤其禁止循环体内 `.Where()` / `.Select()` / `.ToList()`。
- 避免隐式装箱和隐式堆分配。
- 大集合计算优先使用数组、Span、对象池。
- 禁止在核心排程循环中写日志。
- 禁止在核心排程循环中访问外部服务。
- 如做性能优化，应补 BenchmarkDotNet 或等价基准测试。

## 测试规则

- 每个公共计算方法应有单元测试。
- 边界条件必须覆盖：空集合、资源不可用、时间窗冲突、冻结任务、跨阶段依赖。
- 性能热点修改必须说明复杂度变化。

## 输出要求

每次修改本目录代码时，必须说明：

1. 是否影响排程结果
2. 是否改变 Task 生成逻辑
3. 是否产生 ExplanationFactDraft
4. 是否影响性能热点路径
5. 是否需要 2号位落库结构配合
