using Mythosia.AI.Models.Messages;
using System.Collections.Generic;

namespace Mythosia.AI.Models
{
    public sealed class AIRequestContext
    {
        public string? SystemMessagePrefix { get; set; }
        public string? SystemMessageSuffix { get; set; }
        public Message? RequestMessageOverride { get; set; }
        public IReadOnlyList<Message>? AdditionalMessages { get; set; }
    }
}
