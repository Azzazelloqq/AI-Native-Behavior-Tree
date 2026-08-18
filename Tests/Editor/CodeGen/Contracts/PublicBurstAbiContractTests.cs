using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using AIBT.Burst;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace AIBT.Tests.CodeGen.Contracts
{
    public sealed class PublicBurstAbiContractTests
    {
        [Test]
        public void PublicSurface_MatchesAcceptedV2ManifestLineForLine()
        {
            var expected = ReadFixture("ExpectedPublicAbiV2.txt");

            var actual = BuildPublicAbiManifest();

            Assert.That(actual, Is.EqualTo(expected), FirstDifference(expected, actual));
        }

        [Test]
        public void PublicSurfaceV2_ChangesOnlyEnterAndTickOpaqueSizePins()
        {
            var v1 = ReadFixture("ExpectedPublicAbiV1.txt");
            var v2 = ReadFixture("ExpectedPublicAbiV2.txt");
            var v1Lines = v1.TrimEnd('\n').Split('\n');
            var v2Lines = v2.TrimEnd('\n').Split('\n');

            Assert.That(v1Lines, Has.Length.EqualTo(352), "The independently accepted ABI v1 snapshot must remain intact.");
            Assert.That(v2Lines, Has.Length.EqualTo(352), "ABI v2 must not add or remove a public record.");

            const string enterV1 = "T|struct|AIBT.Burst.BurstEnterContext|abstract=False|sealed=True|layout=Sequential,pack=8,size=24,charset=Ansi";
            const string enterV2 = "T|struct|AIBT.Burst.BurstEnterContext|abstract=False|sealed=True|layout=Sequential,pack=8,size=0,charset=Ansi";
            const string tickV1 = "T|struct|AIBT.Burst.BurstTickContext|abstract=False|sealed=True|layout=Sequential,pack=8,size=24,charset=Ansi";
            const string tickV2 = "T|struct|AIBT.Burst.BurstTickContext|abstract=False|sealed=True|layout=Sequential,pack=8,size=0,charset=Ansi";

            var differences = Enumerable.Range(0, v1Lines.Length)
                .Where(index => !string.Equals(v1Lines[index], v2Lines[index], StringComparison.Ordinal))
                .ToArray();
            Assert.That(differences, Has.Length.EqualTo(2));
            Assert.That(v1Lines[differences[0]], Is.EqualTo(enterV1));
            Assert.That(v2Lines[differences[0]], Is.EqualTo(enterV2));
            Assert.That(v1Lines[differences[1]], Is.EqualTo(tickV1));
            Assert.That(v2Lines[differences[1]], Is.EqualTo(tickV2));

            var normalizedV2 = v2.Replace(enterV2, enterV1).Replace(tickV2, tickV1);
            Assert.That(normalizedV2, Is.EqualTo(v1), "Every public signature, enum value, and non-context layout record must remain ABI v1-exact.");
        }

        [Test]
        public void PinnedLayouts_ContextPrefixes_AndDefaultOpaqueValues_FailClosed()
        {
            Assert.That(Marshal.SizeOf<BlackboardReadHandle<int>>(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<BurstEnterContext>("_validationToken").ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<BurstEnterContext>("_randomState").ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<BurstEnterContext>("_randomIncrement").ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<BurstTickContext>("_validationToken").ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<BurstTickContext>("_randomState").ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<BurstTickContext>("_randomIncrement").ToInt32(), Is.EqualTo(16));
            Assert.That(Marshal.SizeOf<BurstCatalogValidationResult>(), Is.EqualTo(4));
            Assert.That(Marshal.SizeOf<BurstExecutionResult>(), Is.EqualTo(16));

            var tick = default(BurstTickContext);
            Assert.That(tick.TryNextUInt32(0u, out var random), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(random, Is.Zero);
            Assert.That(BurstGeneratedRuntimeBridge.TryGetCatalogHandshake(default, out var handshake), Is.EqualTo(BurstContextResult.InvalidHandle));
            Assert.That(handshake, Is.EqualTo(default(BurstCatalogHandshake)));
        }

        [Test]
        public void ProductionGenerator_EmitsOnlyApprovedMetadataBoundary()
        {
            Assert.That(ContractTestShard.IsUsable, Is.True);
            Assert.That(ContractTestShard.AbiVersion, Is.EqualTo(2u));

            var shardMembers = typeof(ContractTestShard)
                .GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(member => member.MemberType == MemberTypes.Field || member.MemberType == MemberTypes.NestedType)
                .Select(member => member.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.That(shardMembers, Is.EqualTo(new[] { "AbiVersion", "AibtGeneratedMetadata", "BurstAccess", "IsUsable" }));
        }

        private static string BuildPublicAbiManifest()
        {
            var lines = new List<string>();
            var types = typeof(BurstContextResult).Assembly.GetTypes()
                .Where(type => type.IsPublic && type.Namespace == "AIBT.Burst")
                .OrderBy(ReflectionTypeName, StringComparer.Ordinal);
            foreach (var type in types)
            {
                var kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class";
                var typeLine = "T|" + kind + "|" + ReflectionTypeName(type) + "|abstract=" + type.IsAbstract + "|sealed=" + type.IsSealed;
                if (type.IsGenericTypeDefinition) typeLine += "|" + GenericConstraintManifest(type.GetGenericArguments());
                var layout = type.StructLayoutAttribute;
                if (layout != null)
                {
                    // Mono reports effective default pack/size values while CoreCLR
                    // reports declaration defaults. Normalize only declarations whose
                    // layout is intentionally left unspecified by the accepted ABI.
                    var explicitLayout = ExplicitLayoutTypes.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type);
                    var manifestPack = explicitLayout ? layout.Pack : 0;
                    var manifestSize = explicitLayout ? layout.Size : type == typeof(BurstExitContext) ? 1 : 0;
                    typeLine += "|layout=" + layout.Value + ",pack=" + manifestPack + ",size=" + manifestSize + ",charset=" + layout.CharSet;
                }
                var usage = type.GetCustomAttribute<AttributeUsageAttribute>();
                if (usage != null) typeLine += "|usage=" + usage.ValidOn + ",multi=" + usage.AllowMultiple + ",inherited=" + usage.Inherited;
                lines.Add(typeLine);
                if (type.IsEnum)
                {
                    lines.Add("E|underlying=" + ReflectionTypeName(Enum.GetUnderlyingType(type)));
                    foreach (var name in Enum.GetNames(type)) lines.Add("E|" + name + "=" + Convert.ToUInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture));
                }
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(FieldManifest, StringComparer.Ordinal)) lines.Add(FieldManifest(field));
                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).OrderBy(ConstructorManifest, StringComparer.Ordinal)) lines.Add(ConstructorManifest(ctor));
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(PropertyManifest, StringComparer.Ordinal)) lines.Add(PropertyManifest(property));
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(method => !method.IsSpecialName).OrderBy(MethodManifest, StringComparer.Ordinal)) lines.Add(MethodManifest(method));
                foreach (var @event in type.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(item => item.Name, StringComparer.Ordinal))
                    lines.Add("V|" + (IsStatic(@event.AddMethod) ? "static|" : "instance|") + @event.Name + "|" + ReflectionTypeName(@event.EventHandlerType));
            }
            return string.Join("\n", lines) + "\n";
        }

        private static string ReadFixture(string fileName)
        {
            var package = PackageInfo.FindForAssembly(typeof(PublicBurstAbiContractTests).Assembly);
            var packageRoot = package == null ? Path.Combine(Application.dataPath, "AIBT") : package.resolvedPath;
            var value = File.ReadAllText(Path.Combine(packageRoot, "Tests/Editor/CodeGen/Contracts", fileName)).Replace("\r\n", "\n");
            return value.EndsWith("\n", StringComparison.Ordinal) ? value : value + "\n";
        }

        private static readonly HashSet<Type> ExplicitLayoutTypes = new HashSet<Type>
        {
            typeof(AsyncOperationHandle<,>),
            typeof(BlackboardReadHandle<>),
            typeof(BlackboardReadWriteHandle<>),
            typeof(BlackboardWriteHandle<>),
            typeof(BurstCatalogFingerprint),
            typeof(BurstCatalogHandshake),
            typeof(BurstCatalogValidationResult),
            typeof(BurstEnterContext),
            typeof(BurstExecutionResult),
            typeof(BurstHash256),
            typeof(BurstTickContext),
            typeof(CommandHandle<>),
            typeof(CompletionHandle<>),
            typeof(SnapshotReadHandle<>)
        };

        private static string FieldManifest(FieldInfo field) => "F|" + (field.IsStatic ? "static|" : "instance|")
            + (field.IsLiteral ? "const|" : field.IsInitOnly ? "readonly|" : "mutable|") + field.Name + "|" + ReflectionTypeName(field.FieldType)
            + (field.IsLiteral ? "|" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : string.Empty);
        private static string ConstructorManifest(ConstructorInfo constructor) => "C|" + ParameterManifest(constructor.GetParameters());
        private static string PropertyManifest(PropertyInfo property) => "P|" + (IsStatic(property.GetMethod ?? property.SetMethod) ? "static|" : "instance|")
            + property.Name + "|" + ReflectionTypeName(property.PropertyType) + "|get=" + (property.GetMethod?.IsPublic == true)
            + "|set=" + (property.SetMethod?.IsPublic == true) + "|index=" + ParameterManifest(property.GetIndexParameters());
        private static string MethodManifest(MethodInfo method) => "M|" + (method.IsStatic ? "static|" : "instance|") + method.Name
            + "|return=" + ReflectionTypeName(method.ReturnType) + "|" + (method.IsGenericMethodDefinition ? GenericConstraintManifest(method.GetGenericArguments()) : "generic=0")
            + "|params=" + ParameterManifest(method.GetParameters());
        private static string ParameterManifest(IEnumerable<ParameterInfo> parameters) => string.Join(",", parameters.Select(parameter =>
        {
            var type = parameter.ParameterType;
            var modifier = parameter.IsOut ? "out" : type.IsByRef && parameter.IsIn ? "in" : type.IsByRef ? "ref" : "value";
            if (type.IsByRef) type = type.GetElementType();
            return modifier + ":" + ReflectionTypeName(type) + ":" + parameter.Name + ":optional=" + parameter.IsOptional
                + (parameter.HasDefaultValue ? ":default=" + Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) : string.Empty);
        }));
        private static string GenericConstraintManifest(IEnumerable<Type> arguments) => "generic=" + string.Join(";", arguments.Select(argument =>
            argument.Name + ":" + argument.GenericParameterAttributes + ":" + string.Join("&", argument.GetGenericParameterConstraints().Select(ReflectionTypeName).OrderBy(value => value, StringComparer.Ordinal))));
        private static string ReflectionTypeName(Type type)
        {
            if (type.IsByRef) return ReflectionTypeName(type.GetElementType()) + "&";
            if (type.IsArray) return ReflectionTypeName(type.GetElementType()) + "[]";
            if (type.IsGenericParameter) return "!" + type.Name;
            if (!type.IsGenericType) return type.FullName ?? type.Name;
            var definition = type.GetGenericTypeDefinition();
            var name = definition.FullName ?? definition.Name;
            return name.Substring(0, name.IndexOf('`')) + "<" + string.Join(",", type.GetGenericArguments().Select(ReflectionTypeName)) + ">";
        }
        private static bool IsStatic(MethodInfo method) => method?.IsStatic == true;
        private static string FirstDifference(string expected, string actual)
        {
            var left = expected.Split('\n');
            var right = actual.Split('\n');
            for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
            {
                var expectedLine = index < left.Length ? left[index] : "<missing>";
                var actualLine = index < right.Length ? right[index] : "<missing>";
                if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
                    return "line " + (index + 1) + " expected '" + expectedLine + "' actual '" + actualLine + "'";
            }
            return "unknown manifest difference";
        }
    }

    [AibtCatalogShard("aibt.contract-tests.shard", 1u)]
    public partial struct ContractTestShard { }

    public partial struct ContractTestConfiguration
    {
        [AibtConfigField("enabled", "Bool", 1u)]
        public bool Enabled;
    }

    public partial struct ContractTestMemory
    {
        [AibtMemoryField("count", "UInt32", 1u)]
        public uint Count;
    }

    [AibtNodeDocumentation("Contract test node", "Tests", "Verify analyzer import", "Never use in a tree", "contract-test")]
    [AibtBurstNode(
        "aibt.contract-tests.node",
        1u,
        BurstNodeKind.Condition,
        typeof(ContractTestConfiguration),
        typeof(ContractTestMemory),
        NodeMemoryLifetime.Activation,
        true,
        BurstCancellationMode.NotApplicable,
        BurstNodeCost.Trivial,
        BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
    public partial struct ContractTestNode
    {
        public static void Enter(in ContractTestConfiguration config, ref ContractTestMemory memory, ref BurstEnterContext context) { }
        public static NodeStatus Tick(in ContractTestConfiguration config, ref ContractTestMemory memory, ref BurstTickContext context) => config.Enabled ? NodeStatus.Success : NodeStatus.Failure;
        public static void Abort(in ContractTestConfiguration config, ref ContractTestMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
        public static void Exit(in ContractTestConfiguration config, ref ContractTestMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
    }
}
