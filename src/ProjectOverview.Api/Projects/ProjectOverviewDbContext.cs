using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ProjectOverview.Api.Milestones;
using ProjectOverview.Api.WorkPackages;

namespace ProjectOverview.Api.Projects;

public sealed class ProjectOverviewDbContext(DbContextOptions<ProjectOverviewDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkPackage> WorkPackages => Set<WorkPackage>();
    public DbSet<Milestone> Milestones => Set<Milestone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(project =>
        {
            project.HasKey(p => p.Id);
            project.Property(p => p.Name).IsRequired().HasMaxLength(ProjectLimits.NameMaxLength);
            project.Property(p => p.Description).HasMaxLength(ProjectLimits.DescriptionMaxLength);
            project.Property(p => p.Status).HasConversion<string>();
            // SQLite cannot order DateTimeOffset values, so store UTC ticks.
            project.Property(p => p.CreatedAt).HasConversion(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            project.HasIndex(p => p.CreatedAt);
        });

        modelBuilder.Entity<WorkPackage>(package =>
        {
            package.HasKey(p => p.Id);
            package.Property(p => p.Title).IsRequired().HasMaxLength(WorkPackageLimits.TitleMaxLength);
            package.Property(p => p.Description).HasMaxLength(WorkPackageLimits.DescriptionMaxLength);
            package.Property(p => p.Status).HasConversion<string>();
            package.Property(p => p.CreatedAt).HasConversion(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            // A work package belongs to exactly one project and goes away with it.
            package.HasOne<Project>().WithMany().HasForeignKey(p => p.ProjectId).OnDelete(DeleteBehavior.Cascade);
            package.HasIndex(p => new { p.ProjectId, p.CreatedAt });
            // Deleting a milestone only removes the assignment.
            package.HasOne<Milestone>().WithMany().HasForeignKey(p => p.MilestoneId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Milestone>(milestone =>
        {
            milestone.HasKey(m => m.Id);
            milestone.Property(m => m.Title).IsRequired().HasMaxLength(MilestoneLimits.TitleMaxLength);
            milestone.Property(m => m.Description).HasMaxLength(MilestoneLimits.DescriptionMaxLength);
            milestone.Property(m => m.CreatedAt).HasConversion(
                value => value.UtcTicks,
                ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            milestone.HasOne<Project>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Cascade);
            milestone.HasIndex(m => new { m.ProjectId, m.TargetDate });
        });
    }
}

/// <summary>Lets `dotnet ef` create the context without starting the host (and its migrations).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProjectOverviewDbContext>
{
    public ProjectOverviewDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProjectOverviewDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
