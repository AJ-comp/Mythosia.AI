using Mythosia.AI.Builders;
using Mythosia.AI.Models;
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
        private FunctionCallingPolicy? _customPolicy;

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
            _customPolicy = policy.Clone();
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

        private AIRequestBuilder CreateRequest()
        {
            var request = _service.CreateRequest(_builder.Build());
            return _customPolicy == null ? request : request.WithPolicy(_customPolicy);
        }

        /// <summary>Sends the message using an independent request configuration.</summary>
        public Task<string> SendAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateRequest().SendMessageAsync(cancellationToken);
        }

        /// <summary>Sends a one-off request without changing the service default mode.</summary>
        public Task<string> SendOnceAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateRequest().WithStatelessMode().GetCompletionAsync(cancellationToken);
        }

        /// <summary>Observes streaming text using the captured request configuration.</summary>
        public async Task StreamAsync(Action<string> onContent)
        {
            await foreach (var chunk in CreateRequest().StreamAsync()) onContent(chunk);
        }

        /// <summary>Observes streaming text without maintaining conversation history.</summary>
        public async Task StreamOnceAsync(Action<string> onContent)
        {
            await foreach (var chunk in CreateRequest().WithStatelessMode().StreamAsync()) onContent(chunk);
        }

        /// <summary>Streams the captured message request.</summary>
        public async IAsyncEnumerable<string> StreamAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in CreateRequest().StreamAsync(cancellationToken)) yield return chunk;
        }

        /// <summary>Streams the captured message as a one-off request.</summary>
        public async IAsyncEnumerable<string> StreamOnceAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var chunk in CreateRequest().WithStatelessMode().StreamAsync(cancellationToken)) yield return chunk;
        }
    }
}
