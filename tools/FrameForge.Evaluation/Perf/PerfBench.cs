using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameForge.AI;
using FrameForge.Application;
using FrameForge.Application.Decisions;
using FrameForge.Application.Editing;
using FrameForge.Application.Photos;
using FrameForge.Core.Decisions;
using FrameForge.Core.Editing;
using FrameForge.Core.Photos;
using FrameForge.Imaging;
using FrameForge.Infrastructure;
using FrameForge.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FrameForge.Evaluation.Perf;

/// <summary>
/// M07 phase D baseline. Two parts:
/// <list type="number">
/// <item>Library scale: generated small JPEGs in folders of 500, imported with the real services into a fresh data
/// folder per size; then the loads the library does at startup are timed, and the data folder is kept so the app can be
/// started on it.</item>
/// <item>Real photos (read-only): per-photo costs (metadata, thumbnail, viewer decode at full and screen size) and
/// memory around an edited export.</item>
/// </list>
/// Everything is written under the work folder; photos are only read.
/// </summary>
internal static class PerfBench
{
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;
    private const int FolderSize = 500;

    public static async Task<int> RunAsync(string workDir, IReadOnlyList<int> sizes, string? photosDir, int count, StringBuilder report)
    {
        Directory.CreateDirectory(workDir);
        report.AppendLine("## Library scale (generated photos)");
        report.AppendLine();
        report.AppendLine("Generated 256×171 JPEGs with a capture date and camera, in folders of 500. Import reads metadata only, so");
        report.AppendLine("these sizes stand in for real files there. 30 % are marked Keep (some with ratings), 10 % Reject, 5 % edited.");
        report.AppendLine("Loads are timed on a fresh set of services each time (median of 3).");
        report.AppendLine();
        report.AppendLine("| Photos | Generate s | Import s | Photos/s | DB MB | Load photos ms | Load decisions ms | Load edits ms | Managed MB for the list |");
        report.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var size in sizes)
        {
            Console.Error.WriteLine($"Library {size:N0}");
            report.AppendLine(await LibraryAsync(workDir, size).ConfigureAwait(false));
        }

        report.AppendLine();
        report.AppendLine(C, $"Data folders (start the app with FRAMEFORGE_DATA_DIR pointing at one): `{workDir}\\data-<size>`");
        report.AppendLine();

        if (photosDir is not null)
        {
            await RealPhotosAsync(workDir, photosDir, count, report).ConfigureAwait(false);
        }

