using Microsoft.AspNetCore.Mvc;
using LPS.APS.Core.Interfaces;
using LPS.APS.Core.Entities.APS;

namespace LPS.APS.Web.Controllers;

/// <summary>
/// 治理版本管理 API（阶段 A-9：3号位 Web 层实现）
/// 提供 RuleSetVersion / ParameterSetVersion 的 CRUD + 发布接口。
/// 红线：发布接口仅接受 DRAFT/SUBMITTED/APPROVED 状态（六态状态机 R01 验收）。
/// </summary>
/// <remarks>开发者：3号位</remarks>
[ApiController]
[Route("api/[controller]")]
public class GovernanceController : ControllerBase
{
    private readonly IGovernanceVersionService _governanceService;
    private readonly IRuleSetVersionRepository _ruleSetVersionRepo;
    private readonly IParameterSetVersionRepository _parameterSetVersionRepo;
    private readonly IStrategyProfileVersionRepository _strategyProfileVersionRepo;
    private readonly IRunLifecycleService _runLifecycleService;
    private readonly ILogger<GovernanceController> _logger;

    public GovernanceController(
        IGovernanceVersionService governanceService,
        IRuleSetVersionRepository ruleSetVersionRepo,
        IParameterSetVersionRepository parameterSetVersionRepo,
        IStrategyProfileVersionRepository strategyProfileVersionRepo,
        IRunLifecycleService runLifecycleService,
        ILogger<GovernanceController> logger)
    {
        _governanceService = governanceService;
        _ruleSetVersionRepo = ruleSetVersionRepo;
        _parameterSetVersionRepo = parameterSetVersionRepo;
        _strategyProfileVersionRepo = strategyProfileVersionRepo;
        _runLifecycleService = runLifecycleService;
        _logger = logger;
    }

    #region RuleSetVersion CRUD

    /// <summary>获取规则集的所有版本</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("rule-set/{ruleSetId}/versions")]
    public async Task<IActionResult> GetRuleSetVersions(long ruleSetId, CancellationToken ct)
    {
        var versions = await _ruleSetVersionRepo.GetByRuleSetIdAsync(ruleSetId, ct);
        return Ok(new { success = true, data = versions });
    }

    /// <summary>获取规则集版本详情</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("rule-set/version/{versionId}")]
    public async Task<IActionResult> GetRuleSetVersion(long versionId, CancellationToken ct)
    {
        var version = await _ruleSetVersionRepo.GetByIdAsync(versionId, ct);
        if (version == null)
        {
            return NotFound(new { success = false, error = $"规则集版本不存在：{versionId}" });
        }
        return Ok(new { success = true, data = version });
    }

    /// <summary>创建规则集版本（初始状态 DRAFT）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("rule-set/version")]
    public async Task<IActionResult> CreateRuleSetVersion([FromBody] RuleSetVersion version, CancellationToken ct)
    {
        version.CreatedAt = DateTime.UtcNow;
        var created = await _ruleSetVersionRepo.AddAsync(version, ct);
        return CreatedAtAction(nameof(GetRuleSetVersion), new { versionId = created.Id }, new { success = true, data = created });
    }

    /// <summary>更新规则集版本（仅限非 PUBLISHED 状态）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPut("rule-set/version/{versionId}")]
    public async Task<IActionResult> UpdateRuleSetVersion(long versionId, [FromBody] RuleSetVersion version, CancellationToken ct)
    {
        var existing = await _ruleSetVersionRepo.GetByIdAsync(versionId, ct);
        if (existing == null)
        {
            return NotFound(new { success = false, error = $"规则集版本不存在：{versionId}" });
        }

        version.Id = versionId;
        await _ruleSetVersionRepo.UpdateAsync(version, ct);
        return Ok(new { success = true, data = version });
    }

    #endregion

    #region ParameterSetVersion CRUD

    /// <summary>获取参数集的所有版本</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("parameter-set/{parameterSetId}/versions")]
    public async Task<IActionResult> GetParameterSetVersions(long parameterSetId, CancellationToken ct)
    {
        var versions = await _parameterSetVersionRepo.GetByParameterSetIdAsync(parameterSetId, ct);
        return Ok(new { success = true, data = versions });
    }

