using LPS.APS.Core.Authorization;
using LPS.APS.Core.DTOs.Ctp;
using LPS.APS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace LPS.APS.Application.Services;

/// <summary>
/// CTP 评估服务实现（APS V1 CTP 评估接口契约登记 v1.0 §3.2）。
/// 处理链：显式 domainKey 优先；缺省按 materialCode+factoryCode 自动 resolve（纯后端容错兜底）；
/// resolve 无唯一匹配 → InvalidOperationException（调用方转 400）；
/// F-G4 业务范围二次校验（fail-closed）→ ScopeViolationException（调用方转 403）。
/// 核心试算能力归 1号位/2号位，接入前由宿主返回 501，本服务不承载试算编排（E-4 宿主链）。
/// </summary>
/// <remarks>开发者：3号位（CTP 薄层契约）。</remarks>
public sealed class CtpService : ICtpService
{
    private readonly IDomainResolver _domainResolver;
    private readonly IDataScopeService _dataScopeService;
    private readonly ILogger<CtpService> _logger;

    public CtpService(
        IDomainResolver domainResolver,
        IDataScopeService dataScopeService,
        ILogger<CtpService> logger)
    {
        _domainResolver = domainResolver ?? throw new ArgumentNullException(nameof(domainResolver));
        _dataScopeService = dataScopeService ?? throw new ArgumentNullException(nameof(dataScopeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> EvaluateAsync(CtpEvaluateRequest request, int actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ① 确定 DomainKey：显式优先；缺省自动 resolve（纯后端容错兜底，契约 §3.2）
        var domainKey = request.DomainKey;
        if (string.IsNullOrWhiteSpace(domainKey))
        {
            domainKey = await _domainResolver.ResolveAsync(request.MaterialCode, request.FactoryCode, cancellationToken);

            // 无唯一匹配（无映射 / 多域歧义）→ 业务错，调用方转 400（红线 #4，禁盲目 .First()）
            if (string.IsNullOrWhiteSpace(domainKey))
            {
                _logger.LogWarning(
                    "CTP domainKey 缺失且自动 resolve 无唯一匹配：MaterialCode={MaterialCode}, FactoryCode={FactoryCode}",
                    request.MaterialCode, request.FactoryCode);
                throw new InvalidOperationException("无法自动确定 DomainKey。");
            }

            _logger.LogInformation(
                "CTP domainKey 自动 resolve：MaterialCode={MaterialCode}, FactoryCode={FactoryCode} → {DomainKey}",
                request.MaterialCode, request.FactoryCode, domainKey);
        }

        // ② F-G4 业务范围二次校验（fail-closed，不因自动 resolve 而跳过）
        await _dataScopeService.EnsureInScopeAsync(actorUserId, DataScopeTypes.Domain, domainKey, cancellationToken);

        return domainKey;
    }
}
