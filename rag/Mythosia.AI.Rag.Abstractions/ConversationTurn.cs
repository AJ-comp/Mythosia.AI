namespace Mythosia.AI.Rag
{
    /// <summary>
    /// A lightweight representation of a single conversation turn (user or assistant).
    /// Used by <see cref="IQueryRewriter"/> to provide conversation context without
    /// depending on Mythosia.AI's Message class.
    /// </summary>
    public class ConversationTurn
    {
        /// <summary>
        /// The role of the speaker: "user" or "assistant".
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// The text content of this turn.
        /// </summary>
        public string Content { get; }

        public ConversationTurn(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
