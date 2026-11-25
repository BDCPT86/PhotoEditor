using Microsoft.ML.OnnxRuntime;

namespace FrameForge.AI;

/// <summary>
/// Turns off ONNX Runtime's telemetry. Official builds collect usage events and, on Windows, send them to Microsoft
/// over HTTPS; this is on by default (Privacy.md in the Microsoft.ML.OnnxRuntime package). FrameForge promises that
/// nothing leaves the computer, so both documented switches are used, before the runtime starts:
/// <list type="number">
/// <item><c>ORT_DISABLE_TELEMETRY=1</c> in this process's environment: the uploader, the events and the persistent
/// device identifier are never created.</item>
/// <item><see cref="OrtEnv.DisableTelemetryEvents"/>: also suppresses the non-essential events.</item>
/// </list>
/// See ADR 0018.
/// </summary>
public static class OnnxPrivacy
{
    public const string DisableTelemetryVariable = "ORT_DISABLE_TELEMETRY";

    private static readonly Lazy<bool> Applied = new(Apply, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Call before any other ONNX Runtime use (the native runtime reads the setting when it starts).</summary>
    public static void EnsureApplied() => _ = Applied.Value;

    private static bool Apply()
    {
        Environment.SetEnvironmentVariable(DisableTelemetryVariable, "1");
        OrtEnv.Instance().DisableTelemetryEvents();
        return true;
    }
}
