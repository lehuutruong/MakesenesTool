using Microsoft.EntityFrameworkCore;

namespace MigrationCommon.Data;

public class MigrationDbContext(DbContextOptions<MigrationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
