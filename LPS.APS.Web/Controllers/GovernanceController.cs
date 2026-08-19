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
    private readonly ILogger<GovernanceController> _logger;

    public GovernanceController(
        IGovernanceVersionService governanceService,
        IRuleSetVersionRepository ruleSetVersionRepo,
        IParameterSetVersionRepository parameterSetVersionRepo,
        ILogger<GovernanceController> logger)
    {
        _governanceService = governanceService;
        _ruleSetVersionRepo = ruleSetVersionRepo;
        _parameterSetVersionRepo = parameterSetVersionRepo;
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

    /// <summary>获取规则集默认版本</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("rule-set/{ruleSetId}/default-version")]
    public async Task<IActionResult> GetDefaultRuleSetVersion(long ruleSetId, CancellationToken ct)
    {
        var version = await _ruleSetVersionRepo.GetDefaultByRuleSetIdAsync(ruleSetId, ct);
        if (version == null)
        {
            return NotFound(new { success = false, error = $"规则集无默认版本：{ruleSetId}" });
        }
        return Ok(new { success = true, data = version });
    }

    /// <summary>创建规则集版本（初始状态 DRAFT）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("rule-set/version")]
    public async Task<IActionResult> CreateRuleSetVersion([FromBody] RuleSetVersion version, CancellationToken ct)
    {
        version.CreatedAt = DateTime.UtcNow;
        version.UpdatedAt = DateTime.UtcNow;
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
        version.UpdatedAt = DateTime.UtcNow;
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

    /// <summary>获取参数集默认版本</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpGet("parameter-set/{parameterSetId}/default-version")]
    public async Task<IActionResult> GetDefaultParameterSetVersion(long parameterSetId, CancellationToken ct)
    {
        var version = await _parameterSetVersionRepo.GetDefaultByParameterSetIdAsync(parameterSetId, ct);
        if (version == null)
        {
            return NotFound(new { success = false, error = $"参数集无默认版本：{parameterSetId}" });
        }
        return Ok(new { success = true, data = version });
    }

    /// <summary>创建参数集版本（初始状态 DRAFT）</summary>
    /// <remarks>开发者：3号位</remarks>
    [HttpPost("parameter-set/version")]
    public async Task<IActionResult> CreateParameterSetVersion([FromBody] ParameterSetVersion version, CancellationToken ct)
    {
        version.CreatedAt = DateTime.UtcNow;
        version.UpdatedAt = DateTime.UtcNow;
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
        version.UpdatedAt = DateTime.UtcNow;
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
}

/// <summary>发布请求 DTO（3号位 Web 层契约）</summary>
/// <remarks>开发者：3号位</remarks>
public class PublishRequest
{
    public string? PublishedBy { get; set; }
}
