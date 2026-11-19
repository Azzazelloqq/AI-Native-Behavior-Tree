using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Burst;
using static AIBT.Tests.Integration.NativeRuntime.LibraryTrees;

namespace AIBT.Tests.Integration.NativeRuntime
{
    /// <summary>
    /// P11-002 stage 1: one behaviour case per contract point of each library node (ADR-P11-002 §5.1),
    /// as a tree whose facts are the keys' defaults. The same trees run on ProductionTreeHost
    /// (<see cref="NativeLibraryNodeTests"/>) and in TreeSimulation groups in place and with Jobs
    /// (<see cref="AIBT.Tests.Integration.Simulation.NativeLibrarySimulationTests"/>).
    /// </summary>
    public sealed class LibraryCase
    {
        internal string Node;
        internal string Name;
        internal string Json;
        internal int Updates = 1;
        internal NodeStatus? Root;
        internal readonly List<(string Key, object Value)> Values = new List<(string, object)>();

        public override string ToString() => Name;

        internal LibraryCase Expect(string key, object value)
        {
            Values.Add((key, value));
            return this;
        }
    }

    internal static class LibraryCases
    {
        private static int s_counter;

        internal static IReadOnlyList<LibraryCase> All { get; } = Build();

        internal static IEnumerable<LibraryCase> Of(string node) => All.Where(c => c.Node == node);

