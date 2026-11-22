using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-007 (review F5): a domain reload after which no tree file changed walks no tree files,
    /// registers nothing and requests no <see cref="AssetDatabase.Refresh()"/>; while no tree uses
    /// subtrees nothing is registered at all. Before the fix every reload scanned, re-registered every
    /// tree ID and requested a refresh. A reload is simulated by clearing what a reload clears (the
    /// index's static state) and running the index's after-reload step; the index's state is put back
    /// afterwards. Tree files that change still re-import their dependants: SubtreeImportAndBakeTests.
    /// </summary>
    public sealed class ProjectTreeIndexReloadTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const string RegisteredKey = "AIBT.Subtrees.ProjectTreeIndex.Registered";
        private const string UsesSubtreesKey = "AIBT.Subtrees.ProjectTreeIndex.UsesSubtrees";
        private const string SavedKey = "AIBT.Subtrees.ProjectTreeIndex.Saved";
        private const string FolderName = "AibtP11007ReloadTest";

        private Dictionary<string, Hash128> _savedRegistered;
        private bool _savedSubscribed;
        private string _savedSessionRegistered;
        private bool _savedSessionUsesSubtrees;
        private bool _savedSessionSaved;

        [SetUp]
        public void SetUp()
        {
            _savedRegistered = new Dictionary<string, Hash128>(Registered(), StringComparer.Ordinal);
            _savedSubscribed = (bool)Field("s_registrySubscribed").GetValue(null);
            _savedSessionRegistered = SessionState.GetString(RegisteredKey, string.Empty);
            _savedSessionUsesSubtrees = SessionState.GetBool(UsesSubtreesKey, false);
            _savedSessionSaved = SessionState.GetBool(SavedKey, false);
        }

        [TearDown]
        public void TearDown()
        {
            var registered = Registered();
            registered.Clear();
            foreach (var pair in _savedRegistered) registered.Add(pair.Key, pair.Value);
            SetSubscribed(_savedSubscribed);
            SessionState.SetString(RegisteredKey, _savedSessionRegistered);
            SessionState.SetBool(UsesSubtreesKey, _savedSessionUsesSubtrees);
            SessionState.SetBool(SavedKey, _savedSessionSaved);
            if (Directory.Exists(FolderName)) Directory.Delete(FolderName, true);
        }

        [Test]
        public void AReloadWithNothingChanged_ScansNothing_RegistersNothing_AndRequestsNoRefresh()
        {
            // Up to date with the project's tree files as they are now, as after an import of one of them.
            Invoke("Update", ProjectTreeIndex.Scan());
            var registeredBefore = new Dictionary<string, Hash128>(Registered(), StringComparer.Ordinal);
            var scans = ProjectTreeIndex.ScanCount;
            var refreshes = ProjectTreeIndex.RefreshRequests;

            SimulateReload();
            Invoke("RegisterAfterReload");

            Assert.That(ProjectTreeIndex.ScanCount - scans, Is.EqualTo(0), "no walk of the tree files");
            Assert.That(ProjectTreeIndex.RefreshRequests - refreshes, Is.EqualTo(0), "no AssetDatabase.Refresh requested");
            Assert.That(Registered(), Is.EquivalentTo(registeredBefore), "the session's registered values are known again");
        }

        [Test]
        public void WithoutATreeThatUsesSubtrees_NothingIsRegistered()
        {
            Directory.CreateDirectory(FolderName);
            File.WriteAllText(FolderName + "/plain.aibt", @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11007.reload.plain"", ""name"": ""plain"", ""root"": ""hold"",
  ""nodes"": { ""hold"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } } } }");
            var snapshot = ProjectTreeIndex.Scan(new[] { new ProjectTreeIndex.ScanRoot("Packages/com.example.p11007", Path.GetFullPath(FolderName), false) });
            Assert.That(snapshot.UsesSubtrees, Is.False);
            Assert.That(snapshot.Paths.Count(), Is.EqualTo(1));

            SimulateReload();
            SessionState.SetBool(SavedKey, false);
            var refreshes = ProjectTreeIndex.RefreshRequests;
            Invoke("Update", snapshot);

            Assert.That(Registered(), Is.Empty, "no tree uses subtrees, so no importer depends on a tree ID");
            Assert.That(ProjectTreeIndex.RefreshRequests - refreshes, Is.EqualTo(0));
            Assert.That((bool)Field("s_registrySubscribed").GetValue(null), Is.False);
        }

        private static void SimulateReload()
        {
            Registered().Clear();
            SetSubscribed(false);
            Field("s_refreshScheduled").SetValue(null, false);
        }

        private static void SetSubscribed(bool subscribed)
        {
            var handler = (Action)Delegate.CreateDelegate(typeof(Action), typeof(ProjectTreeIndex).GetMethod("RegisterRegistryDependency", Static));
            ProjectNodeCatalogCache.Changed -= handler;
            if (subscribed) ProjectNodeCatalogCache.Changed += handler;
            Field("s_registrySubscribed").SetValue(null, subscribed);
        }

        private static Dictionary<string, Hash128> Registered() => (Dictionary<string, Hash128>)Field("s_registered").GetValue(null);

        private static FieldInfo Field(string name)
        {
            var field = typeof(ProjectTreeIndex).GetField(name, Static);
            Assert.That(field, Is.Not.Null, name);
            return field;
        }

        private static void Invoke(string method, params object[] arguments)
        {
            var info = typeof(ProjectTreeIndex).GetMethod(method, Static);
            Assert.That(info, Is.Not.Null, method);
            info.Invoke(null, arguments);
        }
    }
}
