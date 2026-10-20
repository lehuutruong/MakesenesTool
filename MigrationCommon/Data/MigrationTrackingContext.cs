using Microsoft.EntityFrameworkCore;
using MigrationCommon.Models;

namespace MigrationCommon.Data;

/// <summary>EF Core context for the local SQLite migration tracking store.</summary>
public class MigrationTrackingContext(DbContextOptions<MigrationTrackingContext> options) : DbContext(options)
{
    /// <summary>Per-row migration status records.</summary>
    public DbSet<MigrationTrackingRecord> MigrationRecords => Set<MigrationTrackingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationTrackingRecord>().HasKey(r => r.Id);
    }
}
