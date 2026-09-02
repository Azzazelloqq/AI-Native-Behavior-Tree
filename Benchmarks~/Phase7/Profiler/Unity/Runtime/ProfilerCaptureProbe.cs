using System;
using AIBT.Authoring.Benchmarking;
using AIBT.Runtime.Scheduling;
using Unity.Collections;
using UnityEngine;

namespace AIBT.Benchmarks.Phase7.Profiler
{
    /// <summary>
    /// P7-003: keeps a real, compiled `deep-sequence-selector-traversal` scenario (`P4-001`'s own
    /// catalog, via `SchedulingScenarios`/`SchedulingPolicyDriver`, copied in unchanged like every
    /// other Phase 4/7 isolated Player harness) running continuously under the `Immediate` policy
    /// for a fixed wall-clock duration, so a live-connected Unity Profiler has sustained,
    /// repeated `AIBT.Native.*` marker activity to capture -- unlike `P4-008`'s own probe, this one
    /// deliberately never exits after one sweep.
    /// </summary>
    internal sealed class ProfilerCaptureProbe : MonoBehaviour
    {
        private const string SuccessMarker = "AIBT_P7_003_PROFILER_PROBE_OK|";
        private const string FailureMarker = "AIBT_P7_003_PROFILER_PROBE_FAIL|";
        private const int AgentCount = 64;
        private const float RunSeconds = 150f;

        private SchedulingScenarios.CompiledScenario _compiled;
        private ulong _frameCount;
        private float _elapsed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var host = new GameObject("AIBT.P7003.ProfilerCaptureProbe");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<ProfilerCaptureProbe>();
        }

        private void Awake()
        {
            try
            {
                var definition = FindScenario("deep-sequence-selector-traversal");
                _compiled = definition.Build();
                Debug.Log(SuccessMarker + "scenario=" + definition.Name + " agents=" + AgentCount);
            }
            catch (Exception exception)
            {
                Debug.LogError(FailureMarker + exception);
                Application.Quit(1);
            }
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            try
            {
                // A fresh agent set per frame, exactly like the proven `RunOneSample` pattern in
                // `WindowsPlatformSchedulingProbe` (P4-008) -- a persistent agent set re-driven
                // across frames with an incrementing updateId was tried first and hit a real
                // `NativeLifetimeStateInvalid` failure on the second frame; not worth debugging
                // further for a verification-only Profiler-capture harness.
                if (!SchedulingPolicyDriver.TryCreateAgents(
                        _compiled.Program, _compiled.NodeKinds, AgentCount, Allocator.Persistent,
                        out var agents, out var createFailure))
                    throw new InvalidOperationException("Agent creation failed: " + createFailure.Code);
                try
                {
                    if (!SchedulingPolicyDriver.TryRunImmediate(
                            agents, 1, _compiled.LeafStatusByRuntimeIndex, out _, out var runFailure))
                        throw new InvalidOperationException("TryRunImmediate failed: " + runFailure.Code);
                }
                finally
                {
                    foreach (var agent in agents) agent.Dispose();
                }
                _frameCount++;
            }
            catch (Exception exception)
            {
                Debug.LogError(FailureMarker + exception);
                Application.Quit(1);
                return;
            }

            if (_elapsed >= RunSeconds)
            {
                Debug.Log(SuccessMarker + "completed frames=" + _frameCount);
                Application.Quit(0);
            }
        }

        private static SchedulingScenarios.ScenarioDefinition FindScenario(string name)
        {
            foreach (var definition in SchedulingScenarios.Catalog)
                if (definition.Name == name && definition.Implemented)
                    return definition;
            throw new InvalidOperationException("Scenario not found or not implemented: " + name);
        }
    }
}