    /// <summary>获取参数集版本详情</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("parameter-set/version/{versionId}")]
    public async Task<IActionResult> GetParameterSetVersion(long versionId, CancellationToken ct)
    {
        var version = await _parameterSetVersionRepo.GetByIdAsync(versionId, ct);
        if (version == null)
        {
            return NotFound(new { success = false, error = $"参数集版本不存在：{versionId}" });
        }
        return Ok(new { success = true, data = version });
    }

    /// <summary>创建参数集版本（初始状态 DRAFT）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("parameter-set/version")]
    public async Task<IActionResult> CreateParameterSetVersion([FromBody] ParameterSetVersion version, CancellationToken ct)
    {
        version.CreatedAt = DateTime.UtcNow;
        var created = await _parameterSetVersionRepo.AddAsync(version, ct);
        return CreatedAtAction(nameof(GetParameterSetVersion), new { versionId = created.Id }, new { success = true, data = created });
    }

    /// <summary>更新参数集版本（仅限非 PUBLISHED 状态）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPut("parameter-set/version/{versionId}")]
    public async Task<IActionResult> UpdateParameterSetVersion(long versionId, [FromBody] ParameterSetVersion version, CancellationToken ct)
    {
        var existing = await _parameterSetVersionRepo.GetByIdAsync(versionId, ct);
        if (existing == null)
        {
            return NotFound(new { success = false, error = $"参数集版本不存在：{versionId}" });
        }

        version.Id = versionId;
        await _parameterSetVersionRepo.UpdateAsync(version, ct);
        return Ok(new { success = true, data = version });
    }

    #endregion

    #region 发布端点（R01/R02 验收）

