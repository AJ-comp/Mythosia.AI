using Mythosia.AI.Models.Messages;
using Mythosia.AI.Protocols;
using System.Net.Http;
using System.Text;

namespace Mythosia.AI.Services.DeepSeek
{
    public partial class DeepSeekService
    {
        private static readonly ChatCompletionsProtocol _protocol = ChatCompletionsProtocol.Instance;

        #region Request Creation

        protected override HttpRequestMessage CreateMessageRequest()
        {
            var systemMsg = GetEffectiveSystemMessageWithRequestContext();

            var p = new ProtocolRequestParams
            {
                Model = Model,
                Messages = GetLatestMessages(),
                SystemMessage = systemMsg,
                Temperature = Temperature,
                TopP = TopP,
                FrequencyPenalty = FrequencyPenalty,
                PresencePenalty = PresencePenalty,
                MaxTokens = GetEffectiveMaxTokens(),
                Stream = Stream,
                StructuredOutputSchemaJson = _structuredOutputSchemaJson
            };

            var body = _protocol.BuildRequestBody(p, ConvertMessageForDeepSeek);
            return _protocol.CreateRequest(ApiKey, body);
        }

        private object ConvertMessageForDeepSeek(Message message)
        {
            var role = message.Role.ToDescription();

            // DeepSeek currently doesn't support multimodal in their public API
            // But we'll prepare the structure for when they do
            if (!message.HasMultimodalContent)
            {
                return new { role, content = message.Content };
            }

            // For now, convert multimodal to text description
            var textContent = new StringBuilder();
            var hasImages = false;

            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    textContent.Append(text.Text);
                }
                else if (content is ImageContent)
                {
                    hasImages = true;
                    textContent.Append(" [Image] ");
                }
            }

            if (hasImages)
            {
                // Log warning or throw exception based on requirements
                System.Console.WriteLine("Warning: DeepSeek doesn't currently support image inputs. Images will be ignored.");
            }

            return new { role, content = textContent.ToString() };
        }

        #endregion
    }
}