using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Rag
{
    internal static class AgenticRagTraceRegistry
    {
        private sealed class ObserverState
        {
            public object SyncRoot { get; } = new object();

            public Dictionary<string, List<Action<AgenticRagSearchTrace>>> ObserversByToolName { get; }
                = new Dictionary<string, List<Action<AgenticRagSearchTrace>>>(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<object, ObserverState> _states = new ConditionalWeakTable<object, ObserverState>();

        public static void Add(object service, string toolName, Action<AgenticRagSearchTrace> observer)
        {
            var state = _states.GetOrCreateValue(service);

            lock (state.SyncRoot)
            {
                if (!state.ObserversByToolName.TryGetValue(toolName, out var observers))
                {
                    observers = new List<Action<AgenticRagSearchTrace>>();
                    state.ObserversByToolName[toolName] = observers;
                }

                observers.Add(observer);
            }
        }

        public static void Notify(object service, string toolName, AgenticRagSearchTrace trace)
        {
            if (!_states.TryGetValue(service, out var state))
                return;

            List<Action<AgenticRagSearchTrace>>? observers = null;

            lock (state.SyncRoot)
            {
                if (state.ObserversByToolName.TryGetValue(toolName, out var registered) && registered.Count > 0)
                    observers = new List<Action<AgenticRagSearchTrace>>(registered);
            }

            if (observers == null)
                return;

            foreach (var observer in observers)
            {
                try
                {
                    observer(trace);
                }
                catch
                {
                    // Trace callbacks are observability helpers and should not break agent execution.
                }
            }
        }
    }
}
