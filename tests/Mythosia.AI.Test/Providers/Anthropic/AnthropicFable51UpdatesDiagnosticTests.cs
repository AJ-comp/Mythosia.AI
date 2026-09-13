namespace Mythosia.AI.Tests.Anthropic;

// Separate bounded diagnostics share the exact scenario exercised by the strict release runner.
[TestClass]
[TestCategory("Live")]
[TestCategory("Anthropic")]
[TestCategory("FableUpdatesDiagnostic")]
[DoNotParallelize]
public class AnthropicFable51UpdatesDiagnosticTests
{
    [TestMethod]
    public Task AllocationWithToolResultBoundaries_EmitsActualReadableUpdates()
        => AnthropicFableAllocationLiveScenario.VerifyAsync(FableExecutionMode.Completion);

    [TestMethod]
    [DataRow(FableExecutionMode.Run)]
    [DataRow(FableExecutionMode.LegacyStream)]
    public Task AllocationStreaming_EmitsActualReadableUpdates(FableExecutionMode mode)
        => AnthropicFableAllocationLiveScenario.VerifyAsync(mode);
}
