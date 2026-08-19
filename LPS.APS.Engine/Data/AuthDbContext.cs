using Microsoft.EntityFrameworkCore;
using LPS.APS.Core.Entities.Auth;

namespace LPS.APS.Engine.Data;

/// <summary>
/// Auth权限库 EF Core DbContext
/// 专门用于 Auth 库的 CRUD 操作（User、Role、Permission、审批流等）
/// </summary>
public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Permission> Permissions { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<RolePermission> RolePermissions { get; set; } = null!;
    public DbSet<DataScopePolicy> DataScopePolicies { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<GovernanceAuditLog> GovernanceAuditLogs { get; set; } = null!;
    public DbSet<ApprovalFlow> ApprovalFlows { get; set; } = null!;
    public DbSet<ApprovalNode> ApprovalNodes { get; set; } = null!;
    public DbSet<ApprovalRecord> ApprovalRecords { get; set; } = null!;
    public DbSet<ApprovalRule> ApprovalRules { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==================== User ====================
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.UserName).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // ==================== Role ====================
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Role");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RoleCode).IsRequired().HasMaxLength(50);
            entity.Property(e => e.RoleName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.RoleCode).IsUnique();
        });

        // ==================== Permission ====================
        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permission");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PermissionCode).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PermissionName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.PermissionCode).IsUnique();
        });

        // ==================== UserRole (多对多) ====================
        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRole");
            entity.HasKey(e => new { e.UserId, e.RoleId });
        });

        // ==================== RolePermission (多对多) ====================
        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermission");
            entity.HasKey(e => new { e.RoleId, e.PermissionId });
        });

        // ==================== DataScopePolicy ====================
        modelBuilder.Entity<DataScopePolicy>(entity =>
        {
            entity.ToTable("DataScopePolicy");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyName).IsRequired().HasMaxLength(100);
        });

        // ==================== AuditLog ====================
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionType).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.CreatedAt);
        });

        // ==================== GovernanceAuditLog (3号位：A-7 治理审计日志) ====================
        modelBuilder.Entity<GovernanceAuditLog>(entity =>
        {
            entity.ToTable("GovernanceAuditLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OperationType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.VersionCode).HasMaxLength(50);
            entity.Property(e => e.BeforeStatus).HasMaxLength(50);
            entity.Property(e => e.AfterStatus).HasMaxLength(50);
            entity.Property(e => e.OperatedBy).HasMaxLength(100);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.OperatedAt);
        });

        // ==================== ApprovalFlow ====================
        modelBuilder.Entity<ApprovalFlow>(entity =>
        {
            entity.ToTable("ApprovalFlow");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FlowName).IsRequired().HasMaxLength(100);
        });

        // ==================== ApprovalNode ====================
        modelBuilder.Entity<ApprovalNode>(entity =>
        {
            entity.ToTable("ApprovalNode");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NodeName).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.FlowId);
        });

        // ==================== ApprovalRecord ====================
        modelBuilder.Entity<ApprovalRecord>(entity =>
        {
            entity.ToTable("ApprovalRecord");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FlowId);
            entity.HasIndex(e => e.Status);
        });

        // ==================== ApprovalRule ====================
        modelBuilder.Entity<ApprovalRule>(entity =>
        {
            entity.ToTable("ApprovalRule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RuleType).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.FlowId);
        });
    }

    /// <summary>
    /// 保存更改前自动填充审计字段
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is User user)
                {
                    user.CreatedAt = DateTime.UtcNow;
                    user.UpdatedAt = DateTime.UtcNow;
                }
                else if (entry.Entity is AuditLog log)
                {
                    log.CreatedAt = DateTime.UtcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is User user)
                {
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
