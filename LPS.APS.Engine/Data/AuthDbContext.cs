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
    public DbSet<UserDataScope> UserDataScopes { get; set; } = null!;
    public DbSet<RoleDataScope> RoleDataScopes { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
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
            entity.Property(e => e.LoginName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.HasIndex(e => e.LoginName).IsUnique();
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

        // ==================== DataScopePolicy（DDL v1.3 冻结对齐） ====================
        modelBuilder.Entity<DataScopePolicy>(entity =>
        {
            entity.ToTable("DataScopePolicy");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ScopeType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ScopeValue).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
        });

        // ==================== UserDataScope（用户直接范围，复合主键） ====================
        modelBuilder.Entity<UserDataScope>(entity =>
        {
            entity.ToTable("UserDataScope");
            entity.HasKey(e => new { e.UserId, e.ScopePolicyId });
        });

        // ==================== RoleDataScope（角色范围，复合主键） ====================
        modelBuilder.Entity<RoleDataScope>(entity =>
        {
            entity.ToTable("RoleDataScope");
            entity.HasKey(e => new { e.RoleId, e.ScopePolicyId });
        });

        // ==================== AuditLog ====================
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ActionCode).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Result).IsRequired().HasMaxLength(20);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OccurredAt);
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
                    user.CreatedAt = DateTime.Now;
                    user.UpdatedAt = DateTime.Now;
                }
                else if (entry.Entity is AuditLog log)
                {
                    log.OccurredAt = DateTime.Now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is User user)
                {
                    user.UpdatedAt = DateTime.Now;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
