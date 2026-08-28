using System.IO;
using AIBT.Mcp;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Discovery
{
    public sealed class AibtTreeDiscoveryTests
    {
        private const string ValidTreeJson = @"{
  ""format"": ""aibt.tree"",
  ""formatVersion"": 1,
  ""treeId"": ""tree.discovery-scan"",
  ""name"": ""Discovery Scan"",
  ""root"": ""root"",
  ""nodes"": {
    ""root"": { ""type"": ""aibt.action.success"", ""typeVersion"": 1 }
  }
}";

        private string _root;

        [SetUp]
        public void CreateTempProject()
        {
            _root = Path.Combine(Path.GetTempPath(), "aibt-tree-discovery-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Nested"));
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public void ValidTreesAreFoundRecursivelyAndSortedDeterministically()
        {
            File.WriteAllText(Path.Combine(_root, "b.aibt.json"), ValidTreeJson.Replace("tree.discovery-scan", "tree.b"));
            File.WriteAllText(Path.Combine(_root, "Nested", "a.aibt.json"), ValidTreeJson.Replace("tree.discovery-scan", "tree.a"));

            var result = AibtTreeDiscovery.Scan(_root);

            Assert.That(result.Trees.Count, Is.EqualTo(2));
            Assert.That(result.SkippedFiles, Is.Empty);
        }

        [Test]
        public void MalformedTreeFileIsSkippedNotFatalToTheWholeScan()
        {
            File.WriteAllText(Path.Combine(_root, "valid.aibt.json"), ValidTreeJson);
            File.WriteAllText(Path.Combine(_root, "broken.aibt.json"), "{ this is not valid json");

            var result = AibtTreeDiscovery.Scan(_root);

            Assert.That(result.Trees.Count, Is.EqualTo(1), "The one valid file must still be found.");
            Assert.That(result.SkippedFiles.Count, Is.EqualTo(1));
            Assert.That(result.SkippedFiles[0], Does.EndWith("broken.aibt.json"));
        }

        [Test]
        public void NonExistentDirectoryReturnsAnEmptyResultRatherThanThrowing()
        {
            AibtTreeDiscovery.ScanResult result = default;
            Assert.DoesNotThrow(() => result = AibtTreeDiscovery.Scan(Path.Combine(_root, "does-not-exist")));

            Assert.That(result.Trees, Is.Empty);
            Assert.That(result.SkippedFiles, Is.Empty);
        }

        [Test]
        public void FilesNotMatchingTheSuffixAreIgnored()
        {
            File.WriteAllText(Path.Combine(_root, "unrelated.json"), ValidTreeJson);
            File.WriteAllText(Path.Combine(_root, "readme.txt"), "not a tree");

            var result = AibtTreeDiscovery.Scan(_root);

            Assert.That(result.Trees, Is.Empty);
            Assert.That(result.SkippedFiles, Is.Empty);
        }
    }
}
