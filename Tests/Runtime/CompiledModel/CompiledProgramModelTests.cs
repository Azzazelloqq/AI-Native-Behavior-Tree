using System;
using NUnit.Framework;

namespace AIBT.Tests.Runtime
{
    public sealed class CompiledProgramModelTests
    {
        [Test]
        public void Constructor_DefensivelyCopiesEveryInput()
        {
            var fixture = Fixture.Create();
            var program = fixture.Build();

            fixture.Nodes[0] = default;
            fixture.ConfigBlob[0] = 99;
            fixture.DebugMap[0] = default;

            Assert.That(program.Nodes[0].NodeTypeId, Is.EqualTo(7));
            Assert.That(program.Nodes[0].MemoryLifetime, Is.EqualTo(NodeMemoryLifetime.Activation));
            Assert.That(program.ConfigBlob[0], Is.EqualTo(11));
            Assert.That(program.DebugMap[0].AuthoringNodeId, Is.EqualTo(new NodeId("root")));
            Assert.That(program.Nodes, Is.Not.AssignableTo<CompiledNodeRecord[]>());
            Assert.That(program.ConfigBlob, Is.Not.AssignableTo<byte[]>());
        }

        [Test]
        public void CompiledRange_RejectsOverflowAndNamedSentinel()
        {
            Assert.That(CompiledIndex.Invalid, Is.EqualTo(uint.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompiledRange(CompiledIndex.Invalid, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompiledRange(uint.MaxValue - 1, 2));
        }

        [Test]
        public void Header_RejectsInvalidRootAndAlignment()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Header(root: 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Header(maximumAlignment: 3));
        }

        [Test]
        public void NodeRecord_RejectsMisalignedAndOverflowingStorage()
        {
            Assert.Throws<ArgumentException>(() => Fixture.Node(configOffset: 2, configSize: 4, configAlignment: 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Node(configAlignment: 3));
            Assert.Throws<ArgumentException>(() => Fixture.Node(memoryOffset: 2, memorySize: 4, memoryAlignment: 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Node(
                configOffset: uint.MaxValue - 1,
                configSize: 2,
                configAlignment: 1));
        }

        [Test]
        public void NodeRecord_RequiresCanonicalEmptyConfigurationEnvelope()
        {
            Assert.Throws<ArgumentException>(() => Fixture.Node(configOffset: 4, configSize: 0, configAlignment: 1));
            Assert.Throws<ArgumentException>(() => Fixture.Node(configOffset: 0, configSize: 0, configAlignment: 4));

            Assert.DoesNotThrow(() => Fixture.Node(configOffset: 0, configSize: 0, configAlignment: 1));
        }

        [Test]
        public void NodeRecord_RejectsUnknownMemoryLifetime()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Fixture.Node(
                memoryLifetime: (NodeMemoryLifetime)byte.MaxValue));
        }

        [Test]
        public void NodeRecord_PreservesInstanceMemoryLifetime()
        {
            Assert.That((byte)NodeMemoryLifetime.Activation, Is.Zero);
            Assert.That((byte)NodeMemoryLifetime.Instance, Is.EqualTo(1));
            Assert.That(
                Fixture.Node(memoryLifetime: NodeMemoryLifetime.Instance).MemoryLifetime,
                Is.EqualTo(NodeMemoryLifetime.Instance));
        }

        [Test]
        public void BlackboardSlot_RequiresEnumContractOnlyForEnum32()
        {
            var enumContractId = StableHash.Fnv1A64("game.state");
            var enumSlot = new CompiledBlackboardSlotRecord(
                10,
                BuiltInBlackboardTypes.Enum32.TypeId,
                BuiltInBlackboardTypes.Enum32.Version,
                enumContractId,
                BlackboardScope.Tree,
                0, 16, 8, 0,
                CompiledBlackboardAccessFlags.Read);

            Assert.That(enumSlot.EnumContractId, Is.EqualTo(enumContractId));
            Assert.Throws<ArgumentException>(() => new CompiledBlackboardSlotRecord(
                10,
                BuiltInBlackboardTypes.Enum32.TypeId,
                BuiltInBlackboardTypes.Enum32.Version,
                0,
                BlackboardScope.Tree,
                0, 16, 8, 0,
                CompiledBlackboardAccessFlags.Read));
            Assert.Throws<ArgumentException>(() => new CompiledBlackboardSlotRecord(
                10,
                BuiltInBlackboardTypes.Int32.TypeId,
                BuiltInBlackboardTypes.Int32.Version,
                enumContractId,
                BlackboardScope.Tree,
                0, 4, 4, 0,
                CompiledBlackboardAccessFlags.Read));
        }

        [Test]
        public void Constructor_RejectsMismatchedHeaderCounts()
        {
            var fixture = Fixture.Create();
            fixture.HeaderValue = Fixture.Header(nodeCount: 2);

            Assert.Throws<ArgumentException>(() => fixture.Build());
        }

        [Test]
        public void Constructor_RejectsOutOfBoundsChildAndAccessRanges()
        {
            var childFixture = Fixture.Create();
            childFixture.Nodes[0] = Fixture.Node(children: new CompiledRange(0, 1));
            Assert.Throws<ArgumentException>(() => childFixture.Build());

            var accessFixture = Fixture.Create();
            accessFixture.Nodes[0] = Fixture.Node(readSlots: new CompiledRange(0, 1));
            Assert.Throws<ArgumentException>(() => accessFixture.Build());
        }

        [Test]
        public void Constructor_RejectsOverlappingNodeConfigAndMemoryRanges()
        {
            var configFixture = Fixture.CreateTwoNodes();
            configFixture.Nodes[1] = Fixture.Node(
                configOffset: 2,
                configSize: 4,
                configAlignment: 2,
                memoryOffset: 4,
                debugIdentityIndex: 1);
            Assert.Throws<ArgumentException>(() => configFixture.Build());

            var memoryFixture = Fixture.CreateTwoNodes();
            memoryFixture.HeaderValue = Fixture.Header(nodeCount: 2, debugCount: 2, configSize: 8, memorySize: 12);
            memoryFixture.Nodes[0] = Fixture.Node(memorySize: 8);
            memoryFixture.Nodes[1] = Fixture.Node(configOffset: 4, memoryOffset: 4, debugIdentityIndex: 1);
            Assert.Throws<ArgumentException>(() => memoryFixture.Build());
        }

        [Test]
        public void Constructor_RejectsOverlappingBlackboardMemoryRanges()
        {
            var fixture = Fixture.Create();
            fixture.HeaderValue = Fixture.Header(slotCount: 2, configSize: 4, debugCount: 1);
            fixture.Slots = new[]
            {
                Fixture.Slot(stableKeyId: 10, offset: 0, defaultOffset: 0),
                Fixture.Slot(stableKeyId: 11, offset: 0, defaultOffset: 4),
            };
            fixture.DefaultBlob = new byte[8];

            Assert.Throws<ArgumentException>(() => fixture.Build());
        }

        [Test]
        public void Constructor_RejectsInvalidDebugAndObserverReferences()
        {
            var debugFixture = Fixture.Create();
            debugFixture.DebugMap[0] = new CompiledDebugMapEntry(1, new NodeId("root"), "/nodes/root");
            Assert.Throws<ArgumentException>(() => debugFixture.Build());

            var observerFixture = Fixture.CreateTwoNodes();
            observerFixture.Slots = new[]
            {
                Fixture.Slot(10, 0, 0, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Observed),
            };
            observerFixture.HeaderValue = Fixture.Header(nodeCount: 2, childCount: 0, slotCount: 1, debugCount: 2, configSize: 8, memorySize: 8);
            observerFixture.DefaultBlob = new byte[4];
            observerFixture.Observers = new[]
            {
                new CompiledObserverRecord(0, 2, CompiledObserverMode.Self, new CompiledRange(0, 1)),
            };
            observerFixture.WatchedSlots = new uint[] { 0 };

            Assert.Throws<ArgumentException>(() => observerFixture.Build());
        }

        [Test]
        public void Constructor_AcceptsObservedSlotAndStrippedDebugIdentity()
        {
            var fixture = Fixture.CreateTwoNodes();
            fixture.Nodes[0] = Fixture.Node(debugIdentityIndex: CompiledIndex.Invalid);
            fixture.Nodes[1] = Fixture.Node(configOffset: 4, memoryOffset: 4, debugIdentityIndex: CompiledIndex.Invalid);
            fixture.HeaderValue = Fixture.Header(nodeCount: 2, slotCount: 1, debugCount: 0, configSize: 8, memorySize: 8);
            fixture.DebugMap = Array.Empty<CompiledDebugMapEntry>();
            fixture.Slots = new[]
            {
                Fixture.Slot(10, 0, 0, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Observed),
            };
            fixture.DefaultBlob = new byte[4];
            fixture.Observers = new[]
            {
                new CompiledObserverRecord(1, 0, CompiledObserverMode.Self, new CompiledRange(0, 1)),
            };
            fixture.WatchedSlots = new uint[] { 0 };

            Assert.That(fixture.Build().Observers, Has.Count.EqualTo(1));
        }

        [Test]
        public void Constructor_RejectsObserverWatchedSlotsOutsideStableKeyIdOrder()
        {
            var fixture = Fixture.CreateTwoNodes();
            fixture.HeaderValue = Fixture.Header(nodeCount: 2, slotCount: 2, debugCount: 2, configSize: 8, memorySize: 8);
            fixture.Slots = new[]
            {
                Fixture.Slot(20, 0, 0, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Observed),
                Fixture.Slot(10, 4, 4, CompiledBlackboardAccessFlags.Read | CompiledBlackboardAccessFlags.Observed),
            };
            fixture.DefaultBlob = new byte[8];
            fixture.Observers = new[]
            {
                new CompiledObserverRecord(1, 0, CompiledObserverMode.Self, new CompiledRange(0, 2)),
            };
            fixture.WatchedSlots = new uint[] { 0, 1 };

            Assert.Throws<ArgumentException>(() => fixture.Build());
        }

        private sealed class Fixture
        {
            public CompiledProgramHeader HeaderValue;
            public CompiledNodeRecord[] Nodes;
            public uint[] ChildIndices;
            public uint[] ReadSlotIndices;
            public uint[] WriteSlotIndices;
            public CompiledBlackboardSlotRecord[] Slots;
            public CompiledObserverRecord[] Observers;
            public uint[] WatchedSlots;
            public byte[] ConfigBlob;
            public byte[] DefaultBlob;
            public CompiledDebugMapEntry[] DebugMap;

            public static Fixture Create()
            {
                return new Fixture
                {
                    HeaderValue = Header(),
                    Nodes = new[] { Node() },
                    ChildIndices = Array.Empty<uint>(),
                    ReadSlotIndices = Array.Empty<uint>(),
                    WriteSlotIndices = Array.Empty<uint>(),
                    Slots = Array.Empty<CompiledBlackboardSlotRecord>(),
                    Observers = Array.Empty<CompiledObserverRecord>(),
                    WatchedSlots = Array.Empty<uint>(),
                    ConfigBlob = new byte[] { 11, 12, 13, 14 },
                    DefaultBlob = Array.Empty<byte>(),
                    DebugMap = new[] { new CompiledDebugMapEntry(0, new NodeId("root"), "/nodes/root", "Root") },
                };
            }

            public static Fixture CreateTwoNodes()
            {
                var result = Create();
                result.HeaderValue = Header(nodeCount: 2, debugCount: 2, configSize: 8, memorySize: 8);
                result.Nodes = new[]
                {
                    Node(),
                    Node(configOffset: 4, memoryOffset: 4, debugIdentityIndex: 1),
                };
                result.ConfigBlob = new byte[8];
                result.DebugMap = new[]
                {
                    new CompiledDebugMapEntry(0, new NodeId("root"), "/nodes/root"),
                    new CompiledDebugMapEntry(1, new NodeId("leaf"), "/nodes/leaf"),
                };
                return result;
            }

            public CompiledProgram Build()
            {
                return new CompiledProgram(
                    HeaderValue,
                    Nodes,
                    ChildIndices,
                    ReadSlotIndices,
                    WriteSlotIndices,
                    Slots,
                    Observers,
                    WatchedSlots,
                    ConfigBlob,
                    DefaultBlob,
                    DebugMap);
            }

            public static CompiledProgramHeader Header(
                uint root = 0,
                uint nodeCount = 1,
                uint childCount = 0,
                uint slotCount = 0,
                uint debugCount = 1,
                uint configSize = 4,
                uint memorySize = 4,
                uint maximumAlignment = 4)
            {
                return new CompiledProgramHeader(
                    1,
                    1,
                    new CompiledCompilerVersion(1, 0, 0, 0),
                    Hash('a'),
                    Hash('b'),
                    Hash('c'),
                    1,
                    Hash('d'),
                    root,
                    nodeCount,
                    childCount,
                    slotCount,
                    debugCount,
                    configSize,
                    memorySize,
                    maximumAlignment,
                    0,
                    true);
            }

            public static CompiledNodeRecord Node(
                uint configOffset = 0,
                uint configSize = 4,
                uint configAlignment = 4,
                uint memoryOffset = 0,
                uint memorySize = 4,
                uint memoryAlignment = 4,
                NodeMemoryLifetime memoryLifetime = NodeMemoryLifetime.Activation,
                CompiledRange children = default,
                CompiledRange readSlots = default,
                CompiledRange writeSlots = default,
                uint debugIdentityIndex = 0)
            {
                return new CompiledNodeRecord(
                    7,
                    1,
                    configOffset,
                    configSize,
                    configAlignment,
                    memoryOffset,
                    memorySize,
                    memoryAlignment,
                    memoryLifetime,
                    children,
                    CompiledNodeFlags.BurstDomain | CompiledNodeFlags.SupportsTracing,
                    debugIdentityIndex,
                    readSlots,
                    writeSlots);
            }

            public static CompiledBlackboardSlotRecord Slot(
                ulong stableKeyId,
                uint offset,
                uint defaultOffset,
                CompiledBlackboardAccessFlags flags = CompiledBlackboardAccessFlags.Read)
            {
                return new CompiledBlackboardSlotRecord(
                    stableKeyId,
                    20,
                    1,
                    0,
                    BlackboardScope.Tree,
                    offset,
                    4,
                    4,
                    defaultOffset,
                    flags);
            }

            private static CompiledHash Hash(char character) => new CompiledHash(new string(character, 64));
        }
    }
}
