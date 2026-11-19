using System.Runtime.InteropServices;
using AIBT.Burst;

namespace AIBT.StandardNodes
{
    // P11-002 stage 3: random nodes of the native node library (ADR-P11-002, category Native/Random).
    // Each node declares [AibtRandomStream]: the host (ProductionTreeHost, NativeTreeAgent) or the
    // TreeSimulation container derives its stream from the root seed, the program's semantic hash,
    // the tree instance id and the node's runtime index (time-and-random-v1). The nodes are declared
    // deterministic: one seed, one instance id and one input sequence give one result sequence.
    //
    // Shared contract of the five nodes:
    // - An invalid configuration fails at once, writes nothing and draws nothing (the stream does not
    //   move). A valid one always consumes the same number of draws, whatever the parameter values
    //   (min = max included), so tuning a range does not shift later draws.
    // - "One draw" is one step of the stream for TryNextFloat32 and the unbounded TryNextUInt32; a
    //   bounded TryNextUInt32(n) takes one step and repeats only on rejection (probability
    //   (2^32 mod n) / 2^32 per step, below 1/2), as time-and-random-v1 specifies.
    // - Only arithmetic (+ - * /) and System.Math.Sqrt, both correctly rounded, are used, so a node
    //   gives the same bits with and without Burst; there are no sin/cos/pow calls.

