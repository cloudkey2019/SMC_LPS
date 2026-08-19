using System;
using System.Threading.Tasks;
using LPS.APS.Tests.Integration;

namespace LPS.APS.Tests;

/// <summary>
/// v5.1.2架构集成测试程序入口
///
/// 使用方式：
/// 1. 确保SQL Server运行中
/// 2. 修改 appsettings.Test.json 中的数据库连接字符串
/// 3. 运行: dotnet run --project LPS.APS.Tests
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("LPS.APS v5.1.2 架构集成测试");
        Console.WriteLine();
        Console.WriteLine("测试环境检查...");
        Console.WriteLine("- 请确保SQL Server运行中");
        Console.WriteLine("- 测试数据库: APS_Production_Test");
        Console.WriteLine();
        Console.Write("继续测试? (y/n): ");

        var input = Console.ReadLine();
        if (input?.ToLower() != "y")
        {
            Console.WriteLine("测试已取消");
            return;
        }

        Console.WriteLine();

        try
        {
            var test = new RealSchedulingIntegrationTest();
            await test.RunAllTestsAsync();

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"测试失败: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
            Environment.Exit(1);
        }
    }
}
