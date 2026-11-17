using System;
using System.Collections.Generic;

namespace AIBT.Simulation
{
    /// <summary>
    /// P10-012: a container the game registered for debugging tools (<see cref="TreeSimulationDebug.CopyRegistered"/>).
    /// Experimental until G5.
    /// </summary>
    public readonly struct RegisteredSimulation
    {
        internal RegisteredSimulation(string name, TreeSimulation simulation)
        {
            Name = name;
            Simulation = simulation;
        }

        public string Name { get; }
        public TreeSimulation Simulation { get; }
    }

    /// <summary>
    /// P10-012 (ADR-P10-008 stage G4): the opt-in list through which Editor tools (the AIBT MCP/CLI tools,
    /// the graph window) find the game's simulation containers. Nothing is registered unless the game calls
    /// <see cref="Register"/>; containers are held weakly, a disposed container leaves the list at once, and
    /// a container that is only referenced from here can be collected. The list is never read inside
    /// <see cref="TreeSimulation.Step"/> and nothing in it affects results. It exists in the Editor and in
    /// development builds only: in other builds <see cref="Register"/> returns false and keeps nothing.
    /// Main thread only (as the containers). Experimental until G5.
    /// </summary>
    public static class TreeSimulationDebug
    {
        private struct Entry
        {
            internal WeakReference<TreeSimulation> Simulation;
            internal string Name;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>True where registration works: the Editor and development builds.</summary>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Lists <paramref name="simulation"/> for tools under <paramref name="name"/> (registering it again
        /// renames it). Returns false, keeping nothing, outside the Editor and development builds or for a
        /// disposed container.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="simulation"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is null or white space.</exception>
        public static bool Register(TreeSimulation simulation, string name)
        {
            if (simulation == null) throw new ArgumentNullException(nameof(simulation));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.", nameof(name));
            if (!IsAvailable || simulation.IsDisposed) return false;
            Prune();
            for (var index = 0; index < Entries.Count; index++)
            {
                if (!Entries[index].Simulation.TryGetTarget(out var existing) || !ReferenceEquals(existing, simulation)) continue;
                Entries[index] = new Entry { Simulation = Entries[index].Simulation, Name = name };
                return true;
            }
            Entries.Add(new Entry { Simulation = new WeakReference<TreeSimulation>(simulation), Name = name });
            return true;
        }

        /// <summary>Removes <paramref name="simulation"/> from the list. Returns false when it was not listed.</summary>
        public static bool Unregister(TreeSimulation simulation)
        {
            if (simulation == null) return false;
            var removed = Forget(simulation);
            Prune();
            return removed;
        }

        /// <summary>
        /// Copies the listed containers that are alive and not disposed, in registration order, into
        /// <paramref name="into"/> (cleared first) and returns their count. Allocates; for tools.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
        public static int CopyRegistered(List<RegisteredSimulation> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            Prune();
            for (var index = 0; index < Entries.Count; index++)
                if (Entries[index].Simulation.TryGetTarget(out var simulation))
                    into.Add(new RegisteredSimulation(Entries[index].Name, simulation));
            return into.Count;
        }

        /// <summary>Called by <see cref="TreeSimulation.Dispose"/>: the container leaves the list.</summary>
        internal static bool Forget(TreeSimulation simulation)
        {
            var removed = false;
            for (var index = Entries.Count - 1; index >= 0; index--)
            {
                if (!Entries[index].Simulation.TryGetTarget(out var existing) || !ReferenceEquals(existing, simulation)) continue;
                Entries.RemoveAt(index);
                removed = true;
            }
            return removed;
        }

        /// <summary>Drops collected and disposed containers.</summary>
        private static void Prune()
        {
            for (var index = Entries.Count - 1; index >= 0; index--)
                if (!Entries[index].Simulation.TryGetTarget(out var simulation) || simulation.IsDisposed)
                    Entries.RemoveAt(index);
        }

        /// <summary>Test access: entries held, collected or not.</summary>
        internal static int EntryCountForTests => Entries.Count;
    }
}
