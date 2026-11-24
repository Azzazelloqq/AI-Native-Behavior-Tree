using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Consumer
{
    /// <summary>
    /// P11-015 (F14 finding): a consumer that installs the package compiles every assembly definition
    /// under Tests/ that is not a test assembly. Three fixtures did (AIBT.SampleCustomTool,
    /// AIBT.NodeDevelopment.ShardFixture, AIBT.NodeDevelopment.CatalogFixture), so every consumer's
    /// Editor exposed the fixture MCP tool aibt_custom_sample_write_marker. Unity compiles a package's
    /// test assemblies only when the package is listed in "testables" (or lives in Assets): an assembly
    /// is one when it references TestAssemblies or is constrained to UNITY_INCLUDE_TESTS.
    /// </summary>
    public sealed class TestAssemblyIsolationTests
    {
        /// <summary>
        /// Assemblies the check must see: the three fixtures of the F14 finding and the two test assemblies
        /// that reference most fixtures. P11-014 review item 4: without this, a wrong root (no asmdef found)
        /// passed.
        /// </summary>
        private static readonly string[] Expected =
        {
            "AIBT.SampleCustomTool", "AIBT.NodeDevelopment.ShardFixture", "AIBT.NodeDevelopment.CatalogFixture",
            "AIBT.Editor.Tests", "AIBT.Integration.Tests",
        };

        [Test]
        public void EveryAssemblyDefinitionUnderTests_IsATestAssembly()
        {
            var root = AIBT.Tests.Editor.EditorTestPackagePaths.Resolve("Tests");
            var files = Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories);
            var names = files.Select(path => (string)JObject.Parse(File.ReadAllText(path))["name"]).ToArray();
            UnityEngine.Debug.Log("P11-014 review item 4: " + files.Length + " assembly definitions under " + root);
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(Expected.Length), "assembly definitions found under " + root);
            Assert.That(names, Is.SupersetOf(Expected), "the check read the package's Tests folder");
            var offenders = files
                .Where(path => !IsTestAssembly(JObject.Parse(File.ReadAllText(path))))
                .Select(path => path.Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "These assemblies compile into a consumer's project; add \"optionalUnityReferences\": [\"TestAssemblies\"] "
                + "(or the UNITY_INCLUDE_TESTS define constraint) as the other test assemblies do.");
        }

        private static bool IsTestAssembly(JObject asmdef)
        {
            var optional = asmdef["optionalUnityReferences"] as JArray;
            if (optional != null && optional.Values<string>().Contains("TestAssemblies")) return true;
            var defines = asmdef["defineConstraints"] as JArray;
            return defines != null && defines.Values<string>().Contains("UNITY_INCLUDE_TESTS");
        }
    }
}
