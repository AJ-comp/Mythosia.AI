using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mythosia.AI.Models
{
    /// <summary>
    /// Pure conversation container - holds only conversation identity and history.
    /// All settings (Temperature, MaxTokens, etc.) are managed by the service.
    /// </summary>
    public class ChatBlock
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string SystemMessage { get; set; } = string.Empty;
        public IList<Message> Messages { get; } = new List<Message>();

        /// <summary>
        /// Removes all Function-related messages from the conversation
        /// </summary>
        [Obsolete("This method is deprecated and will be removed in future versions.")]
        public void RemoveFunctionMessages()
        {
            var functionMessages = Messages.Where(m =>
                m.Role == ActorRole.Function ||
                (m.Role == ActorRole.Assistant &&
                 m.Metadata?.GetValueOrDefault("type")?.ToString() == "function_call")
            ).ToList();

            foreach (var msg in functionMessages)
            {
                Messages.Remove(msg);
            }

            if (functionMessages.Count > 0)
            {
                Console.WriteLine($"[Model Change] Removed {functionMessages.Count} function-related messages for compatibility");
            }
        }

        /// <summary>
        /// Clears all messages from the conversation
        /// </summary>
        public void ClearMessages()
        {
            Messages.Clear();
        }

        /// <summary>
        /// Removes the last message if it exists
        /// </summary>
        public void RemoveLastMessage()
        {
            if (Messages.Count > 0)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        }

        /// <summary>
        /// Creates a deep copy of the ChatBlock (SystemMessage + Messages)
        /// </summary>
        public ChatBlock Clone()
        {
            var clone = new ChatBlock
            {
                SystemMessage = SystemMessage
            };

            foreach (var message in Messages)
            {
                clone.Messages.Add(message.Clone());
            }

            return clone;
        }
    }
}