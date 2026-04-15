using LMSupply.Generator.Abstractions;

namespace LMSupply.Generator.Internal;

/// <summary>
/// Internal contract that loaded generator models implement so <c>LocalGenerator</c>
/// can attach auto-selection diagnostics after construction.
/// </summary>
internal interface IDiagnosticsSink
{
    void SetDiagnostics(SelectionDiagnostics diagnostics);
}
