using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Registry;
using AIBT.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Authoring
{
    /// <summary>
    /// P11-001 stage 4 (ADR-P11-001 §4.7): the MCP tools for subtree references at the dispatcher
    /// entry point the bridge calls -- add_node with a `subtree` object, set_subtree_keys,
    /// extract_to_subtree, unpack_subtree, find_subtree_usages, inspect_tree expanded, the manifest
    /// fields, and validate/compile/prepare_runtime resolving references through the project's trees.
    /// Every test works in its own temporary project (Assets plus a permissive .aibt/policy.json, so
    /// the package's managed nodes validate and compile).
    /// </summary>
    public sealed class McpSubtreeToolsTests
    {
        private const string StrikeId = "tests.p11.mcp.strike";
        private const string FightId = "tests.p11.mcp.fight";

        // The stage-3 demo subtree with tree IDs of its own: interface key `hits`, private key `swings`.
        private const string Strike = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.mcp.strike"", ""name"": ""Strike"", ""root"": ""combo"",
  ""interface"": { ""version"": 1, ""keys"": [""hits""] },
  ""blackboard"": { ""hits"": { ""type"": ""Int32"", ""default"": 0 }, ""swings"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": {
    ""combo"": { ""displayName"": ""Combo"", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""windup"", ""hit"", ""count""] },
    ""windup"": { ""displayName"": ""Wind up"", ""type"": ""aibt.unity.wait-seconds"", ""typeVersion"": 1, ""parameters"": { ""seconds"": 0.8 } },
    ""hit"": { ""displayName"": ""Hit"", ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""hits"", ""amount"": 1 } },
    ""count"": { ""displayName"": ""Count swing"", ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""swings"", ""amount"": 1 } }
  }
}";

        // A plain format-1 parent with a same-named key `hits` and a branch to extract.
        private const string Fight = @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tests.p11.mcp.fight"", ""name"": ""Fight"", ""root"": ""fight"",
  ""blackboard"": { ""hits"": { ""type"": ""Int32"", ""default"": 0 }, ""heat"": { ""type"": ""Float32"", ""default"": 0 } },
  ""nodes"": {
    ""fight"": { ""displayName"": ""Fight"", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""taunt"", ""finisher""] },
    ""taunt"": { ""displayName"": ""Taunt"", ""type"": ""aibt.unity.wait-seconds"", ""typeVersion"": 1, ""parameters"": { ""seconds"": 0.5 } },
    ""finisher"": { ""displayName"": ""Finisher"", ""description"": ""Charge, then slam."", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""charge"", ""slam""] },
    ""charge"": { ""displayName"": ""Charge"", ""type"": ""aibt.unity.wait-seconds"", ""typeVersion"": 1, ""parameters"": { ""seconds"": 0.7 } },
    ""slam"": { ""displayName"": ""Slam"", ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""hits"", ""amount"": 3 } }
  }
}";

        private string _projectRoot;
        private string _assetsDir;

        [SetUp]
        public void CreateTempProject()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "aibt-mcp-subtrees-" + Guid.NewGuid().ToString("N"));
            _assetsDir = Path.Combine(_projectRoot, "Assets");
            Directory.CreateDirectory(Path.Combine(_assetsDir, "Trees"));
            WritePolicy(requireTreeDescription: false);
        }

        [TearDown]
        public void RemoveTempProject()
        {
            if (!Directory.Exists(_projectRoot)) return;
            foreach (var file in Directory.GetFiles(_projectRoot, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_projectRoot, recursive: true);
        }

        // ---- add_node with a subtree object ---------------------------------------------------------

        [Test]
        public void AddNode_WithASubtree_AddsAReference_ConnectsSameNamedKeys_AndRaisesTheTreeToFormat3()
        {
            Write("Trees/strike.aibt", Strike);
            Write("Trees/fight.aibt", Fight);

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight", ["insertIndex"] = 1,
                ["node"] = new JObject { ["id"] = "left", ["displayName"] = "Left strike", ["subtree"] = new JObject { ["treeId"] = StrikeId } },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.True, added.ToString());

            var saved = Read("Trees/fight.aibt");
            Assert.That(saved.FormatVersion, Is.EqualTo(TreeDocument.SubtreeFormatVersion));
            Assert.That(saved.Nodes.Single(node => node.Id.Value == "fight").Children.Select(id => id.Value), Is.EqualTo(new[] { "taunt", "left", "finisher" }));
            var reference = saved.Nodes.Single(node => node.Id.Value == "left").Subtree;
            Assert.That(reference.TreeId.Value, Is.EqualTo(StrikeId));
            Assert.That(reference.InterfaceVersion, Is.EqualTo(1u), "A missing interfaceVersion is the subtree's current one.");
            Assert.That(reference.Keys.Single().InterfaceKey, Is.EqualTo("hits"));
            Assert.That(reference.Keys.Single().ParentKey, Is.EqualTo("hits"), "Same-named parent keys of the same type are connected (graph editor rule).");

            var validated = Dispatch("validate", new JObject { ["treeId"] = FightId }, "Compilation");
            Assert.That((bool)validated["result"]["valid"], Is.True, validated.ToString());
            Assert.That((bool)validated["result"]["subtrees"]["expanded"], Is.True);
            var compiled = Dispatch("compile", new JObject { ["treeId"] = FightId }, "Compilation");
            Assert.That((bool)compiled["result"]["success"], Is.True, compiled.ToString());

            // Without a resolver the compiler cannot see the subtree: MCP compiles it through the project's trees.
            var catalog = ProjectNodeCatalogCache.Current;
            var options = new ReferenceCompilerOptions("Trees/fight.aibt", AIBT.Agents.ManagedTreeProgram.Policy, new CompiledCompilerVersion(1, 0, 0, 0));
            var direct = ReferenceCompiler.Compile(saved, catalog.Registry, options,
                new InMemorySubtreeResolver().Add(Read("Trees/strike.aibt"), "Trees/strike.aibt"));
            Assert.That(direct.Success, Is.True, string.Join("; ", direct.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
        }

        [Test]
        public void AddNode_KeyGivenAsNull_StaysNotConnected_SoTheEditIsRejectedWithAIBT3025()
        {
            Write("Trees/strike.aibt", Strike);
            Write("Trees/fight.aibt", Fight);
            var before = File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt"));

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight",
                ["node"] = new JObject { ["id"] = "left", ["subtree"] = new JObject { ["treeId"] = StrikeId, ["keys"] = new JObject { ["hits"] = null } } },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.False, added.ToString());
            Assert.That(Codes(added), Does.Contain("AIBT3025"));
            Assert.That(File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt")), Is.EqualTo(before));

            var withoutAutoConnect = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight", ["connectSameNamedKeys"] = false,
                ["node"] = new JObject { ["id"] = "left", ["subtree"] = new JObject { ["treeId"] = StrikeId } },
            }, "SemanticEdit");
            Assert.That(Codes(withoutAutoConnect), Does.Contain("AIBT3025"), withoutAutoConnect.ToString());
        }

        [Test]
        public void AddNode_WithoutAnId_GetsAShortReferenceId_AndADomainPatchCanAddAReferenceToo()
        {
            Write("Trees/strike.aibt", Strike);
            Write("Trees/fight.aibt", Fight);

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight",
                ["node"] = new JObject { ["subtree"] = new JObject { ["treeId"] = StrikeId } },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.True, added.ToString());
            Assert.That(Read("Trees/fight.aibt").Nodes.Any(node => node.Id.Value == "strike" && node.Subtree != null), Is.True,
                "A generated ID is the last segment of the tree ID.");

            var patched = Dispatch("apply_domain_patch", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId),
                ["operations"] = new JArray(new JObject
                {
                    ["op"] = "add", ["parentId"] = "finisher",
                    ["node"] = new JObject { ["id"] = "extra", ["subtree"] = new JObject { ["treeId"] = StrikeId, ["interfaceVersion"] = 1, ["keys"] = new JObject { ["hits"] = "hits" } } },
                }),
            }, "SemanticEdit");
            Assert.That((bool)patched["result"]["accepted"], Is.True, patched.ToString());
            Assert.That(Read("Trees/fight.aibt").Nodes.Single(node => node.Id.Value == "finisher").Children.Last().Value, Is.EqualTo("extra"));
        }

        [Test]
        public void AddNode_ToATreeIdNobodyDeclares_IsRejectedWithAIBT3020_AndWritesNothing()
        {
            Write("Trees/fight.aibt", Fight);
            var before = File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt"));

            var added = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight",
                ["node"] = new JObject { ["id"] = "ghost", ["subtree"] = new JObject { ["treeId"] = "tests.p11.mcp.nowhere", ["interfaceVersion"] = 1 } },
            }, "SemanticEdit");
            Assert.That((bool)added["result"]["accepted"], Is.False, added.ToString());
            Assert.That(Codes(added), Does.Contain("AIBT3020"));
            Assert.That(File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt")), Is.EqualTo(before));

            var malformed = Dispatch("add_node", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["parentId"] = "fight",
                ["node"] = new JObject { ["id"] = "both", ["typeId"] = "aibt.core.memory-sequence", ["subtree"] = new JObject { ["treeId"] = StrikeId } },
            }, "SemanticEdit");
            Assert.That((string)malformed["error"]["code"], Is.EqualTo("AIBT9018"), malformed.ToString());
        }

        // ---- set_subtree_keys -----------------------------------------------------------------------

        [Test]
        public void SetSubtreeKeys_BindsAConstantAndBack_AndRejectsATypeMismatchAndAnUnknownKey()
        {
            WriteParentWithReference();

            var constant = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left", ["keys"] = new JObject { ["hits"] = new JObject { ["value"] = 5 } },
            }, "SemanticEdit");
            Assert.That((bool)constant["result"]["accepted"], Is.True, constant.ToString());
            var binding = Read("Trees/fight.aibt").Nodes.Single(node => node.Id.Value == "left").Subtree.Keys.Single();
            Assert.That(binding.IsConstant, Is.True);
            Assert.That(binding.Constant.TryGetInt64(out var value) && value == 5, Is.True);
            var expanded = Dispatch("inspect_tree", new JObject { ["treeId"] = FightId, ["expanded"] = true }, "Read");
            Assert.That((string)expanded["result"]["expansion"]["tree"]["blackboard"]["left:hits"]["default"], Is.EqualTo("5"),
                "A constant-bound interface key becomes the private key r:key with the constant as default.");

            var mismatch = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left", ["keys"] = new JObject { ["hits"] = "heat" },
            }, "SemanticEdit");
            Assert.That((bool)mismatch["result"]["accepted"], Is.False);
            Assert.That(Codes(mismatch), Does.Contain("AIBT3026"), mismatch.ToString());

            var unknown = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left", ["keys"] = new JObject { ["misses"] = "hits" },
            }, "SemanticEdit");
            Assert.That(Codes(unknown), Does.Contain("AIBT3025"), unknown.ToString());

            var back = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left", ["keys"] = new JObject { ["hits"] = null },
                ["connectSameNamedKeys"] = true,
            }, "SemanticEdit");
            Assert.That((bool)back["result"]["accepted"], Is.True, back.ToString());
            Assert.That(Read("Trees/fight.aibt").Nodes.Single(node => node.Id.Value == "left").Subtree.Keys.Single().ParentKey, Is.EqualTo("hits"));

            var notAReference = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "taunt", ["keys"] = new JObject(),
            }, "SemanticEdit");
            Assert.That((string)notAReference["error"]["code"], Is.EqualTo("AIBT9018"));
            var missing = Dispatch("set_subtree_keys", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "nobody", ["keys"] = new JObject(),
            }, "SemanticEdit");
            Assert.That((string)missing["error"]["code"], Is.EqualTo("AIBT9019"));
        }

        // ---- extract_to_subtree ----------------------------------------------------------------------

        [Test]
        public void ExtractToSubtree_WritesTheNewTreeAndAReference_AndTheExpansionKeepsTheNodes()
        {
            Write("Trees/fight.aibt", Fight);
            var originalHash = Hash(FightId);

            var dryRun = Dispatch("extract_to_subtree", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = originalHash, ["nodeId"] = "finisher", ["newTreeId"] = "tests.p11.mcp.finisher",
                ["path"] = "Trees/Sub/finisher.aibt", ["dryRun"] = true,
            }, "SemanticEdit");
            Assert.That((bool)dryRun["result"]["accepted"], Is.True, dryRun.ToString());
            Assert.That(File.Exists(Path.Combine(_assetsDir, "Trees/Sub/finisher.aibt")), Is.False, "A dry run writes no file.");
            Assert.That(Hash(FightId), Is.EqualTo(originalHash));

            var extracted = Dispatch("extract_to_subtree", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = originalHash, ["nodeId"] = "finisher", ["newTreeId"] = "tests.p11.mcp.finisher",
                ["path"] = "Trees/Sub/finisher.aibt", ["name"] = "Finisher",
            }, "SemanticEdit");
            Assert.That((bool)extracted["result"]["accepted"], Is.True, extracted.ToString());
            Assert.That((string)extracted["result"]["referenceNodeId"], Is.EqualTo("finisher"));
            Assert.That((string)extracted["result"]["subtree"]["path"], Is.EqualTo("Trees/Sub/finisher.aibt"));
            Assert.That(extracted["result"]["subtree"]["interface"]["keys"].Values<string>(), Is.EqualTo(new[] { "hits" }));

            var subtree = Read("Trees/Sub/finisher.aibt");
            Assert.That(subtree.TreeId.Value, Is.EqualTo("tests.p11.mcp.finisher"));
            Assert.That(subtree.Nodes.Select(node => node.Id.Value), Is.EquivalentTo(new[] { "finisher", "charge", "slam" }));
            var parent = Read("Trees/fight.aibt");
            var reference = parent.Nodes.Single(node => node.Id.Value == "finisher");
            Assert.That(reference.Subtree.TreeId.Value, Is.EqualTo("tests.p11.mcp.finisher"));
            Assert.That(reference.Description, Is.EqualTo("Charge, then slam."), "The reference keeps the moved node's description.");
            Assert.That(parent.Nodes.Any(node => node.Id.Value == "charge"), Is.False);

            var expansion = Dispatch("inspect_tree", new JObject { ["treeId"] = FightId, ["expanded"] = true }, "Read")["result"]["expansion"];
            Assert.That((bool)expansion["success"], Is.True, expansion.ToString());
            Assert.That(((JObject)expansion["tree"]["nodes"]).Properties().Select(property => property.Name),
                Is.EquivalentTo(new[] { "fight", "taunt", "finisher:finisher", "finisher:charge", "finisher:slam" }));
            Assert.That((string)expansion["nodeSources"]["finisher:slam"], Is.EqualTo("Trees/Sub/finisher.aibt"));
            Assert.That((string)expansion["tree"]["nodes"]["finisher:slam"]["parameters"]["key"], Is.EqualTo("hits"),
                "The interface key is connected to the parent key of the same ID, so the slam still writes the parent's hits.");
            Assert.That((bool)Dispatch("compile", new JObject { ["treeId"] = FightId }, "Compilation")["result"]["success"], Is.True);
        }

        [Test]
        public void ExtractToSubtree_RejectedByValidation_ExistingTreeIdOrFile_WritesNoFile()
        {
            // A native tree, validated under the project's own policy, which requires tree descriptions.
            // (A tree with managed nodes is validated under the managed policy, as every MCP edit is.)
            Write("Trees/strike.aibt", Strike);
            Write("Trees/patrol.aibt", @"{ ""format"": ""aibt.tree"", ""formatVersion"": 1, ""treeId"": ""tests.p11.mcp.patrol"", ""name"": ""Patrol"", ""description"": ""Waits."", ""root"": ""main"",
  ""nodes"": { ""main"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""first"", ""rest""] },
    ""first"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } },
    ""rest"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""second""] },
    ""second"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 3 } } } }");
            WritePolicy(requireTreeDescription: true);
            const string PatrolId = "tests.p11.mcp.patrol";
            var patrolPath = Path.Combine(_assetsDir, "Trees/patrol.aibt");
            var before = File.ReadAllBytes(patrolPath);
            JObject Extract(string nodeId, string newTreeId, string path, string description = null)
            {
                var args = new JObject
                {
                    ["treeId"] = PatrolId, ["expectedHash"] = Hash(PatrolId), ["nodeId"] = nodeId, ["newTreeId"] = newTreeId, ["path"] = path,
                };
                if (description != null) args["description"] = description;
                return Dispatch("extract_to_subtree", args, "SemanticEdit");
            }

            var rejected = Extract("rest", "tests.p11.mcp.rest", "Trees/rest.aibt");
            Assert.That((bool)rejected["result"]["accepted"], Is.False, rejected.ToString());
            Assert.That(rejected["result"]["diagnostics"].Any(item => ((string)item["message"]).IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0), Is.True, rejected.ToString());
            Assert.That(File.Exists(Path.Combine(_assetsDir, "Trees/rest.aibt")), Is.False);
            Assert.That(File.ReadAllBytes(patrolPath), Is.EqualTo(before));

            var existingId = Extract("rest", StrikeId, "Trees/rest.aibt", "The rest.");
            Assert.That((string)existingId["error"]["code"], Is.EqualTo("AIBT9017"), existingId.ToString());
            var existingFile = Extract("rest", "tests.p11.mcp.rest", "Trees/strike.aibt", "The rest.");
            Assert.That((string)existingFile["error"]["code"], Is.EqualTo("AIBT9017"), existingFile.ToString());
            var root = Extract("main", "tests.p11.mcp.all", "Trees/all.aibt", "All.");
            Assert.That((string)root["error"]["code"], Is.EqualTo("AIBT9018"), "The root cannot be extracted: " + root);
            Assert.That(File.ReadAllBytes(patrolPath), Is.EqualTo(before));
            Assert.That(File.Exists(Path.Combine(_assetsDir, "Trees/rest.aibt")) || File.Exists(Path.Combine(_assetsDir, "Trees/all.aibt")), Is.False);

            var accepted = Extract("rest", "tests.p11.mcp.rest", "Trees/rest.aibt", "The rest.");
            Assert.That((bool)accepted["result"]["accepted"], Is.True, accepted.ToString());
            Assert.That(Read("Trees/rest.aibt").Description, Is.EqualTo("The rest."));
            Assert.That(accepted["result"]["subtree"]["interface"].Type, Is.EqualTo(JTokenType.Null), "No keys used, no interface.");
            Assert.That((bool)Dispatch("validate", new JObject { ["treeId"] = PatrolId }, "Compilation")["result"]["valid"], Is.True);
        }

        [Test]
        public void ExtractToSubtree_WhenTheParentCannotBeWritten_RemovesTheNewFileAgain()
        {
            Write("Trees/fight.aibt", Fight);
            var parentPath = Path.Combine(_assetsDir, "Trees/fight.aibt");
            var before = File.ReadAllBytes(parentPath);
            var hash = Hash(FightId);
            File.SetAttributes(parentPath, FileAttributes.ReadOnly);

            var response = Dispatch("extract_to_subtree", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = hash, ["nodeId"] = "finisher", ["newTreeId"] = "tests.p11.mcp.finisher", ["path"] = "Trees/finisher.aibt",
            }, "SemanticEdit");
            Assert.That((string)response["error"]["code"], Is.EqualTo("AIBT9020"), response.ToString());
            Assert.That(File.Exists(Path.Combine(_assetsDir, "Trees/finisher.aibt")), Is.False, "No orphan subtree file.");
            Assert.That(File.ReadAllBytes(parentPath), Is.EqualTo(before));
        }

        // ---- unpack_subtree ---------------------------------------------------------------------------

        [Test]
        public void UnpackSubtree_ReplacesTheReferenceByItsExpansion_AndRefusesACollision()
        {
            WriteParentWithReference();
            var unpacked = Dispatch("unpack_subtree", new JObject { ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left" }, "SemanticEdit");
            Assert.That((bool)unpacked["result"]["accepted"], Is.True, unpacked.ToString());
            Assert.That((string)unpacked["result"]["innerRootId"], Is.EqualTo("left:combo"));
            var document = Read("Trees/fight.aibt");
            Assert.That(document.Nodes.Any(node => node.Subtree != null), Is.False);
            Assert.That(document.Nodes.Single(node => node.Id.Value == "left:hit").DisplayName, Is.EqualTo("Left strike / Hit"));
            Assert.That(document.Blackboard.Any(key => key.Id == "left:swings"), Is.True, "The private key becomes a Tree key r:key.");
            Assert.That(document.Nodes.Single(node => node.Id.Value == "fight").Children.Select(id => id.Value), Is.EqualTo(new[] { "taunt", "left:combo", "finisher" }));

            // A second reference with a node that already uses its expanded IDs: AIBT3027, nothing written.
            WriteParentWithReference(extraNode: "\"right:combo\": { \"type\": \"aibt.core.memory-sequence\", \"typeVersion\": 1 },", referenceId: "right");
            var before = File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt"));
            var collision = Dispatch("unpack_subtree", new JObject { ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "right" }, "SemanticEdit");
            Assert.That((bool)collision["result"]["accepted"], Is.False, collision.ToString());
            Assert.That(Codes(collision), Does.Contain("AIBT3027"));
            Assert.That(File.ReadAllBytes(Path.Combine(_assetsDir, "Trees/fight.aibt")), Is.EqualTo(before));
        }

        // ---- find_subtree_usages, manifest, inspect -------------------------------------------------

        [Test]
        public void FindSubtreeUsages_ListsEveryReferenceWithItsNodeId_AndTheDeclaringFiles()
        {
            WriteParentWithReference();
            Write("Trees/other.aibt", @"{ ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.mcp.other"", ""name"": ""Other"", ""root"": ""main"",
  ""blackboard"": { ""hits"": { ""type"": ""Int32"", ""default"": 0 } },
  ""nodes"": { ""main"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""b"", ""a""] },
    ""a"": { ""subtree"": { ""treeId"": ""tests.p11.mcp.strike"", ""interfaceVersion"": 1, ""keys"": { ""hits"": ""hits"" } } },
    ""b"": { ""subtree"": { ""treeId"": ""tests.p11.mcp.strike"", ""interfaceVersion"": 1, ""keys"": { ""hits"": { ""value"": 2 } } } } } }");

            var usages = Dispatch("find_subtree_usages", new JObject { ["treeId"] = StrikeId }, "Read")["result"];
            Assert.That((string)usages["status"], Is.EqualTo("Found"), usages.ToString());
            Assert.That((string)usages["declaredIn"].Single()["path"], Is.EqualTo("Trees/strike.aibt"));
            Assert.That((int)usages["usageCount"], Is.EqualTo(3));
            Assert.That(usages["usages"].Select(item => (string)item["treeId"] + "#" + (string)item["nodeId"]),
                Is.EqualTo(new[] { FightId + "#left", "tests.p11.mcp.other#a", "tests.p11.mcp.other#b" }));
            Assert.That((int)usages["usages"][2]["subtree"]["keys"]["hits"]["value"], Is.EqualTo(2));

            Assert.That((string)Dispatch("find_subtree_usages", new JObject { ["treeId"] = "tests.p11.mcp.nowhere" }, "Read")["result"]["status"], Is.EqualTo("NotFound"));
            Write("Trees/strike-copy.aibt", Strike);
            Assert.That((string)Dispatch("find_subtree_usages", new JObject { ["treeId"] = StrikeId }, "Read")["result"]["status"], Is.EqualTo("Ambiguous"));
        }

        [Test]
        public void ProjectManifest_ListsEachTreesInterfaceAndReferencedTreeIds()
        {
            WriteParentWithReference();
            var trees = (JArray)Dispatch("get_project_manifest", new JObject(), "Read")["result"]["trees"];
            var strike = trees.Single(item => (string)item["treeId"] == StrikeId);
            Assert.That((int)strike["interface"]["version"], Is.EqualTo(1));
            Assert.That(strike["interface"]["keys"].Values<string>(), Is.EqualTo(new[] { "hits" }));
            Assert.That((JArray)strike["referencedTreeIds"], Is.Empty);
            var fight = trees.Single(item => (string)item["treeId"] == FightId);
            Assert.That(fight["interface"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(fight["referencedTreeIds"].Values<string>(), Is.EqualTo(new[] { StrikeId }));
        }

        [Test]
        public void InspectTree_Expanded_IsTheExpanderOutput_AndATreeWithoutSubtreesIsItsOwnExpansion()
        {
            WriteParentWithReference();
            var plain = Dispatch("inspect_tree", new JObject { ["treeId"] = StrikeId }, "Read")["result"];
            Assert.That(plain["expansion"], Is.Null, "Without expanded=true the result is unchanged.");

            var result = Dispatch("inspect_tree", new JObject { ["treeId"] = FightId, ["expanded"] = true }, "Read")["result"];
            var catalog = ProjectNodeCatalogCache.Current;
            var expected = SubtreeExpander.Expand(Read("Trees/fight.aibt"),
                new InMemorySubtreeResolver().Add(Read("Trees/strike.aibt"), "Trees/strike.aibt"), catalog.Registry,
                McpSubtrees.StructuralPolicy.CreateValidationOptions("Trees/fight.aibt"), catalog.RegisteredTypes);
            Assert.That(expected.Success, Is.True, string.Join("; ", expected.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
            var expectedJson = JObject.Parse(System.Text.Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(expected.Document, catalog.RegisteredTypes).Utf8));
            Assert.That(JToken.DeepEquals(result["expansion"]["tree"], expectedJson), Is.True, result["expansion"].ToString());
            Assert.That((int)result["expansion"]["tree"]["formatVersion"], Is.EqualTo(2));
            Assert.That((bool)result["expansion"]["identity"], Is.False);
            Assert.That(result["expansion"]["referencedTreeIds"].Values<string>(), Is.EqualTo(new[] { StrikeId }));

            Write("Trees/plain.aibt", Fight.Replace(FightId, "tests.p11.mcp.plain"));
            var identity = Dispatch("inspect_tree", new JObject { ["treeId"] = "tests.p11.mcp.plain", ["expanded"] = true }, "Read")["result"];
            Assert.That((bool)identity["expansion"]["identity"], Is.True);
            Assert.That(JToken.DeepEquals(identity["expansion"]["tree"], identity["tree"]), Is.True);
            Assert.That((string)identity["expansion"]["contentHash"], Is.EqualTo((string)identity["contentHash"]));
        }

        // ---- validate / compile / prepare_runtime ----------------------------------------------------

        [TestCase("missing", "AIBT3020")]
        [TestCase("duplicate", "AIBT3021")]
        [TestCase("cycle", "AIBT3022")]
        public void ValidateAndCompile_ReportTheExpansionDiagnostic_InsteadOfThrowing(string problem, string code)
        {
            WriteParentWithReference();
            if (problem == "missing") File.Delete(Path.Combine(_assetsDir, "Trees/strike.aibt"));
            if (problem == "duplicate") Write("Trees/strike-copy.aibt", Strike);
            if (problem == "cycle")
            {
                // strike uses fight, fight uses strike.
                Write("Trees/strike.aibt", Strike.Replace(@"""children"": [""windup"", ""hit"", ""count""]", @"""children"": [""windup"", ""hit"", ""count"", ""loop""]")
                    .Replace(@"""count"": { ""displayName""", @"""loop"": { ""subtree"": { ""treeId"": ""tests.p11.mcp.fight"", ""interfaceVersion"": 1, ""keys"": {} } },
    ""count"": { ""displayName"""));
            }

            var validated = Dispatch("validate", new JObject { ["treeId"] = FightId }, "Compilation");
            Assert.That((bool)validated["result"]["valid"], Is.False, validated.ToString());
            Assert.That(Codes(validated), Does.Contain(code));
            Assert.That((bool)validated["result"]["subtrees"]["expanded"], Is.False);
            var compiled = Dispatch("compile", new JObject { ["treeId"] = FightId }, "Compilation");
            Assert.That((bool)compiled["result"]["success"], Is.False, compiled.ToString());
            Assert.That(Codes(compiled), Does.Contain(code));
        }

        [Test]
        public void PrepareRuntime_ResolvesTheSubtree_AndReportsAMissingOneAsAIBT3020()
        {
            var catalog = ProjectNodeCatalog.RuntimeCatalogTypes().FirstOrDefault(type => type.FullName == "AIBT.Authoring.BuiltInLeaves.BuiltInLeafCatalog");
            Assume.That(catalog, Is.Not.Null, "The package's built-in leaf catalog is a Player catalog.");
            Write("Trees/pause.aibt", @"{ ""format"": ""aibt.tree"", ""formatVersion"": 2, ""treeId"": ""tests.p11.mcp.pause"", ""name"": ""Pause"", ""root"": ""wait"",
  ""nodes"": { ""wait"": { ""type"": ""aibt.stdlib.wait"", ""typeVersion"": 1, ""parameters"": { ""ticks"": 2 } } } }");
            Write("Trees/patrol.aibt", @"{ ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.mcp.patrol"", ""name"": ""Patrol"", ""root"": ""main"",
  ""nodes"": { ""main"": { ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""first"", ""second""] },
    ""first"": { ""subtree"": { ""treeId"": ""tests.p11.mcp.pause"", ""interfaceVersion"": 1, ""keys"": {} } },
    ""second"": { ""subtree"": { ""treeId"": ""tests.p11.mcp.pause"", ""interfaceVersion"": 1, ""keys"": {} } } } }");
            JObject Prepare() => Dispatch("prepare_runtime", new JObject
            {
                ["treeId"] = "tests.p11.mcp.patrol", ["expectedHash"] = Hash("tests.p11.mcp.patrol"), ["catalog"] = catalog.AssemblyQualifiedName,
                ["outputPath"] = "Assets/patrol.asset", ["dryRun"] = true,
            }, "Compilation");

            var prepared = Prepare();
            Assert.That((bool)prepared["result"]["success"], Is.True, prepared.ToString());
            Assert.That(File.Exists(Path.Combine(_assetsDir, "patrol.asset")), Is.False);

            File.Delete(Path.Combine(_assetsDir, "Trees/pause.aibt"));
            var missing = Prepare();
            Assert.That((bool)missing["result"]["success"], Is.False, missing.ToString());
            Assert.That(Codes(missing), Does.Contain("AIBT3020"));
        }

        [Test]
        public void ATreeWithoutReferences_CompilesThroughMcpExactlyAsThroughTheDirectEntryPoint()
        {
            Write("Trees/fight.aibt", Fight);
            var compiled = Dispatch("compile", new JObject { ["treeId"] = FightId }, "Compilation");
            Assert.That((bool)compiled["result"]["success"], Is.True, compiled.ToString());
            var catalog = ProjectNodeCatalogCache.Current;
            var policy = AIBT.Mcp.Verification.McpVerificationJson.ToCompilationPolicy(ReadPolicy());
            var direct = ReferenceCompiler.Compile(Read("Trees/fight.aibt"), catalog.Registry,
                new ReferenceCompilerOptions("Trees/fight.aibt", policy, new CompiledCompilerVersion(1, 0, 0, 0)));
            Assert.That(direct.Success, Is.True);
            Assert.That((string)compiled["result"]["contentHash"], Is.EqualTo(direct.Program.Header.CompiledContentHash.HexadecimalValue));
        }

        // ---- the P6-006 payload tools are unchanged --------------------------------------------------

        [Test]
        public void ExtractSubtreeAndInlineSubtree_KeepTheirShape_AndRoundTripAReferenceNode()
        {
            WriteParentWithReference();
            var original = Hash(FightId);
            var extracted = Dispatch("extract_subtree", new JObject { ["treeId"] = FightId, ["expectedHash"] = original, ["nodeId"] = "left" }, "SemanticEdit");
            Assert.That((bool)extracted["result"]["accepted"], Is.True, extracted.ToString());
            var payload = (JArray)extracted["result"]["extractedNodes"];
            Assert.That((string)payload.Single()["subtree"]["treeId"], Is.EqualTo(StrikeId), "A reference node's payload carries its subtree object.");
            Assert.That(File.Exists(Path.Combine(_assetsDir, "Trees/strike.aibt")), Is.True, "extract_subtree creates and deletes no tree file.");

            var inlined = Dispatch("inline_subtree", new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = (string)extracted["result"]["contentHash"], ["parentId"] = "fight", ["subtreeRootId"] = "left",
                ["nodes"] = payload, ["insertIndex"] = (int)extracted["result"]["attachment"]["insertIndex"],
            }, "SemanticEdit");
            Assert.That((bool)inlined["result"]["accepted"], Is.True, inlined.ToString());
            Assert.That((string)inlined["result"]["contentHash"], Is.EqualTo(original), "The round trip restores the reference exactly.");
        }

        // ---- catalog and permissions -----------------------------------------------------------------

        [TestCase("set_subtree_keys", "SemanticEdit")]
        [TestCase("extract_to_subtree", "SemanticEdit")]
        [TestCase("unpack_subtree", "SemanticEdit")]
        [TestCase("find_subtree_usages", "Read")]
        public void EachNewToolIsCatalogued_AndDeniedWithoutItsCategory(string tool, string category)
        {
            Assert.That(McpBuiltInTools.BridgeToolNames, Does.Contain(tool));
            var description = McpOperationCatalog.Describe(tool);
            Assert.That((string)description["permission"], Is.EqualTo(category));
            Assert.That((string)description["sideEffect"], Is.EqualTo(category == "Read" ? "none" : "semantic-source"));

            WriteParentWithReference();
            var before = Directory.GetFiles(_assetsDir, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var args = new JObject
            {
                ["treeId"] = FightId, ["expectedHash"] = Hash(FightId), ["nodeId"] = "left", ["keys"] = new JObject { ["hits"] = new JObject { ["value"] = 1 } },
                ["newTreeId"] = "tests.p11.mcp.left", ["path"] = "Trees/left.aibt",
            };
            foreach (var granted in new[] { null, category == "Read" ? "Compilation" : "Read" })
            {
                var denied = Dispatch(tool, args, granted);
                Assert.That((string)denied["error"]["code"], Is.EqualTo("AIBT9012"), tool + " with " + (granted ?? "nothing") + ": " + denied);
            }
            var after = Directory.GetFiles(_assetsDir, "*", SearchOption.AllDirectories);
            Assert.That(after, Is.EquivalentTo(before.Keys));
            foreach (var path in after) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before[path]), path);
        }

        [Test]
        public void TheExistingToolsKeepTheirPermissions_AndOnlyGainOptionalArguments()
        {
            Assert.That((string)McpOperationCatalog.Describe("add_node")["permission"], Is.EqualTo("SemanticEdit"));
            Assert.That((string)McpOperationCatalog.Describe("inspect_tree")["permission"], Is.EqualTo("Read"));
            Assert.That(McpOperationCatalog.Describe("add_node")["inputSchema"]["required"].Values<string>(), Is.EqualTo(new[] { "treeId", "expectedHash", "parentId", "node" }));
            Assert.That(McpOperationCatalog.Describe("inspect_tree")["inputSchema"]["required"].Values<string>(), Is.EqualTo(new[] { "treeId" }));
            Assert.That(McpOperationCatalog.Describe("extract_subtree")["inputSchema"]["required"].Values<string>(), Is.EqualTo(new[] { "treeId", "expectedHash", "nodeId" }));
            Assert.That(McpOperationCatalog.Describe("inline_subtree")["inputSchema"]["required"].Values<string>(),
                Is.EqualTo(new[] { "treeId", "expectedHash", "parentId", "subtreeRootId", "nodes" }));
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private void WriteParentWithReference(string extraNode = "", string referenceId = "left")
        {
            Write("Trees/strike.aibt", Strike);
            Write("Trees/fight.aibt", @"{
  ""format"": ""aibt.tree"", ""formatVersion"": 3, ""treeId"": ""tests.p11.mcp.fight"", ""name"": ""Fight"", ""root"": ""fight"",
  ""blackboard"": { ""hits"": { ""type"": ""Int32"", ""default"": 0 }, ""heat"": { ""type"": ""Float32"", ""default"": 0 } },
  ""nodes"": {
    ""fight"": { ""displayName"": ""Fight"", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""taunt"", """ + referenceId + @""", ""finisher""] },
    ""taunt"": { ""displayName"": ""Taunt"", ""type"": ""aibt.unity.wait-seconds"", ""typeVersion"": 1, ""parameters"": { ""seconds"": 0.5 } },
    """ + referenceId + @""": { ""displayName"": ""Left strike"", ""subtree"": { ""treeId"": ""tests.p11.mcp.strike"", ""interfaceVersion"": 1, ""keys"": { ""hits"": ""hits"" } } },
    " + extraNode + @"
    ""finisher"": { ""displayName"": ""Finisher"", ""type"": ""aibt.core.memory-sequence"", ""typeVersion"": 1, ""children"": [""slam""] },
    ""slam"": { ""displayName"": ""Slam"", ""type"": ""aibt.unity.increment-int"", ""typeVersion"": 1, ""parameters"": { ""key"": ""hits"", ""amount"": 3 } }
  }
}");
        }

        private void WritePolicy(bool requireTreeDescription)
        {
            Directory.CreateDirectory(Path.Combine(_projectRoot, ".aibt"));
            File.WriteAllText(Path.Combine(_projectRoot, ".aibt", "policy.json"), new JObject
            {
                ["format"] = "aibt.policy", ["formatVersion"] = 1, ["allowManagedNodes"] = true, ["allowMainThreadNodes"] = true,
                ["requireTreeDescription"] = requireTreeDescription, ["requireNodeDescriptions"] = false, ["blackboardNaming"] = "any",
                ["requireDeterministicNodes"] = false, ["allowSideEffects"] = true, ["unreachableNodes"] = "error",
                ["supportsAgentScope"] = false, ["supportsSharedScope"] = false,
                ["forbiddenNodeTypes"] = new JArray(), ["warningsAsErrors"] = new JArray(),
                ["performance"] = new JObject { ["forbidUnboundedRepeaters"] = false, ["requireEventDrivenServices"] = false },
            }.ToString());
        }

        private ProjectPolicySnapshot ReadPolicy()
        {
            Assert.That(ProjectPolicySnapshot.TryReadFile(Path.Combine(_projectRoot, ".aibt", "policy.json"), out var snapshot, out var error), Is.True, error?.Message);
            return snapshot;
        }

        private void Write(string relativePath, string json)
        {
            var path = Path.Combine(_assetsDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var parsed = CanonicalTreeJson.Parse(json, relativePath);
            Assert.That(parsed.Success, Is.True, relativePath + ": " + string.Join("; ", parsed.Diagnostics.Select(d => d.Code.Value + " " + d.Message)));
            File.WriteAllBytes(path, CanonicalTreeJson.Serialize(parsed.Document).Utf8);
        }

        private TreeDocument Read(string relativePath)
        {
            var parsed = CanonicalTreeJson.Parse(File.ReadAllText(Path.Combine(_assetsDir, relativePath)), relativePath);
            Assert.That(parsed.Success, Is.True, string.Join("; ", parsed.Diagnostics));
            return parsed.Document;
        }

        private string Hash(string treeId)
        {
            var inspected = Dispatch("inspect_tree", new JObject { ["treeId"] = treeId }, "Read");
            Assert.That(inspected["result"], Is.Not.Null, inspected.ToString());
            return (string)inspected["result"]["contentHash"];
        }

        private static IEnumerable<string> Codes(JObject response)
            => (response["result"]?["diagnostics"] as JArray ?? new JArray()).Select(item => (string)item["code"]);

        private JObject Dispatch(string tool, JObject args, string grantedCategory)
            => JObject.Parse(McpToolDispatcher.Dispatch(new JObject
            {
                ["tool"] = tool, ["args"] = args,
                ["grantedCategories"] = grantedCategory == null ? new JArray() : new JArray(grantedCategory),
            }.ToString(Newtonsoft.Json.Formatting.None), _assetsDir));
    }
}
