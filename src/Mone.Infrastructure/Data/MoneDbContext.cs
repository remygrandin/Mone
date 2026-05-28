using Mone.Contracts.Models;
using Mone.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Mone.Infrastructure.Data;

public class MoneDbContext(DbContextOptions<MoneDbContext> options) : IdentityDbContext<UserEntity>(options)
{
    public DbSet<HostEntity> Hosts => Set<HostEntity>();
    public DbSet<TagEntity> Tags => Set<TagEntity>();
    public DbSet<HostTagEntity> HostTags => Set<HostTagEntity>();
    public DbSet<ProbeAssignmentEntity> ProbeAssignments => Set<ProbeAssignmentEntity>();
    public DbSet<CheckerAssignmentEntity> CheckerAssignments => Set<CheckerAssignmentEntity>();
    public DbSet<ProbeResultEntity> ProbeResults => Set<ProbeResultEntity>();
    public DbSet<StatusHistoryEntity> StatusHistory => Set<StatusHistoryEntity>();
    public DbSet<NotificationAuditEntity> NotificationAudit => Set<NotificationAuditEntity>();
    public DbSet<NotificationConfigEntity> NotificationConfigs => Set<NotificationConfigEntity>();
    public DbSet<PluginRepositoryEntity> PluginRepositories => Set<PluginRepositoryEntity>();
    public DbSet<PluginManifestEntity> PluginManifests => Set<PluginManifestEntity>();
    public DbSet<PluginGlobalConfigEntity> PluginGlobalConfigs => Set<PluginGlobalConfigEntity>();
    public DbSet<GroupEntity> HostGroups => Set<GroupEntity>();
    public DbSet<GroupMembershipEntity> HostGroupMemberships => Set<GroupMembershipEntity>();
    public DbSet<ProbeAssignmentOverrideEntity> ProbeAssignmentOverrides => Set<ProbeAssignmentOverrideEntity>();
    public DbSet<CheckerAssignmentOverrideEntity> CheckerAssignmentOverrides => Set<CheckerAssignmentOverrideEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<HostEntity>(e =>
        {
            e.ToTable("hosts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Address).IsRequired().HasMaxLength(512);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<TagEntity>(e =>
        {
            e.ToTable("tags");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(128);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<HostTagEntity>(e =>
        {
            e.ToTable("host_tags");
            e.HasKey(x => new { x.HostId, x.TagId });
            e.HasOne(x => x.Host).WithMany(h => h.HostTags).HasForeignKey(x => x.HostId);
            e.HasOne(x => x.Tag).WithMany(t => t.HostTags).HasForeignKey(x => x.TagId);
        });

        modelBuilder.Entity<ProbeAssignmentEntity>(e =>
        {
            e.ToTable("probe_assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProbePluginId).IsRequired().HasMaxLength(256);
            e.Property(x => x.ScheduleCron).IsRequired().HasMaxLength(64);
            e.Property(x => x.TargetAddressOverride).HasMaxLength(512);
            e.HasOne(x => x.Host).WithMany(h => h.ProbeAssignments).HasForeignKey(x => x.HostId);
            e.HasOne(x => x.Group).WithMany(g => g.ProbeAssignments).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_probe_assignments_host_or_group", "(\"HostId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"HostId\" IS NULL AND \"GroupId\" IS NOT NULL)"));
        });

        modelBuilder.Entity<CheckerAssignmentEntity>(e =>
        {
            e.ToTable("checker_assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.CheckerPluginId).IsRequired().HasMaxLength(256);
            e.HasOne(x => x.Host).WithMany(h => h.CheckerAssignments).HasForeignKey(x => x.HostId);
            e.HasOne(x => x.Group).WithMany(g => g.CheckerAssignments).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_checker_assignments_host_or_group", "(\"HostId\" IS NOT NULL AND \"GroupId\" IS NULL) OR (\"HostId\" IS NULL AND \"GroupId\" IS NOT NULL)"));
        });

        modelBuilder.Entity<ProbeResultEntity>(e =>
        {
            e.ToTable("probe_results");
            e.HasKey(x => new { x.Timestamp, x.TargetId, x.ProbeId });
            e.Property(x => x.ProbeId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Summary).IsRequired().HasMaxLength(2048);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<StatusHistoryEntity>(e =>
        {
            e.ToTable("status_history");
            e.HasKey(x => new { x.Timestamp, x.TargetId, x.CheckerId });
            e.Property(x => x.CheckerId).IsRequired().HasMaxLength(256);
            e.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CurrentStatus).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<NotificationAuditEntity>(e =>
        {
            e.ToTable("notification_audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.CheckerId).IsRequired().HasMaxLength(256);
            e.Property(x => x.NotificationPluginId).IsRequired().HasMaxLength(256);
            e.Property(x => x.DeliveryStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ErrorMessage).HasMaxLength(2048);
            e.Property(x => x.PreviousMonitoringStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CurrentMonitoringStatus).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.TargetId);
            e.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<NotificationConfigEntity>(e =>
        {
            e.ToTable("notification_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.PluginId).IsRequired().HasMaxLength(256);
            e.Property(x => x.Scope).HasMaxLength(256);
            e.HasIndex(x => x.PluginId);
        });

        modelBuilder.Entity<PluginRepositoryEntity>(e =>
        {
            e.ToTable("plugin_repositories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Owner).IsRequired().HasMaxLength(256);
            e.Property(x => x.Repo).IsRequired().HasMaxLength(256);
            e.Property(x => x.Branch).HasMaxLength(256);
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(512);
            e.Property(x => x.ETag).HasMaxLength(256);
            e.Property(x => x.LastSyncError).HasMaxLength(2048);
            e.HasIndex(x => new { x.Owner, x.Repo }).IsUnique();
        });

        modelBuilder.Entity<PluginManifestEntity>(e =>
        {
            e.ToTable("plugin_manifests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Version).IsRequired().HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.Property(x => x.PluginType).IsRequired().HasMaxLength(64);
            e.Property(x => x.DownloadUrl).IsRequired().HasMaxLength(2048);
            e.Property(x => x.Sha256).IsRequired().HasMaxLength(128);
            e.Property(x => x.DependenciesJson).HasMaxLength(4096);
            e.Property(x => x.MinMoneVersion).HasMaxLength(32);
            e.Property(x => x.Author).HasMaxLength(256);
            e.Property(x => x.License).HasMaxLength(64);
            e.Property(x => x.Homepage).HasMaxLength(2048);
            e.Property(x => x.TagsJson).HasMaxLength(1024);
            e.Property(x => x.InstalledPath).HasMaxLength(1024);
            e.HasOne(x => x.Repository).WithMany(r => r.Manifests).HasForeignKey(x => x.RepositoryId);
            e.HasIndex(x => x.RepositoryId);
            e.HasIndex(x => new { x.RepositoryId, x.Name, x.Version }).IsUnique();
        });

        modelBuilder.Entity<PluginGlobalConfigEntity>(e =>
        {
            e.ToTable("plugin_global_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.PluginId).IsRequired().HasMaxLength(256);
            e.HasIndex(x => x.PluginId).IsUnique();
        });

        modelBuilder.Entity<GroupEntity>(e =>
        {
            e.ToTable("host_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasOne(x => x.ParentGroup)
                .WithMany(x => x.ChildGroups)
                .HasForeignKey(x => x.ParentGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GroupMembershipEntity>(e =>
        {
            e.ToTable("host_group_memberships");
            e.HasKey(x => new { x.GroupId, x.HostId });
            e.HasOne(x => x.Group)
                .WithMany(g => g.GroupMemberships)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Host)
                .WithMany(h => h.GroupMemberships)
                .HasForeignKey(x => x.HostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProbeAssignmentOverrideEntity>(e =>
        {
            e.ToTable("probe_assignment_overrides");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ProbeAssignment).WithMany().HasForeignKey(x => x.ProbeAssignmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HostId, x.ProbeAssignmentId }).IsUnique();
        });

        modelBuilder.Entity<CheckerAssignmentOverrideEntity>(e =>
        {
            e.ToTable("checker_assignment_overrides");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Host).WithMany().HasForeignKey(x => x.HostId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CheckerAssignment).WithMany().HasForeignKey(x => x.CheckerAssignmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.HostId, x.CheckerAssignmentId }).IsUnique();
        });
    }
}
