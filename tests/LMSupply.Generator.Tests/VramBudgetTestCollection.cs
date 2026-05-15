using Xunit;

namespace LMSupply.Generator.Tests;

/// <summary>
/// Prevents parallel execution of tests that modify the LMSUPPLY_VRAM_BUDGET_MB environment variable.
/// xUnit runs tests within a collection sequentially, preventing env-var race conditions.
/// </summary>
[CollectionDefinition("VramBudget")]
#pragma warning disable CA1711 // xUnit collection marker types conventionally end in "Collection"
public class VramBudgetTestCollection;
#pragma warning restore CA1711
