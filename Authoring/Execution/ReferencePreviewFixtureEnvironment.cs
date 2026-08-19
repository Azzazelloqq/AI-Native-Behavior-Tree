namespace AIBT.Authoring
{
    /// <summary>
    /// The fixed, already-shipped Phase 1 fixture/built-in node-behavior set that
    /// <see cref="ReferencePreviewDriver"/> compiles and executes against. AIBT ships no
    /// production per-project leaf-behavior registration mechanism yet (every real leaf handler in
    /// the repository today is a test fixture -- see <c>ReferenceLeafRegistry.CreatePhase1Fixtures</c>
    /// and <c>ReferenceFixtureNodeManifests</c> in <c>AIBT.Runtime</c>/<c>AIBT.Authoring</c>), so this
    /// is deliberately the same set the headless behavior-case runner already exercises: built-in
    /// composites/decorators plus the <c>aibt.test.success</c>/<c>aibt.test.failure</c>/
    /// <c>aibt.test.running</c> constant leaves. Extending preview to arbitrary project-authored leaf
    /// behavior needs its own accepted decision (see the P3-009 evidence's known limitations).
    /// </summary>
    internal static class ReferencePreviewFixtureEnvironment
    {
        internal static NodeRegistry CreateNodeRegistry()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build();
            return result.Registry;
        }

        internal static ReferenceLeafRegistry CreateLeafRegistry()
            => ReferenceLeafRegistry.CreatePhase1Fixtures();

        internal static ReferenceMemoryCompositeRegistry CreateMemoryCompositeRegistry()
            => ReferenceMemoryCompositeRegistry.CreatePhase1BuiltIns();

        internal static ReferenceReactiveCompositeRegistry CreateReactiveCompositeRegistry()
            => ReferenceReactiveCompositeRegistry.CreatePhase1BuiltIns();

        internal static ReferenceDecoratorRegistry CreateDecoratorRegistry()
            => ReferenceDecoratorRegistry.CreatePhase1BuiltIns();

        internal static ReferenceParallelRegistry CreateParallelRegistry()
            => ReferenceParallelRegistry.CreatePhase1BuiltIns();

        internal static ReferenceObserverConditionRegistry CreateObserverRegistry()
            => ReferenceObserverConditionRegistry.Empty;
    }
}
