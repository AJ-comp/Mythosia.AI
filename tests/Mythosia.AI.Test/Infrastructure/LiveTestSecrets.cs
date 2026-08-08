using Mythosia.Azure;
using System.Collections.Concurrent;

namespace Mythosia.AI.Tests;

/// <summary>
/// Shares Key Vault reads across live tests in the same test-host process.
/// </summary>
internal static class LiveTestSecrets
{
    private const string VaultUri = "https://mythosia-key-vault.vault.azure.net/";

    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> SecretTasks = new();
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    public static async Task<string> GetAsync(string secretName)
    {
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("A Key Vault secret name is required.", nameof(secretName));

        var lazy = SecretTasks.GetOrAdd(
            secretName,
            name => new Lazy<Task<string>>(
                () => FetchAsync(name),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            if (SecretTasks.TryGetValue(secretName, out var cached) &&
                ReferenceEquals(cached, lazy))
            {
                SecretTasks.TryRemove(secretName, out _);
            }

            throw;
        }
    }

    private static async Task<string> FetchAsync(string secretName)
    {
        await FetchGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await new SecretFetcher(VaultUri, secretName)
                .GetKeyValueAsync()
                .ConfigureAwait(false);
        }
        finally
        {
            FetchGate.Release();
        }
    }
}
