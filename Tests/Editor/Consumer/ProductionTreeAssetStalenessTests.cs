using System.Collections;
using System.IO;
using AIBT.Authoring;
using AIBT.Editor.Graph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace AIBT.Tests.Consumer
{
    /// <summary>P9-004: runtime assets remember their source and catalog, show when stale and re-prepare.</summary>
    public sealed class ProductionTreeAssetStalenessTests
    {
        private const string Folder = "Assets/AibtP9004StalenessTest";
        private const string SourcePath = Folder + "/tree.aibt.json";
        private const string AssetPath = Folder + "/tree.runtime.asset";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "AibtP9004StalenessTest");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        [Test]
        public void PreparedAsset_RecordsItsSourceAndCatalog_AndIsCurrent()
        {
            var asset = PrepareFrom(4.5);

            Assert.That(asset.SourceGuid, Is.EqualTo(AssetDatabase.AssetPathToGUID(SourcePath)));
            Assert.That(asset.CatalogTypeName, Does.StartWith(typeof(ConsumerCatalog).FullName + ", "));
            Assert.That(ProductionTreeAssetStaleness.CatalogType(asset), Is.EqualTo(typeof(ConsumerCatalog)));
            Assert.That(ProductionTreeAssetStaleness.SourcePath(asset), Is.EqualTo(SourcePath));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
        }

        [Test]
        public void SavingTheSource_MarksTheAssetStale_AndRefreshRePreparesIt()
        {
            var asset = PrepareFrom(4.5);
            var oldHash = asset.SourceContentHash;

            // Edited on disk but not imported yet: an import would re-prepare it automatically.
            WriteSource(2.0, import: false);
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.SourceChanged));

            Assert.That(ProductionTreeAssetStaleness.RefreshAll(new[] { asset.SourceGuid }), Is.EqualTo(1));
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
            Assert.That(asset.SourceContentHash, Is.Not.EqualTo(oldHash));
            Assert.That(asset.name, Is.EqualTo("tree.runtime"), "Re-preparing in place keeps the asset and its references.");
            Assert.That(asset.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out var program, out var failure), Is.True, failure.ToString());
            program.Dispose();
            Assert.That(ProductionTreeAssetStaleness.RefreshAll(null), Is.Zero, "A current asset is left alone.");
        }

        [Test]
        public void ChangedCatalogFingerprint_IsDetected_AndPrepareAgainRepairsIt()
        {
            var asset = PrepareFrom(4.5);
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("_catalogFingerprint").stringValue = new string('0', 64);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.CatalogChanged));
            Assert.That(asset.TryCreateProgram(ConsumerCatalog.TryCreateRuntimeCatalog, out _, out _), Is.False,
                "The runtime refuses a snapshot whose catalog fingerprint does not match.");
            Assert.That(ProductionTreeAssetStaleness.TryPrepareAgain(asset, out var error), Is.True, error);
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
        }

        [Test]
        public void OlderAssetWithoutProvenance_IsUnknown_AndPreparingAgainLinksIt()
        {
            var asset = PrepareFrom(4.5);
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("_sourceGuid").stringValue = string.Empty;
            serialized.FindProperty("_catalogTypeName").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Unknown));
            Assert.That(ProductionTreeAssetStaleness.RefreshAll(null), Is.Zero, "Unlinked assets are never rebuilt automatically.");
            Assert.That(ProductionTreeAssetStaleness.CatalogType(asset), Is.EqualTo(typeof(ConsumerCatalog)), "Inferred from the stored fingerprint.");
            Assert.That(ProductionTreeAssetStaleness.TryPrepareAgain(asset, out var error), Is.True, error);
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
        }

        [Test]
        public void MissingSource_IsReported_AndPrepareFailsWithoutTouchingTheAsset()
        {
            var asset = PrepareFrom(4.5);
            var payloadHash = new SerializedObject(asset).FindProperty("_payloadHash").stringValue;
            AssetDatabase.DeleteAsset(SourcePath);

            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.SourceMissing));
            Assert.That(ProductionTreeAssetStaleness.TryPrepareAgain(asset, out var error), Is.False);
            Assert.That(error, Does.Contain("source"));
            Assert.That(new SerializedObject(asset).FindProperty("_payloadHash").stringValue, Is.EqualTo(payloadHash));
        }

        [UnityTest]
        public IEnumerator ImportingAnEditedSource_RePreparesLinkedAssetsAutomatically()
        {
            var asset = PrepareFrom(4.5);
            var oldHash = asset.SourceContentHash;
            WriteSource(3.0);
            // Synchronous: the postprocessor must not depend on Editor ticks (none run in the background).
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current),
                "The source postprocessor must re-prepare the asset during the import, without a manual step.");
            Assert.That(asset.SourceContentHash, Is.Not.EqualTo(oldHash));
            yield return null;
            Assert.That(ProductionTreeAssetStaleness.Evaluate(asset), Is.EqualTo(ProductionTreeAssetState.Current));
        }

        private static ProductionTreeAsset PrepareFrom(double radius)
        {
            WriteSource(radius);
            var parsed = CanonicalTreeJson.Parse(File.ReadAllBytes(SourcePath), SourcePath);
            Assert.That(parsed.Success, Is.True);
            var asset = ProductionTreeAssetBaker.Create(parsed.Document, SourcePath, typeof(ConsumerCatalog), out var diagnostics);
            Assert.That(asset, Is.Not.Null, string.Join("; ", diagnostics));
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>(AssetPath);
        }

        private static void WriteSource(double radius, bool import = true)
        {
            var encoded = CanonicalTreeJson.Serialize(ProductionConsumerWorkflowTests.Tree(SemanticValue.FromNumber(radius)));
            Assert.That(encoded.Success, Is.True);
            File.WriteAllBytes(SourcePath, encoded.Utf8);
            if (import) AssetDatabase.ImportAsset(SourcePath, ImportAssetOptions.ForceUpdate);
        }
    }
}
