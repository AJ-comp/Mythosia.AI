using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Perplexity
{
    public partial class PerplexityService
    {
        /// <summary>Submits durable server work. The returned handle can poll, reconnect, cancel and retrieve files.</summary>
        /// <remarks>Captures the conversation without appending a turn. Client functions require StartRunAsync;
        /// background work supports hosted tools and MCP. Jobs are created with streaming enabled so the handle can reconnect.
        /// Caller cancellation stops submission, not a known server job.</remarks>
        public Task<PerplexityBackgroundRun> StartBackgroundAsync(string prompt,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
            => StartBackgroundAsync(new Message(ActorRole.User, prompt ?? throw new ArgumentNullException(nameof(prompt))), context, cancellationToken);

        public async Task<PerplexityBackgroundRun> StartBackgroundAsync(Message message,
            AIRequestContext? context = null, CancellationToken cancellationToken = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            cancellationToken.ThrowIfCancellationRequested();
            using var settingsScope = BeginRequestSettingsScope();
            using var scope = BeginRequestFeaturesScope(message);
            ValidateAgentClientToolSelection();
            if (ShouldUseFunctions && RequestFunctionCallMode != FunctionCallMode.None)
                throw new NotSupportedException("Background jobs cannot execute application functions. Use StartRunAsync, or disable client functions and use hosted tools.");
            var options = (CurrentProviderRequestOptions as PerplexityAgentOptions ?? RequestAgentOptions).Clone();
            if (options.Store == false) throw new ArgumentException("Background jobs require retrieval; Store cannot be false.");
            var policy = GetExecutionPolicy();
            using var timeout = CreateRequestTimeoutCts(policy, cancellationToken);
            var effectiveContext = await BuildEffectiveContextAsync(context, timeout.Token).ConfigureAwait(false);
            var restore = effectiveContext != null ? ApplyRequestContext(effectiveContext) : (Action)(() => { });
            try
            {
                // Background submission never appends its input to history, so apply the
                // override directly to this detached input and retain the saved turns.
                var messages = RequestStatelessMode ? new List<Message>() : ActivateChat.Messages.ToList();
                messages.Add(effectiveContext?.RequestMessageOverride ?? message);
                if (effectiveContext?.AdditionalMessages != null)
                    messages.AddRange(effectiveContext.AdditionalMessages);
                var body = BuildAgentRequestBody(messages, GetEffectiveSystemMessageWithRequestContext(), RequestModel,
                    options, CurrentRequestFeatures, false, true);
                body["background"] = true;
                using var request = CreateAgentHttpRequest(body);
                var transport = new PerplexityBackgroundRun(HttpClient, ApiKey);
                var initial = await transport.SendInitialAsync(request, timeout.Token).ConfigureAwait(false);
                transport.SetInitial(initial);
                LastResponseId = initial.Id;
                return transport;
            }
            finally { restore(); }
        }

        /// <summary>Reattaches to a saved response ID. No network request is made until the handle is used.</summary>
        public PerplexityBackgroundRun ResumeBackgroundRun(string responseId)
            => new PerplexityBackgroundRun(HttpClient, ApiKey, responseId);

        /// <summary>Retrieves a stored response, including responses created without background mode.</summary>
        public Task<PerplexityAgentResponse> GetAgentResponseAsync(string responseId, CancellationToken cancellationToken = default)
            => ResumeBackgroundRun(responseId).GetResponseAsync(cancellationToken);

        public Task<IReadOnlyList<PerplexityResponseFile>> GetResponseFilesAsync(string responseId, CancellationToken cancellationToken = default)
            => ResumeBackgroundRun(responseId).ListFilesAsync(cancellationToken);

        public Task<byte[]> GetResponseFileContentAsync(string responseId, string fileId, CancellationToken cancellationToken = default)
            => ResumeBackgroundRun(responseId).DownloadFileAsync(fileId, cancellationToken);
    }
}
