using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FrameForge.Infrastructure.Persistence;

public sealed class FrameForgeDbContext(DbContextOptions<FrameForgeDbContext> options) : DbContext(options)
{
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(s => s.Key);
            entity.Property(s => s.Key).HasMaxLength(200);
            entity.Property(s => s.ValueJson).IsRequired();
            entity.Property(s => s.UpdatedUtc).IsRequired();
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no timezone-aware type; all stored timestamps are UTC by convention,
        // so mark them as UTC again when they are read back.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        toStore => toStore.Kind == DateTimeKind.Local ? toStore.ToUniversalTime() : toStore,
        fromStore => DateTime.SpecifyKind(fromStore, DateTimeKind.Utc));
}
