using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace AIBT
{
    /// <summary>
    /// Named events a behavior tree raises to gameplay code, for example <c>"attacked"</c> raised by
    /// the <c>aibt.unity.raise-event</c> node. Subscribe from a script with
    /// <see cref="Subscribe"/>, or wire responses in the agent's inspector (Event Responses).
    /// Events are raised synchronously on the main thread, inside the tree's tick.
    /// </summary>
    public sealed class AgentEvents
    {
        private readonly BehaviorTreeAgent _owner;
        private readonly Dictionary<string, Action<BehaviorTreeAgent>> _handlers =
            new Dictionary<string, Action<BehaviorTreeAgent>>(StringComparer.Ordinal);

        internal AgentEvents(BehaviorTreeAgent owner)
        {
            _owner = owner;
        }

        /// <summary>Raised for every event, after the handlers subscribed to its name.</summary>
        public event Action<BehaviorTreeAgent, string> AnyRaised;

        /// <summary>Calls <paramref name="handler"/> whenever the event <paramref name="eventName"/> is raised.</summary>
        public void Subscribe(string eventName, Action<BehaviorTreeAgent> handler)
        {
            if (string.IsNullOrEmpty(eventName)) throw new ArgumentException("An event name is required.", nameof(eventName));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.TryGetValue(eventName, out var existing);
            _handlers[eventName] = existing + handler;
        }

        /// <summary>Removes a handler added with <see cref="Subscribe"/>.</summary>
        public void Unsubscribe(string eventName, Action<BehaviorTreeAgent> handler)
        {
            if (string.IsNullOrEmpty(eventName) || handler == null) return;
            if (!_handlers.TryGetValue(eventName, out var existing)) return;
            existing -= handler;
            if (existing == null) _handlers.Remove(eventName);
            else _handlers[eventName] = existing;
        }

        /// <summary>
        /// Raises an event: subscribed handlers first, then matching inspector responses, then
        /// <see cref="AnyRaised"/>. Returns how many handlers and responses received it.
        /// </summary>
        public int Raise(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return 0;
            var received = 0;
            if (_handlers.TryGetValue(eventName, out var handlers) && handlers != null)
            {
                received += handlers.GetInvocationList().Length;
                handlers(_owner);
            }

            received += _owner.InvokeEventResponses(eventName);
            AnyRaised?.Invoke(_owner, eventName);
            return received;
        }
    }

    /// <summary>An inspector response to a named agent event.</summary>
    [Serializable]
    public sealed class AgentEventResponse
    {
        [Tooltip("The event name a tree raises, for example 'attacked'.")]
        public string eventName;

        [Tooltip("Called when the tree raises the event.")]
        public UnityEvent response = new UnityEvent();
    }

    /// <summary>
    /// A named scene object a tree can address, for example <c>"player"</c>. Nodes that act on
    /// another object take the reference name as a parameter; an empty name means the agent itself.
    /// </summary>
    [Serializable]
    public struct AgentReference
    {
        [Tooltip("The name nodes use, for example 'player'.")]
        public string name;

        [Tooltip("A GameObject or a component.")]
        public UnityEngine.Object target;

        public AgentReference(string name, UnityEngine.Object target)
        {
            this.name = name;
            this.target = target;
        }
    }
}
