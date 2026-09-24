using Mythosia.AI.Models;
using System.Collections.Generic;

namespace Mythosia.AI.Services
{
    /// <summary>Optional access to immutable processing-mode observations from the latest logical request.
    /// Adds no requirements to existing IAIService or request-feature implementations.</summary>
    public interface IAIProcessingInfoService
    {
        IReadOnlyList<AIProcessingInfo> LastProcessing { get; }
    }
}
