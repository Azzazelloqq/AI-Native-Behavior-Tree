using System;
using System.IO;
using AIBT.Mcp;
using AIBT.Mcp.Authoring;
using AIBT.Mcp.NodeDevelopment;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.NodeDevelopment
{
    public sealed class CompileAttemptTests
    {
        private string _root, _assets, _log;
        private CompileAttemptStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "aibt-compile-attempt-" + Guid.NewGuid().ToString("N"));
            _assets = Path.Combine(_root, "Assets");
            StagingSlot.WriteNode(_assets, "Node.cs", "first");
            _log = Path.Combine(_root, "custom-editor.log");
            File.WriteAllText(_log, "[ScriptCompilation] stale\nerror CS0000 stale\n");
            _store = new CompileAttemptStore(_assets, "editor-session");
        }

        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        private string StartRequested()
        {
            var id = (string)_store.Start()["attemptId"];
            Assert.That(_store.Advance(false, "domain-1", _log), Is.EqualTo(CompileAttemptAction.Import));
            Assert.That(_store.Advance(false, "domain-1", _log), Is.EqualTo(CompileAttemptAction.Compile));
            return id;
        }

        private void Finish(string error = null)
        {
            _store.CompilationStarted();
            _store.AssemblyFinished("AIBT.Generated.Staging.dll", error == null ? Array.Empty<string>() : new[] { error });
            _store.AssemblyFinished("AIBT.Generated.Staging.Catalog.dll", Array.Empty<string>());
            _store.CompilationFinished();
        }

        [Test]
        public void FastCompletionIsRetainedAcrossReloadAndRepeatedChecks()
        {
            var id = StartRequested();
            Finish();
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("still-compiling"), "Do not expose old loaded metadata.");
            _store = new CompileAttemptStore(_assets, "editor-session");
            _store.Advance(false, "domain-2", _log);
            var first = _store.Check(id);
            Assert.That((string)first["status"], Is.EqualTo("compiled"));
            Assert.That((string)first["contentHash"], Is.EqualTo(StagingSlot.ComputeContentHash(_assets)));
            Assert.That(_store.Check(id).ToString(), Is.EqualTo(first.ToString()));
        }

        [Test]
        public void ExistingCompilationFinishingCannotCompleteQueuedAttempt()
        {
            var id = (string)_store.Start()["attemptId"];
            Assert.That(_store.Advance(true, "domain-1", _log), Is.EqualTo(CompileAttemptAction.None));
            Finish();
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("pending"));
            Assert.That(_store.Advance(false, "domain-2", _log), Is.EqualTo(CompileAttemptAction.Import));
            Assert.That(_store.Advance(false, "domain-2", _log), Is.EqualTo(CompileAttemptAction.Compile));
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("still-compiling"));
            Finish();
            _store.Advance(false, "domain-3", _log);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("compiled"));
        }

        [Test]
        public void UnrelatedLogMarkersCannotEstablishSuccess()
        {
            var id = StartRequested();
            File.AppendAllText(_log, "[ScriptCompilation] unrelated marker\n");
            _store.Advance(false, "domain-1", _log);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("still-compiling"));
        }

        [Test]
        public void CompilerFailureIsObservableWithoutReload()
        {
            var id = StartRequested();
            Finish("error CS1002");
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("failed"));
            Assert.That(_store.Check(id)["diagnostics"].ToString(), Does.Contain("CS1002"));
        }

        [Test]
        public void MissingStagingAssemblyIsNotSuccess()
        {
            var id = StartRequested();
            _store.CompilationStarted();
            _store.AssemblyFinished("Other.dll", Array.Empty<string>());
            _store.CompilationFinished();
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("failed"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompilerVerifiedUpToDateAssembliesCompleteWithoutUnnecessaryReload(bool dependencyRebuilt)
        {
            var id = StartRequested();
            _store.CompilationStarted();
            _store.AssemblyFinished("AIBT.Generated.Staging.dll", Array.Empty<string>(), rebuilt: false);
            _store.AssemblyFinished("AIBT.Generated.Staging.Catalog.dll", Array.Empty<string>(), rebuilt: false);
            if (dependencyRebuilt) _store.AssemblyFinished("Dependency.dll", Array.Empty<string>());
            _store.CompilationFinished();
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo(dependencyRebuilt ? "still-compiling" : "compiled"));
            _store.Advance(false, "domain-2", _log);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("compiled"));
        }

        [Test]
        public void ChangedStagingRequiresNewAttempt()
        {
            var id = StartRequested();
            File.WriteAllText(Path.Combine(StagingSlot.RootPath(_assets), "Node.cs"), "second");
            Finish();
            Assert.Throws<McpToolException>(() => _store.Check(id));
        }

        [Test]
        public void SupersededUnknownAndPreviousEditorAttemptsAreRejected()
        {
            var id = StartRequested();
            _store.Start();
            Assert.Throws<McpToolException>(() => _store.Check(id));
            Assert.Throws<McpToolException>(() => _store.Check("unknown"));
            var latest = (string)_store.Start()["attemptId"];
            Assert.Throws<McpToolException>(() => new CompileAttemptStore(_assets, "different-session").Check(latest));
        }

        [Test]
        public void ReloadWithLostCompletionIsRecoverableFailure()
        {
            var id = StartRequested();
            _store.CompilationStarted();
            _store = new CompileAttemptStore(_assets, "editor-session");
            _store.Advance(false, "domain-2", _log);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("failed"));
            Assert.That((string)_store.Start()["status"], Is.EqualTo("pending"));
        }

        [Test]
        public void ActualLogPathIsReadAndUnavailableOrTruncatedLogIsExplicit()
        {
            var position = EditorLogCompileWatcher.Capture(_log, out var warning);
            Assert.That(warning, Is.Null);
            File.AppendAllText(_log, "fresh diagnostic\n");
            Assert.That(EditorLogCompileWatcher.ReadTail(_log, position, out warning), Is.EqualTo("fresh diagnostic\n"));
            File.WriteAllText(_log, "");
            Assert.That(EditorLogCompileWatcher.ReadTail(_log, position, out warning), Is.Empty);
            Assert.That(warning, Is.Not.Null);
            Assert.That(EditorLogCompileWatcher.Capture(Path.Combine(_root, "missing.log"), out warning), Is.EqualTo(-1));
            Assert.That(warning, Is.Not.Null);
        }

        [Test]
        public void MissingLogDoesNotSubstituteForCompilationEvidence()
        {
            var id = (string)_store.Start()["attemptId"];
            _store.Advance(false, "domain-1", null);
            _store.Advance(false, "domain-1", null);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("still-compiling"));
            Finish();
            _store.Advance(false, "domain-2", null);
            Assert.That((string)_store.Check(id)["status"], Is.EqualTo("compiled"));
            Assert.That(_store.Check(id)["logWarning"], Is.Not.Null);
        }
    }
}