        return 0;
    }

    // ------------------------------------------------------------------ library scale

    private static ServiceProvider Services(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddFrameForgeApplication();
        services.AddFrameForgeInfrastructure(new AppDataPaths(dataDir));
        services.AddFrameForgeImaging();
        services.AddFrameForgeAi();
        return services.BuildServiceProvider();
    }

    private static async Task<string> LibraryAsync(string workDir, int size)
    {
        var photos = Path.Combine(workDir, $"lib-{size}");
        var data = Path.Combine(workDir, $"data-{size}");
        if (Directory.Exists(data))
        {
            throw new InvalidOperationException($"{data} already exists; use a new work folder.");
        }

        var sw = Stopwatch.StartNew();
        Generate(photos, size);
        var generateS = sw.Elapsed.TotalSeconds;

        double importS;
        await using (var sp = Services(data))
        {
            await sp.GetRequiredService<DatabaseMigrator>().MigrateAsync().ConfigureAwait(false);
            sw.Restart();
            var summary = await sp.GetRequiredService<IPhotoImportService>().ImportFolderAsync(photos, includeSubfolders: true, null, CancellationToken.None).ConfigureAwait(false);
            importS = sw.Elapsed.TotalSeconds;
            if (summary.Added != size)
            {
                throw new InvalidOperationException($"Imported {summary.Added} of {size}.");
            }

            await SeedAsync(sp).ConfigureAwait(false);
        }

        var photoMs = new List<double>();
        var decisionMs = new List<double>();
        var editMs = new List<double>();
        double listMb = 0;
        for (var run = 0; run < 3; run++)
        {
            await using var sp = Services(data);
            GC.Collect();
            var before = GC.GetTotalMemory(forceFullCollection: true);
            sw.Restart();
            var list = await sp.GetRequiredService<IPhotoLibraryService>().GetPhotosAsync().ConfigureAwait(false);
            photoMs.Add(sw.Elapsed.TotalMilliseconds);
            listMb = (GC.GetTotalMemory(forceFullCollection: true) - before) / (1024d * 1024);
            sw.Restart();
            _ = await sp.GetRequiredService<IDecisionService>().LoadAsync().ConfigureAwait(false);
            decisionMs.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            _ = await sp.GetRequiredService<IEditService>().LoadAsync().ConfigureAwait(false);
            editMs.Add(sw.Elapsed.TotalMilliseconds);
            GC.KeepAlive(list);
        }

        var dbMb = Directory.EnumerateFiles(data, AppDataPaths.DatabaseFileName + "*").Sum(f => new FileInfo(f).Length) / (1024d * 1024); // with the -wal journal
        return string.Create(C, $"| {size:N0} | {generateS:0.0} | {importS:0.0} | {size / importS:N0} | {dbMb:0.0} | {Median(photoMs):0} | {Median(decisionMs):0} | {Median(editMs):0} | {listMb:0.0} |");
    }

    /// <summary>Small JPEGs with a date and camera; a different tone per file so they aren't identical.</summary>
    private static void Generate(string root, int size)
    {
        var start = new DateTime(2024, 3, 1, 8, 0, 0, DateTimeKind.Unspecified);
        for (var i = 0; i < size; i++)
        {
            var folder = Path.Combine(root, $"Shoot {i / FolderSize + 1:000}");
            if (i % FolderSize == 0)
            {
                Directory.CreateDirectory(folder);
            }

            var shade = (byte)(40 + (i * 37 % 180));
            var source = BitmapSource.Create(256, 171, 96, 96, PixelFormats.Gray8, null, Enumerable.Repeat(shade, 256 * 171).ToArray(), 256);
            var metadata = new BitmapMetadata("jpg")
            {
                CameraManufacturer = "Canon",
                CameraModel = "Canon EOS R50",
                DateTaken = start.AddSeconds(i * 7).ToString("yyyy-MM-ddTHH:mm:ss", C),
            };
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(source, null, metadata, null));
            using var file = File.Create(Path.Combine(folder, $"IMG_{i:000000}.JPG"));
            encoder.Save(file);
        }
    }

    private static async Task SeedAsync(IServiceProvider sp)
    {
        var photos = await sp.GetRequiredService<IPhotoLibraryService>().GetPhotosAsync().ConfigureAwait(false);
        var decisions = new List<(Guid, PhotoDecision)>();
        var edits = new List<(Guid, EditRecipe)>();
        for (var i = 0; i < photos.Count; i++)
        {
            var id = photos[i].Id;
            switch (i % 10)
            {
                case 0 or 1 or 2:
                    decisions.Add((id, new PhotoDecision(CullState.Keep, i % 3 == 0 ? 1 + (i % 5) : null, i % 7 == 0 ? ColourLabel.Green : ColourLabel.None)));
                    break;
                case 3:
                    decisions.Add((id, new PhotoDecision(CullState.Reject)));
                    break;
            }

            if (i % 20 == 0)
            {
                edits.Add((id, EditRecipe.Neutral with { Exposure = 0.3 }));
            }
        }

        await sp.GetRequiredService<IDecisionService>().SaveAsync(decisions).ConfigureAwait(false);
        await sp.GetRequiredService<IEditService>().SaveAsync(edits).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ real photos

    private static async Task RealPhotosAsync(string workDir, string photosDir, int count, StringBuilder report)
    {
        var files = Directory.EnumerateFiles(photosDir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
        report.AppendLine(C, $"## Real photos ({files.Count} JPEGs, read-only)");
        report.AppendLine();

        var reader = new WicPhotoMetadataReader();
        var thumbs = new WicThumbnailGenerator();
        var loader = new WicPhotoBitmapLoader();
        var metaMs = new List<double>();
        var thumbMs = new List<double>();
        var fullMs = new List<double>();
        var screenMs = new List<double>();
        var hdMs = new List<double>();
        long fullBytes = 0, screenBytes = 0, hdBytes = 0;
        var photos = new List<Photo>();
        var sw = new Stopwatch();
        foreach (var file in files)
        {
            Console.Error.WriteLine("Photo " + Path.GetFileName(file));
            sw.Restart();
            var metadata = reader.Read(file);
            metaMs.Add(sw.Elapsed.TotalMilliseconds);
            var photo = new Photo
            {
                Id = Guid.NewGuid(),
                FilePath = file,
                FileName = Path.GetFileName(file),
                FolderPath = photosDir,
                FileSizeBytes = new FileInfo(file).Length,
                LastModifiedUtc = File.GetLastWriteTimeUtc(file),
            };
            photo.ApplyMetadata(metadata);
            photos.Add(photo);

            sw.Restart();
            using (var thumb = new MemoryStream())
            {
                thumbs.Generate(file, thumb, 320, CancellationToken.None);
            }

            thumbMs.Add(sw.Elapsed.TotalMilliseconds);

            sw.Restart();
            var full = loader.LoadFull(file, CancellationToken.None);
            fullMs.Add(sw.Elapsed.TotalMilliseconds);
            fullBytes = Bytes(full);

            sw.Restart();
            var screen = loader.LoadForDisplay(file, 2560, CancellationToken.None);
            screenMs.Add(sw.Elapsed.TotalMilliseconds);
            screenBytes = Bytes(screen);

            sw.Restart();
            var hd = loader.LoadForDisplay(file, 1920, CancellationToken.None);
            hdMs.Add(sw.Elapsed.TotalMilliseconds);
            hdBytes = Bytes(hd);
        }

        report.AppendLine("Median (90th percentile) ms per photo:");
        report.AppendLine();
        report.AppendLine("| Step | ms | Pixels in memory |");
        report.AppendLine("|---|---:|---:|");
        report.AppendLine(C, $"| Read metadata (import, per file) | {Median(metaMs):0.0} ({P90(metaMs):0.0}) | – |");
        report.AppendLine(C, $"| Make a 320 px thumbnail | {Median(thumbMs):0} ({P90(thumbMs):0}) | – |");
        report.AppendLine(C, $"| Viewer: decode at full size (what it does now) | {Median(fullMs):0} ({P90(fullMs):0}) | {fullBytes / (1024d * 1024):0} MB |");
        report.AppendLine(C, $"| Viewer: decode to 2560 px (a 1440p/4K fit view) | {Median(screenMs):0} ({P90(screenMs):0}) | {screenBytes / (1024d * 1024):0} MB |");
        report.AppendLine(C, $"| Viewer: decode to 1920 px (a 1080p fit view) | {Median(hdMs):0} ({P90(hdMs):0}) | {hdBytes / (1024d * 1024):0} MB |");
        report.AppendLine();

        await EditedExportAsync(workDir, photos, report).ConfigureAwait(false);
    }

    private static long Bytes(BitmapSource bitmap) => (long)bitmap.PixelWidth * bitmap.PixelHeight * ((bitmap.Format.BitsPerPixel + 7) / 8);

    private static async Task EditedExportAsync(string workDir, List<Photo> photos, StringBuilder report)
    {
        var data = Path.Combine(workDir, "data-export");
        var output = Path.Combine(workDir, "export");
        Directory.CreateDirectory(output);
        var sp = Services(data);
        await sp.GetRequiredService<DatabaseMigrator>().MigrateAsync().ConfigureAwait(false);
        var exporter = sp.GetRequiredService<IEditedExportService>();
        var recipe = EditRecipe.Neutral with { Exposure = 0.3, Contrast = 10 };
        var items = photos.Select(p => new EditedExportItem(p, recipe)).ToList();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var before = Snapshot();
        using var memory = new MemorySampler();
        memory.Reset();
        var sw = Stopwatch.StartNew();
        var summary = await exporter.ExportAsync(items, output, EditedExportOptions.Default, null, CancellationToken.None).ConfigureAwait(false);
        var seconds = sw.Elapsed.TotalSeconds;
        var peak = memory.PeakMb;
        // Measure from another thread: the export completes inline here, with its last frame (and the last image) still on
        // the stack, which would count as reachable.
        await Task.Delay(200).ConfigureAwait(false);
        var after = Snapshot();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var collected = Snapshot();

        // If memory drops only now, a long-lived service was holding it.
        await sp.DisposeAsync().ConfigureAwait(false);
        FullCollect();
        var disposed = Snapshot();

        report.AppendLine(C, $"### Edited export, full size ({summary.Exported} of {items.Count} photos, exposure +0.3, contrast +10, quality {EditedExportOptions.Default.Quality})");
        report.AppendLine();
        report.AppendLine(C, $"{seconds:0.0} s in all, {seconds / Math.Max(1, summary.Exported):0.00} s per photo. Working-set peak during the export: {peak:0} MB.");
        report.AppendLine();
        report.AppendLine("| When | Working set MB | Private MB | Managed heap MB | GC committed MB |");
        report.AppendLine("|---|---:|---:|---:|---:|");
        report.AppendLine(Row("Before", before));
        report.AppendLine(Row("Right after", after));
        report.AppendLine(Row("After a full, compacting GC", collected));
        report.AppendLine(Row("After disposing the services too", disposed));
        report.AppendLine();
    }

    private static void FullCollect()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed record Memory(double WorkingSet, double Private, double Managed, double Committed);

    private static Memory Snapshot()
    {
        using var process = Process.GetCurrentProcess();
        const double Mb = 1024d * 1024;
        var info = GC.GetGCMemoryInfo();
        return new Memory(process.WorkingSet64 / Mb, process.PrivateMemorySize64 / Mb, GC.GetTotalMemory(false) / Mb, info.TotalCommittedBytes / Mb);
    }

    private static string Row(string label, Memory m) =>
        string.Create(C, $"| {label} | {m.WorkingSet:0} | {m.Private:0} | {m.Managed:0} | {m.Committed:0} |");

    private static double Median(List<double> values) => Percentile(values, 0.5);

    private static double P90(List<double> values) => Percentile(values, 0.9);

    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? double.NaN : sorted[(int)Math.Clamp(Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
    }
}
