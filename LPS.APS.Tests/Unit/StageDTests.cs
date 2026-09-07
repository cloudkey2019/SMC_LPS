using System.Text.Json;
using FluentAssertions;
using LPS.APS.Core.Dto;
using LPS.APS.Core.Rules;
using Xunit;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 阶段 D 验收测试（门 R07~R13）
/// 验收口径：各参数块必须"可表达"——即 2 号位/1 号位能从冻结配置 JSON 读取正确值。
/// 每个测试以 JSON 序列化→反序列化往返验证字段保持（快照以 JSON 持久化，往返即"下游可读取"）。
/// 契约来源：《3号位规则参数与运行生命周期开发实施包》最低验收场景 R07~R13。
/// 开发者：3号位
/// </summary>
public class StageDTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), JsonOptions)!;

    // ============ R07：Demand Protection 触发（2号位能读取触发配置） ============

    [Fact]
    public void R07_Protection触发配置_可读取()
    {
        // Arrange：完整触发 + Sticky 配置（D-1）
        var block = new LockBlock
        {
            Trigger = new ProtectionTriggerParams
            {
                UseRemainingTimeThreshold = true,
                RemainingTimeThresholdHours = 48,
                ProtectDelayed = true,
                ProtectVipTier = true,
                VipTierValue = "VIP",
                ProtectedOrderTypes = new[] { "MTO", "MTS" },
                AllowPmcManualProtection = true
            },
            Sticky = new StickyProtectionParams
            {
                RequireReleaseRecord = true,
                ProtectUntilCompletion = true,
                ProtectUntilSupplyInvalid = true
            }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：2号位读取触发配置时字段值完整
        restored.Trigger.UseRemainingTimeThreshold.Should().BeTrue();
        restored.Trigger.RemainingTimeThresholdHours.Should().Be(48);
        restored.Trigger.ProtectDelayed.Should().BeTrue();
        restored.Trigger.ProtectVipTier.Should().BeTrue();
        restored.Trigger.VipTierValue.Should().Be("VIP");
        restored.Trigger.ProtectedOrderTypes.Should().Equal("MTO", "MTS");
        restored.Trigger.AllowPmcManualProtection.Should().BeTrue();
        restored.Sticky.RequireReleaseRecord.Should().BeTrue();
        restored.Sticky.ProtectUntilCompletion.Should().BeTrue();
        restored.Sticky.ProtectUntilSupplyInvalid.Should().BeTrue();
    }

    // ============ R08：PI 默认排序（Issue/Create Time 规则正确） ============

    [Fact]
    public void R08_PI默认排序_可读取()
    {
        // Arrange：默认 Issue/Create Time ASC（D-3）
        var block = new PiSortParams
        {
            SortBy = PiSortBy.IssueDateAsc,
            UseStablePiNoTieBreak = true
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：排序方向枚举可往返（Issue 优先；稳定 PI 号作平局决胜）
        restored.SortBy.Should().Be(PiSortBy.IssueDateAsc);
        restored.UseStablePiNoTieBreak.Should().BeTrue();
    }

    [Fact]
    public void R08_PI排序枚举_覆盖Issue与CreatedAt两种规则()
    {
        // 验收：R08 要求 Issue/Create Time 规则正确——两种排序方向均可表达
        Enum.GetValues<PiSortBy>().Should().Contain(new[] { PiSortBy.IssueDateAsc, PiSortBy.CreatedAtAsc });
    }

    // ============ R09：Warehouse Availability（规则可正确解析） ============

    [Fact]
    public void R09_WarehouseAvailability规则_可解析()
    {
        // Arrange：库存可用规则（D-2）
        var block = new SupplyBlock
        {
            Inventory = new InventoryAvailabilityRule
            {
                IsEnabled = true,
                WarehousePriority = new[] { "WH-01", "WH-02", "WH-03" },
                RequireFactoryContext = true,
                RequireProductFamilyContext = false
            }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：仓库优先级顺序与上下文要求保持（解析顺序即履约顺序）
        restored.Inventory.IsEnabled.Should().BeTrue();
        restored.Inventory.WarehousePriority.Should().Equal("WH-01", "WH-02", "WH-03");
        restored.Inventory.RequireFactoryContext.Should().BeTrue();
        restored.Inventory.RequireProductFamilyContext.Should().BeFalse();
    }

    [Fact]
    public void R09_WarehouseAvailability_契约JSON形状_可被下游反序列化()
    {
        // 自往返（R09 上例）只证明 DTO 自洽；本例用与冻结契约一致的 PascalCase 属性名 + 数字枚举
        // 字面 JSON，验证 2号位读取的真实快照持久化形状（属性未重命名、枚举可解析）。
        // Arrange：契约字面形状（《3号位实施包》/冻结 DDL JSON 列存储形状）
        const string contractJson = """
            {
              "Inventory": {
                "IsEnabled": true,
                "WarehousePriority": ["WH-01", "WH-02"],
                "RequireFactoryContext": true,
                "RequireProductFamilyContext": false
              },
              "PiSort": { "SortBy": 0, "UseStablePiNoTieBreak": true }
            }
            """;

        // Act：按下游读取路径反序列化（PascalCase 属性 + 数字枚举 SortBy=0 → IssueDateAsc）
        var restored = JsonSerializer.Deserialize<SupplyBlock>(contractJson, JsonOptions);

        // Assert
        restored.Should().NotBeNull();
        restored.Inventory.IsEnabled.Should().BeTrue();
        restored.Inventory.WarehousePriority.Should().Equal("WH-01", "WH-02");
        restored.Inventory.RequireFactoryContext.Should().BeTrue();
        restored.PiSort.SortBy.Should().Be(PiSortBy.IssueDateAsc);
        restored.PiSort.UseStablePiNoTieBreak.Should().BeTrue();
    }

    // ============ R10：Manual ETA > ERP ETA > DefaultLT（参数链可表达） ============

    [Fact]
    public void R10_ETA链_优先级固定可表达()
    {
        // 验收：参数链可表达——固化链行为确定性（完整语义见 EtaInvariantTests）
        // Arrange
        var manualEta = new DateTime(2026, 9, 1);
        var erpEta = new DateTime(2026, 9, 5);
        var defaultLtEta = new DateTime(2026, 9, 10);

        // Act
        var full = EtaInvariant.Resolve(manualEta, erpEta, defaultLtEta);
        var noManual = EtaInvariant.Resolve(null, erpEta, defaultLtEta);
        var onlyDefaultLt = EtaInvariant.Resolve(null, null, defaultLtEta);

        // Assert：链三段各自可表达，来源标记正确
        full.Source.Should().Be(EtaSource.Manual);
        noManual.Source.Should().Be(EtaSource.Erp);
        onlyDefaultLt.Source.Should().Be(EtaSource.DefaultLt);
        onlyDefaultLt.IsEstimated.Should().BeTrue();   // §10.3 估算标记
    }

    // ============ R11：DefaultLT 逾期 Margin（Margin 可配置） ============

    [Fact]
    public void R11_逾期Margin_可配置()
    {
        // Arrange：保守修正参数（D-5）
        var block = new ProcurementBlock
        {
            OverdueMargin = new OverdueMarginParams
            {
                MarginPercent = 0.1m,
                MinimumExtraDays = 3
            }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：ReleaseDate + DefaultLT < Now 时的保守修正参数可读取
        restored.OverdueMargin.MarginPercent.Should().Be(0.1m);
        restored.OverdueMargin.MinimumExtraDays.Should().Be(3);
    }

    // ============ R12：Arrival Offset（Warehouse 级生效） ============

    [Fact]
    public void R12_ArrivalOffset_按Warehouse级生效()
    {
        // Arrange：到货后可用偏移（D-6），多仓库各自生效
        var block = new ProcurementBlock
        {
            ArrivalToUsableOffsets = new List<WarehouseOffsetRule>
            {
                new WarehouseOffsetRule { WarehouseCode = "WH-01", OffsetHours = 24 },
                new WarehouseOffsetRule { WarehouseCode = "WH-02", OffsetHours = 8 }
            }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：每个 Warehouse 独立生效，不合并不串扰
        restored.ArrivalToUsableOffsets.Should().HaveCount(2);
        restored.ArrivalToUsableOffsets.Single(o => o.WarehouseCode == "WH-01").OffsetHours.Should().Be(24);
        restored.ArrivalToUsableOffsets.Single(o => o.WarehouseCode == "WH-02").OffsetHours.Should().Be(8);
    }

    [Fact]
    public void R09_Procurement仓库优先级_可读取()
    {
        // 验收（0号位 2026-08-28 最终裁决）：采购/Timed Supply 排序链固定、**无 Enable/Disable 开关**；
        // 3号位 只治理参数值——"Warehouse Priority"为采购链第 2 步的参数值（有序列表，隐含优先级），
        // 随 Procurement 子块 JSON 往返，2号位可读取。
        // Arrange
        var block = new ProcurementBlock
        {
            WarehousePriority = new[] { "WH-A", "WH-B", "WH-C" }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：优先级顺序保持（顺序即履约顺序）
        restored.WarehousePriority.Should().Equal("WH-A", "WH-B", "WH-C");
    }

    // ============ R13：Planning Yield（2号位可取正确版本值） ============

    [Fact]
    public void R13_PlanningYield_可取正确版本值()
    {
        // Arrange：Material/Stage 级良率（D-7，契约 C2-5）
        // 红线固化（注释）：已有 PI Supply 不得按 Yield 再放大——放大判定属下游消费方，本参数层只表达 YieldPercent。
        var block = new ProcurementBlock
        {
            PlanningYields = new List<PlanningYieldRule>
            {
                new PlanningYieldRule { MaterialId = "MAT-001", StageCode = null, YieldPercent = 0.95m },
                new PlanningYieldRule { MaterialId = "MAT-001", StageCode = "ST-10", YieldPercent = 0.9m }
            }
        };

        // Act
        var restored = RoundTrip(block);

        // Assert：2号位按 Material/Stage 键可取正确版本值
        restored.PlanningYields.Should().HaveCount(2);
        var stageLevel = restored.PlanningYields.Single(r => r.StageCode == "ST-10");
        stageLevel.MaterialId.Should().Be("MAT-001");
        stageLevel.YieldPercent.Should().Be(0.9m);
        var materialLevel = restored.PlanningYields.Single(r => r.StageCode == null);
        materialLevel.YieldPercent.Should().Be(0.95m);
    }
}
