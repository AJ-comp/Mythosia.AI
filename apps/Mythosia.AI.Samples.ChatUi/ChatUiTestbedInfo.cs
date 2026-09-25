using Mythosia.AI.Rag;
using Mythosia.AI.Services.Base;
using Mythosia.VectorDb.Postgres;
using System.Reflection;

namespace Mythosia.AI.Samples.ChatUi;

internal static class ChatUiTestbedInfo
{
    internal static object Build() => new
    {
        name = "Mythosia AI Playground",
        packages = new[]
        {
            Describe(typeof(AIService).Assembly),
            Describe(typeof(RagStore).Assembly),
            Describe(typeof(PostgresStore).Assembly)
        }
    };

    private static object Describe(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Informational versions can contain a source hash; show just the running package version.
        version = version?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "Unknown";
        return new { name = assembly.GetName().Name, version };
    }
}
