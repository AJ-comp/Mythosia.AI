using System.ComponentModel;

namespace Mythosia.AI.Models
{
    /// <summary>
    /// Represents the role of an actor in a conversation
    /// </summary>
    public enum ActorRole
    {
        [Description("user")]
        User,

        [Description("system")]
        System,

        [Description("assistant")]
        Assistant,

        [Description("function")]
        Function
    }
}