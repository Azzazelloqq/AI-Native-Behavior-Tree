using System;
using System.Collections.Generic;
using AIBT.Mcp;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Mcp.Discovery
{
    public sealed class McpPermissionEnforcerTests
    {
        private static readonly McpPermissionCategory[] AllCategories =
            (McpPermissionCategory[])Enum.GetValues(typeof(McpPermissionCategory));

        [TestCaseSource(nameof(AllCategories))]
        public void GrantedCategoryIsAllowed(McpPermissionCategory category)
        {
            var granted = new HashSet<McpPermissionCategory> { category };

            var allowed = McpPermissionEnforcer.Require(granted, category, out var denial);

            Assert.That(allowed, Is.True);
            Assert.That(denial, Is.Null);
        }

        [TestCaseSource(nameof(AllCategories))]
        public void UngrantedCategoryIsRejectedWithAStructuredDiagnostic(McpPermissionCategory category)
        {
            var granted = new HashSet<McpPermissionCategory>(); // nothing granted

            var allowed = McpPermissionEnforcer.Require(granted, category, out var denial);

            Assert.That(allowed, Is.False);
            Assert.That(denial, Is.Not.Null);
            Assert.That(denial.Code.Value, Is.EqualTo("AIBT9012"));
        }

        [Test]
        public void OneGrantedCategoryDoesNotImplyAnother()
        {
            var granted = new HashSet<McpPermissionCategory> { McpPermissionCategory.Read };

            var allowed = McpPermissionEnforcer.Require(granted, McpPermissionCategory.SemanticEdit, out var denial);

            Assert.That(allowed, Is.False, "Read must not imply SemanticEdit.");
            Assert.That(denial, Is.Not.Null);
        }
    }
}
