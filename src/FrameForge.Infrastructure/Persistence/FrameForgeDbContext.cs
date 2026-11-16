using FrameForge.Core.Photos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FrameForge.Infrastructure.Persistence;

public sealed class FrameForgeDbContext(DbContextOptions<FrameForgeDbContext> options) : DbContext(options)
{
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    public DbSet<Photo> Photos => Set<Photo>();

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

        modelBuilder.Entity<Photo>(entity =>
        {
            entity.ToTable("Photos");
            entity.HasKey(p => p.Id);

            // Windows paths are case-insensitive: NOCASE makes the unique index and
            // path lookups treat "C:\A.JPG" and "c:\a.jpg" as the same file.
            entity.Property(p => p.FilePath).IsRequired().UseCollation("NOCASE");
            entity.HasIndex(p => p.FilePath).IsUnique();
            entity.Property(p => p.FolderPath).IsRequired().UseCollation("NOCASE");
            entity.HasIndex(p => p.FolderPath);
            entity.Property(p => p.FileName).IsRequired();

            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(p => p.CameraMake).HasMaxLength(128);
            entity.Property(p => p.CameraModel).HasMaxLength(128);
            entity.Property(p => p.LensModel).HasMaxLength(256);
            entity.Property(p => p.ExposureTime).HasMaxLength(32);

            // Camera-local wall-clock time; must not be re-marked as UTC by the convention below.
            entity.Property(p => p.CaptureLocalTime).HasConversion(new LocalWallClockConverter());
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no timezone-aware type; stored timestamps are UTC by convention,
        // so mark them as UTC again when they are read back.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        toStore => toStore.Kind == DateTimeKind.Local ? toStore.ToUniversalTime() : toStore,
        fromStore => DateTime.SpecifyKind(fromStore, DateTimeKind.Utc));

    private sealed class LocalWallClockConverter() : ValueConverter<DateTime, DateTime>(
        toStore => DateTime.SpecifyKind(toStore, DateTimeKind.Unspecified),
        fromStore => DateTime.SpecifyKind(fromStore, DateTimeKind.Unspecified));
}
