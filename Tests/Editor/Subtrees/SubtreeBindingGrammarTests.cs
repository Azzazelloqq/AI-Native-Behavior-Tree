using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using NUnit.Framework;
using static AIBT.Tests.Editor.Subtrees.SubtreeTestKit;

namespace AIBT.Tests.Editor.Subtrees
{
    /// <summary>
    /// ADR-P11-001 owner decision 9: a generated binding value names a blackboard key, so it uses the
    /// blackboard key ID grammar (an expanded private key is <c>r:key</c>); every value the member
    /// grammar accepted stays valid, and binding IDs keep the member grammar.
    /// </summary>
    public sealed class SubtreeBindingGrammarTests
    {
        [TestCase("live-target")]
        [TestCase("a.b")]
        [TestCase("a:armed")]
        [TestCase("outer:inner:hits")]
        [TestCase("Alarm.Level")]
        public void BindingValue_AcceptsBlackboardKeyIds(string value)
        {
            var map = new NodeBindingMap(new[] { Pair("live-target", value) });
            Assert.That(map.Values["live-target"], Is.EqualTo(value));
        }

        [Test]
        public void BindingValue_KeepsEveryMemberGrammarValue()
        {
            // The member grammar allows 255 characters; the union keeps those values valid.
            var longMember = string.Join(".", Enumerable.Repeat(new string('a', 50), 5));
            Assert.That(longMember.Length, Is.GreaterThan(128));
            Assert.That(new NodeBindingMap(new[] { Pair("live-target", longMember) }).Values["live-target"], Is.EqualTo(longMember));
        }

        [TestCase("")]
        [TestCase("a b")]
        [TestCase("-x")]
        [TestCase(":x")]
        [TestCase("a/b")]
        [TestCase("a#b")]
        public void BindingValue_RejectsInvalidIds(string value)
        {
            var exception = Assert.Throws<ArgumentException>(() => new NodeBindingMap(new[] { Pair("live-target", value) }));
            Assert.That(exception.Message, Does.Contain("blackboard key ID grammar"));
        }

        [Test]
        public void BindingValue_RejectsAnOverlongKeyIdOutsideTheMemberGrammar()
            => Assert.Throws<ArgumentException>(() => new NodeBindingMap(new[] { Pair("live-target", "A" + new string('a', 128)) }));

        [TestCase("live:target")]
        [TestCase("Live")]
        [TestCase("a_b")]
        public void BindingId_KeepsTheMemberGrammar(string bindingId)
        {
            var exception = Assert.Throws<ArgumentException>(() => new NodeBindingMap(new[] { Pair(bindingId, "a:armed") }));
            Assert.That(exception.Message, Does.Contain("Binding IDs"));
        }

        [Test]
        public void TreeJson_RoundTripsABindingToAQualifiedKey()
        {
            var json = Q(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11.grammar', 'name': 'grammar', 'root': 'emit',
  'blackboard': { 'a:armed': { 'type': 'Float32', 'typeVersion': 1, 'scope': 'tree', 'default': 0.0 } },
  'nodes': { 'emit': { 'type': 'aibt.tests.any', 'typeVersion': 1, 'bindings': { 'live-target': 'a:armed' } } } }");
            var read = CanonicalTreeJson.Parse(json, "trees/grammar.aibt");
            Assert.That(read.Success, Is.True, Describe(read.Diagnostics));
            Assert.That(NodeById(read.Document, "emit").Bindings.Values["live-target"], Is.EqualTo("a:armed"));

            var written = CanonicalTreeJson.Serialize(read.Document);
            Assert.That(written.Success, Is.True);
            var again = CanonicalTreeJson.Parse(written.Utf8, "trees/grammar.aibt");
            Assert.That(again.Success, Is.True, Describe(again.Diagnostics));
            Assert.That(CanonicalTreeJson.Serialize(again.Document).Utf8, Is.EqualTo(written.Utf8));
            Assert.That(CanonicalTreeJson.Serialize(again.Document).SemanticHash, Is.EqualTo(written.SemanticHash));
        }

        [TestCase("a b")]
        [TestCase("-x")]
        public void TreeJson_RejectsAnInvalidBindingValue(string value)
        {
            var json = Q(@"{ 'format': 'aibt.tree', 'formatVersion': 2, 'treeId': 'tests.p11.grammar', 'name': 'grammar', 'root': 'emit',
  'nodes': { 'emit': { 'type': 'aibt.tests.any', 'typeVersion': 1, 'bindings': { 'live-target': '" + value + "' } } } }");
            var read = CanonicalTreeJson.Parse(json, "trees/grammar.aibt");

            Assert.That(read.Success, Is.False);
            var error = read.Diagnostics.Single(item => item.Severity == DiagnosticSeverity.Error);
            Assert.That(error.Code, Is.EqualTo(TreeJsonDiagnosticCodes.SchemaViolation));
            Assert.That(error.Location.JsonPointer, Is.EqualTo("/nodes/emit/bindings/live-target"));
        }

        private static KeyValuePair<string, string> Pair(string bindingId, string keyId) => new KeyValuePair<string, string>(bindingId, keyId);
    }
}
