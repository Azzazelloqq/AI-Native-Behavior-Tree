using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIBT;
using AIBT.Authoring;
using AIBT.CodeGen;
using AIBT.Execution.Burst.Dispatch;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

internal static class Program
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp9);
    private static readonly MetadataReference[] PlatformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable."))
        .Split(Path.PathSeparator)
        .Where(path => !Path.GetFileName(path).StartsWith("AIBT.", StringComparison.Ordinal))
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();
    private static int assertions;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: AIBT.CodeGen.Tests <package-root>");
            return 2;
        }

        var packageRoot = Path.GetFullPath(args[0]);
        var contractSources = new[]
        {
            File.ReadAllText(Path.Combine(packageRoot, "Tools~/Verification/P2/CodeGen/Verifier/RuntimeStubs.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/NativeBurstDispatchContractsV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingContractsV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/BurstDispatchBackingV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/NativeBurstDispatchBindingValidationV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/NativeBurstDispatchCanonicalV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Execution/Burst/Dispatch/BurstDispatchBridgeCoreV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Nodes/Contracts/BurstBindingBridgeCoreV2.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Nodes/Contracts/BurstNodeContracts.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Nodes/Contracts/BurstGeneratedRuntimeBridge.cs"), Encoding.UTF8),
            File.ReadAllText(Path.Combine(packageRoot, "Runtime/Nodes/Contracts/RuntimeBuiltInCatalogAuthority.cs"), Encoding.UTF8)
        };
        var contracts = await CompileAsync("AIBT.Runtime.Contracts", contractSources, Array.Empty<MetadataReference>(), false, false);
        RequireClean(contracts, "production ABI contracts");
        var contractReference = MetadataReference.CreateFromImage(ImmutableArray.Create(contracts.Image));

        VerifyDispatchPrebinding();
        await VerifyValidAndDeterministicGeneration(contractReference);
        await VerifyNodeAttributeArgumentTotality(contractReference);
        await VerifyNullAttributeAndStorageLocations(contractReference);
        await VerifyBlackboardScopeTotality(contractReference);
        await VerifyRegisteredIdentityTotality(contractReference);
        await VerifyNestedRegisteredLayoutOrder(contractReference);
        await VerifyAccessAndCodecGeneration(contractReference);
        await VerifyDiagnosticMatrix(contractReference);
        await VerifyTransitiveCapabilityFlow(contractReference);
        await VerifyForbiddenExceptionFlow(contractReference);
        await VerifyForbiddenUnityApiFlow(contractReference);
        await VerifyBuiltInAuthorityFailures(contractSources);
        await ObserveImportCost(contractReference);

        Console.WriteLine("P2-012 Roslyn matrix passed.");
        Console.WriteLine("Assertions: " + assertions.ToString(CultureInfo.InvariantCulture));
        return 0;
    }

    private static async Task VerifyAccessAndCodecGeneration(MetadataReference contracts)
    {
        const string source = @"
using AIBT;
using AIBT.Burst;
[AibtBurstValue(""aibt.verifier.payload"",1u,""aibt.verifier.payload.schema"")]
public partial struct Payload
{
    [AibtValueField(""asset"",""AssetId"",1u)] public AssetId Asset;
    [AibtValueField(""count"",""Int32"",1u)] public int Count;
    [AibtValueField(""label"",""FixedString32"",1u)] public Unity.Collections.FixedString32Bytes Label;
    [AibtValueField(""vector"",""Float2"",1u)] public Float2Value Vector;
}
public partial struct AccessConfig
{
    [AibtConfigField(""enabled"",""Bool"",1u)] public bool Enabled;
    [AibtConfigField(""limit"",""UInt32"",1u)] public uint Limit;
    [AibtConfigField(""seed"",""UInt64"",1u)] public ulong Seed;
    [AibtConfigField(""read"",""GeneratedHandle"",1u),AibtBlackboardBinding(""read"",BurstBlackboardAccess.Read,BlackboardScope.Tree,""aibt.verifier.payload"",1u)] public BlackboardReadHandle<Payload> Read;
    [AibtConfigField(""write"",""GeneratedHandle"",1u),AibtBlackboardBinding(""write"",BurstBlackboardAccess.Write,BlackboardScope.Tree,""aibt.verifier.payload"",1u)] public BlackboardWriteHandle<Payload> Write;
    [AibtConfigField(""read-write"",""GeneratedHandle"",1u),AibtBlackboardBinding(""read-write"",BurstBlackboardAccess.ReadWrite,BlackboardScope.Tree,""aibt.verifier.payload"",1u)] public BlackboardReadWriteHandle<Payload> ReadWrite;
    [AibtConfigField(""snapshot"",""GeneratedHandle"",1u),AibtSnapshotBinding(""snapshot"",""aibt.verifier.payload"",1u)] public SnapshotReadHandle<Payload> Snapshot;
    [AibtConfigField(""command"",""GeneratedHandle"",1u),AibtCommandBinding(""command"",""aibt.verifier.payload"",1u)] public CommandHandle<Payload> Command;
    [AibtConfigField(""async"",""GeneratedHandle"",1u),AibtAsyncOperationBinding(""async"",""aibt.verifier.payload"",1u,""aibt.verifier.payload"",1u)] public AsyncOperationHandle<Payload,Payload> Async;
    [AibtConfigField(""completion"",""GeneratedHandle"",1u),AibtCompletionBinding(""completion"",""aibt.verifier.payload"",1u)] public CompletionHandle<Payload> Completion;
}
public partial struct AccessMemory
{
    [AibtMemoryField(""operation"",""OperationId"",1u)] public OperationId Operation;
    [AibtMemoryField(""payload"",""aibt.verifier.payload"",1u)] public Payload Payload;
}
[AibtCatalogShard(""aibt.verifier.access-shard"",1u)] public partial struct AccessShard { }
[AibtNodeDocumentation(""Access"",""Tests"",""Use"",""Avoid"",""access"")]
[AibtBurstNode(""aibt.verifier.access"",1u,BurstNodeKind.Action,typeof(AccessConfig),typeof(AccessMemory),NodeMemoryLifetime.Activation,true,BurstCancellationMode.Command,BurstNodeCost.Trivial,BurstNodeStatusMask.Success|BurstNodeStatusMask.Running)]
public partial struct AccessNode
{
    public static void Enter(in AccessConfig config, ref AccessMemory memory, ref BurstEnterContext context)
    {
        Payload value;
        BurstContextResult result = AccessShard.BurstAccess.TryRead(ref context, config.Read, out value);
        result = AccessShard.BurstAccess.TryRead(ref context, config.ReadWrite, out value);
        result = AccessShard.BurstAccess.TryWrite(ref context, config.Write, in value);
        result = AccessShard.BurstAccess.TryWrite(ref context, config.ReadWrite, in value);
        result = AccessShard.BurstAccess.TryReadSnapshot(ref context, config.Snapshot, out value);
        result = AccessShard.BurstAccess.TryEmit(ref context, config.Command, in value);
        result = AccessShard.BurstAccess.TryStart(ref context, config.Async, in value, in value, out memory.Operation);
        BurstCompletionOutcome outcome;
        result = AccessShard.BurstAccess.TryConsume(ref context, config.Completion, memory.Operation, out outcome, out value);
    }
    public static NodeStatus Tick(in AccessConfig config, ref AccessMemory memory, ref BurstTickContext context) => NodeStatus.Success;
    public static void Abort(in AccessConfig config, ref AccessMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason)
    {
        Payload value = memory.Payload;
        AccessShard.BurstAccess.TryCancel(ref context, config.Async, memory.Operation, in value);
    }
    public static void Exit(in AccessConfig config, ref AccessMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
}";
        var result = await CompileAsync("Valid.AccessAndCodec", new[] { source }, new[] { contracts }, true, true);
        RequireClean(result, "access/codec declarations");
        VerifyBoundBurstAccessInvocations(result.Compilation);
        foreach (var required in new[]
        {
            "class BurstAccess", "class BurstCodec", "TryReadValue", "TryWriteValue", "TryReadSnapshot",
            "TryConsume", "TryEmit", "TryStart", "TryCancel", "BlackboardReadWriteHandle",
            "new global::AIBT.AssetId", "new global::AIBT.Float2Value", "fixedValue"
        })
            Require(result.GeneratedSource.Contains(required, StringComparison.Ordinal), "generated access/codec product missing: " + required);
        foreach (var forbidden in new[] { "System.Reflection", "System.Delegate", "FunctionPointer", "SharedStatic", "Marshal.", "unsafe" })
            Require(!result.GeneratedSource.Contains(forbidden, StringComparison.Ordinal), "generated access/codec contains forbidden mechanism: " + forbidden);

        const string catalogSource = "using AIBT.Burst; [AibtCatalogSet(\"aibt.verifier.access-catalog\",1u,typeof(AccessShard))] public static partial class AccessCatalog { }";
        var catalog = await CompileAsync("Valid.AccessAndCodec.Catalog", new[] { catalogSource }, new[] { contracts, Reference(result) }, true, true);
        RequireClean(catalog, "access/codec catalog with mixed configuration and memory");
        VerifyGeneratedDispatchCalls(catalog.Compilation);
    }

    private static void VerifyDispatchPrebinding()
    {
        const string registeredTypeId = "aibt.verifier.payload";
        const string registeredSchemaId = "aibt.verifier.payload.schema";
        var registeredSchemaHash = new string('a', 64);
        var registeredDescriptor = new RegisteredUnmanagedTypeDescriptor(
            StableHash.Fnv1A64(registeredTypeId),
            1u,
            72,
            8,
            0x69e3a80e385e338eUL,
            StableHash.Fnv1A64(registeredSchemaId));
        var registeredFields = new[]
        {
            Field("asset", "AssetId", 0u, 32u, GeneratedFieldEncoding.FixedBytes),
            Field("label", "FixedString32", 32u, 32u, GeneratedFieldEncoding.FixedBytes),
            Field("vector", "Float2", 64u, 8u, GeneratedFieldEncoding.FixedBytes)
        };
        var catalog = new RegisteredBlackboardTypeCatalog(new[]
        {
            new RegisteredBlackboardTypeCatalogEntry(
                registeredTypeId, 1u, registeredSchemaHash, registeredDescriptor, registeredFields,
                registeredSchemaId)
        });
        var configuration = new[]
        {
            Field("async", "GeneratedHandle", 0u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("command", "GeneratedHandle", 4u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("completion", "GeneratedHandle", 8u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("enabled", "Bool", 12u, 1u, GeneratedFieldEncoding.Bool8),
            Field("limit", "UInt32", 16u, 4u, GeneratedFieldEncoding.UInt32LE),
            Field("read", "GeneratedHandle", 20u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("read-write", "GeneratedHandle", 24u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("seed", "UInt64", 32u, 8u, GeneratedFieldEncoding.UInt64LE),
            Field("snapshot", "GeneratedHandle", 40u, 4u, GeneratedFieldEncoding.GeneratedHandle),
            Field("write", "GeneratedHandle", 44u, 4u, GeneratedFieldEncoding.GeneratedHandle)
        };
        var memory = new[]
        {
            Field("operation", "OperationId", 0u, 24u, GeneratedFieldEncoding.FixedBytes),
            Field("payload", registeredTypeId, 24u, 72u, GeneratedFieldEncoding.Registered,
                registeredSchemaHash, registeredDescriptor)
        };
        var descriptor = new GeneratedNodeDescriptor(configuration, memory);
        var configurationRuns = GeneratedBurstDispatchPrebindingV2.ConfigurationFields(descriptor, catalog);
        var memoryRuns = GeneratedBurstDispatchPrebindingV2.MemoryFields(descriptor, catalog);

        var handleOrdinals = new[] { 0u, 1u, 2u, 5u, 6u, 8u, 9u };
        Require(configurationRuns.Count == configuration.Length, "configuration prebinding must retain one run per scalar/handle field");
        for (var index = 0; index < configurationRuns.Count; index++)
        {
            var run = configurationRuns[index];
            Require(run.FieldOrdinal == (uint)index && run.FirstElementIndex == 0u && run.ElementCount == 1u,
                "configuration prebinding ordinal/element range differs");
        }
        foreach (var ordinal in handleOrdinals)
            AssertRun(configurationRuns[(int)ordinal], ordinal, 0u, configuration[(int)ordinal].Offset, 1u, 4u,
                NativeBurstDispatchFieldEncodingV2.GeneratedHandle, "generated handle " + ordinal.ToString(CultureInfo.InvariantCulture));
        AssertRun(configurationRuns[3], 3u, 0u, 12u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean, "bool configuration");
        AssertRun(configurationRuns[4], 4u, 0u, 16u, 1u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32, "uint configuration");
        AssertRun(configurationRuns[7], 7u, 0u, 32u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64, "ulong configuration");

        Require(memoryRuns.Count == 10, "mixed memory must publish ten canonical transport runs");
        AssertRun(memoryRuns[0], 0u, 0u, 0u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64, "OperationId tree",
            NativeBurstDispatchCanonicalRuleKindV2.OperationId);
        AssertRun(memoryRuns[1], 0u, 1u, 8u, 2u, 4u, NativeBurstDispatchFieldEncodingV2.UInt32, "OperationId node/generation");
        AssertRun(memoryRuns[2], 0u, 3u, 16u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64, "OperationId sequence");
        var operationLeafEncodings = ExpandEncodings(memoryRuns.Where(run => run.FieldOrdinal == 0u));
        Require(operationLeafEncodings.SequenceEqual(new[]
        {
            NativeBurstDispatchFieldEncodingV2.UInt64,
            NativeBurstDispatchFieldEncodingV2.UInt32,
            NativeBurstDispatchFieldEncodingV2.UInt32,
            NativeBurstDispatchFieldEncodingV2.UInt64
        }), "OperationId transport leaf sequence must be U64/U32/U32/U64");
        AssertRun(memoryRuns[3], 1u, 0u, 24u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64, "registered AssetId GUID high",
            NativeBurstDispatchCanonicalRuleKindV2.AssetId);
        AssertRun(memoryRuns[4], 1u, 1u, 32u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.UInt64, "registered AssetId GUID low");
        AssertRun(memoryRuns[5], 1u, 2u, 40u, 1u, 8u, NativeBurstDispatchFieldEncodingV2.Int64, "registered AssetId local ID");
        AssertRun(memoryRuns[6], 1u, 3u, 48u, 1u, 1u, NativeBurstDispatchFieldEncodingV2.Boolean, "registered AssetId local marker");
        AssertRun(memoryRuns[7], 1u, 4u, 56u, 1u, 2u, NativeBurstDispatchFieldEncodingV2.UInt16, "registered fixed-string length",
            NativeBurstDispatchCanonicalRuleKindV2.FixedString32);
        AssertRun(memoryRuns[8], 1u, 5u, 58u, 30u, 1u, NativeBurstDispatchFieldEncodingV2.UInt8, "registered fixed-string payload");
        AssertRun(memoryRuns[9], 1u, 35u, 88u, 2u, 4u, NativeBurstDispatchFieldEncodingV2.Float32, "registered Float2");

        var repeated = GeneratedBurstDispatchPrebindingV2.MemoryFields(descriptor, catalog);
        Require(RunSignature(memoryRuns) == RunSignature(repeated), "dispatch prebinding changes across repeated builds");
    }

    private static GeneratedStorageField Field(
        string fieldId,
        string valueTypeId,
        uint offset,
        uint size,
        GeneratedFieldEncoding encoding,
        string schemaHash = "",
        RegisteredUnmanagedTypeDescriptor registeredDescriptor = default)
        => new(fieldId, valueTypeId, 1u, offset, size, encoding, schemaHash, registeredDescriptor);

    private static void AssertRun(
        NativeBurstDispatchFieldV2 actual,
        uint fieldOrdinal,
        uint firstElementIndex,
        uint byteOffset,
        uint elementCount,
        uint elementSize,
        NativeBurstDispatchFieldEncodingV2 encoding,
        string label,
        NativeBurstDispatchCanonicalRuleKindV2 canonicalRuleKind = NativeBurstDispatchCanonicalRuleKindV2.None)
    {
        Require(actual.FieldOrdinal == fieldOrdinal && actual.FirstElementIndex == firstElementIndex
            && actual.ByteOffset == byteOffset && actual.ElementCount == elementCount
            && actual.ElementSize == elementSize && actual.Encoding == encoding
            && actual.CanonicalRuleKind == canonicalRuleKind,
            label + " prebinding run differs");
    }

    private static NativeBurstDispatchFieldEncodingV2[] ExpandEncodings(IEnumerable<NativeBurstDispatchFieldV2> runs)
        => runs.SelectMany(run => Enumerable.Repeat(run.Encoding, checked((int)run.ElementCount))).ToArray();

    private static string RunSignature(IEnumerable<NativeBurstDispatchFieldV2> runs)
        => string.Join(";", runs.Select(run => string.Join(",", new[]
        {
            run.FieldOrdinal.ToString(CultureInfo.InvariantCulture),
            run.FirstElementIndex.ToString(CultureInfo.InvariantCulture),
            run.ByteOffset.ToString(CultureInfo.InvariantCulture),
            run.ElementCount.ToString(CultureInfo.InvariantCulture),
            run.ElementSize.ToString(CultureInfo.InvariantCulture),
            ((byte)run.Encoding).ToString(CultureInfo.InvariantCulture)
        })));

    private static void VerifyBoundBurstAccessInvocations(CSharpCompilation compilation)
    {
        var actual = new List<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression.ToString().IndexOf(".BurstAccess.", StringComparison.Ordinal) < 0) continue;
                var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                Require(method != null && method.ContainingType.Name == "BurstAccess", "authored BurstAccess invocation did not bind to generated code");
                var field = model.GetSymbolInfo(invocation.ArgumentList.Arguments[1].Expression).Symbol as IFieldSymbol;
                Require(field != null, "authored BurstAccess invocation did not bind its generated handle field");
                actual.Add(method!.Name + ":" + field!.Name);
            }
        }
        var expected = new[]
        {
            "TryRead:Read", "TryRead:ReadWrite", "TryWrite:Write", "TryWrite:ReadWrite",
            "TryReadSnapshot:Snapshot", "TryEmit:Command", "TryStart:Async",
            "TryConsume:Completion", "TryCancel:Async"
        };
        Require(actual.SequenceEqual(expected), "authored BurstAccess operation bindings differ: " + string.Join(",", actual));
    }

    private static void VerifyGeneratedDispatchCalls(CSharpCompilation compilation)
    {
        var calls = new List<(int Position, string Method, uint Field, uint Element)>();
        var handles = new List<(int Position, string Method, uint Field)>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (method?.ContainingType.ToDisplayString() != "AIBT.Burst.BurstGeneratedRuntimeBridge"
                    || invocation.ArgumentList.Arguments.Count < 3) continue;
                var field = model.GetConstantValue(invocation.ArgumentList.Arguments[1].Expression);
                if (field.HasValue && field.Value is uint handleField && method.Name.EndsWith("Handle", StringComparison.Ordinal))
                    handles.Add((invocation.SpanStart, method.Name, handleField));
                var element = model.GetConstantValue(invocation.ArgumentList.Arguments[2].Expression);
                if (field.HasValue && field.Value is uint fieldOrdinal && element.HasValue && element.Value is uint elementIndex)
                    calls.Add((invocation.SpanStart, method.Name, fieldOrdinal, elementIndex));
            }
        }
        calls.Sort((left, right) => left.Position.CompareTo(right.Position));
        handles.Sort((left, right) => left.Position.CompareTo(right.Position));

        var operationReads = calls.Where(call => call.Method.StartsWith("TryReadMemory", StringComparison.Ordinal)
            && call.Field == 0u && call.Element < 4u).Select(call => call.Method).ToArray();
        var operationWrites = calls.Where(call => call.Method.StartsWith("TryWriteMemory", StringComparison.Ordinal)
            && call.Field == 0u && call.Element < 4u).Select(call => call.Method).ToArray();
        var expectedOperation = new[] { "TryReadMemoryUInt64", "TryReadMemoryUInt32", "TryReadMemoryUInt32", "TryReadMemoryUInt64" };
        var expectedOperationWrites = new[] { "TryWriteMemoryUInt64", "TryWriteMemoryUInt32", "TryWriteMemoryUInt32", "TryWriteMemoryUInt64" };
        Require(operationReads.SequenceEqual(expectedOperation), "generated OperationId read callback differs: " + string.Join(",", operationReads));
        Require(operationWrites.SequenceEqual(expectedOperationWrites), "generated OperationId write callback differs: " + string.Join(",", operationWrites));

        var handleCalls = handles.Select(call => call.Method + ":" + call.Field.ToString(CultureInfo.InvariantCulture)).ToArray();
        var expectedHandles = new[]
        {
            "TryReadAsyncOperationHandle:0", "TryReadCommandHandle:1", "TryReadCompletionHandle:2",
            "TryReadBlackboardReadHandle:5", "TryReadBlackboardReadWriteHandle:6",
            "TryReadSnapshotHandle:8", "TryReadBlackboardWriteHandle:9"
        };
        Require(handleCalls.SequenceEqual(expectedHandles), "generated handle prebinding calls differ: " + string.Join(",", handleCalls));
        Require(calls.Any(call => call.Method == "TryReadBoolean" && call.Field == 3u && call.Element == 0u), "generated bool configuration read is absent");
        Require(calls.Any(call => call.Method == "TryReadUInt32" && call.Field == 4u && call.Element == 0u), "generated uint configuration read is absent");
        Require(calls.Any(call => call.Method == "TryReadUInt64" && call.Field == 7u && call.Element == 0u), "generated ulong configuration read is absent");
    }

    private static async Task VerifyValidAndDeterministicGeneration(MetadataReference contracts)
    {
        var normal = DeclarationAssembly(10, false);
        var reverse = DeclarationAssembly(10, true);
        var first = await CompileAsync("Valid.Normal", new[] { normal }, new[] { contracts }, true, true);
        RequireClean(first, "valid declarations");

        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        CompilationResult second;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            second = await CompileAsync("Valid.Reversed", new[] { reverse }, new[] { contracts }, true, true);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        RequireClean(second, "reversed declarations");
        Require(first.GeneratedSource == second.GeneratedSource, "generated metadata changes with declaration order or culture");
        Require(first.GeneratedSource.Contains("public const bool IsUsable = true;", StringComparison.Ordinal), "usable shard marker missing");
        Require(first.GeneratedSource.Contains("public const uint AbiVersion = 2u;", StringComparison.Ordinal), "ABI v2 marker missing");
        Require(first.GeneratedSource.Contains("BurstAccess", StringComparison.Ordinal), "generated BurstAccess surface missing");

        const string catalogSource = "using AIBT.Burst; [AibtCatalogSet(\"aibt.cost.catalog\",1u,typeof(ProbeShard))] public static partial class ProbeCatalog { }";
        var firstCatalog = await CompileAsync("Catalog.Normal", new[] { catalogSource }, new[] { contracts, Reference(first) }, true, true);
        var secondCatalog = await CompileAsync("Catalog.Reversed", new[] { catalogSource }, new[] { contracts, Reference(second) }, true, true);
        RequireClean(firstCatalog, "valid catalog");
        RequireClean(secondCatalog, "reversed valid catalog");
        Require(firstCatalog.GeneratedSource == secondCatalog.GeneratedSource, "generated catalog changes with shard declaration order or culture");
        foreach (var required in new[] { "Fingerprint", "ExecuteImmediate", "Schedule(", "IJob", "TryAcquireDispatchFrame", "switch (catalogCaseIndex)", "handshake.AbiVersion != 2u" })
            Require(firstCatalog.GeneratedSource.Contains(required, StringComparison.Ordinal), "P2-012 generated catalog product missing: " + required + "; diagnostics=" + string.Join(" | ", firstCatalog.Diagnostics.Select(value => value.Id + ":" + value.GetMessage(CultureInfo.InvariantCulture))) + "; source=" + firstCatalog.GeneratedSource);
        Require(!firstCatalog.GeneratedSource.Contains("aibt.core.verifier", StringComparison.Ordinal), "Runtime built-in leaked into generated dispatch cases");

        var unrelated = await CompileAsync("Unrelated", new[] { "namespace Plain { public sealed class Ordinary { public int Value { get; set; } } }" }, new[] { contracts }, true, true);
        RequireClean(unrelated, "unrelated assembly");
        Require(string.IsNullOrEmpty(unrelated.GeneratedSource), "unrelated assembly received generated AIBT source");
    }

    private static async Task VerifyDiagnosticMatrix(MetadataReference contracts)
    {
        var cases = new[]
        {
            new Negative("AIBT5001", Node("aibt.invalid.shape", nodeBody: "public int Forbidden;"), "Forbidden"),
            new Negative("AIBT5002", Node("aibt.invalid.storage", config: "public partial struct Config { [AibtConfigField(\"value\",\"aibt.string\",1u)] public string Value; }"), "Config"),
            new Negative("AIBT5003", Node("aibt.invalid.callback", tick: "public static void Tick(in Config config, ref Memory memory, ref BurstTickContext context) { }"), "Tick"),
            new Negative("AIBT5004", Node("aibt.invalid.kind", kind: "(BurstNodeKind)9"), "(BurstNodeKind)9"),
            new Negative("AIBT5005", Node("aibt.duplicate.node", nodeName: "NodeA") + Node("aibt.duplicate.node", nodeName: "NodeB", includeSupport: false), "\"aibt.duplicate.node\"", 1),
            new Negative("AIBT5006", Node("aibt.invalid.forged", tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { ProbeShard.BurstAccess.TryRead(ref context, default(BlackboardReadHandle<int>), out var value); return NodeStatus.Success; }"), "ProbeShard.BurstAccess.TryRead(ref context, default(BlackboardReadHandle<int>), out var value)"),
            new Negative("AIBT5007", Node("aibt.invalid.binding", config: SharedWriteConfig), "BlackboardScope.Shared"),
            new Negative("AIBT5008", Support + "namespace UnityEngine { public sealed class GameObject { } }" + Node("aibt.invalid.unity", includeSupport: false, tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var value = new UnityEngine.GameObject(); return NodeStatus.Success; }"), "new UnityEngine.GameObject()"),
            new Negative("AIBT5009", Node("Invalid"), "\"Invalid\""),
            new Negative("AIBT5011", Node("aibt.invalid.no-shard", includeShard: false), "ProbeNode")
        };

        foreach (var test in cases)
        {
            var result = await CompileAsync("Negative." + test.Id, new[] { test.Source }, new[] { contracts }, true, true);
            RequireDiagnostic(result, test.Id, true, test.ExpectedText, test.AdditionalLocations);
            RequireAtomicUnusable(result, test.Id);

            var suppressed = await CompileAsync("Suppressed." + test.Id, new[] { test.Source }, new[] { contracts }, true, true, test.Id);
            RequireDiagnostic(suppressed, test.Id, true, test.ExpectedText, test.AdditionalLocations);
            RequireAtomicUnusable(suppressed, "suppressed " + test.Id);
        }

        BurstNodeGenerator.ForceNumericIdentityCollisionForTests = true;
        try
        {
            var collision = await CompileAsync("Negative.AIBT5010", new[] { Node("aibt.collision.first", nodeName: "NodeA") + Node("aibt.collision.second", nodeName: "NodeB", includeSupport: false) }, new[] { contracts }, true, true);
            RequireDiagnostic(collision, "AIBT5010", true, "\"aibt.collision.second\"", 1);
            RequireAtomicUnusable(collision, "AIBT5010");
        }
        finally
        {
            BurstNodeGenerator.ForceNumericIdentityCollisionForTests = false;
        }

        const string externalShard = "using AIBT.Burst; [AibtCatalogShard(\"aibt.external.shard\",1u)] public partial struct ExternalShard { public const bool IsUsable=true; public const uint AbiVersion=1u; }";
        var external = await CompileAsync("External.Shard", new[] { externalShard }, new[] { contracts }, false, false);
        RequireClean(external, "external ABI mismatch fixture");

        const string unusableShard = "using AIBT.Burst; [AibtCatalogShard(\"aibt.external.unusable\",1u)] public partial struct ExternalUnusableShard { public const bool IsUsable=false; public const uint AbiVersion=2u; }";
        var unusable = await CompileAsync("External.Unusable", new[] { unusableShard }, new[] { contracts }, false, false);
        RequireClean(unusable, "external unusable shard fixture");
        const string unusableCatalog = "using AIBT.Burst; [AibtCatalogSet(\"aibt.consumer.unusable\",1u,typeof(ExternalUnusableShard))] public static partial class UnusableCatalog { }";
        var selectedUnusable = await CompileAsync("Negative.AIBT5011.Selected", new[] { unusableCatalog }, new[] { contracts, Reference(unusable) }, true, true);
        RequireDiagnostic(selectedUnusable, "AIBT5011", true, "typeof(ExternalUnusableShard)");
        RequireAtomicUnusable(selectedUnusable, "AIBT5011 selected shard");

        const string catalog = "using AIBT.Burst; [AibtCatalogSet(\"aibt.consumer.catalog\",1u,typeof(ExternalShard))] public static partial class Catalog { }";
        var mismatch = await CompileAsync("Negative.AIBT5012", new[] { catalog }, new[] { contracts, Reference(external) }, true, true);
        RequireDiagnostic(mismatch, "AIBT5012", false);
        RequireAtomicUnusable(mismatch, "AIBT5012");
    }

    private static async Task VerifyNodeAttributeArgumentTotality(MetadataReference contracts)
    {
        var cases = new[]
        {
            new ForbiddenFlow("InvalidMemoryLifetime",
                Node("aibt.invalid.memory-lifetime", memoryLifetime: "(NodeMemoryLifetime)2"),
                "(NodeMemoryLifetime)2"),
            new ForbiddenFlow("InvalidCancellation",
                Node("aibt.invalid.cancellation", cancellation: "(BurstCancellationMode)3"),
                "(BurstCancellationMode)3"),
            new ForbiddenFlow("InvalidCost",
                Node("aibt.invalid.cost", cost: "(BurstNodeCost)255"),
                "(BurstNodeCost)255"),
            new ForbiddenFlow("EmptyStatusMask",
                Node("aibt.invalid.status-none", statuses: "BurstNodeStatusMask.None"),
                "BurstNodeStatusMask.None"),
            new ForbiddenFlow("ReservedStatusBit",
                Node("aibt.invalid.status-reserved", statuses: "(BurstNodeStatusMask)8"),
                "(BurstNodeStatusMask)8")
        };

        foreach (var test in cases)
        {
            var result = await CompileAsync("NodeAttribute." + test.Name, new[] { test.Source }, new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(result, "AIBT5004", test.ExpectedText, test.Name);
            RequireAtomicUnusable(result, test.Name);

            var suppressed = await CompileAsync("NodeAttribute.Suppressed." + test.Name,
                new[] { test.Source }, new[] { contracts }, true, true, "AIBT5004");
            RequireSingleDiagnosticAt(suppressed, "AIBT5004", test.ExpectedText, "suppressed " + test.Name);
            RequireAtomicUnusable(suppressed, "suppressed " + test.Name);
        }

        var boundary = await CompileAsync("NodeAttribute.ValidBoundary", new[]
        {
            Node("aibt.valid.attribute-boundary",
                kind: "BurstNodeKind.Action",
                memoryLifetime: "NodeMemoryLifetime.Instance",
                deterministic: "false",
                cancellation: "BurstCancellationMode.AbortOnly",
                cost: "BurstNodeCost.Variable",
                statuses: "BurstNodeStatusMask.Success|BurstNodeStatusMask.Failure|BurstNodeStatusMask.Running")
        }, new[] { contracts }, true, true);
        RequireClean(boundary, "closed node attribute boundary values");
        Require(boundary.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal),
            "closed node attribute boundary values must retain a usable shard");
    }

    private static async Task VerifyBlackboardScopeTotality(MetadataReference contracts)
    {
        var invalidScopes = new[]
        {
            new { Name = "node-local", Expression = "BlackboardScope.NodeLocal" },
            new { Name = "undefined-4", Expression = "(BlackboardScope)4" },
            new { Name = "undefined-255", Expression = "(BlackboardScope)255" }
        };
        foreach (var scope in invalidScopes)
        {
            var source = Node("aibt.invalid.scope-" + scope.Name, config: BlackboardReadConfig(scope.Expression));
            var result = await CompileAsync("Scope." + scope.Name, new[] { source }, new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(result, "AIBT5007", scope.Expression, scope.Name);
            RequireAtomicUnusable(result, scope.Name);

            var suppressed = await CompileAsync("Scope.Suppressed." + scope.Name, new[] { source }, new[] { contracts }, true, true, "AIBT5007");
            RequireSingleDiagnosticAt(suppressed, "AIBT5007", scope.Expression, "suppressed " + scope.Name);
            RequireAtomicUnusable(suppressed, "suppressed " + scope.Name);
        }

        var sharedRead = await CompileAsync("Scope.ValidSharedRead",
            new[] { Node("aibt.valid.shared-read", config: BlackboardReadConfig("BlackboardScope.Shared")) },
            new[] { contracts }, true, true);
        RequireClean(sharedRead, "Shared read binding");

        var analyzerMirror = ScopeAnalyzerSource("(BlackboardScope)4");
        var analyzerOnly = await CompileAsync("Scope.AnalyzerMirror", new[] { analyzerMirror }, new[] { contracts }, false, true);
        RequireSingleDiagnosticAt(analyzerOnly, "AIBT5007",
            "ProbeShard.BurstAccess.TryRead(ref context, config.Read, out var value)", "scope analyzer mirror");
    }

    private static async Task VerifyNullAttributeAndStorageLocations(MetadataReference contracts)
    {
        var nullDocumentationSource = Node("aibt.invalid.null-documentation").Replace(
            "[AibtNodeDocumentation(\"Probe\",\"Tests\",\"Use\",\"Avoid\",\"probe\")]",
            "[AibtNodeDocumentation(\"Probe\",\"Tests\",\"Use\",\"Avoid\",null)]",
            StringComparison.Ordinal);
        var nullDocumentation = await CompileAsync("Attribute.NullDocumentation",
            new[] { nullDocumentationSource }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(nullDocumentation, "AIBT5009", "null", "null documentation examples");
        Require(!nullDocumentation.Diagnostics.Any(value => value.Id == "CS8785"),
            "null documentation examples must fail closed without a generator exception");
        RequireAtomicUnusable(nullDocumentation, "null documentation examples");

        const string nullCatalogSource = "using AIBT.Burst; [AibtCatalogSet(\"aibt.invalid.null-catalog\",1u,null)] public static partial class NullCatalog { }";
        var nullCatalog = await CompileAsync("Attribute.NullCatalog",
            new[] { nullCatalogSource }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(nullCatalog, "AIBT5011", "null", "null catalog shard array");
        Require(!nullCatalog.Diagnostics.Any(value => value.Id == "CS8785"),
            "null catalog shard array must fail closed without a generator exception");
        RequireAtomicUnusable(nullCatalog, "null catalog shard array");

        var nullConfigIdentity = await CompileAsync("Storage.NullConfigValueIdentity",
            new[] { Node("aibt.invalid.null-config-value", config: "public partial struct Config { [AibtConfigField(\"value\",null,1u)] public bool Value; }") },
            new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(nullConfigIdentity, "AIBT5009", "null", "null configuration value identity");
        RequireAtomicUnusable(nullConfigIdentity, "null configuration value identity");

        var nullMemoryIdentity = await CompileAsync("Storage.NullMemoryValueIdentity",
            new[] { Node("aibt.invalid.null-memory-value", memory: "public partial struct Memory { [AibtMemoryField(\"value\",null,1u)] public uint Value; }") },
            new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(nullMemoryIdentity, "AIBT5009", "null", "null memory value identity");
        RequireAtomicUnusable(nullMemoryIdentity, "null memory value identity");
    }

    private static async Task VerifyRegisteredIdentityTotality(MetadataReference contracts)
    {
        var duplicateType = await CompileAsync("Registered.DuplicateType",
            new[] { DuplicateRegisteredTypeSource() }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(duplicateType, "AIBT5005", "\"aibt.values.duplicate\"", "duplicate registered type identity", 1);
        RequireAtomicUnusable(duplicateType, "duplicate registered type identity");

        var duplicateTypeSuppressed = await CompileAsync("Registered.DuplicateType.Suppressed",
            new[] { DuplicateRegisteredTypeSource() }, new[] { contracts }, true, true, "AIBT5005");
        RequireSingleDiagnosticAt(duplicateTypeSuppressed, "AIBT5005", "\"aibt.values.duplicate\"", "suppressed duplicate registered type identity", 1);
        RequireAtomicUnusable(duplicateTypeSuppressed, "suppressed duplicate registered type identity");

        var duplicateSchema = await CompileAsync("Registered.DuplicateSchema",
            new[] { RegisteredPairSource("aibt.values.first", "aibt.values.second", "aibt.schemas.duplicate", "aibt.schemas.duplicate") },
            new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(duplicateSchema, "AIBT5010", "\"aibt.schemas.duplicate\"", "duplicate registered schema identity", 1);
        RequireAtomicUnusable(duplicateSchema, "duplicate registered schema identity");

        BurstNodeGenerator.ForceRegisteredTypeNumericIdentityCollisionForTests = true;
        try
        {
            var collision = await CompileAsync("Registered.TypeNumericCollision",
                new[] { RegisteredPairSource("aibt.values.first", "aibt.values.second", "aibt.schemas.first", "aibt.schemas.second") },
                new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(collision, "AIBT5010", "\"aibt.values.second\"", "registered type numeric collision", 1);
            RequireAtomicUnusable(collision, "registered type numeric collision");
        }
        finally
        {
            BurstNodeGenerator.ForceRegisteredTypeNumericIdentityCollisionForTests = false;
        }

        BurstNodeGenerator.ForceRegisteredSchemaNumericIdentityCollisionForTests = true;
        try
        {
            var collision = await CompileAsync("Registered.SchemaNumericCollision",
                new[] { RegisteredPairSource("aibt.values.first", "aibt.values.second", "aibt.schemas.first", "aibt.schemas.second") },
                new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(collision, "AIBT5010", "\"aibt.schemas.second\"", "registered schema numeric collision", 1);
            RequireAtomicUnusable(collision, "registered schema numeric collision");
        }
        finally
        {
            BurstNodeGenerator.ForceRegisteredSchemaNumericIdentityCollisionForTests = false;
        }

        var invalidSchema = await CompileAsync("Registered.NullSchema",
            new[] { InvalidRegisteredSchemaSource() }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(invalidSchema, "AIBT5009", "null", "null registered schema identity");
        Require(!invalidSchema.Diagnostics.Any(value => value.Id == "CS8785"),
            "null registered schema identity must fail closed without a generator exception");
        RequireAtomicUnusable(invalidSchema, "null registered schema identity");

        var missingFieldAttribute = await CompileAsync("Registered.MissingFieldAttribute",
            new[] { InvalidRegisteredFieldSource() }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(missingFieldAttribute, "AIBT5002", "Missing", "registered field without AibtValueField");
        Require(!missingFieldAttribute.Diagnostics.Any(value => value.Id == "CS8785"),
            "registered field without AibtValueField must fail closed without a generator exception");
        RequireAtomicUnusable(missingFieldAttribute, "registered field without AibtValueField");

        var emptyValue = await CompileAsync("Registered.EmptyValue",
            new[] { EmptyRegisteredValueSource() }, new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(emptyValue, "AIBT5002", "EmptyValue", "empty registered value");
        RequireAtomicUnusable(emptyValue, "empty registered value");

        var emptyNodeStorage = await CompileAsync("Registered.EmptyNodeStorage",
            new[] { Node("aibt.valid.empty-node-storage", config: "public partial struct Config { }", memory: "public partial struct Memory { }") },
            new[] { contracts }, true, true);
        RequireClean(emptyNodeStorage, "empty node configuration and memory");
        Require(emptyNodeStorage.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal),
            "empty node configuration and memory must remain usable");

        var duplicateBinding = await CompileAsync("Binding.DuplicateIdentity",
            new[] { Node("aibt.invalid.binding-duplicate", config: DuplicateBindingConfig()) },
            new[] { contracts }, true, true);
        RequireSingleDiagnosticAt(duplicateBinding, "AIBT5010", "\"first\"", "duplicate binding identity", 1);
        RequireAtomicUnusable(duplicateBinding, "duplicate binding identity");

        var independentBindingIdentity = await CompileAsync("Binding.IndependentFromFieldIdentity",
            new[] { ScopeAnalyzerSource("BlackboardScope.Tree", "other") }, new[] { contracts }, false, true);
        RequireClean(independentBindingIdentity, "binding identity independent from configuration field identity");

        BurstNodeGenerator.ForceBindingNumericIdentityCollisionForTests = true;
        try
        {
            var collision = await CompileAsync("Binding.NumericCollision",
                new[] { Node("aibt.invalid.binding-collision", config: DistinctBindingConfig()) },
                new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(collision, "AIBT5010", "\"second\"", "binding numeric collision", 1);
            RequireAtomicUnusable(collision, "binding numeric collision");
        }
        finally
        {
            BurstNodeGenerator.ForceBindingNumericIdentityCollisionForTests = false;
        }
    }

    private static async Task VerifyNestedRegisteredLayoutOrder(MetadataReference contracts)
    {
        var normal = await CompileAsync("RegisteredLayout.Normal",
            new[] { NestedRegisteredLayoutSource(false) }, new[] { contracts }, true, true);
        var reordered = await CompileAsync("RegisteredLayout.Reordered",
            new[] { NestedRegisteredLayoutSource(true) }, new[] { contracts }, true, true);
        RequireClean(normal, "normal nested registered layout");
        RequireClean(reordered, "reordered nested registered layout");
        Require(normal.GeneratedSource == reordered.GeneratedSource,
            "nested registered value declaration order changes generated source, canonical size, or hashes");
        Require(normal.GeneratedSource.Contains("NodeRegistryHash", StringComparison.Ordinal)
            && normal.GeneratedSource.Contains("DescriptorHash", StringComparison.Ordinal)
            && normal.GeneratedSource.Contains("\\\"size\\\": 24", StringComparison.Ordinal),
            "nested registered value canary is missing the expected canonical size/hash products");
    }

    private static async Task VerifyBuiltInAuthorityFailures(string[] contractSources)
    {
        var withoutAuthority = await CompileAsync(
            "AIBT.Runtime.Contracts.NoAuthority",
            contractSources.Take(contractSources.Length - 1),
            Array.Empty<MetadataReference>(),
            false,
            false);
        RequireClean(withoutAuthority, "contracts without built-in authority");
        var missingReference = MetadataReference.CreateFromImage(ImmutableArray.Create(withoutAuthority.Image));
        var missingShard = await CompileAsync("Authority.Missing.Shard", new[] { DeclarationAssembly(1, false) }, new[] { missingReference }, true, true);
        RequireClean(missingShard, "missing-authority shard");
        const string catalog = "using AIBT.Burst; [AibtCatalogSet(\"aibt.authority.catalog\",1u,typeof(ProbeShard))] public static partial class AuthorityCatalog { }";
        var missingCatalog = await CompileAsync("Authority.Missing.Catalog", new[] { catalog }, new[] { missingReference, Reference(missingShard) }, true, true);
        RequireDiagnostic(missingCatalog, "AIBT5011", false);
        RequireAtomicUnusable(missingCatalog, "missing Runtime built-in authority");

        var malformedSources = contractSources.ToArray();
        const string hashPrefix = "const string NodeRegistryHash = \"";
        var hashStart = malformedSources[^1].IndexOf(hashPrefix, StringComparison.Ordinal);
        Require(hashStart >= 0, "Runtime built-in authority hash constant was not found");
        hashStart += hashPrefix.Length;
        var replacement = malformedSources[^1][hashStart] == '0' ? '1' : '0';
        malformedSources[^1] = malformedSources[^1].Substring(0, hashStart) + replacement + malformedSources[^1].Substring(hashStart + 1);
        var malformedAuthority = await CompileAsync(
            "AIBT.Runtime.Contracts.MalformedAuthority",
            malformedSources,
            Array.Empty<MetadataReference>(),
            false,
            false);
        RequireClean(malformedAuthority, "contracts with malformed built-in authority");
        var malformedReference = MetadataReference.CreateFromImage(ImmutableArray.Create(malformedAuthority.Image));
        var malformedShard = await CompileAsync("Authority.Malformed.Shard", new[] { DeclarationAssembly(1, false) }, new[] { malformedReference }, true, true);
        RequireClean(malformedShard, "malformed-authority shard");
        var malformedCatalog = await CompileAsync("Authority.Malformed.Catalog", new[] { catalog }, new[] { malformedReference, Reference(malformedShard) }, true, true);
        RequireDiagnostic(malformedCatalog, "AIBT5012", false);
        RequireAtomicUnusable(malformedCatalog, "malformed Runtime built-in authority");
    }

    private static async Task VerifyForbiddenExceptionFlow(MetadataReference contracts)
    {
        var cases = new[]
        {
            new ForbiddenFlow(
                "DirectThrowStatement",
                Node("aibt.invalid.throw-statement", tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { throw null; }"),
                "throw"),
            new ForbiddenFlow(
                "DirectThrowExpression",
                Node("aibt.invalid.throw-expression", tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) => config.Enabled ? NodeStatus.Success : throw null;"),
                "throw"),
            new ForbiddenFlow(
                "DirectTryCatch",
                Node("aibt.invalid.try-catch", tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { try { return NodeStatus.Success; } catch { return NodeStatus.Failure; } }"),
                "try"),
            new ForbiddenFlow(
                "TransitiveThrow",
                Support
                    + "public static class BurstLeaf { public static NodeStatus Run() { throw null; } }"
                    + "public static class BurstMiddle { public static NodeStatus Run() => BurstLeaf.Run(); }"
                    + Node("aibt.invalid.transitive-throw", includeSupport: false, tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) => BurstMiddle.Run();"),
                "throw"),
            new ForbiddenFlow(
                "TransitiveTryFinally",
                Support
                    + "public static class BurstTryLeaf { public static NodeStatus Run() { try { return NodeStatus.Success; } finally { } } }"
                    + "public static class BurstTryMiddle { public static NodeStatus Run() => BurstTryLeaf.Run(); }"
                    + Node("aibt.invalid.transitive-try", includeSupport: false, tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) => BurstTryMiddle.Run();"),
                "try")
        };

        foreach (var test in cases)
        {
            var result = await CompileAsync("Forbidden." + test.Name, new[] { test.Source }, new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(result, "AIBT5008", test.ExpectedText, test.Name);
            RequireAtomicUnusable(result, test.Name);

            var suppressed = await CompileAsync("Forbidden.Suppressed." + test.Name, new[] { test.Source }, new[] { contracts }, true, true, "AIBT5008");
            RequireSingleDiagnosticAt(suppressed, "AIBT5008", test.ExpectedText, "suppressed " + test.Name);
            RequireAtomicUnusable(suppressed, "suppressed " + test.Name);
        }
    }

    private static async Task VerifyTransitiveCapabilityFlow(MetadataReference contracts)
    {
        const string readConfig = "public partial struct Config { [AibtConfigField(\"read\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"read\",BurstBlackboardAccess.Read,BlackboardScope.Tree,\"Int32\",1u)] public BlackboardReadHandle<int> Read; }";
        const string commandConfig = "public partial struct Config { [AibtConfigField(\"command\",\"GeneratedHandle\",1u),AibtCommandBinding(\"command\",\"Int32\",1u)] public CommandHandle<int> Command; }";
        var cases = new[]
        {
            new ForbiddenFlow(
                "DirectBindingHelper",
                Support
                    + "public static class BindingHelper { public static void Run(ref BurstTickContext context, BlackboardReadHandle<int> handle) { ProbeShard.BurstAccess.TryRead(ref context, handle, out var value); } }"
                    + Node("aibt.invalid.binding-helper", includeSupport: false, config: readConfig,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { BindingHelper.Run(ref context, config.Read); return NodeStatus.Success; }"),
                "ProbeShard.BurstAccess.TryRead(ref context, handle, out var value)"),
            new ForbiddenFlow(
                "WholeConfigurationHelper",
                Support
                    + "public static class ConfigurationHelper { public static void Run(ref BurstTickContext context, in Config config) { ProbeShard.BurstAccess.TryRead(ref context, config.Read, out var value); } }"
                    + Node("aibt.invalid.configuration-helper", includeSupport: false, config: readConfig,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { ConfigurationHelper.Run(ref context, in config); return NodeStatus.Success; }"),
                "ProbeShard.BurstAccess.TryRead(ref context, config.Read, out var value)"),
            new ForbiddenFlow(
                "TwoHopContextHelper",
                Support
                    + "public static class ContextLeaf { public static void Run(ref BurstTickContext context, CommandHandle<int> handle) { context.TryBeginEffect(handle, out var writer); } }"
                    + "public static class ContextMiddle { public static void Run(ref BurstTickContext context, CommandHandle<int> handle) { ContextLeaf.Run(ref context, handle); } }"
                    + Node("aibt.invalid.context-helper", includeSupport: false, config: commandConfig, kind: "BurstNodeKind.Action",
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { ContextMiddle.Run(ref context, config.Command); return NodeStatus.Success; }"),
                "context.TryBeginEffect(handle, out var writer)"),
            new ForbiddenFlow(
                "UnmanagedHelperConstructor",
                Support
                    + "public struct ConstructorHelper { public ConstructorHelper(ref BurstTickContext context, BlackboardReadHandle<int> handle) { ProbeShard.BurstAccess.TryRead(ref context, handle, out var value); } }"
                    + Node("aibt.invalid.constructor-helper", includeSupport: false, config: readConfig,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var helper = new ConstructorHelper(ref context, config.Read); return NodeStatus.Success; }"),
                "ProbeShard.BurstAccess.TryRead(ref context, handle, out var value)"),
            new ForbiddenFlow(
                "PropertyGetterHelper",
                Support
                    + "public struct PropertyHelper { private BurstTickContext context; private BlackboardReadHandle<int> handle; public PropertyHelper(BurstTickContext context, BlackboardReadHandle<int> handle) { this.context=context; this.handle=handle; } public int Read { get { ProbeShard.BurstAccess.TryRead(ref context, handle, out var value); return value; } } }"
                    + Node("aibt.invalid.property-helper", includeSupport: false, config: readConfig,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var helper = new PropertyHelper(context, config.Read); return helper.Read == 0 ? NodeStatus.Success : NodeStatus.Failure; }"),
                "ProbeShard.BurstAccess.TryRead(ref context, handle, out var value)"),
            new ForbiddenFlow(
                "OperatorHelper",
                Support
                    + "public struct OperatorHelper { private BurstTickContext context; private BlackboardReadHandle<int> handle; public OperatorHelper(BurstTickContext context, BlackboardReadHandle<int> handle) { this.context=context; this.handle=handle; } public static int operator +(OperatorHelper helper, int value) { ProbeShard.BurstAccess.TryRead(ref helper.context, helper.handle, out var read); return read + value; } }"
                    + Node("aibt.invalid.operator-helper", includeSupport: false, config: readConfig,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var helper = new OperatorHelper(context, config.Read); return helper + 1 == 0 ? NodeStatus.Success : NodeStatus.Failure; }"),
                "ProbeShard.BurstAccess.TryRead(ref helper.context, helper.handle, out var read)")
        };

        foreach (var test in cases)
        {
            var result = await CompileAsync("CapabilityFlow." + test.Name,
                new[] { test.Source }, new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(result, "AIBT5006", test.ExpectedText, test.Name);
            RequireAtomicUnusable(result, test.Name);

            var suppressed = await CompileAsync("CapabilityFlow.Suppressed." + test.Name,
                new[] { test.Source }, new[] { contracts }, true, true, "AIBT5006");
            RequireSingleDiagnosticAt(suppressed, "AIBT5006", test.ExpectedText, "suppressed " + test.Name);
            RequireAtomicUnusable(suppressed, "suppressed " + test.Name);
        }

        var plainScalarHelper = await CompileAsync(
            "CapabilityFlow.AllowedScalarHelper",
            new[]
            {
                Support
                    + "public readonly struct ScalarHelper { private readonly int value; public ScalarHelper(int value) { this.value=value; } public int Value => value; public static int operator +(ScalarHelper helper, int right) => helper.Value + right; }"
                    + Node("aibt.valid.scalar-helper", includeSupport: false,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var helper = new ScalarHelper(config.Enabled ? 1 : 0); return helper + 1 == 2 ? NodeStatus.Success : NodeStatus.Failure; }")
            },
            new[] { contracts },
            true,
            true);
        RequireClean(plainScalarHelper, "plain scalar helper");
        Require(plainScalarHelper.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal),
            "Plain scalar helper must not make the shard unusable.");
    }

    private static async Task VerifyForbiddenUnityApiFlow(MetadataReference contracts)
    {
        const string jobs = "namespace Unity.Jobs { public interface IJob { void Execute(); } public struct JobHandle { } public static class IJobExtensions { public static JobHandle Schedule<T>(T job) where T : struct, IJob => default; } } public struct ProbeJob : Unity.Jobs.IJob { public void Execute() { } }";
        const string native = "namespace Unity.Collections { public struct NativeArray<T> where T : struct { public NativeArray(int length) { } public int Length => 0; } }";
        const string unsafeApi = "namespace Unity.Collections.LowLevel.Unsafe { public static class UnsafeUtility { public static int SizeOf<T>() where T : struct => 0; } }";
        const string sharedStatic = "namespace Unity.Burst { public struct SharedStatic<T> where T : struct { public static SharedStatic<T> GetOrCreate<TContext>() where TContext : struct => default; public T Data; } }";
        var cases = new[]
        {
            new ForbiddenFlow(
                "DirectJobs",
                Support + jobs + Node("aibt.invalid.jobs", includeSupport: false,
                    tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var value = Unity.Jobs.IJobExtensions.Schedule(new ProbeJob()); return NodeStatus.Success; }"),
                "Unity.Jobs.IJobExtensions.Schedule(new ProbeJob())"),
            new ForbiddenFlow(
                "TransitiveJobs",
                Support + jobs + "public static class JobsHelper { public static int Run() { var value = Unity.Jobs.IJobExtensions.Schedule(new ProbeJob()); return 0; } }"
                    + Node("aibt.invalid.jobs-helper", includeSupport: false,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { JobsHelper.Run(); return NodeStatus.Success; }"),
                "Unity.Jobs.IJobExtensions.Schedule(new ProbeJob())"),
            new ForbiddenFlow(
                "DirectNativeContainer",
                Support + native + Node("aibt.invalid.native", includeSupport: false,
                    tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var value = new Unity.Collections.NativeArray<int>(1); return NodeStatus.Success; }"),
                "new Unity.Collections.NativeArray<int>(1)"),
            new ForbiddenFlow(
                "TransitiveNativeContainer",
                Support + native + "public static class NativeHelper { public static int Run() { var value = new Unity.Collections.NativeArray<int>(1); return 0; } }"
                    + Node("aibt.invalid.native-helper", includeSupport: false,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { NativeHelper.Run(); return NodeStatus.Success; }"),
                "new Unity.Collections.NativeArray<int>(1)"),
            new ForbiddenFlow(
                "DirectLowLevelUnsafe",
                Support + unsafeApi + Node("aibt.invalid.unsafe-api", includeSupport: false,
                    tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var value = Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<int>(); return NodeStatus.Success; }"),
                "Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<int>()"),
            new ForbiddenFlow(
                "TransitiveLowLevelUnsafe",
                Support + unsafeApi + "public static class UnsafeHelper { public static int Run() => Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<int>(); }"
                    + Node("aibt.invalid.unsafe-helper", includeSupport: false,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { UnsafeHelper.Run(); return NodeStatus.Success; }"),
                "Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<int>()"),
            new ForbiddenFlow(
                "DirectSharedStatic",
                Support + sharedStatic + Node("aibt.invalid.shared-static", includeSupport: false,
                    tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var value = Unity.Burst.SharedStatic<int>.GetOrCreate<ProbeNode>(); return NodeStatus.Success; }"),
                "Unity.Burst.SharedStatic<int>.GetOrCreate<ProbeNode>()"),
            new ForbiddenFlow(
                "TransitiveSharedStatic",
                Support + sharedStatic + "public static class SharedStaticHelper { public static int Run() { var value = Unity.Burst.SharedStatic<int>.GetOrCreate<ProbeNode>(); return 0; } }"
                    + Node("aibt.invalid.shared-static-helper", includeSupport: false,
                        tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { SharedStaticHelper.Run(); return NodeStatus.Success; }"),
                "Unity.Burst.SharedStatic<int>.GetOrCreate<ProbeNode>()")
        };

        foreach (var test in cases)
        {
            var result = await CompileAsync("Forbidden.UnityApi." + test.Name, new[] { test.Source }, new[] { contracts }, true, true);
            RequireSingleDiagnosticAt(result, "AIBT5008", test.ExpectedText, test.Name);
            RequireAtomicUnusable(result, test.Name);

            var suppressed = await CompileAsync("Forbidden.UnityApi.Suppressed." + test.Name,
                new[] { test.Source }, new[] { contracts }, true, true, "AIBT5008");
            RequireSingleDiagnosticAt(suppressed, "AIBT5008", test.ExpectedText, "suppressed " + test.Name);
            RequireAtomicUnusable(suppressed, "suppressed " + test.Name);
        }

        const string allowedApis = "namespace Unity.Mathematics { public struct int2 { public int x; public int y; public int2(int x, int y) { this.x = x; this.y = y; } } } namespace Unity.Collections { public struct FixedString32Bytes { public int Length => 0; public bool Append(char value) => true; } }";
        var allowed = await CompileAsync(
            "Allowed.UnityValueApis",
            new[]
            {
                Support + allowedApis + Node("aibt.valid.unity-values", includeSupport: false,
                    tick: "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { var vector = new Unity.Mathematics.int2(1, 2); var label = default(Unity.Collections.FixedString32Bytes); label.Append('x'); return vector.x == label.Length ? NodeStatus.Success : NodeStatus.Failure; }")
            },
            new[] { contracts },
            true,
            true);
        RequireClean(allowed, "Unity.Mathematics and FixedString value APIs");
        Require(allowed.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal),
            "Allowed Unity value APIs must not make the shard unusable.");
    }

    private static async Task ObserveImportCost(MetadataReference contracts)
    {
        foreach (var count in new[] { 0, 10, 100, 1000 })
        {
            var source = count == 0 ? "namespace Plain { public sealed class Empty { } }" : DeclarationAssembly(count, false);
            var stopwatch = Stopwatch.StartNew();
            var result = await CompileAsync("Cost." + count.ToString(CultureInfo.InvariantCulture), new[] { source }, new[] { contracts }, true, true);
            stopwatch.Stop();
            RequireClean(result, "import-cost " + count);
            Console.WriteLine("Import-cost observation: declarations=" + count.ToString(CultureInfo.InvariantCulture)
                + ", elapsedMs=" + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)
                + ", generatedUtf8Bytes=" + Encoding.UTF8.GetByteCount(result.GeneratedSource).ToString(CultureInfo.InvariantCulture));
        }
    }

    private static string DeclarationAssembly(int count, bool reverse)
    {
        var indices = Enumerable.Range(0, count);
        if (reverse) indices = indices.Reverse();
        var builder = new StringBuilder(Support + "[AibtCatalogShard(\"aibt.cost.shard\",1u)] public partial struct ProbeShard { }");
        builder.Append(ValidConfig).Append(ValidMemory);
        foreach (var index in indices)
            builder.Append(Node(
                "aibt.cost.node" + index.ToString("D4", CultureInfo.InvariantCulture),
                "Node" + index.ToString(CultureInfo.InvariantCulture),
                includeSupport: false,
                includeShard: false,
                includeStorage: false));
        return builder.ToString();
    }

    private static string Node(
        string typeId,
        string nodeName = "ProbeNode",
        bool includeSupport = true,
        bool includeShard = true,
        string? nodeBody = null,
        string? config = null,
        string? tick = null,
        string kind = "BurstNodeKind.Condition",
        bool includeStorage = true,
        string memoryLifetime = "NodeMemoryLifetime.Activation",
        string deterministic = "true",
        string cancellation = "BurstCancellationMode.NotApplicable",
        string cost = "BurstNodeCost.Trivial",
        string statuses = "BurstNodeStatusMask.Success|BurstNodeStatusMask.Failure",
        string? memory = null)
    {
        var builder = new StringBuilder();
        if (includeSupport) builder.Append(Support);
        if (includeShard) builder.Append("[AibtCatalogShard(\"aibt.probe.shard\",1u)] public partial struct ProbeShard { }");
        if (includeStorage) builder.Append(config ?? ValidConfig).Append(memory ?? ValidMemory);
        builder.Append("[AibtNodeDocumentation(\"Probe\",\"Tests\",\"Use\",\"Avoid\",\"probe\")]")
            .Append("[AibtBurstNode(\"").Append(typeId).Append("\",1u,").Append(kind)
            .Append(",typeof(Config),typeof(Memory),").Append(memoryLifetime).Append(',').Append(deterministic).Append(',')
            .Append(cancellation).Append(',').Append(cost).Append(',').Append(statuses).Append(")]" )
            .Append("public partial struct ").Append(nodeName).Append(" {")
            .Append(nodeBody ?? string.Empty)
            .Append("public static void Enter(in Config config, ref Memory memory, ref BurstEnterContext context) { }")
            .Append(tick ?? "public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) => NodeStatus.Success;")
            .Append("public static void Abort(in Config config, ref Memory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }")
            .Append("public static void Exit(in Config config, ref Memory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }")
            .Append("}");
        return builder.ToString();
    }

    private const string Support = "using AIBT; using AIBT.Burst; ";
    private const string ValidConfig = "public partial struct Config { [AibtConfigField(\"enabled\",\"Bool\",1u)] public bool Enabled; }";
    private const string ValidMemory = "public partial struct Memory { [AibtMemoryField(\"count\",\"UInt32\",1u)] public uint Count; }";
    private const string SharedWriteConfig = "public partial struct Config { [AibtConfigField(\"value\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"value\",BurstBlackboardAccess.Write,BlackboardScope.Shared,\"Int32\",1u)] public BlackboardWriteHandle<int> Value; }";

    private static string BlackboardReadConfig(string scope, string bindingId = "read")
        => "public partial struct Config { [AibtConfigField(\"read\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"" + bindingId + "\",BurstBlackboardAccess.Read," + scope + ",\"Int32\",1u)] public BlackboardReadHandle<int> Read; }";

    private static string ScopeAnalyzerSource(string scope, string bindingId = "read")
        => Support + BlackboardReadConfig(scope, bindingId) + ValidMemory
            + "public partial struct ProbeShard { public static class BurstAccess { public static BurstContextResult TryRead(ref BurstTickContext context, BlackboardReadHandle<int> handle, out int value) { value=0; return BurstContextResult.Success; } } }"
            + "[AibtNodeDocumentation(\"Probe\",\"Tests\",\"Use\",\"Avoid\",\"probe\")][AibtBurstNode(\"aibt.invalid.scope-analyzer\",1u,BurstNodeKind.Condition,typeof(Config),typeof(Memory),NodeMemoryLifetime.Activation,true,BurstCancellationMode.NotApplicable,BurstNodeCost.Trivial,BurstNodeStatusMask.Success|BurstNodeStatusMask.Failure)]"
            + "public partial struct ProbeNode { public static void Enter(in Config config, ref Memory memory, ref BurstEnterContext context) { } public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) { ProbeShard.BurstAccess.TryRead(ref context, config.Read, out var value); return NodeStatus.Success; } public static void Abort(in Config config, ref Memory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { } public static void Exit(in Config config, ref Memory memory, ref BurstExitContext context, BurstNodeExitReason reason) { } }";

    private static string DuplicateRegisteredTypeSource()
        => Support + "[AibtCatalogShard(\"aibt.values.duplicate-shard\",1u)] public partial struct ProbeShard { }"
            + "[AibtBurstValue(\"aibt.values.duplicate\",1u,\"aibt.schemas.first\")] public partial struct FirstValue { [AibtValueField(\"value\",\"UInt32\",1u)] public uint Value; }"
            + "[AibtBurstValue(\"aibt.values.duplicate\",1u,\"aibt.schemas.second\")] public partial struct SecondValue { [AibtValueField(\"value\",\"UInt64\",1u)] public ulong Value; }";

    private static string RegisteredPairSource(string firstTypeId, string secondTypeId, string firstSchemaId, string secondSchemaId)
        => Support + "[AibtCatalogShard(\"aibt.values.pair-shard\",1u)] public partial struct ProbeShard { }"
            + "[AibtBurstValue(\"" + firstTypeId + "\",1u,\"" + firstSchemaId + "\")] public partial struct FirstValue { [AibtValueField(\"value\",\"UInt32\",1u)] public uint Value; }"
            + "[AibtBurstValue(\"" + secondTypeId + "\",1u,\"" + secondSchemaId + "\")] public partial struct SecondValue { [AibtValueField(\"value\",\"UInt64\",1u)] public ulong Value; }";

    private static string InvalidRegisteredSchemaSource()
        => Support + "[AibtCatalogShard(\"aibt.values.invalid-schema-shard\",1u)] public partial struct ProbeShard { }"
            + "[AibtBurstValue(\"aibt.values.valid\",1u,\"aibt.schemas.valid\")] public partial struct ValidValue { [AibtValueField(\"value\",\"UInt32\",1u)] public uint Value; }"
            + "[AibtBurstValue(\"aibt.values.invalid\",1u,null)] public partial struct InvalidValue { [AibtValueField(\"value\",\"UInt32\",1u)] public uint Value; }";

    private static string InvalidRegisteredFieldSource()
        => Support + "[AibtCatalogShard(\"aibt.values.missing-field-shard\",1u)] public partial struct ProbeShard { }"
            + "[AibtBurstValue(\"aibt.values.missing-field\",1u,\"aibt.schemas.missing-field\")] public partial struct MissingFieldValue { public uint Missing; }";

    private static string EmptyRegisteredValueSource()
        => Support + "[AibtCatalogShard(\"aibt.values.empty-shard\",1u)] public partial struct ProbeShard { }"
            + "[AibtBurstValue(\"aibt.values.empty\",1u,\"aibt.schemas.empty\")] public partial struct EmptyValue { }";

    private static string DuplicateBindingConfig()
        => "public partial struct Config {"
            + "[AibtConfigField(\"first\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"first\",BurstBlackboardAccess.Read,BlackboardScope.Tree,\"Int32\",1u)] public BlackboardReadHandle<int> First;"
            + "[AibtConfigField(\"second\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"first\",BurstBlackboardAccess.Read,BlackboardScope.Tree,\"Int32\",1u)] public BlackboardReadHandle<int> Second; }";

    private static string DistinctBindingConfig()
        => "public partial struct Config {"
            + "[AibtConfigField(\"first\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"first\",BurstBlackboardAccess.Read,BlackboardScope.Tree,\"Int32\",1u)] public BlackboardReadHandle<int> First;"
            + "[AibtConfigField(\"second\",\"GeneratedHandle\",1u),AibtBlackboardBinding(\"second\",BurstBlackboardAccess.Read,BlackboardScope.Tree,\"Int32\",1u)] public BlackboardReadHandle<int> Second; }";

    private static string NestedRegisteredLayoutSource(bool reordered)
    {
        var fields = reordered
            ? "[AibtValueField(\"m-wide\",\"UInt64\",1u)] public ulong Wide; [AibtValueField(\"a-first\",\"UInt32\",1u)] public uint First; [AibtValueField(\"z-last\",\"UInt32\",1u)] public uint Last;"
            : "[AibtValueField(\"a-first\",\"UInt32\",1u)] public uint First; [AibtValueField(\"m-wide\",\"UInt64\",1u)] public ulong Wide; [AibtValueField(\"z-last\",\"UInt32\",1u)] public uint Last;";
        return Support
            + "[AibtBurstValue(\"aibt.values.nested-layout\",1u,\"aibt.schemas.nested-layout\")] public partial struct NestedValue { " + fields + " }"
            + "[AibtCatalogShard(\"aibt.values.nested-layout-shard\",1u)] public partial struct ProbeShard { }"
            + "public partial struct Config { } public partial struct Memory { [AibtMemoryField(\"value\",\"aibt.values.nested-layout\",1u)] public NestedValue Value; }"
            + "[AibtNodeDocumentation(\"Probe\",\"Tests\",\"Use\",\"Avoid\",\"probe\")][AibtBurstNode(\"aibt.values.nested-layout-node\",1u,BurstNodeKind.Condition,typeof(Config),typeof(Memory),NodeMemoryLifetime.Activation,true,BurstCancellationMode.NotApplicable,BurstNodeCost.Trivial,BurstNodeStatusMask.Success|BurstNodeStatusMask.Failure)]"
            + "public partial struct ProbeNode { public static void Enter(in Config config, ref Memory memory, ref BurstEnterContext context) { } public static NodeStatus Tick(in Config config, ref Memory memory, ref BurstTickContext context) => NodeStatus.Success; public static void Abort(in Config config, ref Memory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { } public static void Exit(in Config config, ref Memory memory, ref BurstExitContext context, BurstNodeExitReason reason) { } }";
    }

    private static async Task<CompilationResult> CompileAsync(
        string assemblyName,
        IEnumerable<string> sources,
        IEnumerable<MetadataReference> references,
        bool generator,
        bool analyzer,
        string? suppress = null)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, "Source" + index.ToString(CultureInfo.InvariantCulture) + ".cs", Encoding.UTF8)).ToArray();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: true, deterministic: true);
        if (suppress != null) options = options.WithSpecificDiagnosticOptions(ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(suppress, ReportDiagnostic.Suppress));
        var compilation = CSharpCompilation.Create(assemblyName, trees, PlatformReferences.Concat(references), options);
        var diagnostics = ImmutableArray<Diagnostic>.Empty;
        var generated = string.Empty;
        if (generator)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new BurstNodeGenerator().AsSourceGenerator() },
                parseOptions: ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics);
            compilation = (CSharpCompilation)updated;
            diagnostics = diagnostics.AddRange(generatorDiagnostics).AddRange(driver.GetRunResult().Diagnostics);
            generated = string.Join("\n", driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources)
                .OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SourceText.ToString()));
        }
        if (analyzer)
        {
            var analyzerDiagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BurstNodeUsageAnalyzer())).GetAnalyzerDiagnosticsAsync();
            diagnostics = diagnostics.AddRange(analyzerDiagnostics);
        }
        diagnostics = diagnostics.AddRange(compilation.GetDiagnostics());
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        diagnostics = diagnostics.AddRange(emit.Diagnostics);
        return new CompilationResult(compilation, stream.ToArray(), diagnostics.Distinct(DiagnosticComparer.Instance).ToArray(), generated);
    }

    private static MetadataReference Reference(CompilationResult result) => MetadataReference.CreateFromImage(ImmutableArray.Create(result.Image));
    private static void RequireClean(CompilationResult result, string label)
    {
        var fatalIds = new HashSet<string>(StringComparer.Ordinal) { "CS8785", "CS8032", "AD0001" };
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || fatalIds.Contains(diagnostic.Id)).ToArray();
        Require(errors.Length == 0, label + " errors: " + string.Join(" | ", errors.Select(diagnostic => diagnostic.Id + "@" + diagnostic.Location.GetLineSpan().Path + ":" + (diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).ToString(CultureInfo.InvariantCulture) + ":" + diagnostic.GetMessage(CultureInfo.InvariantCulture))));
    }
    private static void RequireDiagnostic(CompilationResult result, string id, bool sourceLocation, string? expectedText = null, int? additionalLocations = null)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Require(matches.Length >= 1, id + " missing; actual=" + string.Join(",", result.Diagnostics.Select(diagnostic => diagnostic.Id).Distinct()));
        var diagnostic = matches[0];
        Require(diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Descriptor.IsEnabledByDefault
            && diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal), id + " descriptor contract differs");
        Require(sourceLocation ? diagnostic.Location.IsInSource : diagnostic.Location == Location.None, id + " location kind differs");
        if (expectedText != null)
        {
            var actualText = diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? string.Empty;
            Require(actualText == expectedText, id + " location text differs: expected '" + expectedText + "', actual '" + actualText + "'");
        }
        if (additionalLocations.HasValue)
            Require(diagnostic.AdditionalLocations.Count == additionalLocations.Value, id + " additional-location count differs");
    }
    private static void RequireSingleDiagnosticAt(
        CompilationResult result,
        string id,
        string expectedText,
        string? label = null,
        int expectedAdditionalLocations = 0)
    {
        var matches = result.Diagnostics.Where(diagnostic => diagnostic.Id == id).ToArray();
        Require(matches.Length == 1,
            id + " exact diagnostic count differs for " + (label ?? expectedText) + ": "
            + matches.Length.ToString(CultureInfo.InvariantCulture) + "; locations="
            + string.Join(" | ", matches.Select(diagnostic =>
                diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? "<none>")));
        var diagnostic = matches[0];
        Require(diagnostic.Severity == DiagnosticSeverity.Error && diagnostic.Descriptor.IsEnabledByDefault
            && diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal), id + " descriptor contract differs");
        Require(diagnostic.Location.IsInSource, id + " must have an exact source location");
        var actualText = diagnostic.Location.SourceTree?.GetText().ToString(diagnostic.Location.SourceSpan) ?? string.Empty;
        Require(actualText == expectedText, id + " location text differs: expected '" + expectedText + "', actual '" + actualText + "'");
        Require(diagnostic.AdditionalLocations.Count == expectedAdditionalLocations,
            id + " additional-location count differs for " + (label ?? expectedText));
    }
    private static void RequireAtomicUnusable(CompilationResult result, string label)
    {
        Require(result.GeneratedSource.Contains("IsUsable = false", StringComparison.Ordinal), label + " missing unusable marker");
        foreach (var forbidden in new[] { "IsUsable = true", "AbiVersion = 1u", "AbiVersion = 2u", "BurstAccess", "BurstCodec", "Fingerprint", "ExecuteImmediate", "Schedule(" })
            Require(!result.GeneratedSource.Contains(forbidden, StringComparison.Ordinal), label + " emitted usable/out-of-scope product " + forbidden);
    }
    private static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Negative(string Id, string Source, string ExpectedText, int? AdditionalLocations = null);
    private sealed record ForbiddenFlow(string Name, string Source, string ExpectedText);
    private sealed record CompilationResult(CSharpCompilation Compilation, byte[] Image, Diagnostic[] Diagnostics, string GeneratedSource);
    private sealed class DiagnosticComparer : IEqualityComparer<Diagnostic>
    {
        internal static readonly DiagnosticComparer Instance = new();
        public bool Equals(Diagnostic? left, Diagnostic? right) => left?.Id == right?.Id && left?.Location.SourceSpan == right?.Location.SourceSpan && left?.GetMessage(CultureInfo.InvariantCulture) == right?.GetMessage(CultureInfo.InvariantCulture);
        public int GetHashCode(Diagnostic value) => HashCode.Combine(value.Id, value.Location.SourceSpan, value.GetMessage(CultureInfo.InvariantCulture));
    }
}
