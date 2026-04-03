using Mythosia.AI.Models;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Models.Streaming;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services
{
    /// <summary>
    /// Core abstraction for AI completion and streaming services.
    /// Implement this interface to create custom AI service providers.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// The AI model identifier currently in use.
        /// </summary>
        string Model { get; }

        /// <summary>
        /// The AI provider name (e.g., "OpenAI", "Anthropic").
        /// </summary>
        string Provider { get; }

        /// <summary>
        /// System message for the active chat.
        /// </summary>
        string SystemMessage { get; set; }

        /// <summary>
        /// When true, each request is processed independently without maintaining conversation history.
        /// </summary>
        bool StatelessMode { get; set; }

        /// <summary>
        /// The currently active chat block containing conversation history.
        /// </summary>
        ChatBlock ActivateChat { get; }

        #region Completion

        Task<string> GetCompletionAsync(string prompt, AIRequestProfile? profile = null, AIRequestContext? context = null);
        Task<string> GetCompletionAsync(Message message, AIRequestProfile? profile = null, AIRequestContext? context = null);

        #endregion

        #region Streaming

        IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamAsync(Message message, AIRequestContext? context = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<StreamingContent> StreamAsync(string prompt, StreamOptions options, CancellationToken cancellationToken = default);
        IAsyncEnumerable<StreamingContent> StreamAsync(Message message, StreamOptions options, AIRequestContext? context = null, CancellationToken cancellationToken = default);

        #endregion
    }
}