    public partial struct RandomChanceConfig
    {
        [AibtConfigField("chance", "Float32", 1u)]
        public float Chance;
    }

    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Succeeds with probability 'chance' (0 to 1) and fails otherwise; one draw per tick from the node's seeded random stream. A chance outside 0 to 1 or not finite fails without drawing.",
        "Native/Random",
        "Use in a native (Burst) tree for a probabilistic branch, for example 'one in four patrols stops to look around'; the result repeats for the same seed and tree instance.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.random-chance there. Use it instead of aibt.stdlib.random-condition (a whole-percent, non-deterministic built-in that the default Phase1 policy rejects) whenever the tree must pass Phase1 or reproduce its rolls; random-condition only remains for trees validated without the determinism requirement.",
        "native-random-chance")]
    [AibtBurstNode(
        "aibt.native.random-chance", 1u, BurstNodeKind.Condition,
        typeof(RandomChanceConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct RandomChanceNode
    {
        public static NodeStatus Tick(in RandomChanceConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            // 0 <= chance <= 1 is false for NaN, so NaN is refused here too.
            if (!(config.Chance >= 0f && config.Chance <= 1f)) return NodeStatus.Failure;
            // sample is in [0, 1 - 2^-24]: chance 0 never succeeds, chance 1 always does.
            return context.TryNextFloat32(out var sample) == BurstContextResult.Success && sample < config.Chance
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct RandomFloatConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float32", 1u)]
        public BlackboardWriteHandle<float> Key;

        [AibtConfigField("min", "Float32", 1u)]
        public float Min;

        [AibtConfigField("max", "Float32", 1u)]
        public float Max;
    }

    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Writes a uniform random float in [min, max) to a float blackboard key and succeeds; one draw per tick. min = max writes min; min > max or a bound that is not finite fails without writing or drawing.",
        "Native/Random",
        "Use in a native (Burst) tree to vary a number per run, such as a speed, a delay factor or a score threshold, reproducibly for the seed.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.random-float there. For a whole number use aibt.native.random-int; for a random pause use aibt.native.wait-random-microseconds.",
        "native-random-float")]
    [AibtBurstNode(
        "aibt.native.random-float", 1u, BurstNodeKind.Action,
        typeof(RandomFloatConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct RandomFloatNode
    {
        public static NodeStatus Tick(in RandomFloatConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (!StandardNativeMath.IsFinite(config.Min) || !StandardNativeMath.IsFinite(config.Max) || config.Min > config.Max)
                return NodeStatus.Failure;
            if (context.TryNextFloat32(out var sample) != BurstContextResult.Success) return NodeStatus.Failure;
            var value = StandardNativeRandomMath.InHalfOpenRange(config.Min, config.Max, sample);
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct RandomIntConfig
    {
        [AibtConfigField("key", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("key", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Int32", 1u)]
        public BlackboardWriteHandle<int> Key;

        [AibtConfigField("min", "UInt32", 1u)]
        public uint Min;

        [AibtConfigField("max", "UInt32", 1u)]
        public uint Max;
    }

    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Writes a uniform random whole number in [min, max], both ends included, to an Int32 blackboard key and succeeds; one bounded draw per tick. min > max or max above 2147483647 fails without writing or drawing.",
        "Native/Random",
        "Use in a native (Burst) tree to pick one of several options, such as an index into waypoints or a signal code, reproducibly for the seed.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.random-int there. Bounds are 0 to 2147483647; for negative numbers subtract an offset in your own code, and for a fraction use aibt.native.random-float.",
        "native-random-int")]
    [AibtBurstNode(
        "aibt.native.random-int", 1u, BurstNodeKind.Action,
        typeof(RandomIntConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct RandomIntNode
    {
        public static NodeStatus Tick(in RandomIntConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (config.Max > (uint)int.MaxValue || config.Min > config.Max) return NodeStatus.Failure;
            // max - min + 1 <= 2^31, so the bound never wraps to 0 (which the stream refuses).
            if (context.TryNextUInt32(config.Max - config.Min + 1u, out var offset) != BurstContextResult.Success)
                return NodeStatus.Failure;
            var value = (int)(config.Min + offset);
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Key, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    public partial struct WaitRandomMicrosecondsConfig
    {
        [AibtConfigField("min-microseconds", "UInt32", 1u)]
        public uint MinMicroseconds;

        [AibtConfigField("max-microseconds", "UInt32", 1u)]
        public uint MaxMicroseconds;
    }

    public partial struct WaitRandomMicrosecondsMemory
    {
        [AibtMemoryField("started-at", "Int64", 1u)]
        public long StartedAt;

        [AibtMemoryField("duration", "UInt32", 1u)]
        public uint Duration;
    }

    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Picks a uniform random duration in [min, max] microseconds of host time when it starts (one draw), stays running until that much time has passed, then succeeds. min > max fails without drawing; aborting it is safe.",
        "Native/Random",
        "Use in a native (Burst) tree for pauses that should not line up across agents, such as idle or look-around times, reproducibly for the seed.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.wait-random there. For a fixed pause use aibt.native.wait-microseconds; to count ticks rather than host time use aibt.stdlib.wait.",
        "native-wait-random-microseconds")]
    [AibtBurstNode(
        "aibt.native.wait-random-microseconds", 1u, BurstNodeKind.Action,
        typeof(WaitRandomMicrosecondsConfig), typeof(WaitRandomMicrosecondsMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Running | BurstNodeStatusMask.Failure)]
    public partial struct WaitRandomMicrosecondsNode
    {
        public static void Enter(in WaitRandomMicrosecondsConfig config, ref WaitRandomMicrosecondsMemory memory, ref BurstEnterContext context)
        {
            memory.StartedAt = long.MinValue;
            memory.Duration = 0u;
            if (config.MinMicroseconds > config.MaxMicroseconds) return;
            if (context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success) return;
            var span = config.MaxMicroseconds - config.MinMicroseconds;
            uint offset;
            // [0, 2^32 - 1] is the whole UInt32 range: one unbounded draw (the bound 2^32 does not fit).
            var drawn = span == uint.MaxValue
                ? context.TryNextUInt32(out offset)
                : context.TryNextUInt32(span + 1u, out offset);
            if (drawn != BurstContextResult.Success) return;
            memory.StartedAt = now;
            memory.Duration = config.MinMicroseconds + offset;
        }

        public static NodeStatus Tick(in WaitRandomMicrosecondsConfig config, ref WaitRandomMicrosecondsMemory memory, ref BurstTickContext context)
        {
            if (memory.StartedAt == long.MinValue || context.TryGetTimeMicroseconds(out var now) != BurstContextResult.Success)
                return NodeStatus.Failure;
            return StandardNativeTime.HasReached(now, memory.StartedAt, memory.Duration) ? NodeStatus.Success : NodeStatus.Running;
        }

        public static void Abort(in WaitRandomMicrosecondsConfig config, ref WaitRandomMicrosecondsMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }

        public static void Exit(in WaitRandomMicrosecondsConfig config, ref WaitRandomMicrosecondsMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }

    public partial struct RandomFloat3InRadiusConfig
    {
        [AibtConfigField("center", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("center", BurstBlackboardAccess.Read, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardReadHandle<Float3Value> Center;

        [AibtConfigField("result", "GeneratedHandle", 1u)]
        [AibtBlackboardBinding("result", BurstBlackboardAccess.Write, BlackboardScope.Tree, "Float3", 1u)]
        public BlackboardWriteHandle<Float3Value> Result;

        [AibtConfigField("min-radius", "Float32", 1u)]
        public float MinRadius;

        [AibtConfigField("radius", "Float32", 1u)]
        public float Radius;

        [AibtConfigField("flat", "Bool", 1u)]
        public bool Flat;
    }

    [AibtRandomStream]
    [AibtNodeDocumentation(
        "Writes center + a uniform random offset with min-radius <= distance <= radius to a Float3 key and succeeds; flat keeps the centre's height (Y) and spreads on the XZ ring, otherwise in the 3D shell. Two draws per tick when flat, three otherwise. A radius that is negative or not finite, or min-radius > radius, fails without drawing.",
        "Native/Random",
        "Use in a native (Burst) tree to choose a wander or search point around a position (often flat, around the agent's home), reproducibly for the seed.",
        "Native-only: it does not run in a BehaviorTreeAgent; use aibt.unity.random-point there. The point is not checked against the NavMesh or obstacles: validate it in gameplay code. A result that is not finite (a huge centre plus radius) fails without writing, after drawing.",
        "native-random-float3-in-radius")]
    [AibtBurstNode(
        "aibt.native.random-float3-in-radius", 1u, BurstNodeKind.Action,
        typeof(RandomFloat3InRadiusConfig), typeof(StandardNativeNoMemory), NodeMemoryLifetime.Activation,
        true, BurstCancellationMode.AbortOnly, BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct RandomFloat3InRadiusNode
    {
        public static NodeStatus Tick(in RandomFloat3InRadiusConfig config, ref StandardNativeNoMemory memory, ref BurstTickContext context)
        {
            if (!StandardNativeMath.IsFinite(config.MinRadius) || !StandardNativeMath.IsFinite(config.Radius)
                || config.MinRadius < 0f || config.MinRadius > config.Radius)
                return NodeStatus.Failure;
            if (StandardNativeShard.BurstAccess.TryRead(ref context, config.Center, out var center) != BurstContextResult.Success)
                return NodeStatus.Failure;
            // Draw order: distance, then (3D only) height, then angle.
            if (context.TryNextFloat32(out var distanceSample) != BurstContextResult.Success) return NodeStatus.Failure;
            var heightSample = 0f;
            if (!config.Flat && context.TryNextFloat32(out heightSample) != BurstContextResult.Success) return NodeStatus.Failure;
            if (context.TryNextFloat32(out var angleSample) != BurstContextResult.Success) return NodeStatus.Failure;

            StandardNativeRandomMath.CosSinTurn(angleSample, out var cos, out var sin);
            double x, y, z;
            if (config.Flat)
            {
                var r = StandardNativeRandomMath.RingRadius(config.MinRadius, config.Radius, distanceSample);
                x = center.X + r * cos;
                y = center.Y;
                z = center.Z + r * sin;
            }
            else
            {
                var r = StandardNativeRandomMath.ShellRadius(config.MinRadius, config.Radius, distanceSample);
                // Height uniform in [-1, 1) gives a uniform direction on the sphere (Archimedes).
                var h = 2.0 * heightSample - 1.0;
                var around = System.Math.Sqrt(System.Math.Max(0.0, 1.0 - h * h));
                x = center.X + r * around * cos;
                y = center.Y + r * h;
                z = center.Z + r * around * sin;
            }
            var fx = (float)x;
            var fy = (float)y;
            var fz = (float)z;
            if (!StandardNativeMath.IsFinite(fx) || !StandardNativeMath.IsFinite(fy) || !StandardNativeMath.IsFinite(fz))
                return NodeStatus.Failure;
            var value = new Float3Value(fx, fy, fz);
            return StandardNativeShard.BurstAccess.TryWrite(ref context, config.Result, in value) == BurstContextResult.Success
                ? NodeStatus.Success
                : NodeStatus.Failure;
        }
    }

    /// <summary>
    /// Burst-safe helpers of the random nodes: only + - * / and Math.Sqrt (correctly rounded), so the
    /// same inputs give the same bits under Burst and under Mono.
    /// </summary>
    internal static class StandardNativeRandomMath
    {
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public int Int;
        }

        /// <summary>min + sample * (max - min), kept below max when rounding reaches it; min when min = max.</summary>
        internal static float InHalfOpenRange(float min, float max, float sample)
        {
            if (min == max) return min;
            var value = (float)(min + (double)sample * ((double)max - min));
            return value < max ? value : NextDown(max);
        }

        /// <summary>The largest float below a finite <paramref name="value"/>.</summary>
        internal static float NextDown(float value)
        {
            var bits = new FloatBits { Float = value };
            if (value > 0f) bits.Int -= 1;
            else if (value < 0f) bits.Int += 1;
            else return -float.Epsilon;
            return bits.Float;
        }

        /// <summary>Distance with a uniform area density on the ring [min, max] (sample in [0, 1)).</summary>
        internal static double RingRadius(float min, float max, float sample)
        {
            double lo = min, hi = max;
            var squared = lo * lo + sample * (hi * hi - lo * lo);
            return Clamp(System.Math.Sqrt(squared), lo, hi);
        }

        /// <summary>Distance with a uniform volume density in the shell [min, max] (sample in [0, 1)).</summary>
        internal static double ShellRadius(float min, float max, float sample)
        {
            double lo = min, hi = max;
            var target = lo * lo * lo + sample * (hi * hi * hi - lo * lo * lo);
            // Cube root by bisection on [min, max]: 64 halvings reach the precision of a double.
            for (var step = 0; step < 64; step++)
            {
                var mid = 0.5 * (lo + hi);
                if (mid * mid * mid < target) lo = mid;
                else hi = mid;
            }
            return Clamp(0.5 * (lo + hi), min, max);
        }

        /// <summary>cos and sin of <paramref name="turn"/> full turns (turn in [0, 1)), by Taylor series.</summary>
        internal static void CosSinTurn(float turn, out double cos, out double sin)
        {
            // x = 2*pi*turn - pi lies in [-pi, pi); cos(x + pi) = -cos x, sin(x + pi) = -sin x.
            var x = 2.0 * System.Math.PI * turn - System.Math.PI;
            var squared = x * x;
            double sinTerm = x, sinSum = x, cosTerm = 1.0, cosSum = 1.0;
            for (var n = 1; n <= 16; n++)
            {
                sinTerm *= -squared / ((2.0 * n) * (2.0 * n + 1.0));
                cosTerm *= -squared / ((2.0 * n - 1.0) * (2.0 * n));
                sinSum += sinTerm;
                cosSum += cosTerm;
            }
            cos = -cosSum;
            sin = -sinSum;
        }

        private static double Clamp(double value, double min, double max)
            => value < min ? min : value > max ? max : value;
    }
}
