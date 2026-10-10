using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProjectOverview.Api.Projects;

public sealed class ProjectOverviewDbContext(DbContextOptions<ProjectOverviewDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

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
    }
}

/// <summary>Lets `dotnet ef` create the context without starting the host (and its migrations).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProjectOverviewDbContext>
{
    public ProjectOverviewDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProjectOverviewDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
