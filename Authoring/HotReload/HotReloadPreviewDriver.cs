using System;
using System.Collections.Generic;
using System.Linq;

namespace AIBT.Authoring
{
    /// <summary>
    /// Drives a hot-reloadable instance of the accepted Phase 1 managed reference executor
    /// (<c>ReferenceExecutionMachine</c>, internal to <c>AIBT.Runtime</c>) for the Editor workflow
    /// (<c>P5-008</c>), so <c>AIBT.Editor</c> (no internals visibility into <c>AIBT.Runtime</c>) can
    /// trigger and observe a real reload without crossing the assembly boundary itself. This type
    /// owns no reload semantics of its own: every classification and state transition is a direct
    /// call into <c>HotReloadCompatibilityClassifier</c>/<c>HotReloadStateMigration</c>
    /// (<c>P5-003</c>/<c>P5-005</c>/<c>P5-006</c>), mirroring exactly how <see cref="ReferencePreviewDriver"/>
    /// crosses the same boundary for stepping.
    /// <para>
    /// Same fixed Phase 1 fixture/built-in node-behavior set as <see cref="ReferencePreviewDriver"/>
    /// (<see cref="ReferencePreviewFixtureEnvironment"/>) -- and the same known limitation: AIBT
    /// ships no production per-project leaf-behavior registration mechanism yet.
    /// </para>
    /// </summary>
    public sealed class HotReloadPreviewDriver
    {
        private static readonly CompiledCompilerVersion CompilerVersion = new CompiledCompilerVersion(1, 0, 0, 1);

        private CompiledProgram _program;
        private ReferenceExecutionMachine _machine;
        private ulong _updateId;

        private HotReloadPreviewDriver(CompiledProgram program, ReferenceExecutionMachine machine)
        {
            _program = program;
            _machine = machine;
        }

        /// <summary>Compiles <paramref name="document"/> and constructs a driver ready to tick or reload.</summary>
        public static bool TryCreate(
            TreeDocument document,
            string sourceId,
            out HotReloadPreviewDriver driver,
            out DiagnosticCollection diagnostics)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));

            if (!TryCompile(document, sourceId, out var program, out diagnostics))
            {
                driver = null;
                return false;
            }

            var machine = CreateMachine(program);
            driver = new HotReloadPreviewDriver(program, machine);
            return true;
        }

        /// <summary>The fixed node registry <see cref="TryCreate"/> and <see cref="TryReload"/> compile against.</summary>
        public static NodeRegistry CreatePreviewNodeRegistry() => ReferencePreviewFixtureEnvironment.CreateNodeRegistry();

        /// <summary>The terminal root status of the currently live instance, if it has reached one.</summary>
        public NodeStatus? TerminalResult => _machine.TerminalResult;

        /// <summary>How many nodes the currently live instance considers active right now.</summary>
        public uint ActiveNodeCount => _machine.CaptureInspection().ActiveNodeCount;

        /// <summary>Runs one full update ("tick") of the currently live instance to its next boundary.</summary>
        public NodeStatus? RunOneTick(long timeMicroseconds = 0)
        {
            _updateId++;
            var envelope = _machine.Update(new ReferenceUpdateContext(_updateId, new Revision(_updateId), timeMicroseconds));
            return envelope.RootResult;
        }

        /// <summary>
        /// Compiles <paramref name="newDocument"/>, classifies it against the currently live
        /// instance's compiled program (<c>P5-003</c>), and applies whichever reload strategy
        /// <c>P5-005</c>/<c>P5-006</c>'s shared mechanism decides -- full restart, localized
        /// subtree restart, or full migration -- swapping in the resulting fresh instance. Returns
        /// <c>false</c> (with no outcome) only if <paramref name="newDocument"/> itself fails to
        /// compile; the reload itself never fails outright, since full restart is always available.
        /// </summary>
        public bool TryReload(
            TreeDocument newDocument,
            string newSourceId,
            out HotReloadPreviewOutcome outcome,
            out DiagnosticCollection diagnostics)
        {
            if (newDocument == null) throw new ArgumentNullException(nameof(newDocument));
            if (string.IsNullOrEmpty(newSourceId)) throw new ArgumentException("A source ID is required.", nameof(newSourceId));

            if (!TryCompile(newDocument, newSourceId, out var newProgram, out diagnostics))
            {
                outcome = null;
                return false;
            }

            var classification = HotReloadCompatibilityClassifier.Classify(_program, newProgram);
            _updateId++;
            var abortContext = new ReferenceUpdateContext(_updateId, new Revision(_updateId), 0);

            var freshMachine = HotReloadStateMigration.Migrate(
                _machine, _program, newProgram, classification, abortContext,
                new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(), null,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry(),
                out var migrationReport);

            outcome = new HotReloadPreviewOutcome(
                migrationReport.FellBackToFullRestart,
                classification.RequiresFullRestart,
                classification.NodeVerdicts.ToDictionary(pair => pair.Key, pair => pair.Value.Category.ToString()),
                classification.RestartSubtreeRootNodeIds,
                migrationReport.MigratedNodeCount,
                migrationReport.ResetNodeCount,
                migrationReport.DroppedNodeCount);

            _program = newProgram;
            _machine = freshMachine;
            _updateId = 0;
            return true;
        }

        private static bool TryCompile(
            TreeDocument document, string sourceId, out CompiledProgram program, out DiagnosticCollection diagnostics)
        {
            var registry = ReferencePreviewFixtureEnvironment.CreateNodeRegistry();
            var options = new ReferenceCompilerOptions(sourceId, ReferenceCompilationPolicy.Phase1, CompilerVersion);
            var compilation = ReferenceCompiler.Compile(document, registry, options);
            program = compilation.Success ? compilation.Program : null;
            diagnostics = compilation.Diagnostics;
            return compilation.Success;
        }

        private static ReferenceExecutionMachine CreateMachine(CompiledProgram program)
        {
            return new ReferenceExecutionMachine(
                program,
                new TreeInstanceId(1),
                ReferencePreviewFixtureEnvironment.CreateLeafRegistry(),
                null,
                ReferencePreviewFixtureEnvironment.CreateMemoryCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateReactiveCompositeRegistry(),
                ReferencePreviewFixtureEnvironment.CreateDecoratorRegistry(),
                ReferencePreviewFixtureEnvironment.CreateParallelRegistry(),
                RegisteredBlackboardRegistry.Empty,
                ReferencePreviewFixtureEnvironment.CreateObserverRegistry());
        }
    }
}
