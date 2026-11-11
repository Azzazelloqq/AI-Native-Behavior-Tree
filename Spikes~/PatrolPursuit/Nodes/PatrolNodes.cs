using System;
using AIBT.Burst;

namespace AIBT.PatrolResearch
{
    [AibtCatalogShard("research.patrol", 1u)]
    public partial struct PatrolShard { }

    public partial struct EmptyMemory { }

    public partial struct FactConfig
    {
        [AibtConfigField("fact", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("fact", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardReadHandle<bool> Fact;
    }

    [AibtNodeDocumentation("Succeeds when the bound fact is true.", "Research/Perception", "Gate a branch using a sampled or tree-owned fact.", "Does not inspect Unity objects.", "fact")]
    [AibtBurstNode("research.fact", 1u, BurstNodeKind.Condition, typeof(FactConfig), typeof(EmptyMemory), NodeMemoryLifetime.Activation, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct FactNode
    {
        public static void Enter(in FactConfig c, ref EmptyMemory m, ref BurstEnterContext x) { }
        public static NodeStatus Tick(in FactConfig c, ref EmptyMemory m, ref BurstTickContext x)
            => PatrolShard.BurstAccess.TryRead(ref x, c.Fact, out var value) == BurstContextResult.Success && value ? NodeStatus.Success : NodeStatus.Failure;
        public static void Abort(in FactConfig c, ref EmptyMemory m, ref BurstAbortContext x, BurstNodeAbortReason reason) { }
        public static void Exit(in FactConfig c, ref EmptyMemory m, ref BurstExitContext x, BurstNodeExitReason reason) { }
    }

    public partial struct FlagConfig
    {
        [AibtConfigField("value", "Bool", 1u)] public bool Value;
        [AibtConfigField("flag", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("flag", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Bool", 1u)]
        public BlackboardWriteHandle<bool> Flag;
    }

    [AibtNodeDocumentation("Writes a tree-owned flag.", "Research/State", "Clear the return requirement after reaching the patrol route.", "Not an external perception input.", "flag")]
    [AibtBurstNode("research.set-flag", 1u, BurstNodeKind.Action, typeof(FlagConfig), typeof(EmptyMemory), NodeMemoryLifetime.Activation, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct FlagNode
    {
        public static void Enter(in FlagConfig c, ref EmptyMemory m, ref BurstEnterContext x) { }
        public static NodeStatus Tick(in FlagConfig c, ref EmptyMemory m, ref BurstTickContext x)
            => PatrolShard.BurstAccess.TryWrite(ref x, c.Flag, in c.Value) == BurstContextResult.Success ? NodeStatus.Success : NodeStatus.Failure;
        public static void Abort(in FlagConfig c, ref EmptyMemory m, ref BurstAbortContext x, BurstNodeAbortReason reason) { }
        public static void Exit(in FlagConfig c, ref EmptyMemory m, ref BurstExitContext x, BurstNodeExitReason reason) { }
    }

    public partial struct MotionConfig
    {
        [AibtConfigField("continuous", "Bool", 1u)] public bool Continuous;
        [AibtConfigField("speed", "Float32", 1u)] public float Speed;
        [AibtConfigField("radius", "Float32", 1u)] public float Radius;
        [AibtConfigField("mode", "UInt32", 1u)] public uint Mode;
        [AibtConfigField("offset", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("offset", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)] public BlackboardReadHandle<Float3Value> Offset;
        [AibtConfigField("dt", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("dt", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)] public BlackboardReadHandle<float> DeltaTime;
        [AibtConfigField("move", "GeneratedHandle", 1u)] [AibtCommandBinding("move", "Float3", 1u)] public CommandHandle<Float3Value> Move;
        [AibtConfigField("mode-event", "GeneratedHandle", 1u)] [AibtCommandBinding("mode-event", "Int32", 1u)] public CommandHandle<int> ModeEvent;
        [AibtConfigField("arrived", "GeneratedHandle", 1u)] [AibtCommandBinding("arrived", "Int32", 1u)] public CommandHandle<int> Arrived;
    }

    internal static class Motion
    {
        internal static float Distance(Float3Value offset) => (float)Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z);
        internal static Float3Value Step(Float3Value offset, float speed, float dt, float radius)
        {
            var distance = Distance(offset);
            var scale = distance > radius ? Math.Min(speed * dt, distance - radius) / distance : 0f;
            return new Float3Value(offset.X * scale, offset.Y * scale, offset.Z * scale);
        }

    }

    [AibtNodeDocumentation("Moves to a supplied destination and succeeds at its radius.", "Research/Movement", "Return to the suspended patrol waypoint.", "Continuous mode emits arrival and keeps Running; one-shot mode succeeds on arrival.", "move-to")]
    [AibtBurstNode("research.move", 1u, BurstNodeKind.Action, typeof(MotionConfig), typeof(EmptyMemory), NodeMemoryLifetime.Activation, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Low, BurstNodeStatusMask.Running | BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct MoveNode
    {
        public static void Enter(in MotionConfig c, ref EmptyMemory m, ref BurstEnterContext x) { }
        public static NodeStatus Tick(in MotionConfig c, ref EmptyMemory m, ref BurstTickContext x)
        {
            if (c.Speed <= 0f || c.Radius < 0f || c.Mode > int.MaxValue
                || PatrolShard.BurstAccess.TryRead(ref x, c.Offset, out var offset) != BurstContextResult.Success
                || PatrolShard.BurstAccess.TryRead(ref x, c.DeltaTime, out var dt) != BurstContextResult.Success || dt < 0f) return NodeStatus.Failure;
            var mode = (int)c.Mode;
            if (PatrolShard.BurstAccess.TryEmit(ref x, c.ModeEvent, in mode) != BurstContextResult.Success) return NodeStatus.Failure;
            if (Motion.Distance(offset) <= c.Radius + 0.001f)
            {
                if (!c.Continuous) return NodeStatus.Success;
                var arrived = 1;
                return PatrolShard.BurstAccess.TryEmit(ref x, c.Arrived, in arrived) == BurstContextResult.Success ? NodeStatus.Running : NodeStatus.Failure;
            }
            var delta = Motion.Step(offset, c.Speed, dt, c.Radius);
            return PatrolShard.BurstAccess.TryEmit(ref x, c.Move, in delta) == BurstContextResult.Success ? NodeStatus.Running : NodeStatus.Failure;
        }
        public static void Abort(in MotionConfig c, ref EmptyMemory m, ref BurstAbortContext x, BurstNodeAbortReason reason) { }
        public static void Exit(in MotionConfig c, ref EmptyMemory m, ref BurstExitContext x, BurstNodeExitReason reason) { }
    }

    public partial struct ChaseMemory { [AibtMemoryField("target", "Int32", 1u)] public int Target; }
    public partial struct ChaseConfig
    {
        [AibtConfigField("speed", "Float32", 1u)] public float Speed;
        [AibtConfigField("radius", "Float32", 1u)] public float Radius;
        [AibtConfigField("lose-distance", "Float32", 1u)] public float LoseDistance;
        [AibtConfigField("candidate", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("candidate", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Int32", 1u)] public BlackboardReadHandle<int> Candidate;
        [AibtConfigField("target-a", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("target-a", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)] public BlackboardReadHandle<Float3Value> TargetA;
        [AibtConfigField("target-b", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("target-b", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)] public BlackboardReadHandle<Float3Value> TargetB;
        [AibtConfigField("dt", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("dt", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float32", 1u)] public BlackboardReadHandle<float> DeltaTime;
        [AibtConfigField("return-required", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("return-required", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Bool", 1u)] public BlackboardWriteHandle<bool> ReturnRequired;
        [AibtConfigField("move", "GeneratedHandle", 1u)] [AibtCommandBinding("move", "Float3", 1u)] public CommandHandle<Float3Value> Move;
        [AibtConfigField("mode-event", "GeneratedHandle", 1u)] [AibtCommandBinding("mode-event", "Int32", 1u)] public CommandHandle<int> ModeEvent;
        [AibtConfigField("target-event", "GeneratedHandle", 1u)] [AibtCommandBinding("target-event", "Int32", 1u)] public CommandHandle<int> TargetEvent;
    }

    [AibtNodeDocumentation("Locks a detected target across reactive reactivations until outdistanced.", "Research/Pursuit", "Continue pursuit outside the sight cone until the larger loss distance is exceeded.", "No combat or pathfinding. Instance memory is intentional: reactive parents abort on every update.", "chase")]
    [AibtBurstNode("research.chase", 1u, BurstNodeKind.Action, typeof(ChaseConfig), typeof(ChaseMemory), NodeMemoryLifetime.Instance, true,
        BurstCancellationMode.AbortOnly, BurstNodeCost.Low, BurstNodeStatusMask.Running | BurstNodeStatusMask.Failure)]
    public partial struct ChaseNode
    {
        public static void Enter(in ChaseConfig c, ref ChaseMemory m, ref BurstEnterContext x) { }
        public static NodeStatus Tick(in ChaseConfig c, ref ChaseMemory m, ref BurstTickContext x)
        {
            if (m.Target == 0 && PatrolShard.BurstAccess.TryRead(ref x, c.Candidate, out m.Target) != BurstContextResult.Success) return NodeStatus.Failure;
            if (m.Target == 0) return NodeStatus.Failure;
            if (m.Target != 1 && m.Target != 2 || c.Speed <= 0f || c.Radius < 0f || c.LoseDistance <= c.Radius) return NodeStatus.Failure;
            Float3Value offset;
            var read = m.Target == 1 ? PatrolShard.BurstAccess.TryRead(ref x, c.TargetA, out offset)
                : PatrolShard.BurstAccess.TryRead(ref x, c.TargetB, out offset);
            if (read != BurstContextResult.Success
                || PatrolShard.BurstAccess.TryRead(ref x, c.DeltaTime, out var dt) != BurstContextResult.Success || dt < 0f) return NodeStatus.Failure;
            var required = true;
            if (PatrolShard.BurstAccess.TryWrite(ref x, c.ReturnRequired, in required) != BurstContextResult.Success) return NodeStatus.Failure;
            if (Motion.Distance(offset) > c.LoseDistance) { m.Target = 0; return NodeStatus.Failure; }
            var mode = 2;
            var delta = Motion.Step(offset, c.Speed, dt, c.Radius);
            if (PatrolShard.BurstAccess.TryEmit(ref x, c.TargetEvent, in m.Target) != BurstContextResult.Success
                || PatrolShard.BurstAccess.TryEmit(ref x, c.ModeEvent, in mode) != BurstContextResult.Success
                || PatrolShard.BurstAccess.TryEmit(ref x, c.Move, in delta) != BurstContextResult.Success) return NodeStatus.Failure;
            return NodeStatus.Running;
        }
        public static void Abort(in ChaseConfig c, ref ChaseMemory m, ref BurstAbortContext x, BurstNodeAbortReason reason) { }
        public static void Exit(in ChaseConfig c, ref ChaseMemory m, ref BurstExitContext x, BurstNodeExitReason reason) { }
    }
}
