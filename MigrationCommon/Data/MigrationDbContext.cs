using Microsoft.EntityFrameworkCore;

namespace MigrationCommon.Data;

/// <summary>Read-only EF Core context for the Portal SQL Server database. Used for lookups only.</summary>
public class MigrationDbContext(DbContextOptions<MigrationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map only the Portal entities that migration tools need. Do not scaffold the full schema.
    }
}
