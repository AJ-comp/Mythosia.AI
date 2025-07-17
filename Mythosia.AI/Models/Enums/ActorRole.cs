using System.ComponentModel;

namespace Mythosia.AI.Models.Enums
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
        Assistant
    }
}