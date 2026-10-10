using LPS.APS.Engine.Configuration;
using LPS.APS.Engine.Data;
using Microsoft.Extensions.Options;

namespace LPS.APS.BusinessRules.Tests.Helpers;

/// <summary>
/// 可 Mock 的 DatabaseConnectionManager 测试子类
///
/// 用途：提供无参构造函数，使 Moq 创建代理时无需传入 IOptions&lt;DatabaseOptions&gt;。
/// 基类 QueryAsync 等方法已声明为 virtual，Moq 可直接 Setup。
///
/// 如需从根本解决构造问题，建议2号位抽取 IDatabaseConnectionManager 接口。
/// </summary>
public class TestableDatabaseConnectionManager : DatabaseConnectionManager
{
    private static readonly IOptions<DatabaseOptions> DefaultTestOptions = Options.Create(new DatabaseOptions
    {
        APS = new DatabaseConnectionOptions { ConnectionString = "Server=test;Database=APS_Test;Trusted_Connection=true;" },
        ODS = new DatabaseConnectionOptions { ConnectionString = "Server=test;Database=ODS_Test;Trusted_Connection=true;" },
        Auth = new DatabaseConnectionOptions { ConnectionString = "Server=test;Database=Auth_Test;Trusted_Connection=true;" }
    });

    public TestableDatabaseConnectionManager() : base(DefaultTestOptions)
    {
    }
}
