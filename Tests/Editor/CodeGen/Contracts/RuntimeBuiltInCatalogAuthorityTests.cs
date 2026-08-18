using System;
using System.Reflection;
using AIBT.Burst;
using NUnit.Framework;

namespace AIBT.Tests.CodeGen.Contracts
{
    public sealed class RuntimeBuiltInCatalogAuthorityTests
    {
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags StaticAny = StaticNonPublic | BindingFlags.Public;

        [Test]
        public void Authority_ExactlyMatchesCanonicalAuthoringBuiltIns()
        {
            var verifier = LoadVerifier();
            var authority = LoadAuthority();
            var canonicalJson = (string)Invoke(verifier, "RebuildManifestRegistryJson");
            var canonicalHash = (string)Invoke(verifier, "RebuildNodeRegistryHash");
            var formatVersion = ReadConstant<uint>(authority, "FormatVersion");
            var registryJson = ReadConstant<string>(authority, "ManifestRegistryJson");
            var registryHash = ReadConstant<string>(authority, "NodeRegistryHash");

            Assert.That(authority.IsNotPublic, Is.True, "Authority metadata must not expand the public Burst ABI.");
            Assert.That(authority.GetFields(StaticAny | BindingFlags.DeclaredOnly), Has.Length.EqualTo(3));
            Assert.That(authority.GetMethods(StaticAny | BindingFlags.DeclaredOnly), Is.Empty,
                "Runtime authority must remain metadata-only and cannot own dispatch cases.");
            Assert.That(formatVersion, Is.EqualTo(1u));
            Assert.That(registryJson, Is.EqualTo(canonicalJson));
            Assert.That(registryHash, Is.EqualTo(canonicalHash));
            Assert.That(Invoke(verifier, "Validate", registryJson, registryHash), Is.Null);
        }

        [Test]
        public void AuthorityVerifier_RejectsStaleRegistryBytesAndHash()
        {
            var verifier = LoadVerifier();
            var authority = LoadAuthority();
            var registryJson = ReadConstant<string>(authority, "ManifestRegistryJson");
            var registryHash = ReadConstant<string>(authority, "NodeRegistryHash");

            AssertInvocationFails(verifier, registryJson + " ", registryHash);
            AssertInvocationFails(verifier, registryJson, new string('0', 64));
        }

        private static Type LoadAuthority()
        {
            return typeof(BurstContextResult).Assembly.GetType(
                "AIBT.Burst.RuntimeBuiltInCatalogAuthority",
                throwOnError: true);
        }

        private static Type LoadVerifier()
        {
            return Type.GetType(
                "AIBT.Authoring.RuntimeBuiltInCatalogAuthorityVerifier, AIBT.Authoring",
                throwOnError: true);
        }

        private static object Invoke(Type verifier, string methodName, params object[] arguments)
        {
            var method = verifier.GetMethod(methodName, StaticNonPublic);
            Assert.That(method, Is.Not.Null, methodName + " is missing.");
            return method.Invoke(null, arguments);
        }

        private static T ReadConstant<T>(Type authority, string fieldName)
        {
            var field = authority.GetField(fieldName, StaticAny);
            Assert.That(field, Is.Not.Null, fieldName + " is missing.");
            Assert.That(field.IsLiteral, Is.True, fieldName + " must remain const metadata.");
            return (T)field.GetRawConstantValue();
        }

        private static void AssertInvocationFails(Type verifier, string registryJson, string hash)
        {
            var exception = Assert.Throws<TargetInvocationException>(() =>
                Invoke(verifier, "Validate", registryJson, hash));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }
}
