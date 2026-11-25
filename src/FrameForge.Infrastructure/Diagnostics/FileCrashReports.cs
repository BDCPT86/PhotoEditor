using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FrameForge.Application.Diagnostics;

namespace FrameForge.Infrastructure.Diagnostics;

/// <summary>
/// Writes and lists crash reports in <see cref="AppDataPaths.CrashReportsDirectory"/>. Nothing is sent anywhere.
/// A report holds the version, Windows and .NET versions, the error with its stack trace and the end of the log. The
/// Windows user name and computer name are replaced (in paths and anywhere else) so a report can be shared as it is.
/// The newest <see cref="Kept"/> reports are kept. Writing never throws: it runs while the app is failing.
/// </summary>
public sealed class FileCrashReports(AppDataPaths paths, TimeProvider timeProvider) : ICrashReports
{
    public const int Kept = 10;
    public const int LogLinesIncluded = 200;
    private const string Prefix = "crash-";
    private const string SeenSuffix = ".seen.txt";

    /// <summary>Writes a report; returns its path, or null if it couldn't be written.</summary>
    /// <param name="what">What happened, in a few words ("The app closed", "Error on the UI thread; the app kept running").</param>
    public string? Write(Exception exception, string what)
    {
        try
        {
            Directory.CreateDirectory(paths.CrashReportsDirectory);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var path = Path.Combine(paths.CrashReportsDirectory, $"{Prefix}{now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.txt");
            File.WriteAllText(path, Redact(Compose(exception, what, now)));
            Prune();
            return path;
        }
#pragma warning disable CA1031 // the app is already failing; a report that can't be written must not make it worse
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    public IReadOnlyList<string> Unseen() =>
        Directory.Exists(paths.CrashReportsDirectory)
            ? Directory.EnumerateFiles(paths.CrashReportsDirectory, Prefix + "*.txt")
                .Where(f => !f.EndsWith(SeenSuffix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .ToList()
            : [];

    public void MarkSeen()
    {
        foreach (var file in Unseen())
        {
            try
            {
                File.Move(file, file[..^".txt".Length] + SeenSuffix);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Shown again next time; nothing else depends on it.
            }
        }
    }

    public string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Compose(Exception exception, string what, DateTime nowUtc)
    {
        var c = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("FrameForge crash report");
        text.AppendLine("This file stays on your computer. FrameForge never sends it; share it yourself only if you want to.");
        text.AppendLine();
        text.AppendLine(c, $"When (UTC):   {nowUtc:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine(c, $"What:         {what}");
        text.AppendLine(c, $"Version:      {(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(FileCrashReports).Assembly).GetName().Version}");
        text.AppendLine(c, $"Windows:      {Environment.OSVersion.VersionString} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine(c, $".NET:         {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})");
        text.AppendLine(c, $"Running for:  {Uptime()}");
        text.AppendLine();
        text.AppendLine("--- Error ---");
        text.AppendLine(exception.ToString());
        text.AppendLine();
        text.AppendLine(c, $"--- Last {LogLinesIncluded} log lines ---");
        text.AppendLine(LogTail());
        return text.ToString();
    }

    private static string Uptime()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var span = DateTime.Now - process.StartTime;
        return span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>The end of the newest log file, read while the logger may still have it open.</summary>
    private string LogTail()
    {
        try
        {
            var newest = new DirectoryInfo(paths.LogsDirectory).EnumerateFiles("frameforge-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (newest is null)
            {
                return "(no log file)";
            }

            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new Queue<string>(LogLinesIncluded);
            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == LogLinesIncluded)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }

            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(the log could not be read: " + ex.Message + ")";
        }
    }

    /// <summary>Replaces the user profile folder, the user name and the computer name.</summary>
    internal static string Redact(string text) => Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);

    internal static string Redact(string text, string userProfile, string userName, string machineName)
    {
        if (!string.IsNullOrEmpty(userProfile))
        {
            text = text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        // The computer name first: it often contains the user name ("BYRON-PC").
        if (machineName.Length >= 2)
        {
            text = text.Replace(machineName, "<computer>", StringComparison.OrdinalIgnoreCase);
        }

        if (userName.Length >= 2)
        {
            text = text.Replace(userName, "<user>", StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private void Prune()
    {
        foreach (var old in new DirectoryInfo(paths.CrashReportsDirectory).EnumerateFiles(Prefix + "*.txt").OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Kept))
        {
            try
            {
                old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Removed next time.
            }
        }
    }
}
