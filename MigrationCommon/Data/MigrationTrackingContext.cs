using Microsoft.EntityFrameworkCore;
using MigrationCommon.Models;

namespace MigrationCommon.Data;

public class MigrationTrackingContext(DbContextOptions<MigrationTrackingContext> options) : DbContext(options)
{
    public DbSet<MigrationTrackingRecord> MigrationRecords => Set<MigrationTrackingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MigrationTrackingRecord>().HasKey(r => r.Id);
    }
}
