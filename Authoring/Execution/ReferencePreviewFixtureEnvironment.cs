using System.Collections.Generic;

namespace AIBT.Authoring
{
    /// <summary>
    /// Constructs the reference preview's built-in structural and leaf behavior registries.
    /// </summary>
    internal static class ReferencePreviewFixtureEnvironment
    {
        internal static NodeRegistry CreateNodeRegistry()
        {
            var result = NodeRegistryBuilder.CreateWithBuiltIns().AddTestFixtures().Build();
            return result.Registry;
        }

        internal static ReferenceLeafRegistry CreateLeafRegistry()
        {
            var providers = new List<ReferencePreviewLeafRegistration>();
            foreach (var provider in BuiltInLeafManifests.All)
                providers.Add(new ReferencePreviewLeafRegistration(provider.Manifest, provider, provider.CreateBehavior()));
            return CreateLeafRegistry(providers, out _);
        }

        internal static ReferenceLeafRegistry CreateLeafRegistry(
            IEnumerable<ReferencePreviewLeafRegistration> providers,
            out IReadOnlyList<ReferencePreviewLeafFactoryHandler> factories)
        {
            var bindings = new List<ReferenceLeafBinding>();
            var created = new List<ReferencePreviewLeafFactoryHandler>();
            foreach (var provider in providers)
            {
                var factory = new ReferencePreviewLeafFactoryHandler(provider.Provider, provider.InitialBehavior);
                created.Add(factory);
                bindings.Add(new ReferenceLeafBinding(StableHash.Fnv1A64(provider.Manifest.TypeId), provider.Manifest.Version, factory));
            }
            factories = created;
            return ReferenceLeafRegistry.CreatePhase1Fixtures(bindings);
        }

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

    internal readonly struct ReferencePreviewLeafRegistration
    {
        internal ReferencePreviewLeafRegistration(NodeManifest manifest, IReferenceLeafBehaviorProvider provider, IReferenceLeafBehavior initialBehavior)
        {
            Manifest = manifest;
            Provider = provider;
            InitialBehavior = initialBehavior;
        }

        internal NodeManifest Manifest { get; }
        internal IReferenceLeafBehaviorProvider Provider { get; }
        internal IReferenceLeafBehavior InitialBehavior { get; }
    }

    internal sealed class ReferencePreviewLeafFactoryHandler : IReferenceLeafHandler
    {
        private readonly IReferenceLeafBehaviorProvider _provider;
        private readonly Dictionary<uint, ProjectReferenceLeafHandlerAdapter> _handlers = new Dictionary<uint, ProjectReferenceLeafHandlerAdapter>();
        private IReferenceLeafBehavior _initialBehavior;

        internal ReferencePreviewLeafFactoryHandler(IReferenceLeafBehaviorProvider provider, IReferenceLeafBehavior initialBehavior)
        {
            _provider = provider;
            _initialBehavior = initialBehavior;
        }

        internal void Reset()
        {
            _handlers.Clear();
            _initialBehavior = null;
        }

        private ProjectReferenceLeafHandlerAdapter Handler(uint node)
        {
            if (!_handlers.TryGetValue(node, out var handler))
            {
                var behavior = _initialBehavior ?? _provider.CreateBehavior();
                _initialBehavior = null;
                handler = new ProjectReferenceLeafHandlerAdapter(behavior);
                _handlers.Add(node, handler);
            }
            return handler;
        }

        public void Enter(ref ReferenceNodeContext context) => Handler(context.NodeIndex.Value).Enter(ref context);
        public NodeStatus Tick(ref ReferenceNodeContext context) => Handler(context.NodeIndex.Value).Tick(ref context);
        public void Abort(ref ReferenceNodeContext context, NodeAbortReason reason) => Handler(context.NodeIndex.Value).Abort(ref context, reason);
        public void Exit(ref ReferenceNodeContext context, NodeExitReason reason) => Handler(context.NodeIndex.Value).Exit(ref context, reason);
    }
}
