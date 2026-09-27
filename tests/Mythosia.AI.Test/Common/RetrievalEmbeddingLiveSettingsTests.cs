namespace Mythosia.AI.Tests;

[TestClass]
[TestCategory("Unit")]
public sealed class RetrievalEmbeddingLiveSettingsTests
{
    [TestMethod]
    public void VoyagePacingAcceptsOnlyBoundedWholeSeconds()
    {
        Assert.AreEqual(TimeSpan.Zero, RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval(null));
        Assert.AreEqual(TimeSpan.Zero, RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval("0"));
        Assert.AreEqual(TimeSpan.FromSeconds(22), RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval("22"));
        Assert.AreEqual(TimeSpan.FromSeconds(120), RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval("120"));
        foreach (var invalid in new[] { "-1", "121", "1.5", "invalid" })
            Assert.ThrowsExactly<InvalidOperationException>(() => RetrievalEmbeddingLiveSettings.ParseVoyageRequestInterval(invalid));
    }

    [TestMethod]
    public async Task VoyagePacingSpacesStartsAndSubtractsElapsedRequestTime()
    {
        var elapsed = TimeSpan.Zero;
        var delays = new List<TimeSpan>();
        var pacer = new RetrievalEmbeddingLiveRequestPacer(() => elapsed, (delay, _) =>
        { delays.Add(delay); elapsed += delay; return Task.CompletedTask; });
        await pacer.WaitAsync(TimeSpan.FromSeconds(22), CancellationToken.None);
        Assert.HasCount(0, delays);
        await pacer.WaitAsync(TimeSpan.FromSeconds(22), CancellationToken.None);
        elapsed += TimeSpan.FromSeconds(5);
        await pacer.WaitAsync(TimeSpan.FromSeconds(22), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(22), TimeSpan.FromSeconds(17) }, delays);
    }

    [TestMethod]
    public async Task CanceledPacingReleasesGateWithoutReservingAnotherStart()
    {
        var elapsed = TimeSpan.Zero;
        var delays = new List<TimeSpan>();
        using var cancellation = new CancellationTokenSource();
        var pacer = new RetrievalEmbeddingLiveRequestPacer(() => elapsed, (delay, token) =>
        {
            delays.Add(delay);
            if (token.CanBeCanceled)
            { cancellation.Cancel(); return Task.FromCanceled(token); }
            elapsed += delay;
            return Task.CompletedTask;
        });
        await pacer.WaitAsync(TimeSpan.FromSeconds(22), CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => pacer.WaitAsync(TimeSpan.FromSeconds(22), cancellation.Token));
        await pacer.WaitAsync(TimeSpan.FromSeconds(22), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(22), TimeSpan.FromSeconds(22) }, delays);
    }

    [TestMethod]
    public async Task DisabledGateReadsNeitherCredentialsNorVault()
    {
        await Assert.ThrowsAsync<AssertInconclusiveException>(() => RetrievalEmbeddingLiveSettings.ResolveKeyAsync("Gemini",
            name => name == RetrievalEmbeddingLiveSettings.OptInVariable ? null : throw new InvalidOperationException("Credentials must not be read."),
            _ => throw new InvalidOperationException("Vault must not be read.")));
    }

    [TestMethod]
    [DataRow("Voyage", "VOYAGE_API_KEY")]
    [DataRow("Voyage", "MYTHOSIA_VOYAGE_API_KEY")]
    [DataRow("Gemini", "GEMINI_API_KEY")]
    [DataRow("Gemini", "GOOGLE_API_KEY")]
    [DataRow("Gemini", "MYTHOSIA_GEMINI_API_KEY")]
    public async Task EnvironmentCredentialDoesNotContactVault(string provider, string variable)
    {
        var result = await RetrievalEmbeddingLiveSettings.ResolveKeyAsync(provider,
            name => name == RetrievalEmbeddingLiveSettings.OptInVariable ? "1" : name == variable ? "synthetic-test-key" : null,
            _ => throw new InvalidOperationException("Vault must not be read."));
        Assert.AreEqual("synthetic-test-key", result);
    }

    [TestMethod]
    public async Task VoyageMissingCredentialDoesNotInventVaultSecret()
    {
        await Assert.ThrowsAsync<AssertInconclusiveException>(() => RetrievalEmbeddingLiveSettings.ResolveKeyAsync("Voyage",
            name => name == RetrievalEmbeddingLiveSettings.OptInVariable ? "1" : null,
            _ => throw new InvalidOperationException("Vault must not be read.")));
    }

    [TestMethod]
    [DataRow("Voyage", "existing-voyage-secret")]
    [DataRow("Gemini", "gemini-secret")]
    public async Task VaultResolutionUsesExistingConfiguredName(string provider, string expectedSecret)
    {
        string? requested = null;
        var result = await RetrievalEmbeddingLiveSettings.ResolveKeyAsync(provider,
            name => name == RetrievalEmbeddingLiveSettings.OptInVariable ? "1"
                : name == "MYTHOSIA_VOYAGE_SECRET_NAME" ? expectedSecret : null,
            name => { requested = name; return Task.FromResult("synthetic-vault-key"); });
        Assert.AreEqual(expectedSecret, requested);
        Assert.AreEqual("synthetic-vault-key", result);
    }

    [TestMethod]
    public async Task UnavailableVaultIsInconclusiveWithoutExposingErrorDetails()
    {
        var error = await Assert.ThrowsAsync<AssertInconclusiveException>(() => RetrievalEmbeddingLiveSettings.ResolveKeyAsync("Gemini",
            name => name == RetrievalEmbeddingLiveSettings.OptInVariable ? "1" : null,
            _ => throw new InvalidOperationException("sensitive-account-detail")));
        Assert.IsFalse(error.Message.Contains("sensitive-account-detail", StringComparison.Ordinal));
    }
}
