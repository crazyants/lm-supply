namespace LMSupply;

/// <summary>
/// Specifies the logging severity level for ONNX Runtime sessions.
/// Maps to <c>OrtLoggingLevel</c> in the ONNX Runtime API.
/// </summary>
public enum OrtLogLevel
{
    /// <summary>
    /// Verbose logging. Useful for debugging ONNX Runtime internals.
    /// </summary>
    Verbose = 0,

    /// <summary>
    /// Informational messages.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warnings, including expected provider fallback messages.
    /// This is the ONNX Runtime default.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Errors only. Suppresses provider fallback warnings.
    /// This is the LMSupply default.
    /// </summary>
    Error = 3,

    /// <summary>
    /// Fatal errors only.
    /// </summary>
    Fatal = 4
}
