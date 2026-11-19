using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// ADR-P11-001 stage 2 follow-up: the Editor's tree-ID index also covers trees in packages. A
    /// package is not created here (that would change the project's package manifest and lock file):
    /// the index is given package roots that map a <c>Packages/&lt;name&gt;</c> asset path to a
    /// temporary folder, which is exactly how it maps a registered package to its resolved folder.
    /// </summary>
    public sealed class ProjectTreeIndexPackageTests
    {
        private const string FakePackage = "Packages/com.aibt-tests.fake-trees";
        private const string OtherPackage = "Packages/com.aibt-tests.other-trees";
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "aibt-p11-package-index", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void PackageTrees_ResolveUnderTheirPackagesAssetPath_AndSkipHiddenTildeAndCvsFolders()
        {
            var fake = Folder("fake");
            WriteTree(fake, "Trees/attack.aibt", "tests.p11.pkg.attack");
            WriteTree(fake, "Trees/Deep/guard.aibt.json", "tests.p11.pkg.guard");
            WriteTree(fake, "Samples~/hidden.aibt", "tests.p11.pkg.tilde");
            WriteTree(fake, ".git/hidden.aibt", "tests.p11.pkg.dot-folder");
            WriteTree(fake, "CVS/hidden.aibt", "tests.p11.pkg.cvs");
            WriteTree(fake, "Trees/.dotfile.aibt", "tests.p11.pkg.dot-file");

            var snapshot = ProjectTreeIndex.Scan(new[] { new ProjectTreeIndex.ScanRoot(FakePackage, fake, false) });

            var attack = snapshot.Resolve(new TreeId("tests.p11.pkg.attack"));
            Assert.That(attack.Status, Is.EqualTo(SubtreeResolutionStatus.Found));
            Assert.That(attack.SourceId, Is.EqualTo(FakePackage + "/Trees/attack.aibt"));
            Assert.That(snapshot.Resolve(new TreeId("tests.p11.pkg.guard")).SourceId, Is.EqualTo(FakePackage + "/Trees/Deep/guard.aibt.json"));
            foreach (var skipped in new[] { "tests.p11.pkg.tilde", "tests.p11.pkg.dot-folder", "tests.p11.pkg.cvs", "tests.p11.pkg.dot-file" })
                Assert.That(snapshot.Resolve(new TreeId(skipped)).Status, Is.EqualTo(SubtreeResolutionStatus.NotFound), skipped);
            Assert.That(snapshot.Paths.Single(pair => pair.Key.Value == "tests.p11.pkg.attack").Value,
                Is.EqualTo(new[] { FakePackage + "/Trees/attack.aibt" }), "The tree-ID dependency value is computed from these asset paths.");
        }

        [Test]
        public void SameTreeIdInAssetsAndAPackage_IsAmbiguous_AIBT3021()
        {
            var assets = Folder("assets");
            var package = Folder("package");
            WriteTree(assets, "Trees/attack.aibt", "tests.p11.pkg.attack");
            WriteTree(package, "Trees/attack.aibt", "tests.p11.pkg.attack");
            var snapshot = ProjectTreeIndex.Scan(new[]
            {
                new ProjectTreeIndex.ScanRoot("Assets", assets, false),
                new ProjectTreeIndex.ScanRoot(OtherPackage, package, true),
            });

            var resolution = snapshot.Resolve(new TreeId("tests.p11.pkg.attack"));
            Assert.That(resolution.Status, Is.EqualTo(SubtreeResolutionStatus.Ambiguous));
            Assert.That(resolution.CandidateSourceIds, Is.EquivalentTo(new[] { "Assets/Trees/attack.aibt", OtherPackage + "/Trees/attack.aibt" }));

            var parent = CanonicalTreeJson.Parse(Json(@"{ 'format': 'aibt.tree', 'formatVersion': 3, 'treeId': 'tests.p11.pkg.parent', 'name': 'Parent', 'root': 'root',
  'nodes': { 'root': { 'type': 'aibt.core.memory-sequence', 'typeVersion': 1, 'children': ['use'] },
             'use': { 'subtree': { 'treeId': 'tests.p11.pkg.attack', 'interfaceVersion': 1, 'keys': { } } } } }"), "Assets/parent.aibt");
            Assert.That(parent.Success, Is.True);
            var expansion = SubtreeExpander.Expand(parent.Document, snapshot, NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry);
            Assert.That(expansion.Success, Is.False);
            Assert.That(expansion.Diagnostics.Select(d => d.Code.Value), Is.EqualTo(new[] { "AIBT3021" }));
        }

        [Test]
        public void ImmutablePackageFolders_AreListedOncePerResolvedFolder()
        {
            var package = Folder("immutable");
            WriteTree(package, "Trees/first.aibt", "tests.p11.pkg.first");
            var root = new ProjectTreeIndex.ScanRoot(OtherPackage, package, true);
            Assert.That(ProjectTreeIndex.Scan(new[] { root }).Resolve(new TreeId("tests.p11.pkg.first")).Status, Is.EqualTo(SubtreeResolutionStatus.Found));

            // A registry, Git or built-in package changes only by getting a new resolved folder.
            WriteTree(package, "Trees/second.aibt", "tests.p11.pkg.second");
            Assert.That(ProjectTreeIndex.Scan(new[] { root }).Resolve(new TreeId("tests.p11.pkg.second")).Status, Is.EqualTo(SubtreeResolutionStatus.NotFound));
            var mutable = new ProjectTreeIndex.ScanRoot(OtherPackage, package, false);
            Assert.That(ProjectTreeIndex.Scan(new[] { mutable }).Resolve(new TreeId("tests.p11.pkg.second")).Status, Is.EqualTo(SubtreeResolutionStatus.Found),
                "Embedded and local packages are listed on every scan.");
        }

        [Test]
        public void DefaultRoots_AreAssetsAndEveryRegisteredPackageWithAFolder()
        {
            var roots = ProjectTreeIndex.Roots();
            Assert.That(roots[0].AssetRoot, Is.EqualTo("Assets"));
            Assert.That(roots[0].Immutable, Is.False);
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(package => !string.IsNullOrEmpty(package.resolvedPath) && Directory.Exists(package.resolvedPath)).ToList();
            Assert.That(packages, Is.Not.Empty);
            Assert.That(roots.Skip(1).Select(root => root.AssetRoot), Is.EquivalentTo(packages.Select(package => package.assetPath)));
            foreach (var package in packages)
            {
                var root = roots.Single(item => item.AssetRoot == package.assetPath);
                Assert.That(root.Directory, Is.EqualTo(package.resolvedPath));
                Assert.That(root.Immutable, Is.EqualTo(package.source != UnityEditor.PackageManager.PackageSource.Embedded
                                                       && package.source != UnityEditor.PackageManager.PackageSource.Local), package.name);
            }
        }

        private string Folder(string name)
        {
            var path = Path.Combine(_directory, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteTree(string root, string relative, string treeId)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Json(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': '" + treeId + @"', 'name': 'T', 'root': 'hold',
  'nodes': { 'hold': { 'type': 'aibt.stdlib.wait', 'typeVersion': 1, 'parameters': { 'ticks': 1 } } } }"));
        }

        private static string Json(string text) => text.Replace('\'', '"');
    }
}
