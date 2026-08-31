using System.Collections.Generic;
using AIBT.Mcp;
using AIBT.Mcp.CustomTools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.CustomTools
{
    public sealed class CustomMcpToolProviderDiscoveryTests
    {
        [Test]
        public void ValidDistinctProvidersAreAllAccepted()
        {
            var a = new FakeProvider("aibt_custom_a", McpPermissionCategory.Read);
            var b = new FakeProvider("aibt_custom_b", McpPermissionCategory.SemanticEdit);

            var result = CustomMcpToolProviderDiscovery.Build(new ICustomMcpToolProvider[] { a, b });

            Assert.That(result.Diagnostics.Count, Is.EqualTo(0));
            Assert.That(result.ByToolName.Count, Is.EqualTo(2));
            Assert.That(result.ByToolName["aibt_custom_a"], Is.SameAs(a));
            Assert.That(result.ByToolName["aibt_custom_b"], Is.SameAs(b));
        }

        [Test]
        public void DuplicateToolNameIsRejectedWithAStructuredDiagnostic()
        {
            var first = new FakeProvider("aibt_custom_dup", McpPermissionCategory.Read);
            var second = new FakeProvider("aibt_custom_dup", McpPermissionCategory.SemanticEdit);

            var result = CustomMcpToolProviderDiscovery.Build(new ICustomMcpToolProvider[] { first, second });

            Assert.That(result.ByToolName.Count, Is.EqualTo(1), "Exactly one of the two colliding providers keeps the name.");
            var codes = new List<string>();
            foreach (var diagnostic in result.Diagnostics)
            {
                codes.Add(diagnostic.Code.Value);
            }
            Assert.That(codes, Does.Contain("AIBT9038"));
        }

        [Test]
        public void NameCollidingWithABuiltInToolIsRejected()
        {
            var provider = new FakeProvider("add_node", McpPermissionCategory.SemanticEdit);

            var result = CustomMcpToolProviderDiscovery.Build(new ICustomMcpToolProvider[] { provider });

            Assert.That(result.ByToolName.Count, Is.EqualTo(0));
            var codes = new List<string>();
            foreach (var diagnostic in result.Diagnostics)
            {
                codes.Add(diagnostic.Code.Value);
            }
            Assert.That(codes, Does.Contain("AIBT9039"));
        }

        [Test]
        public void OneBadProviderDoesNotBlockOtherwiseValidOnes()
        {
            var good = new FakeProvider("aibt_custom_good", McpPermissionCategory.Read);
            var bad = new FakeProvider("search_nodes", McpPermissionCategory.Read);

            var result = CustomMcpToolProviderDiscovery.Build(new ICustomMcpToolProvider[] { good, bad });

            Assert.That(result.ByToolName.Count, Is.EqualTo(1));
            Assert.That(result.ByToolName.ContainsKey("aibt_custom_good"), Is.True);
            Assert.That(result.Diagnostics.Count, Is.EqualTo(1));
        }

        private sealed class FakeProvider : ICustomMcpToolProvider
        {
            internal FakeProvider(string toolName, McpPermissionCategory category)
            {
                ToolName = toolName;
                PermissionCategory = category;
            }

            public string ToolName { get; }

            public string Description => "fake";

            public JObject InputSchema => new JObject();

            public JObject OutputSchema => null;

            public McpPermissionCategory PermissionCategory { get; }

            public IReadOnlyList<string> SideEffects => System.Array.Empty<string>();

            public bool SupportsCancellation => false;

            public bool SupportsDryRun => false;

            public JObject Invoke(string projectRoot, JObject args, bool dryRun) => new JObject();
        }
    }
}
