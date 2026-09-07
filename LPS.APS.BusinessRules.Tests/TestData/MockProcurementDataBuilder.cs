using LPS.APS.BusinessRules.Models;

namespace LPS.APS.BusinessRules.Tests.TestData;

public static class MockProcurementDataBuilder
{
    public static List<RawProcurementFact> BuildValidProcurementFacts(int count = 10)
    {
        var baseDate = new DateTime(2026, 8, 15);
        var facts = new List<RawProcurementFact>();

        for (int i = 1; i <= count; i++)
        {
            facts.Add(new RawProcurementFact
            {
                MaterialCode = $"MAT{i:D4}",
                MaterialId = 1000 + i,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 100 * i,
                ManualEta = i % 3 == 0 ? baseDate.AddDays(10 + i) : null,
                Eta = i % 2 == 0 ? baseDate.AddDays(15 + i) : null,
                ReleaseDate = baseDate.AddDays(i),
                StorageCode = $"WH{(i % 3) + 1:D2}",
                SupplyType = GetSupplyType(i),
                CommitmentStatus = i % 4 == 0 ? "TENTATIVE" : "COMMITTED",
                Confidence = i % 5 == 0 ? "MEDIUM" : "HIGH",
                PhysicalSourceKey = $"PO-202608{i:D2}-{i:D3}",
                SourceDocumentLineNo = $"{i * 10}",
                SourceUpdatedAt = baseDate.AddDays(i).AddHours(10)
            });
        }

        return facts;
    }

    public static List<RawProcurementFact> BuildMixedQualityFacts()
    {
        var baseDate = new DateTime(2026, 8, 15);
        var facts = new List<RawProcurementFact>();

        facts.Add(new RawProcurementFact
        {
            MaterialCode = "MAT-GOOD-01",
            MaterialId = 1001,
            FactoryId = 5001,
            FactoryCode = "CN",
            RemainingQty = 500,
            ManualEta = baseDate.AddDays(10),
            Eta = baseDate.AddDays(12),
            ReleaseDate = baseDate,
            StorageCode = "WH01",
            SupplyType = "OPEN_PO_REMAINING",
            CommitmentStatus = "COMMITTED",
            Confidence = "HIGH",
            PhysicalSourceKey = "PO-GOOD-001",
            SourceDocumentLineNo = "10",
            SourceUpdatedAt = baseDate.AddHours(8)
        });

        facts.Add(new RawProcurementFact
        {
            MaterialCode = "MAT-BAD-F15",
            MaterialId = 1002,
            FactoryId = 5001,
            FactoryCode = "CN",
            RemainingQty = 300,
            ManualEta = null,
            Eta = null,
            ReleaseDate = null,
            StorageCode = "WH01",
            SupplyType = "OPEN_PO_REMAINING",
            CommitmentStatus = "COMMITTED",
            Confidence = "HIGH",
            PhysicalSourceKey = "PO-BAD-F15-001",
            SourceDocumentLineNo = "20",
            SourceUpdatedAt = baseDate.AddHours(9)
        });

        facts.Add(new RawProcurementFact
        {
            MaterialCode = "MAT-BAD-TYPE",
            MaterialId = 1003,
            FactoryId = 5001,
            FactoryCode = "CN",
            RemainingQty = 200,
            ManualEta = null,
            Eta = null,
            ReleaseDate = baseDate.AddDays(5),
            StorageCode = "WH02",
            SupplyType = "INVALID_SUPPLY_TYPE",
            CommitmentStatus = "COMMITTED",
            Confidence = "HIGH",
            PhysicalSourceKey = "PO-BAD-TYPE-001",
            SourceDocumentLineNo = "30",
            SourceUpdatedAt = baseDate.AddHours(10)
        });

        facts.Add(new RawProcurementFact
        {
            MaterialCode = "MAT-GOOD-02",
            MaterialId = 1004,
            FactoryId = 5001,
            FactoryCode = "CN",
            RemainingQty = 150,
            ManualEta = null,
            Eta = baseDate.AddDays(8),
            ReleaseDate = baseDate.AddDays(3),
            StorageCode = "WH01",
            SupplyType = "PURCHASE_IN_TRANSIT",
            CommitmentStatus = "COMMITTED",
            Confidence = "HIGH",
            PhysicalSourceKey = "PO-GOOD-002",
            SourceDocumentLineNo = "40",
            SourceUpdatedAt = baseDate.AddHours(11)
        });

        return facts;
    }