        private static LibraryCase Case(string node, string name, NodeStatus? root, string rootId, IEnumerable<string> keys, params string[] nodes)
        {
            var id = "tests.p11002." + node.Substring("aibt.native.".Length) + "." + (++s_counter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new LibraryCase { Node = node, Name = node.Substring("aibt.native.".Length) + ": " + name, Root = root, Json = Tree(id, rootId, keys, nodes) };
        }

        // A single condition at the root, reading key defaults.
        private static LibraryCase Condition(string node, string name, bool success, string parameters, string bindings, params string[] keys)
            => Case(node, name, success ? NodeStatus.Success : NodeStatus.Failure, "c", keys, Leaf("c", node, parameters, bindings));

        private static LibraryCase Action(string node, string name, NodeStatus root, string parameters, string bindings, params string[] keys)
            => Case(node, name, root, "a", keys, Leaf("a", node, parameters, bindings));

        private static List<LibraryCase> Build()
        {
            const string S = "aibt.native.";
            var cases = new List<LibraryCase>
            {
                // ---- group 1: conditions -------------------------------------------------------------
                Condition(S + "int-at-least", "3 >= 3", true, "\"threshold\": 3", "\"key\": \"k\"", Int("k", 3)),
                Condition(S + "int-at-least", "2 >= 3 fails", false, "\"threshold\": 3", "\"key\": \"k\"", Int("k", 2)),
                Condition(S + "int-at-least", "-5 >= 0 fails", false, "\"threshold\": 0", "\"key\": \"k\"", Int("k", -5)),
                Condition(S + "int-at-least", "int.MaxValue >= 4294967295 fails", false, "\"threshold\": 4294967295", "\"key\": \"k\"", Int("k", int.MaxValue)),
                Condition(S + "int-at-least", "int.MaxValue >= 2147483647", true, "\"threshold\": 2147483647", "\"key\": \"k\"", Int("k", int.MaxValue)),

                Condition(S + "int-equals", "4 == 4", true, "\"value\": 4", "\"key\": \"k\"", Int("k", 4)),
                Condition(S + "int-equals", "5 == 4 fails", false, "\"value\": 4", "\"key\": \"k\"", Int("k", 5)),
                Condition(S + "int-equals", "-4 == 4 fails", false, "\"value\": 4", "\"key\": \"k\"", Int("k", -4)),
                Condition(S + "int-equals", "int.MinValue == 2147483648 fails", false, "\"value\": 2147483648", "\"key\": \"k\"", Int("k", int.MinValue)),

                Condition(S + "int-keys-at-least", "-3 >= -5", true, null, "\"left\": \"l\", \"right\": \"r\"", Int("l", -3), Int("r", -5)),
                Condition(S + "int-keys-at-least", "-5 >= -3 fails", false, null, "\"left\": \"l\", \"right\": \"r\"", Int("l", -5), Int("r", -3)),
                Condition(S + "int-keys-at-least", "7 >= 7", true, null, "\"left\": \"l\", \"right\": \"r\"", Int("l", 7), Int("r", 7)),

                Condition(S + "float-keys-at-least", "1.5 >= 1.5", true, null, "\"left\": \"l\", \"right\": \"r\"", Float("l", 1.5f), Float("r", 1.5f)),
                Condition(S + "float-keys-at-least", "1.25 >= 1.5 fails", false, null, "\"left\": \"l\", \"right\": \"r\"", Float("l", 1.25f), Float("r", 1.5f)),
                Condition(S + "float-keys-at-least", "-2 >= -3", true, null, "\"left\": \"l\", \"right\": \"r\"", Float("l", -2f), Float("r", -3f)),

                Condition(S + "float3-within-distance", "3-4-5 at distance 5", true, "\"distance\": 5.0, \"flat\": false", "\"a\": \"a\", \"b\": \"b\"", Float3("a"), Float3("b", 3f, 4f, 0f)),
                Condition(S + "float3-within-distance", "3-4-5 at distance 4.99 fails", false, "\"distance\": 4.99, \"flat\": false", "\"a\": \"a\", \"b\": \"b\"", Float3("a"), Float3("b", 3f, 4f, 0f)),
                Condition(S + "float3-within-distance", "flat ignores Y", true, "\"distance\": 5.0, \"flat\": true", "\"a\": \"a\", \"b\": \"b\"", Float3("a"), Float3("b", 3f, 100f, 4f)),
                Condition(S + "float3-within-distance", "not flat counts Y", false, "\"distance\": 5.0, \"flat\": false", "\"a\": \"a\", \"b\": \"b\"", Float3("a"), Float3("b", 3f, 100f, 4f)),
                Condition(S + "float3-within-distance", "same point at distance 0", true, "\"distance\": 0.0, \"flat\": false", "\"a\": \"a\", \"b\": \"b\"", Float3("a", 1f, 2f, 3f), Float3("b", 1f, 2f, 3f)),
                Condition(S + "float3-within-distance", "negative distance never succeeds", false, "\"distance\": -1.0, \"flat\": false", "\"a\": \"a\", \"b\": \"b\"", Float3("a", 1f, 2f, 3f), Float3("b", 1f, 2f, 3f)),

                Condition(S + "float3-is-set", "zero vector is not set", false, null, "\"key\": \"p\"", Float3("p")),
                Condition(S + "float3-is-set", "(0, 0, 0.5) is set", true, null, "\"key\": \"p\"", Float3("p", 0f, 0f, 0.5f)),
                Condition(S + "float3-is-set", "(0, -1, 0) is set", true, null, "\"key\": \"p\"", Float3("p", 0f, -1f, 0f)),

                // ---- group 2: actions -----------------------------------------------------------------
                Action(S + "set-bool", "writes true", NodeStatus.Success, "\"value\": true", "\"key\": \"b\"", Bool("b")).Expect("b", true),
                Action(S + "set-bool", "writes false", NodeStatus.Success, "\"value\": false", "\"key\": \"b\"", Bool("b", true)).Expect("b", false),

                Action(S + "set-int", "writes 42", NodeStatus.Success, "\"value\": 42", "\"key\": \"i\"", Int("i", 9)).Expect("i", 42),
                Action(S + "set-int", "writes 2147483647", NodeStatus.Success, "\"value\": 2147483647", "\"key\": \"i\"", Int("i", 9)).Expect("i", int.MaxValue),
                Action(S + "set-int", "2147483648 fails without writing", NodeStatus.Failure, "\"value\": 2147483648", "\"key\": \"i\"", Int("i", 9)).Expect("i", 9),
                Action(S + "set-int", "4294967295 fails without writing", NodeStatus.Failure, "\"value\": 4294967295", "\"key\": \"i\"", Int("i", 9)).Expect("i", 9),

                Action(S + "set-float3", "writes (1.5, -2, 3)", NodeStatus.Success, "\"x\": 1.5, \"y\": -2.0, \"z\": 3.0", "\"key\": \"p\"", Float3("p")).Expect("p", new Float3Value(1.5f, -2f, 3f)),
                Action(S + "set-float3", "writes the zero vector", NodeStatus.Success, "\"x\": 0.0, \"y\": 0.0, \"z\": 0.0", "\"key\": \"p\"", Float3("p", 1f, 1f, 1f)).Expect("p", new Float3Value(0f, 0f, 0f)),

                Action(S + "copy-bool", "copies true", NodeStatus.Success, null, "\"from\": \"f\", \"to\": \"t\"", Bool("f", true), Bool("t")).Expect("t", true).Expect("f", true),
                Action(S + "copy-int", "copies a negative value", NodeStatus.Success, null, "\"from\": \"f\", \"to\": \"t\"", Int("f", -7), Int("t", 3)).Expect("t", -7).Expect("f", -7),
                Action(S + "copy-float", "copies -2.5", NodeStatus.Success, null, "\"from\": \"f\", \"to\": \"t\"", Float("f", -2.5f), Float("t", 1f)).Expect("t", -2.5f),
                Action(S + "copy-float3", "copies (1, -2, 3)", NodeStatus.Success, null, "\"from\": \"f\", \"to\": \"t\"", Float3("f", 1f, -2f, 3f), Float3("t")).Expect("t", new Float3Value(1f, -2f, 3f)),

                Action(S + "increment-int", "5 + 3", NodeStatus.Success, "\"amount\": 3", "\"key\": \"i\"", Int("i", 5)).Expect("i", 8),
                Action(S + "increment-int", "wraps at int.MaxValue", NodeStatus.Success, "\"amount\": 1", "\"key\": \"i\"", Int("i", int.MaxValue)).Expect("i", int.MinValue),
                Action(S + "increment-int", "amount 4294967295 subtracts one", NodeStatus.Success, "\"amount\": 4294967295", "\"key\": \"i\"", Int("i", 0)).Expect("i", -1),
                Case(S + "increment-int", "two increments in one pass", NodeStatus.Success, "seq", new[] { Int("i", 2147483646) },
                    Composite("seq", "aibt.core.memory-sequence", "one", "two"),
                    Leaf("one", S + "increment-int", "\"amount\": 1", "\"key\": \"i\""),
                    Leaf("two", S + "increment-int", "\"amount\": 1", "\"key\": \"i\"")).Expect("i", int.MinValue),

                Action(S + "increment-float", "1 - 0.25", NodeStatus.Success, "\"amount\": -0.25", "\"key\": \"f\"", Float("f", 1f)).Expect("f", 0.75f),
                Action(S + "increment-float", "overflow fails without writing", NodeStatus.Failure, "\"amount\": 3e38", "\"key\": \"f\"", Float("f", 3e38f)).Expect("f", 3e38f),

                Action(S + "toggle-bool", "false becomes true", NodeStatus.Success, null, "\"key\": \"b\"", Bool("b")).Expect("b", true),
                Action(S + "toggle-bool", "true becomes false", NodeStatus.Success, null, "\"key\": \"b\"", Bool("b", true)).Expect("b", false),
                Case(S + "toggle-bool", "three toggles", NodeStatus.Success, "seq", new[] { Bool("b") },
                    Composite("seq", "aibt.core.memory-sequence", "one", "two", "three"),
                    Leaf("one", S + "toggle-bool", null, "\"key\": \"b\""),
                    Leaf("two", S + "toggle-bool", null, "\"key\": \"b\""),
                    Leaf("three", S + "toggle-bool", null, "\"key\": \"b\"")).Expect("b", true),

                Action(S + "float3-distance", "3-4-5", NodeStatus.Success, "\"flat\": false", "\"a\": \"a\", \"b\": \"b\", \"result\": \"d\"",
                    Float3("a", 1f, 2f, 3f), Float3("b", 4f, 6f, 3f), Float("d", 7f)).Expect("d", 5f),
                Action(S + "float3-distance", "flat ignores Y", NodeStatus.Success, "\"flat\": true", "\"a\": \"a\", \"b\": \"b\", \"result\": \"d\"",
                    Float3("a"), Float3("b", 3f, 10f, 4f), Float("d", 7f)).Expect("d", 5f),
                Action(S + "float3-distance", "zero vector gives 0", NodeStatus.Success, "\"flat\": false", "\"a\": \"a\", \"b\": \"b\", \"result\": \"d\"",
                    Float3("a", 2f, 2f, 2f), Float3("b", 2f, 2f, 2f), Float("d", 7f)).Expect("d", 0f),
                Action(S + "float3-distance", "overflow fails without writing", NodeStatus.Failure, "\"flat\": false", "\"a\": \"a\", \"b\": \"b\", \"result\": \"d\"",
                    Float3("a", 3e38f, 0f, 0f), Float3("b", -3e38f, 0f, 0f), Float("d", 7f)).Expect("d", 7f),

                // ---- group 3: flow ----------------------------------------------------------------------
                Action(S + "succeed", "succeeds", NodeStatus.Success, null, null),
                Action(S + "fail", "fails", NodeStatus.Failure, null, null),
                Case(S + "idle", "stays running", null, "a", Array.Empty<string>(), Leaf("a", S + "idle")),
            };
            cases.Single(c => c.Node == S + "idle").Updates = 5;
            return cases;
        }
    }
}
