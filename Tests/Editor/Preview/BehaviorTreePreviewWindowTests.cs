using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using AIBT.Editor.Preview;
using AIBT.Tests.Editor;
using NUnit.Framework;
using UnityEngine;

namespace AIBT.Tests.Editor.Preview
{
    /// <summary>
    /// P3-009 acceptance criterion: "Preview never mutates .aibt.json or .aibt.layout.json."
    /// </summary>
    public sealed class BehaviorTreePreviewWindowTests
    {
        [Test]
        public void LoadingAndSwitchingDocumentsNeverMutatesTheSourceFileOrCreatesALayoutFile()
        {
            var path = EditorTestPackagePaths.Resolve("Tests", "Editor", "Preview", "Fixtures", "success-then-running.aibt.json");
            var before = File.ReadAllBytes(path);
            var layoutPath = Path.ChangeExtension(Path.ChangeExtension(path, null), ".layout.json");
            Assert.That(File.Exists(layoutPath), Is.False, "Precondition: no layout file should exist next to the fixture.");

            var window = ScriptableObject.CreateInstance<BehaviorTreePreviewWindow>();
            try
            {
                window.LoadFromPath(path);

                // "Edits reflected in the next preview run without an editor restart": reload the
                // same already-open window with an edited in-memory document.
                var result = CanonicalTreeJson.Parse(before, path);
                Assert.That(result.Success, Is.True);
                var edited = SemanticEditOperations.RemoveNode(result.Document, new NodeId("b"));
                window.LoadDocument(edited, "preview/edited-in-memory.aibt.json");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }

            var after = File.ReadAllBytes(path);
            Assert.That(after, Is.EqualTo(before), "Loading and switching documents in a preview must never touch the source file on disk.");
            Assert.That(File.Exists(layoutPath), Is.False, "Preview must never create a layout file as a side effect.");
        }
    }
}
