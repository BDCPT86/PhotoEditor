namespace FrameForge.AI;

/// <summary>Recognises the errors .NET raises when a native library (onnxruntime.dll) can't be loaded or used.</summary>
public static class OnnxNative
{
    /// <summary>
    /// The library or one it depends on is missing (most often the Microsoft Visual C++ runtime), is the wrong
    /// architecture, or lacks an export; or a type that loads it failed to initialise because of that.
    /// </summary>
    /// <remarks>
    /// Windows 11 has its own, older onnxruntime.dll (Windows ML) in System32. If FrameForge's copy can't be found,
    /// Windows may load that one, and ONNX Runtime's native layer then fails to initialise with "does not support API
    /// version" (seen in M07 A0). Any failure to initialise that layer therefore counts, whatever the inner error.
    /// </remarks>
    public static bool IsLoadFailure(Exception exception) => exception switch
    {
        DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => true,
        TypeInitializationException { TypeName: { } type } when type.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.Ordinal) => true,
        TypeInitializationException { InnerException: { } inner } => IsLoadFailure(inner),
        _ => false,
    };

    /// <summary>What to tell the user.</summary>
    public const string Explanation =
        "the AI runtime could not start on this computer. Installing the Microsoft Visual C++ runtime (x64) usually fixes this; FrameForge's installer does it when needed.";
}
