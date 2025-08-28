using Mythosia.AI.Builders;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Extensions
{
    /// <summary>
    /// Fluent chain for building and sending messages
    /// </summary>
    public class MessageChain
    {
        private readonly AIService _service;
        private readonly MessageBuilder _builder;
        private FunctionCallingPolicy _customPolicy;

        internal MessageChain(AIService service)
        {
            _service = service;
            _builder = MessageBuilder.Create();
        }

        public MessageChain AddText(string text)
        {
            _builder.AddText(text);
            return this;
        }

        public MessageChain AddImage(string imagePath)
        {
            _builder.AddImage(imagePath);
            return this;
        }

        public MessageChain AddImage(byte[] imageData, string mimeType)
        {
            _builder.AddImage(imageData, mimeType);
            return this;
        }

        public MessageChain AddImageUrl(string url)
        {
            _builder.AddImageUrl(url);
            return this;
        }

        public MessageChain WithRole(ActorRole role)
        {
            _builder.WithRole(role);
            return this;
        }

        public MessageChain WithHighDetail()
        {
            _builder.WithHighDetail();
            return this;
        }

        /// <summary>
        /// Sets a custom timeout for this request
        /// </summary>
        public MessageChain WithTimeout(int seconds)
        {
            if (_customPolicy == null)
            {
                _customPolicy = (_service.DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            }
            _customPolicy.TimeoutSeconds = seconds;
            return this;
        }

        /// <summary>
        /// Sets max rounds for this request
        /// </summary>
        public MessageChain WithMaxRounds(int rounds)
        {
            if (_customPolicy == null)
            {
                _customPolicy = (_service.DefaultPolicy ?? FunctionCallingPolicy.Default).Clone();
            }
            _customPolicy.MaxRounds = rounds;
            return this;
        }

        /// <summary>
        /// Sets a custom policy for this request
        /// </summary>
        public MessageChain WithPolicy(FunctionCallingPolicy policy)
        {
            _customPolicy = policy;
            return this;
        }

        /// <summary>
        /// Uses the Vision-optimized policy (90 seconds timeout)
        /// </summary>
        public MessageChain WithVisionPolicy()
        {
            _customPolicy = FunctionCallingPolicy.Vision;
            return this;
        }

        /// <summary>
        /// Sends the message and maintains conversation history.
        /// </summary>
        public async Task<string> SendAsync()
        {
            var message = _builder.Build();

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                return await _service.GetCompletionAsync(message);
            }
            finally
            {
                // Reset policy after use
                _service.CurrentPolicy = null;
            }
        }

        /// <summary>
        /// Sends the message as a one-off query without affecting conversation history.
        /// </summary>
        public async Task<string> SendOnceAsync()
        {
            var message = _builder.Build();

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                return await _service.AskOnceAsync(message);
            }
            finally
            {
                // Reset policy after use
                _service.CurrentPolicy = null;
            }
        }

        /// <summary>
        /// Sends the message and streams the response (callback version)
        /// </summary>
        public async Task StreamAsync(Action<string> onContent)
        {
            var message = _builder.Build();

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                await _service.StreamCompletionAsync(message, content =>
                {
                    onContent(content);
                    return Task.CompletedTask;
                });
            }
            finally
            {
                // Reset policy after use
                _service.CurrentPolicy = null;
            }
        }

        /// <summary>
        /// Streams the message as a one-off query (callback version)
        /// </summary>
        public async Task StreamOnceAsync(Action<string> onContent)
        {
            var message = _builder.Build();

            var originalMode = _service.StatelessMode;
            _service.StatelessMode = true;

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                await _service.StreamCompletionAsync(message, content =>
                {
                    onContent(content);
                    return Task.CompletedTask;
                });
            }
            finally
            {
                _service.StatelessMode = originalMode;
                _service.CurrentPolicy = null;
            }
        }

        /// <summary>
        /// Streams the response as IAsyncEnumerable
        /// </summary>
        public async IAsyncEnumerable<string> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = _builder.Build();

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                await foreach (var chunk in _service.StreamAsync(message, cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                // Reset policy after use
                _service.CurrentPolicy = null;
            }
        }

        /// <summary>
        /// Streams as one-off query as IAsyncEnumerable
        /// </summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var message = _builder.Build();

            // Apply custom policy if set
            if (_customPolicy != null)
            {
                _service.CurrentPolicy = _customPolicy;
            }

            try
            {
                await foreach (var chunk in _service.StreamOnceAsync(message, cancellationToken))
                {
                    yield return chunk;
                }
            }
            finally
            {
                // Reset policy after use
                _service.CurrentPolicy = null;
            }
        }
    }
}