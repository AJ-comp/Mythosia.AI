namespace Mythosia.AI.Tests.Modules;

/// <summary>
/// Marker base retained for the modular provider fixtures.
/// Image generation is independent from the selected chat model, so its paid
/// contract coverage is centralized in OpenAILiveContractTests instead of
/// running once for every model fixture.
/// </summary>
public abstract class ImageGenerationTestModule : TestModuleBase
{
}