    public static List<RawProcurementFact> BuildPerformanceTestData(int count = 1000)
    {
        var baseDate = new DateTime(2026, 8, 15);
        var facts = new List<RawProcurementFact>();
        var random = new Random(42);

        for (int i = 1; i <= count; i++)
        {
            var factoryId = 5001 + (i % 5);
            var materialId = 1000 + (i % 100);

            facts.Add(new RawProcurementFact
            {
                MaterialCode = $"MAT{materialId:D6}",
                MaterialId = materialId,
                FactoryId = factoryId,
                FactoryCode = GetFactoryCode(factoryId),
                RemainingQty = random.Next(10, 1000),
                ManualEta = i % 5 == 0 ? baseDate.AddDays(random.Next(5, 30)) : null,
                Eta = i % 3 == 0 ? baseDate.AddDays(random.Next(10, 45)) : null,
                ReleaseDate = baseDate.AddDays(random.Next(0, 7)),
                StorageCode = $"WH{random.Next(1, 10):D2}",
                SupplyType = GetSupplyType(i),
                CommitmentStatus = i % 4 == 0 ? "TENTATIVE" : "COMMITTED",
                Confidence = random.Next(0, 10) < 8 ? "HIGH" : "MEDIUM",
                PhysicalSourceKey = $"PO-PERF-{i:D6}",
                SourceDocumentLineNo = $"{random.Next(10, 999)}",
                SourceUpdatedAt = baseDate.AddDays(random.Next(0, 7)).AddHours(random.Next(0, 24))
            });
        }

        return facts;
    }

    public static List<RawProcurementFact> BuildAllSupplyTypesFacts()
    {
        var baseDate = new DateTime(2026, 8, 15);
        return new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialCode = "MAT-TYPE1",
                MaterialId = 1001,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 100,
                ReleaseDate = baseDate.AddDays(5),
                StorageCode = "WH01",
                SupplyType = "OPEN_PO_REMAINING",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-TYPE1-001",
                SourceDocumentLineNo = "10",
                SourceUpdatedAt = baseDate
            },
            new RawProcurementFact
            {
                MaterialCode = "MAT-TYPE2",
                MaterialId = 1002,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 200,
                ReleaseDate = baseDate.AddDays(3),
                StorageCode = "WH01",
                SupplyType = "PURCHASE_IN_TRANSIT",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-TYPE2-001",
                SourceDocumentLineNo = "20",
                SourceUpdatedAt = baseDate
            },
            new RawProcurementFact
            {
                MaterialCode = "MAT-TYPE3",
                MaterialId = 1003,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 150,
                ReleaseDate = baseDate.AddDays(1),
                StorageCode = "WH02",
                SupplyType = "ARRIVED_NOT_RECEIVED",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-TYPE3-001",
                SourceDocumentLineNo = "30",
                SourceUpdatedAt = baseDate
            },
            new RawProcurementFact
            {
                MaterialCode = "MAT-TYPE4",
                MaterialId = 1004,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 300,
                ReleaseDate = baseDate,
                StorageCode = "WH01",
                SupplyType = "VMI_ONSITE",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "VMI-TYPE4-001",
                SourceDocumentLineNo = "40",
                SourceUpdatedAt = baseDate
            }
        };
    }

    public static List<RawProcurementFact> BuildEtaPriorityTestFacts()
    {
        var baseDate = new DateTime(2026, 8, 15);
        return new List<RawProcurementFact>
        {
            new RawProcurementFact
            {
                MaterialCode = "MAT-MANUAL-ONLY",
                MaterialId = 1001,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 100,
                ManualEta = baseDate.AddDays(20),
                Eta = baseDate.AddDays(15),
                ReleaseDate = baseDate.AddDays(10),
                StorageCode = "WH01",
                SupplyType = "OPEN_PO_REMAINING",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-MANUAL-001",
                SourceDocumentLineNo = "10",
                SourceUpdatedAt = baseDate
            },
            new RawProcurementFact
            {
                MaterialCode = "MAT-ERP-ONLY",
                MaterialId = 1002,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 200,
                ManualEta = null,
                Eta = baseDate.AddDays(18),
                ReleaseDate = baseDate.AddDays(12),
                StorageCode = "WH01",
                SupplyType = "OPEN_PO_REMAINING",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-ERP-001",
                SourceDocumentLineNo = "20",
                SourceUpdatedAt = baseDate
            },
            new RawProcurementFact
            {
                MaterialCode = "MAT-RELEASE-ONLY",
                MaterialId = 1003,
                FactoryId = 5001,
                FactoryCode = "CN",
                RemainingQty = 150,
                ManualEta = null,
                Eta = null,
                ReleaseDate = baseDate.AddDays(14),
                StorageCode = "WH01",
                SupplyType = "OPEN_PO_REMAINING",
                CommitmentStatus = "COMMITTED",
                Confidence = "HIGH",
                PhysicalSourceKey = "PO-RELEASE-001",
                SourceDocumentLineNo = "30",
                SourceUpdatedAt = baseDate
            }
        };
    }

    private static string GetSupplyType(int index)
    {
        return (index % 4) switch
        {
            0 => "OPEN_PO_REMAINING",
            1 => "PURCHASE_IN_TRANSIT",
            2 => "ARRIVED_NOT_RECEIVED",
            3 => "VMI_ONSITE",
            _ => "OPEN_PO_REMAINING"
        };
    }

    private static string GetFactoryCode(int factoryId)
    {
        return factoryId switch
        {
            5001 => "CN",
            5002 => "BJ",
            5003 => "TJ",
            5004 => "CN6",
            5005 => "SH",
            _ => "CN"
        };
    }
}
