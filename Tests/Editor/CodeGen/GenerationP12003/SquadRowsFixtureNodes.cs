using AIBT.Burst;
using AIBT.Native;

// P12-003: nodes for the "game data that is not per agent" recipes, in their own assembly (a node assembly
// declares exactly one shard), consumed by SquadRowsCatalog in AIBT.Integration.Tests.
//
// compare-squads reads one row type through two input channels, "own" and "target". The game binds both
// channels to one rows array (one row per squad) with two index arrays: the agent's own squad and the squad
// the game assigned as its target. It gives one aim command from both rows.
//
// read-own-twice and read-own-thrice read the "own" input two and three times in one callback. The catalog
// sizes one callback's value sessions for its largest node (one session per input or command binding):
// compare-squads needs three (two inputs, one command), so a second read of "own" next to one command fits,
// and a third one does not (CapacityExceeded, the agent faults). Whether a second read fits depends on the
// other nodes of the catalog, so the rule for authors stays: read an input once per callback.
namespace AIBT.Tests.CodeGen.GenerationP12003
{
    [AibtCatalogShard("aibt.tests.p12003.squad-rows", 1u)]
    public partial struct SquadRowsShard { }

    /// <summary>One squad's facts, one row per squad (8 bytes).</summary>
    [AibtBurstValue("aibt.tests.p12003.squad", 1u, "aibt.tests.p12003.squad.schema")]
    public partial struct SquadRow
    {
        [AibtValueField("strength", "Float32", 1u)] public float Strength;
        [AibtValueField("members", "Int32", 1u)] public int Members;
    }

    /// <summary>The command: the two rows the node read (members of each) and the strength difference.</summary>
    [AibtBurstValue("aibt.tests.p12003.aim", 1u, "aibt.tests.p12003.aim.schema")]
    public partial struct AimCommand
    {
        [AibtValueField("own", "Int32", 1u)] public int Own;
        [AibtValueField("target", "Int32", 1u)] public int Target;
        [AibtValueField("edge", "Float32", 1u)] public float Edge;
    }

    public partial struct SquadMemory { }

    public partial struct CompareSquadsConfig
    {
        [AibtInput("own")] public SnapshotReadHandle<SquadRow> Own;
        [AibtInput("target")] public SnapshotReadHandle<SquadRow> Target;
        [AibtCommand("aim")] public CommandHandle<AimCommand> Aim;
    }

    [AibtNodeDocumentation("Reads its own squad's row and its target squad's row, and aims.", "Tests/Simulation",
        "Use for the P12-003 two-channel tests.", "Not a gameplay node.", "p12003-compare-squads")]
    [AibtAction("aibt.tests.p12003.compare-squads", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct CompareSquadsNode
    {
        public static NodeStatus Tick(in CompareSquadsConfig config, ref SquadMemory memory, ref BurstTickContext context)
        {
            if (SquadRowsShard.BurstAccess.TryReadSnapshot(ref context, config.Own, out var own) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (SquadRowsShard.BurstAccess.TryReadSnapshot(ref context, config.Target, out var target) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var aim = new AimCommand { Own = own.Members, Target = target.Members, Edge = own.Strength - target.Strength };
            return SquadRowsShard.BurstAccess.TryEmit(ref context, config.Aim, in aim) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct ReadOwnConfig
    {
        [AibtInput("own")] public SnapshotReadHandle<SquadRow> Own;
        [AibtCommand("aim")] public CommandHandle<AimCommand> Aim;
    }

    /// <summary>
    /// Reads "own" twice in one callback and aims with both values (Own from the first read, Target from the
    /// second, Edge 1 when they agree). Not a pattern to copy: read an input once into a local.
    /// </summary>
    [AibtNodeDocumentation("Reads its own squad's row twice in one callback.", "Tests/Simulation",
        "Pin what a second read of one input does.", "Not production: read an input once into a local.", "p12003-read-own-twice")]
    [AibtAction("aibt.tests.p12003.read-own-twice", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ReadOwnTwiceNode
    {
        public static NodeStatus Tick(in ReadOwnConfig config, ref SquadMemory memory, ref BurstTickContext context)
        {
            if (SquadRowsShard.BurstAccess.TryReadSnapshot(ref context, config.Own, out var first) != BurstContextResult.Success)
                return NodeStatus.Failure;
            if (SquadRowsShard.BurstAccess.TryReadSnapshot(ref context, config.Own, out var second) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var agree = first.Members == second.Members && first.Strength == second.Strength;
            var aim = new AimCommand { Own = first.Members, Target = second.Members, Edge = agree ? 1f : 0f };
            return SquadRowsShard.BurstAccess.TryEmit(ref context, config.Aim, in aim) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    /// <summary>
    /// Aims first, then reads "own" three times in one callback. The aim takes one value session and each read
    /// another, so the third read asks for a fourth session where the catalog has three, and is the refused call.
    /// </summary>
    [AibtNodeDocumentation("Reads its own squad's row three times in one callback.", "Tests/Simulation",
        "Pin the refusal of a read beyond the catalog's value sessions.", "Not production: read an input once into a local.",
        "p12003-read-own-thrice")]
    [AibtAction("aibt.tests.p12003.read-own-thrice", Statuses = BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ReadOwnThriceNode
    {
        public static NodeStatus Tick(in ReadOwnConfig config, ref SquadMemory memory, ref BurstTickContext context)
        {
            var aim = new AimCommand { Own = -1, Target = -1, Edge = 0f };
            if (SquadRowsShard.BurstAccess.TryEmit(ref context, config.Aim, in aim) != BurstContextResult.Success)
                return NodeStatus.Failure;
            for (var read = 0; read < 3; read++)
                if (SquadRowsShard.BurstAccess.TryReadSnapshot(ref context, config.Own, out _) != BurstContextResult.Success)
                    return NodeStatus.Failure;
            return NodeStatus.Success;
        }
    }
}