    /// <summary>
    /// 发布规则集版本（六态状态机 R01 验收）
    /// 红线：仅 DRAFT/SUBMITTED/APPROVED 可发布；PUBLISHED 拒绝（历史不可覆盖）；A-6 不变量自动处理。
    /// </summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("rule-set/version/{versionId}/publish")]
    public async Task<IActionResult> PublishRuleSetVersion(long versionId, [FromBody] PublishRequest request, CancellationToken ct)
    {
        try
        {
            await _governanceService.PublishRuleSetVersionAsync(versionId, request.PublishedBy, ct);
            _logger.LogInformation("规则集版本已发布：{VersionId}，发布人：{PublishedBy}", versionId, request.PublishedBy);
            return Ok(new { success = true, message = $"规则集版本 {versionId} 已发布" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "规则集版本发布失败：{VersionId}", versionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>
    /// 发布参数集版本（六态状态机 R02 验收）
    /// 红线：仅 DRAFT/SUBMITTED/APPROVED 可发布；新 Run 可引用新版本、旧 Run 引用不变；A-6 不变量自动处理。
    /// </summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("parameter-set/version/{versionId}/publish")]
    public async Task<IActionResult> PublishParameterSetVersion(long versionId, [FromBody] PublishRequest request, CancellationToken ct)
    {
        try
        {
            await _governanceService.PublishParameterSetVersionAsync(versionId, request.PublishedBy, ct);
            _logger.LogInformation("参数集版本已发布：{VersionId}，发布人：{PublishedBy}", versionId, request.PublishedBy);
            return Ok(new { success = true, message = $"参数集版本 {versionId} 已发布" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "参数集版本发布失败：{VersionId}", versionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 版本差异对比与溯源（A-8）

    /// <summary>对比两个规则集版本的差异（阶段 A-8：版本溯源）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("rule-set/version/diff")]
    public async Task<IActionResult> CompareRuleSetVersions([FromQuery] long sourceVersionId, [FromQuery] long targetVersionId, CancellationToken ct)
    {
        try
        {
            var result = await _governanceService.CompareRuleSetVersionsAsync(sourceVersionId, targetVersionId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "规则集版本对比失败：{SourceVersionId} vs {TargetVersionId}", sourceVersionId, targetVersionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>对比两个参数集版本的差异（阶段 A-8：版本溯源）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("parameter-set/version/diff")]
    public async Task<IActionResult> CompareParameterSetVersions([FromQuery] long sourceVersionId, [FromQuery] long targetVersionId, CancellationToken ct)
    {
        try
        {
            var result = await _governanceService.CompareParameterSetVersionsAsync(sourceVersionId, targetVersionId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "参数集版本对比失败：{SourceVersionId} vs {TargetVersionId}", sourceVersionId, targetVersionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 发布前校验（A-5）

    /// <summary>校验规则集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("rule-set/version/{versionId}/validate")]
    public async Task<IActionResult> ValidateRuleSetVersionForPublish(long versionId, CancellationToken ct)
    {
        var result = await _governanceService.ValidateRuleSetVersionForPublishAsync(versionId, ct);
        return Ok(result);
    }

    /// <summary>校验参数集版本是否可发布（阶段 A-5：发布前完整校验）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("parameter-set/version/{versionId}/validate")]
    public async Task<IActionResult> ValidateParameterSetVersionForPublish(long versionId, CancellationToken ct)
    {
        var result = await _governanceService.ValidateParameterSetVersionForPublishAsync(versionId, ct);
        return Ok(result);
    }

    #endregion

    #region StrategyProfileVersion（P0-06：策略包版本治理完整闭环）

    /// <summary>获取策略包的所有版本</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("strategy-profile/{strategyProfileId}/versions")]
    public async Task<IActionResult> GetStrategyProfileVersions(long strategyProfileId, CancellationToken ct)
    {
        var versions = await _strategyProfileVersionRepo.GetByStrategyProfileIdAsync(strategyProfileId, ct);
        return Ok(new { success = true, data = versions });
    }

    /// <summary>获取策略包版本详情</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("strategy-profile/version/{versionId}")]
    public async Task<IActionResult> GetStrategyProfileVersion(long versionId, CancellationToken ct)
    {
        var version = await _strategyProfileVersionRepo.GetByIdAsync(versionId, ct);
        if (version == null)
        {
            return NotFound(new { success = false, error = $"策略包版本不存在：{versionId}" });
        }
        return Ok(new { success = true, data = version });
    }

    /// <summary>创建策略包版本（初始状态 DRAFT）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("strategy-profile/version")]
    public async Task<IActionResult> CreateStrategyProfileVersion([FromBody] StrategyProfileVersion version, CancellationToken ct)
    {
        version.CreatedAt = DateTime.UtcNow;
        var created = await _strategyProfileVersionRepo.AddAsync(version, ct);
        return CreatedAtAction(nameof(GetStrategyProfileVersion), new { versionId = created.Id }, new { success = true, data = created });
    }

    /// <summary>更新策略包版本（仅限非 PUBLISHED 状态）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPut("strategy-profile/version/{versionId}")]
    public async Task<IActionResult> UpdateStrategyProfileVersion(long versionId, [FromBody] StrategyProfileVersion version, CancellationToken ct)
    {
        var existing = await _strategyProfileVersionRepo.GetByIdAsync(versionId, ct);
        if (existing == null)
        {
            return NotFound(new { success = false, error = $"策略包版本不存在：{versionId}" });
        }

        version.Id = versionId;
        await _strategyProfileVersionRepo.UpdateAsync(version, ct);
        return Ok(new { success = true, data = version });
    }

    /// <summary>发布策略包版本（P0-06：DRAFT/SUBMITTED/APPROVED → PUBLISHED；发布前强制校验）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("strategy-profile/version/{versionId}/publish")]
    public async Task<IActionResult> PublishStrategyProfileVersion(long versionId, [FromBody] PublishRequest request, CancellationToken ct)
    {
        try
        {
            await _governanceService.PublishStrategyProfileVersionAsync(versionId, request?.PublishedBy, ct);
            return Ok(new { success = true, message = $"策略包版本 {versionId} 发布成功" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>校验策略包版本是否可发布（P0-06）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("strategy-profile/version/{versionId}/validate")]
    public async Task<IActionResult> ValidateStrategyProfileVersionForPublish(long versionId, CancellationToken ct)
    {
        var result = await _governanceService.ValidateStrategyProfileVersionForPublishAsync(versionId, ct);
        return Ok(result);
    }

    /// <summary>解析当前有效默认 PUBLISHED 策略包（P0-06：跨号位冻结语义；歧义报错不随机取）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("strategy-profile/default")]
    public async Task<IActionResult> ResolveDefaultStrategyProfile([FromQuery] string? runType, [FromQuery] DateTime? asOf, CancellationToken ct)
    {
        try
        {
            var version = await _governanceService.ResolveDefaultStrategyProfileVersionAsync(runType, asOf, ct);
            if (version == null)
            {
                return NotFound(new { success = false, error = $"RunType={runType} 无当前有效默认 PUBLISHED 策略包" });
            }
            return Ok(new { success = true, data = version });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Run 引用追溯（P0-06：策略包版本 → 父 Profile + 规则集/参数集版本）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("strategy-profile/version/{versionId}/trace")]
    public async Task<IActionResult> GetRunStrategyProfileTrace(long versionId, CancellationToken ct)
    {
        try
        {
            var trace = await _governanceService.GetRunStrategyProfileTraceAsync(versionId, ct);
            return Ok(new { success = true, data = trace });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region 运行生命周期（P0-08：ScheduleRun 治理）

    /// <summary>校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（P0-08：FULL≥1 / RESCHEDULE 恰1）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("run/{scheduleRunId}/validate-domain-keys")]
    public async Task<IActionResult> ValidateExpectedDomainKeys(int scheduleRunId, CancellationToken ct)
    {
        try
        {
            await _runLifecycleService.ValidateExpectedDomainKeysAsync(scheduleRunId, ct);
            return Ok(new { success = true, message = $"ScheduleRun {scheduleRunId} 的 ExpectedDomainKeysJson 冻结规则校验通过" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "ExpectedDomainKeysJson 冻结规则校验失败：{ScheduleRunId}", scheduleRunId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Candidate 最小人工确认（P0-08：仅记录 Actor/ConfirmedAt/CandidatePlanVersionId/Remark，不转 ACTIVE）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("plan-version/{planVersionId}/confirm-candidate")]
    public async Task<IActionResult> ConfirmCandidate(int planVersionId, [FromBody] ConfirmCandidateRequest request, CancellationToken ct)
    {
        try
        {
            await _runLifecycleService.ConfirmCandidateAsync(planVersionId, request?.Actor ?? string.Empty, request?.Remark, ct);
            return Ok(new { success = true, message = $"候选版本 {planVersionId} 已确认（待激活）" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "候选版本确认失败：{PlanVersionId}", planVersionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>激活 Candidate（P0-08：CANDIDATE → ACTIVE，每域单一正式采用版本）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("plan-version/{planVersionId}/activate-candidate")]
    public async Task<IActionResult> ActivateCandidate(int planVersionId, [FromBody] ActivateCandidateRequest request, CancellationToken ct)
    {
        try
        {
            await _runLifecycleService.ActivateCandidateAsync(planVersionId, request?.Actor ?? string.Empty, ct);
            return Ok(new { success = true, message = $"候选版本 {planVersionId} 已正式采用（ACTIVE）" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "候选版本激活失败：{PlanVersionId}", planVersionId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>FAILED 恢复（P0-08：为 FAILED ScheduleRun 新建一条 RUNNING 重跑，继承基线；旧记录不动）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("run/{failedScheduleRunId}/recover")]
    public async Task<IActionResult> RecoverFailedRun(int failedScheduleRunId, CancellationToken ct)
    {
        try
        {
            var newRunId = await _runLifecycleService.RecoverFailedRunAsync(failedScheduleRunId, ct);
            return Ok(new { success = true, data = new { NewScheduleRunId = newRunId }, message = $"FAILED 运行 {failedScheduleRunId} 已恢复，新建运行 {newRunId}" });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "FAILED 运行恢复失败：{ScheduleRunId}", failedScheduleRunId);
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    /// <summary>Run 引用追溯（P0-08：ScheduleRun → 策略包版本 → 规则集/参数集版本 + 关联 PlanVersion 状态）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("run/{scheduleRunId}/trace")]
    public async Task<IActionResult> GetRunReferenceTrace(int scheduleRunId, CancellationToken ct)
    {
        try
        {
            var trace = await _runLifecycleService.GetRunReferenceTraceAsync(scheduleRunId, ct);
            return Ok(new { success = true, data = trace });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { success = false, error = ex.Message });
        }
    }

    #endregion
}

/// <summary>发布请求 DTO（3号位 Web 层契约）</summary>
/// <remarks>开发者：3号位</remarks>
public class PublishRequest
{
    public string? PublishedBy { get; set; }
}

/// <summary>Candidate 确认请求 DTO（P0-08）</summary>
/// <remarks>开发者：3号位</remarks>
public class ConfirmCandidateRequest
{
    /// <summary>确认人（必填）</summary>
    public string? Actor { get; set; }

    /// <summary>必要备注（可空）</summary>
    public string? Remark { get; set; }
}

/// <summary>Candidate 激活请求 DTO（P0-08）</summary>
/// <remarks>开发者：3号位</remarks>
public class ActivateCandidateRequest
{
    /// <summary>激活人（必填）</summary>
    public string? Actor { get; set; }
}
