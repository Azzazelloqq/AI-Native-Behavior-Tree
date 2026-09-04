namespace AIBT.Tests.Runtime.NativeExecution.HotReload
{
    /// <summary>
    /// Hand-built <see cref="CompiledProgram"/> fixtures for P7-012's own hot-reload tests, mirroring
    /// Tests/Runtime/NativeExecution/ProgramAndState/NativeProgramAndStateTests.cs's own established
    /// "preliminary program -> real content hash -> final program" pattern (a two-pass build, since
    /// NativeProgramImageOwnerV1.TryCreate validates CompiledProgramContentHashV1.Compute(program)
    /// against the header's own claimed hash). No AIBT.Authoring/ReferenceCompiler dependency needed
    /// -- AIBT.Runtime.Tests only references AIBT.Runtime, and CompiledProgram itself lives there.
    /// <para>
    /// Mirrors the disposable Spikes~/NativeHotReloadModel spike's own TwoLeafTreeJson scenario:
    /// sequence("a", "b") or, reversed, sequence("b", "a") -- a pure reorder of the children array.
    /// Each leaf's own NodeId keeps a fixed NodeTypeId across both programs (so a stable-NodeId-keyed
    /// classifier still sees both as Migrate), while the reorder genuinely shifts each leaf's own
    /// compiled index. Each node gets its own 4-byte instance-memory slice (root's own composite
    /// cursor; each leaf's own arbitrary "state" bytes), non-overlapping, so a migration test can
    /// prove real NodeMemory bytes -- not just Frame/Generation -- survive the copy.
    /// </para>
    /// </summary>
    internal static class NativeHotReloadTestProgram
    {
        internal const uint PerNodeMemorySize = 4u;
        internal const uint TotalMemorySize = PerNodeMemorySize * 3u;

        internal static readonly ulong RootTypeId = StableHash.Fnv1A64("aibt.core.memory-sequence");
        internal static readonly ulong LeafATypeId = StableHash.Fnv1A64("aibt.tests.p7012.leaf-a");
        internal static readonly ulong LeafBTypeId = StableHash.Fnv1A64("aibt.tests.p7012.leaf-b");

        internal static readonly NodeId RootNodeId = new NodeId("root");
        internal static readonly NodeId LeafANodeId = new NodeId("a");
        internal static readonly NodeId LeafBNodeId = new NodeId("b");

        internal static CompiledProgram TwoLeafSequence(bool reversed)
        {
            // Memory offsets follow compiled index order: node i owns bytes [i*4, i*4+4).
            var nodes = reversed
                ? new[] { NodeAt(RootTypeId, 0, new CompiledRange(0, 2)), NodeAt(LeafBTypeId, 1, default), NodeAt(LeafATypeId, 2, default) }
                : new[] { NodeAt(RootTypeId, 0, new CompiledRange(0, 2)), NodeAt(LeafATypeId, 1, default), NodeAt(LeafBTypeId, 2, default) };
            var children = new uint[] { 1, 2 };
            var debug = reversed
                ? new[]
                {
                    new CompiledDebugMapEntry(0, RootNodeId, "test/root"),
                    new CompiledDebugMapEntry(1, LeafBNodeId, "test/b"),
                    new CompiledDebugMapEntry(2, LeafANodeId, "test/a"),
                }
                : new[]
                {
                    new CompiledDebugMapEntry(0, RootNodeId, "test/root"),
                    new CompiledDebugMapEntry(1, LeafANodeId, "test/a"),
                    new CompiledDebugMapEntry(2, LeafBNodeId, "test/b"),
                };

            var preliminary = Build(Hash('d'), nodes, children, debug);
            var contentHash = CompiledProgramContentHashV1.Compute(preliminary);
            return Build(contentHash, nodes, children, debug);
        }

        private static CompiledNodeRecord NodeAt(ulong typeId, uint compiledIndex, CompiledRange children) => new CompiledNodeRecord(
            typeId, 1,
            0, 0, 1, // no config
            compiledIndex * PerNodeMemorySize, PerNodeMemorySize, 4,
            NodeMemoryLifetime.Activation,
            children,
            CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
            compiledIndex, // DebugIdentityIndex: `debug` is always built in the same 0/1/2 compiled-index order below.
            default, default);

        private static CompiledProgram Build(CompiledHash contentHash, CompiledNodeRecord[] nodes, uint[] children, CompiledDebugMapEntry[] debug)
        {
            var header = new CompiledProgramHeader(
                1, 1, new CompiledCompilerVersion(1, 0, 0, 0),
                Hash('a'), Hash('b'), Hash('c'), 1, contentHash,
                0, (uint)nodes.Length, (uint)children.Length, 0, (uint)debug.Length,
                0, (uint)nodes.Length * PerNodeMemorySize, 4, 0, true);
            return new CompiledProgram(
                header, nodes, children,
                System.Array.Empty<uint>(), System.Array.Empty<uint>(),
                System.Array.Empty<CompiledBlackboardSlotRecord>(), System.Array.Empty<CompiledObserverRecord>(),
                System.Array.Empty<uint>(), System.Array.Empty<byte>(), System.Array.Empty<byte>(), debug);
        }

        private static CompiledHash Hash(char value) => new CompiledHash(new string(value, 64));

        internal static CompiledProgram NestedSequence(bool reversed, bool reverseOuter = false)
        {
            var nodes = new[] { NodeAt(RootTypeId, 0, new CompiledRange(0, 2)), NodeAt(RootTypeId, 1, new CompiledRange(2, 2)),
                NodeAt(reversed ? LeafBTypeId : LeafATypeId, 2, default), NodeAt(reversed ? LeafATypeId : LeafBTypeId, 3, default),
                NodeAt(LeafATypeId, 4, default) };
            var children = reverseOuter ? new uint[] { 4, 1, 2, 3 } : new uint[] { 1, 4, 2, 3 };
            var debug = new[] { new CompiledDebugMapEntry(0, new NodeId("outer"), "test/outer"),
                new CompiledDebugMapEntry(1, RootNodeId, "test/root"),
                new CompiledDebugMapEntry(2, reversed ? LeafBNodeId : LeafANodeId, "test/first"),
                new CompiledDebugMapEntry(3, reversed ? LeafANodeId : LeafBNodeId, "test/second"),
                new CompiledDebugMapEntry(4, new NodeId("c"), "test/c") };
            return Build(CompiledProgramContentHashV1.Compute(Build(Hash('d'), nodes, children, debug)), nodes, children, debug);
        }

        internal static CompiledProgram Cooldown(bool replaceWithTimeout = false)
        {
            var nodes = new[]
            {
                new CompiledNodeRecord(StableHash.Fnv1A64(replaceWithTimeout ? "aibt.core.timeout" : "aibt.core.cooldown"), 1, 0, 16, 8,
                    0, 8, 8, replaceWithTimeout ? NodeMemoryLifetime.Activation : NodeMemoryLifetime.Instance,
                    new CompiledRange(0, 1), CompiledNodeFlags.BurstDomain, 0, default, default),
                new CompiledNodeRecord(LeafATypeId, 1, 0, 0, 1, 8, 4, 4, NodeMemoryLifetime.Activation,
                    default, CompiledNodeFlags.BurstDomain, 1, default, default)
            };
            var debug = new[] { new CompiledDebugMapEntry(0, RootNodeId, "test/root"), new CompiledDebugMapEntry(1, LeafANodeId, "test/a") };
            // Duration 100, OnEnter, blocked Failure. Leaf owns bytes 8..11.
            var config = new byte[] { 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            CompiledProgram BuildCooldown(CompiledHash hash) => new CompiledProgram(
                new CompiledProgramHeader(1, 1, new CompiledCompilerVersion(1, 0, 0, 0), Hash('a'), Hash('b'), Hash('c'), 1,
                    hash, 0, 2, 1, 0, 2, 16, 16, 8, 0, true), nodes, new uint[] { 1 },
                System.Array.Empty<uint>(), System.Array.Empty<uint>(), System.Array.Empty<CompiledBlackboardSlotRecord>(),
                System.Array.Empty<CompiledObserverRecord>(), System.Array.Empty<uint>(), config, System.Array.Empty<byte>(), debug);
            return BuildCooldown(CompiledProgramContentHashV1.Compute(BuildCooldown(Hash('d'))));
        }
    }
}
