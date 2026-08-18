using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AIBT.Burst;
using AIBT.BurstAbi.Feasibility;
using AIBT.BurstNodeAbi.Feasibility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

internal static class Program
{
    private static int assertionCount;
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp9);
    private static readonly MetadataReference[] PlatformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
        ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable."))
        .Split(Path.PathSeparator).Where(path => !Path.GetFileName(path).StartsWith("AIBT.", StringComparison.Ordinal))
        .Select(path => MetadataReference.CreateFromFile(path)).ToArray();

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: runner <harness-source-directory> <output-directory>"); return 2; }
        var stopwatch = Stopwatch.StartNew();
        var sourceDirectory = Path.GetFullPath(args[0]);
        var outputDirectory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outputDirectory);

        var contractsSource = File.ReadAllText(Path.Combine(sourceDirectory, "AbiContracts.cs"), Encoding.UTF8);
        var randomSource = File.ReadAllText(Path.Combine(sourceDirectory, "DeterministicRandomCanary.cs"), Encoding.UTF8);
        var runtimeSource = File.ReadAllText(Path.GetFullPath(Path.Combine(sourceDirectory, "..", "..", "..", "Runner", "RuntimeStubs.cs")), Encoding.UTF8);
        var contracts = await CompileAsync("AIBT.Burst.Contracts", new[] { runtimeSource, contractsSource, randomSource }, Array.Empty<MetadataReference>(), false, false);
        RequireClean(contracts, "contracts");
        var contractReference = MetadataReference.CreateFromImage(ImmutableArray.Create(contracts.Image));
        VerifyAbiSurface();

        var nodeA = await CompileAsync("AIBT.Nodes.Canary", new[] { NodeA }, new[] { contractReference }, true, true);
        RequireClean(nodeA, "node shard A");
        var nodeB = await CompileAsync("AIBT.Nodes.Observer", new[] { NodeB }, new[] { contractReference }, true, true);
        RequireClean(nodeB, "node shard B");
        var runtimeBuiltins = await CompileAsync("AIBT.Runtime.Builtins", new[] { RuntimeBuiltins }, new[] { contractReference }, true, true);
        RequireClean(runtimeBuiltins, "Runtime built-ins fixture shard");
        var references = new[] { contractReference, Reference(runtimeBuiltins), Reference(nodeA), Reference(nodeB) };

        var first = await CompileAsync("AIBT.Catalog.Consumer", new[] { Catalog }, references, true, true);
        RequireClean(first, "catalog generation one");
        VerifyGeneratedFacade(first.Compilation);
        CompilationResult second;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var alternateCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentCulture = alternateCulture;
            CultureInfo.CurrentUICulture = alternateCulture;
            var reversedReferences = new[] { contractReference, Reference(nodeB), Reference(nodeA), Reference(runtimeBuiltins) };
            second = await CompileAsync("AIBT.Catalog.Consumer", new[] { CatalogReversed }, reversedReferences, true, true);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
        RequireClean(second, "catalog generation two");
        Require(first.GeneratedSource == second.GeneratedSource, "clean generation output differs");
        Require(first.GeneratedSource.Contains("BurstHash256(0x687f9b60u, 0x037d7bf9u, 0xa1730794u, 0x129956cdu, 0xfbe03994u, 0xfd55e64bu, 0x7288bca0u, 0x22bea04bu)", StringComparison.Ordinal),
            "pinned independent catalog-v1 byte vector changed");
        Require(Convert.ToHexString(IndependentCatalogFingerprint()).ToLowerInvariant() == "609b7f68f97b7d03940773a1cd5699129439e0fb4be655fda0bc88724ba0be22",
            "independent full three-shard catalog byte stream changed");
        var registryBytes = IndependentRegistryBytes(); using (var registrySha = SHA256.Create())
        {
            var registryHash = registrySha.ComputeHash(registryBytes);
            Require(Convert.ToHexString(registryHash).ToLowerInvariant() == "7ee137f15483dc75bd251c6469f3f0f189519dfac622a1f8e7498f3f249381a6", "independent complete P1 registry bytes/hash changed: " + Convert.ToHexString(registryHash).ToLowerInvariant());
            Require(first.GeneratedSource.Contains(IndependentHashLiteral(registryHash), StringComparison.Ordinal), "generated registry SHA does not match independently serialized complete P1 bytes");
        }
        Require(registryBytes.Length > 1000 && registryBytes[^1] == (byte)'\n' && registryBytes[^2] == (byte)'}', "canonical P1 registry must retain complete pretty JSON and one trailing LF");
        var randomNodeA = await CompileAsync("AIBT.Nodes.Canary", new[] { NodeA.Replace(
            "[AibtBurstNode(\"aibt.canary.action\"", "[AibtRandomStream]\n[AibtBurstNode(\"aibt.canary.action\"", StringComparison.Ordinal) },
            new[] { contractReference }, true, true);
        RequireClean(randomNodeA, "random-capable shard mutation");
        var randomCatalog = await CompileAsync("AIBT.Catalog.Consumer", new[] { Catalog },
            new[] { contractReference, Reference(runtimeBuiltins), Reference(randomNodeA), Reference(nodeB) }, true, true);
        RequireClean(randomCatalog, "random-capability catalog mutation");
        Require(ExtractCatalogFingerprint(first.GeneratedSource) != ExtractCatalogFingerprint(randomCatalog.GeneratedSource),
            "AibtRandomStream capability bit did not change the catalog fingerprint");
        var baseCatalogBytes = IndependentCatalogBytes(0, out var capabilityOffset);
        var randomCatalogBytes = IndependentCatalogBytes(1, out var randomCapabilityOffset);
        Require(capabilityOffset == randomCapabilityOffset && baseCatalogBytes[capabilityOffset] == 0 && randomCatalogBytes[randomCapabilityOffset] == 1,
            "RandomStream capability byte is not the exact U8 value at its canonical node-record position");
        Require(baseCatalogBytes.Length == randomCatalogBytes.Length && Enumerable.Range(0, baseCatalogBytes.Length).All(index => index == capabilityOffset || baseCatalogBytes[index] == randomCatalogBytes[index]),
            "RandomStream mutation changed bytes outside the exact capability position");
        using (var randomSha = SHA256.Create()) Require(randomCatalog.GeneratedSource.Contains(IndependentHashLiteral(randomSha.ComputeHash(randomCatalogBytes)), StringComparison.Ordinal),
            "generated random-capability catalog does not match independent exact byte stream");
        Require(first.GeneratedSource.Contains("CanaryNode.Enter", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("ObserverNode.Evaluate", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("RuntimeBuiltinNode.Tick", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("case 0u:", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("case 1u:", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("case 2u:", StringComparison.Ordinal), "heterogeneous direct cases missing");
        Require(first.GeneratedSource.Contains("TryReadUInt32", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryReadUInt64", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryReadBoolean", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryWriteMemoryUInt64", StringComparison.Ordinal), "fieldwise bridge canary missing");
        Require(first.GeneratedSource.Contains("TryCompleteTick", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryCompleteObserver", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryGetAbortReason", StringComparison.Ordinal)
            && first.GeneratedSource.Contains("TryGetExitReason", StringComparison.Ordinal), "lifecycle result/reason propagation missing");
        var observerReturn = first.GeneratedSource.IndexOf("TryCompleteObserver", StringComparison.Ordinal);
        var observerBranch = first.GeneratedSource.LastIndexOf("if (phase == global::AIBT.Burst.BurstCallbackPhase.Observer)", observerReturn, StringComparison.Ordinal);
        Require(observerBranch >= 0 && observerReturn > observerBranch, "observer branch missing");
        var observerSlice = first.GeneratedSource.Substring(observerBranch, observerReturn - observerBranch);
        Require(observerSlice.IndexOf("MemoryAccessor", StringComparison.Ordinal) < 0
            && observerSlice.IndexOf("memoryField", StringComparison.Ordinal) < 0, "observer dispatch touched node memory");
        ForbidGenerated(first.GeneratedSource);
        var afterAcquire = first.GeneratedSource.Substring(first.GeneratedSource.IndexOf("TryAcquireDispatchFrame", StringComparison.Ordinal));
        Require(afterAcquire.IndexOf("return bridgeResult;", StringComparison.Ordinal) < 0, "post-acquire direct failure bypasses TryFailDispatch");
        Require(first.GeneratedSource.Contains("TryPrepareSchedule(ref batch, out scheduledView)", StringComparison.Ordinal),
            "Schedule does not use the exact atomic Ready-to-Scheduled bridge seam");

        await VerifyNegativeDiagnostics(contractReference);
        await VerifyGlobalCatalogCollision(contractReference, Reference(runtimeBuiltins), Reference(nodeA), Reference(nodeB));
        await VerifyCompileTimeHandshake(contractReference);
        VerifyBridgeAndAsyncOwnership();
        VerifyRandomContexts();
        VerifyStandaloneRandomVectors();
        VerifyBuiltInCanonicalCodecCanaries();

        var generatedPath = Path.Combine(outputDirectory, "AibtBurstCatalogSet.g.cs");
        File.WriteAllText(generatedPath, first.GeneratedSource, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outputDirectory, "generated.sha256"), Sha256(first.GeneratedSource) + Environment.NewLine, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(outputDirectory, "diagnostics.txt"), first.Diagnostics.Select(Format), new UTF8Encoding(false));
        stopwatch.Stop();
        Console.WriteLine("Roslyn 4.3.1 feasibility passed: 3 shards + consumer catalog, 2 deterministic runs, diagnostic/ABI/RNG/bridge matrices green.");
        Console.WriteLine("Assertions: " + assertionCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Generated SHA-256: " + Sha256(first.GeneratedSource));
        Console.WriteLine("Generated UTF-8 bytes: " + Encoding.UTF8.GetByteCount(first.GeneratedSource).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("Elapsed ms: " + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        return 0;
    }

    private static async Task VerifyNegativeDiagnostics(MetadataReference contracts)
    {
        var cases = new[]
        {
            new Negative("AIBT5001", Node("public string Forbidden;", ValidConfig, ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config { [AibtConfigField(\"a\", \"aibt.string\", 1u)] public string Value; }", ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config { [AibtConfigField(\"a\", \"aibt.intptr\", 1u)] public System.IntPtr Value; }", ValidMemory)),
            new Negative("AIBT5003", Node(string.Empty, ValidConfig, ValidMemory, tickSignature: "public static void Tick(in Config config, ref Memory memory, ref BurstTickContext context) { }")),
            new Negative("AIBT5004", Node(string.Empty, ValidConfig, ValidMemory, kind: "(BurstNodeKind)9")),
            new Negative("AIBT5006", Node(string.Empty, BindingConfig, ValidMemory, tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { ProbeShard.BurstAccess.TryRead(ref context, default(BlackboardReadHandle<int>), out var value); return NodeStatus.Success; }")),
            new Negative("AIBT5007", Node(string.Empty, SharedWriteConfig, ValidMemory, tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { ProbeShard.BurstAccess.TryWrite(ref context, settings.Value, 1); return NodeStatus.Success; }")),
            new Negative("AIBT5008", Node(string.Empty, ValidConfig, ValidMemory, tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { var go = new UnityEngine.GameObject(); var type = typeof(UnityEngine.GameObject); UnityEngine.GameObject.Find(\"x\"); return NodeStatus.Success; }"), includeUnity: true),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, typeId: "Invalid", docs: "[AibtNodeDocumentation(\"\", \"Tests\", \"Use\", \"Avoid\", \"ex\")]")),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, typeId: "aibt..node")),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, typeId: "aibt.-node")),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, typeId: "aibt-.node")),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, docs: "[AibtNodeDocumentation(\"Probe\", \"Tests\", \"Use\", \"Avoid\", \"dup\", \"dup\")]")),
            new Negative("AIBT5009", Node(string.Empty, ValidConfig, ValidMemory, docs: "[AibtNodeDocumentation(\"Probe\", \"Tests\", \"Use\", \"Avoid\", \"-invalid\")]")),
            new Negative("AIBT5002", Node(string.Empty, "partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }", ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }", ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config<T> where T : unmanaged { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }", ValidMemory, configName: "Config<int>")),
            new Negative("AIBT5002", Node(string.Empty, "public static class Holder { public partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; } }", ValidMemory, configName: "Holder.Config")),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config { public int Value; }", ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u), AibtConfigField(\"b\", \"UInt32\", 1u)] public uint Value; }", ValidMemory)),
            new Negative("AIBT5002", Node(string.Empty, "public partial struct Config { [AibtBlackboardBinding(\"input\", BurstBlackboardAccess.Read, AIBT.BlackboardScope.Tree, \"Int32\", 1u)] public BlackboardReadHandle<int> Value; }", ValidMemory)),
            new Negative("AIBT5004", Node(string.Empty, ValidConfig, ValidMemory, statuses: "BurstNodeStatusMask.Success | BurstNodeStatusMask.Running")),
            new Negative("AIBT5004", Node(string.Empty, ValidConfig, ValidMemory, cancellation: "BurstCancellationMode.Command", statuses: "BurstNodeStatusMask.Success | BurstNodeStatusMask.Running")),
            new Negative("AIBT5007", Node(string.Empty, "public partial struct Config { [AibtConfigField(\"operation\", \"GeneratedHandle\", 1u), AibtAsyncOperationBinding(\"operation\", \"Int32\", 1u, \"UInt32\", 1u)] public AsyncOperationHandle<int,uint> Value; }", ValidMemory)),
            new Negative("AIBT5007", Node(string.Empty, "public partial struct Config { [AibtConfigField(\"operation\", \"GeneratedHandle\", 1u), AibtAsyncOperationBinding(\"operation\", \"Int32\", 1u, \"UInt32\", 1u)] public AsyncOperationHandle<int,uint> Value; }", ValidMemory, cancellation: "BurstCancellationMode.AbortOnly")),
        };
        var suppressionCovered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var test in cases)
        {
            var sources = test.IncludeUnity ? new[] { UnityApiStub, test.Source } : new[] { test.Source };
            var result = await CompileAsync("Negative." + test.Id, sources, new[] { contracts }, true, true);
            RequireDiagnostic(result, test.Id);
            Require(!result.LogicallyUsable, test.Id + " did not make compilation unusable");
            RequireAtomicUnusableShard(result, test.Id);
            if (suppressionCovered.Add(test.Id))
            {
                var suppressed = await CompileAsync("Suppressed." + test.Id, sources, new[] { contracts }, true, true, true);
                RequireDiagnostic(suppressed, test.Id); RequireAtomicUnusableShard(suppressed, "suppressed " + test.Id);
            }
        }

        var callbackLocation = await CompileAsync("Negative.Location.Callback", new[] { Node(string.Empty, ValidConfig, ValidMemory, tickSignature: "public static void Tick(in Config config, ref Memory memory, ref BurstTickContext context) { }") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(callbackLocation, "AIBT5003", "Tick", 1, 0);
        var kindLocation = await CompileAsync("Negative.Location.Kind", new[] { Node(string.Empty, ValidConfig, ValidMemory, kind: "(BurstNodeKind)9") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(kindLocation, "AIBT5004", "(BurstNodeKind)9", 1, 0);
        var identityLocation = await CompileAsync("Negative.Location.Identity", new[] { Node(string.Empty, ValidConfig, ValidMemory, typeId: "Invalid") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(identityLocation, "AIBT5009", "\"Invalid\"", 1, 0);
        var documentationLocation = await CompileAsync("Negative.Location.Documentation", new[] { Node(string.Empty, ValidConfig, ValidMemory, docs: "[AibtNodeDocumentation(\"\", \"Tests\", \"Use\", \"Avoid\", \"ex\")]") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(documentationLocation, "AIBT5009", "\"\"", 1, 0);
        var undeclaredInvocation = "ProbeShard.BurstAccess.TryRead(ref context, default(BlackboardReadHandle<int>), out var value)";
        var undeclaredLocation = await CompileAsync("Negative.Location.Undeclared", new[] { Node(string.Empty, BindingConfig, ValidMemory, tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { " + undeclaredInvocation + "; return NodeStatus.Success; }") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(undeclaredLocation, "AIBT5006", undeclaredInvocation, 1, 0);
        var forbiddenExpression = "new UnityEngine.GameObject()";
        var forbiddenLocation = await CompileAsync("Negative.Location.Forbidden", new[] { UnityApiStub, Node(string.Empty, ValidConfig, ValidMemory, tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { var go = " + forbiddenExpression + "; return NodeStatus.Success; }") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(forbiddenLocation, "AIBT5008", forbiddenExpression, 1, 0);
        var fieldBearingNode = await CompileAsync("Negative.Location.NodeField", new[] { Node("public int Forbidden;", ValidConfig, ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(fieldBearingNode, "AIBT5001", "Forbidden", 1, 0);
        var structuralStorage = await CompileAsync("Negative.Location.StorageType", new[] { Node(string.Empty, "partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }", ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(structuralStorage, "AIBT5002", "Config", 1, 0);
        var bindingTypeId = await CompileAsync("Negative.Location.BindingTypeId", new[] { Node(string.Empty, BindingConfig.Replace("\"Int32\", 1u", "\"UInt32\", 1u", StringComparison.Ordinal), ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(bindingTypeId, "AIBT5007", "\"UInt32\"", 1, 0);
        var bindingScope = await CompileAsync("Negative.Location.BindingScope", new[] { Node(string.Empty, SharedWriteConfig, ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(bindingScope, "AIBT5007", "AIBT.BlackboardScope.Shared", 1, 0);
        const string asyncBindingConfig = "public partial struct Config { [AibtConfigField(\"operation\", \"GeneratedHandle\", 1u), AibtAsyncOperationBinding(\"operation\", \"Int32\", 1u, \"UInt32\", 1u)] public AsyncOperationHandle<int,uint> Value; }";
        const string asyncBindingText = "AibtAsyncOperationBinding(\"operation\", \"Int32\", 1u, \"UInt32\", 1u)";
        var notApplicableAsync = await CompileAsync("Negative.Location.NotApplicableAsync", new[] { Node(string.Empty, asyncBindingConfig, ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(notApplicableAsync, "AIBT5007", asyncBindingText, 1, 0);
        var suppressedNotApplicableAsync = await CompileAsync("Suppressed.Location.NotApplicableAsync", new[] { Node(string.Empty, asyncBindingConfig, ValidMemory) }, new[] { contracts }, true, true, true);
        RequireDiagnosticSpan(suppressedNotApplicableAsync, "AIBT5007", asyncBindingText, 1, 0);
        var abortOnlyAsync = await CompileAsync("Negative.Location.AbortOnlyAsync", new[] { Node(string.Empty, asyncBindingConfig, ValidMemory, cancellation: "BurstCancellationMode.AbortOnly") }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(abortOnlyAsync, "AIBT5007", asyncBindingText, 1, 0);
        var suppressedAbortOnlyAsync = await CompileAsync("Suppressed.Location.AbortOnlyAsync", new[] { Node(string.Empty, asyncBindingConfig, ValidMemory, cancellation: "BurstCancellationMode.AbortOnly") }, new[] { contracts }, true, true, true);
        RequireDiagnosticSpan(suppressedAbortOnlyAsync, "AIBT5007", asyncBindingText, 1, 0);
        var fieldVersion = await CompileAsync("Negative.Location.FieldVersion", new[] { Node(string.Empty, "public partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 0u)] public uint Value; }", ValidMemory) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(fieldVersion, "AIBT5009", "0u", 1, 0);
        var valueSchema = await CompileAsync("Negative.Location.ValueSchema", new[] { PaddedPayloadNode.Replace("\"aibt.schema.padded-inner.v1\"", "\"Invalid\"", StringComparison.Ordinal) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(valueSchema, "AIBT5009", "\"Invalid\"", 1, 0);
        var valueFieldVersion = await CompileAsync("Negative.Location.ValueFieldVersion", new[] { PaddedPayloadNode.Replace("[AibtValueField(\"a-flag\", \"UInt8\", 1u)]", "[AibtValueField(\"a-flag\", \"UInt8\", 2u)]", StringComparison.Ordinal) }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(valueFieldVersion, "AIBT5007", "2u", 1, 0);
        var fieldCollisionSource = Node(string.Empty, "public partial struct Config { [AibtConfigField(\"a-same\", \"UInt32\", 1u)] public uint First; [AibtConfigField(\"a-same\", \"UInt32\", 1u)] public uint Second; }", ValidMemory);
        var fieldCollision = await CompileAsync("Negative.Location.FieldCollision", new[] { fieldCollisionSource }, new[] { contracts }, true, true);
        RequireDiagnosticSpan(fieldCollision, "AIBT5010", "\"a-same\"", 1, 1);

        var duplicate = Node(string.Empty, ValidConfig, ValidMemory, nodeName: "First") + Node(string.Empty,
            "public partial struct Config2 { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }",
            "public partial struct Memory2 { [AibtMemoryField(\"a\", \"Int32\", 1u)] public int Value; }", nodeName: "Second", configName: "Config2", memoryName: "Memory2");
        var duplicateResult = await CompileAsync("Negative.Duplicate", new[] { duplicate }, new[] { contracts }, true, true);
        var duplicateDiagnostics = duplicateResult.Diagnostics.Where(item => item.Id == "AIBT5005").ToArray();
        Require(duplicateDiagnostics.Length == 1 && duplicateDiagnostics[0].Location.IsInSource && duplicateDiagnostics[0].AdditionalLocations.Count == 1,
            "duplicate collision set must emit one deterministic primary plus one additional location");
        Require(DiagnosticText(duplicateDiagnostics[0]) == "\"aibt.probe.node\"" && DiagnosticText(duplicateDiagnostics[0], true) == "\"aibt.probe.node\"", "AIBT5005 primary/additional must be canonical-ID attribute arguments: " + DiagnosticText(duplicateDiagnostics[0]) + " / " + DiagnosticText(duplicateDiagnostics[0], true));
        var suppressedDuplicate = await CompileAsync("Suppressed.AIBT5005", new[] { duplicate }, new[] { contracts }, true, true, true); RequireDiagnostic(suppressedDuplicate, "AIBT5005"); RequireAtomicUnusableShard(suppressedDuplicate, "suppressed AIBT5005");

        BurstNodeGenerator.ForceNumericIdentityCollisionForTests = true;
        try
        {
            var collision = Node(string.Empty, ValidConfig, ValidMemory, nodeName: "First", typeId: "aibt.first") + Node(string.Empty,
                "public partial struct Config2 { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }",
                "public partial struct Memory2 { [AibtMemoryField(\"a\", \"Int32\", 1u)] public int Value; }", nodeName: "Second", typeId: "aibt.second", configName: "Config2", memoryName: "Memory2");
            var collisionResult = await CompileAsync("Negative.ForcedCollision", new[] { collision }, new[] { contracts }, true, true);
            var collisionDiagnostics = collisionResult.Diagnostics.Where(item => item.Id == "AIBT5010").ToArray();
            Require(collisionDiagnostics.Length == 1 && collisionDiagnostics[0].Location.IsInSource && collisionDiagnostics[0].AdditionalLocations.Count >= 1,
                "numeric collision set must emit one UTF8-greater primary plus additional locations");
            Require(DiagnosticText(collisionDiagnostics[0]) == "\"aibt.second\"" && DiagnosticText(collisionDiagnostics[0], true) == "\"aibt.first\"", "AIBT5010 primary must be UTF8-greater canonical-ID argument with the other ID additional");
        }
        finally { BurstNodeGenerator.ForceNumericIdentityCollisionForTests = false; }

        const string schemaCollisionValues = "[AibtBurstValue(\"aibt.value.one\",1u,\"aibt.schema.one\")] public partial struct ValueOne { [AibtValueField(\"a\",\"UInt32\",1u)] public uint Value; } [AibtBurstValue(\"aibt.value.two\",1u,\"aibt.schema.two\")] public partial struct ValueTwo { [AibtValueField(\"a\",\"UInt32\",1u)] public uint Value; }";
        BurstNodeGenerator.ForceNumericIdentityCollisionForTests = true;
        try
        {
            var schemaCollisionSource = Node(string.Empty, ValidConfig, ValidMemory) + " namespace Probe { " + schemaCollisionValues + " }";
            var schemaCollision = await CompileAsync("Negative.Location.SchemaCollision", new[] { schemaCollisionSource }, new[] { contracts }, true, true);
            RequireDiagnosticSpan(schemaCollision, "AIBT5010", "\"aibt.schema.two\"", 1, 1);
            var suppressedSchemaCollision = await CompileAsync("Suppressed.AIBT5010", new[] { schemaCollisionSource }, new[] { contracts }, true, true, true);
            RequireDiagnostic(suppressedSchemaCollision, "AIBT5010"); RequireAtomicUnusableShard(suppressedSchemaCollision, "suppressed AIBT5010");
        }
        finally { BurstNodeGenerator.ForceNumericIdentityCollisionForTests = false; }

        const string unusableShard = "using AIBT.Burst; namespace External { [AibtCatalogShard(\"external.bad\",1u)] public partial struct BadShard { public const bool IsUsable=false; public const uint AbiVersion=1u; } }";
        var externalBad = await CompileAsync("External.Bad", new[] { unusableShard }, new[] { contracts }, false, false); RequireClean(externalBad, "external unusable shard fixture");
        const string badCatalogSource = "using AIBT.Burst; namespace Bad.Consumer { [AibtCatalogSet(\"bad.catalog\",1u,typeof(External.BadShard))] public static partial class Catalog { } }";
        var badCatalog = await CompileAsync("Negative.Location.CatalogShard", new[] { badCatalogSource }, new[] { contracts, Reference(externalBad) }, true, true);
        RequireDiagnosticSpan(badCatalog, "AIBT5011", "typeof(External.BadShard)", 1, 0, false); RequireAtomicUnusableCatalog(badCatalog, "selected unusable shard");
        var suppressedBadCatalog = await CompileAsync("Suppressed.AIBT5011", new[] { badCatalogSource }, new[] { contracts, Reference(externalBad) }, true, true, true);
        RequireDiagnostic(suppressedBadCatalog, "AIBT5011"); RequireAtomicUnusableCatalog(suppressedBadCatalog, "suppressed AIBT5011");

        var externalCollision = await CompileAsync("External.Collision", new[] { ExternalCollisionShard }, new[] { contracts }, false, false);
        RequireClean(externalCollision, "external conflicting selected-shard fixture");
        const string collisionCatalogSource = "using AIBT.Burst; namespace Collision.Consumer { [AibtCatalogSet(\"collision.catalog\",1u,typeof(External.CollisionShard))] public static partial class Catalog { } }";
        var collisionCatalog = await CompileAsync("Negative.Location.ExternalShardCollision", new[] { collisionCatalogSource }, new[] { contracts, Reference(externalCollision) }, true, true);
        RequireDiagnosticSpan(collisionCatalog, "AIBT5011", "typeof(External.CollisionShard)", 1, 0, false);
        RequireAtomicUnusableCatalog(collisionCatalog, "external selected-shard collision");
        var suppressedCollisionCatalog = await CompileAsync("Suppressed.Location.ExternalShardCollision", new[] { collisionCatalogSource }, new[] { contracts, Reference(externalCollision) }, true, true, true);
        RequireDiagnosticSpan(suppressedCollisionCatalog, "AIBT5011", "typeof(External.CollisionShard)", 1, 0, false);
        RequireAtomicUnusableCatalog(suppressedCollisionCatalog, "suppressed external selected-shard collision");

        var bindingValid = Node(string.Empty, BindingConfig, ValidMemory,
            tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { ProbeShard.BurstAccess.TryRead(ref context, settings.Value, out var value); return value == 0 ? NodeStatus.Success : NodeStatus.Failure; }");
        RequireNoDiagnostic(await CompileAsync("Positive.ParameterSymbol", new[] { bindingValid }, new[] { contracts }, true, true), "AIBT5006", "AIBT5007");
        var commandConfig = "public partial struct Config { [AibtConfigField(\"operation\", \"GeneratedHandle\", 1u), AibtAsyncOperationBinding(\"operation\", \"Int32\", 1u, \"UInt32\", 1u)] public AsyncOperationHandle<int,uint> Value; }";
        var commandNode = Node(string.Empty, commandConfig, ValidMemory, cancellation: "BurstCancellationMode.Command", statuses: "BurstNodeStatusMask.Success | BurstNodeStatusMask.Running");
        RequireClean(await CompileAsync("Positive.CommandCancellation", new[] { commandNode }, new[] { contracts }, true, true), "Command cancellation declaration matrix");

        var missingRandomMarker = Node(string.Empty, ValidConfig, ValidMemory,
            tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { context.TryNextUInt32(out var value); return value == 0 ? NodeStatus.Success : NodeStatus.Failure; }");
        var missingRandomResult = await CompileAsync("Negative.MissingRandomMarker", new[] { missingRandomMarker }, new[] { contracts }, true, true);
        var missingRandomDiagnostics = missingRandomResult.Diagnostics.Where(item => item.Id == "AIBT5007" && item.Severity == DiagnosticSeverity.Error).ToArray();
        Require(missingRandomDiagnostics.Length == 1
            && missingRandomDiagnostics[0].Descriptor.IsEnabledByDefault
            && missingRandomDiagnostics[0].Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal)
            && missingRandomDiagnostics[0].GetMessage(CultureInfo.InvariantCulture).Contains("TryNextUInt32", StringComparison.Ordinal)
            && missingRandomDiagnostics[0].GetMessage(CultureInfo.InvariantCulture).Contains("random-stream-marker", StringComparison.Ordinal),
            "missing random marker did not produce the exact AIBT5007 diagnostic");
        Require(!missingRandomResult.LogicallyUsable, "missing random marker left a usable node assembly");
        RequireAtomicUnusableShard(missingRandomResult, "missing random marker");
        var suppressedRandom = await CompileAsync("Negative.SuppressedRandom", new[] { missingRandomMarker }, new[] { contracts }, true, true, true);
        RequireDiagnostic(suppressedRandom, "AIBT5007");
        RequireAtomicUnusableShard(suppressedRandom, "suppressed random marker");

        var markedRandom = Node(string.Empty, ValidConfig, ValidMemory, nodeAttributes: "[AibtRandomStream]",
            tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { context.TryNextUInt32(out var value); context.TryNextFloat32(out var sample); return value == 0 && sample >= 0 ? NodeStatus.Success : NodeStatus.Failure; }");
        var markedRandomResult = await CompileAsync("Positive.MarkedRandom", new[] { markedRandom }, new[] { contracts }, true, true);
        RequireClean(markedRandomResult, "marked random stream");
        RequireNoDiagnostic(markedRandomResult, "AIBT5007");

        var wrongHandle = Node(string.Empty, WrongHandleConfig, ValidMemory,
            tickSignature: "public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { var value = 1; ProbeShard.BurstAccess.TryWrite(ref context, settings.Value, in value); return NodeStatus.Success; }");
        var wrongHandleResult = await CompileAsync("Negative.WrongHandleType", new[] { wrongHandle }, new[] { contracts }, true, true);
        var wrongHandleDiagnostics = wrongHandleResult.Diagnostics.Where(item => item.Id == "AIBT5007" && item.Severity == DiagnosticSeverity.Error).ToArray();
        Require(wrongHandleDiagnostics.Length == 1
            && wrongHandleDiagnostics[0].Location.IsInSource
            && wrongHandleDiagnostics[0].GetMessage(CultureInfo.InvariantCulture).Contains("Value", StringComparison.Ordinal),
            "wrong handle type did not produce the exact AIBT5007 diagnostic");
        Require(!wrongHandleResult.LogicallyUsable, "wrong handle type left a usable node assembly");
        RequireAtomicUnusableShard(wrongHandleResult, "wrong handle type");

        var nativeContainer = Node(string.Empty,
            "public partial struct Config { [AibtConfigField(\"a\", \"aibt.native-array\", 1u)] public Unity.Collections.NativeArray<int> Value; }", ValidMemory)
            + " namespace Unity.Collections { public struct NativeArray<T> where T : unmanaged { } }";
        var nativeContainerResult = await CompileAsync("Negative.NativeContainer", new[] { nativeContainer }, new[] { contracts }, true, true);
        RequireDiagnostic(nativeContainerResult, "AIBT5002");
        Require(!nativeContainerResult.LogicallyUsable, "native container declaration left a usable node assembly");

        var padded = await CompileAsync("Positive.PaddedPayload", new[] { PaddedPayloadNode }, new[] { contracts }, true, true);
        RequireClean(padded, "padded registered payload codec");
        Require(padded.GeneratedSource.Contains("PaddedPayload.BurstCodec.TryWrite", StringComparison.Ordinal)
            && padded.GeneratedSource.Count(ch => ch == '\n') > 20
            && padded.GeneratedSource.Contains("TryWriteValue(ref writer, 0u", StringComparison.Ordinal)
            && padded.GeneratedSource.Contains("TryWriteValue(ref writer, 1u", StringComparison.Ordinal), "padded payload was not encoded fieldwise");
        var paddedCatalog = await CompileAsync("Positive.PaddedCatalog", new[] { "using AIBT.Burst; namespace Padded.Consumer { [AibtCatalogSet(\"padded.catalog\", 1u, typeof(Padded.Probe.ProbeShard))] public static partial class Catalog { } }" }, new[] { contracts, Reference(padded) }, true, true);
        RequireClean(paddedCatalog, "padded registered catalog fingerprints");
        VerifyIndependentRegisteredFingerprints(paddedCatalog.GeneratedSource);
        var autoValue = await CompileAsync("Negative.AutoValue", new[] { PaddedPayloadNode.Replace("public byte Flag;", "public byte Flag; public int Auto { get; set; }", StringComparison.Ordinal) }, new[] { contracts }, true, true);
        RequireDiagnostic(autoValue, "AIBT5002"); RequireAtomicUnusableShard(autoValue, "registered auto-property");
        var equalityValue = await CompileAsync("Negative.EqualityValue", new[] { PaddedPayloadNode.Replace("public byte Flag;", "public byte Flag; public override int GetHashCode() { return Flag; }", StringComparison.Ordinal) }, new[] { contracts }, true, true);
        RequireDiagnostic(equalityValue, "AIBT5002"); RequireAtomicUnusableShard(equalityValue, "registered custom equality");
        const string builtInStubs = "namespace AIBT { public struct Float2Value { public float X; public float Y; } public struct AssetId { public ulong High; public ulong Low; public long Local; public bool HasLocal; } } namespace Unity.Collections { public struct FixedString32Bytes { public ulong A; public ulong B; public ulong C; public ulong D; } }";
        var builtInMemory = "public partial struct Memory { [AibtMemoryField(\"a-vector\", \"Float2\", 1u)] public AIBT.Float2Value Vector; [AibtMemoryField(\"b-text\", \"FixedString32\", 1u)] public Unity.Collections.FixedString32Bytes Text; [AibtMemoryField(\"c-asset\", \"AssetId\", 1u)] public AIBT.AssetId Asset; }";
        var builtInPositive = await CompileAsync("Positive.ClosedBuiltIns", new[] { builtInStubs, Node(string.Empty, ValidConfig, builtInMemory) }, new[] { contracts }, true, true);
        RequireClean(builtInPositive, "closed non-scalar built-in identity/layout rows");
        var builtInMismatch = await CompileAsync("Negative.ClosedBuiltInMismatch", new[] { builtInStubs, Node(string.Empty, ValidConfig, builtInMemory.Replace("\"Float2\"", "\"Float3\"", StringComparison.Ordinal)) }, new[] { contracts }, true, true);
        RequireDiagnostic(builtInMismatch, "AIBT5007"); RequireAtomicUnusableShard(builtInMismatch, "closed built-in CLR/ID mismatch");
        var rawEnum = await CompileAsync("Negative.RawEnum", new[] { Node(string.Empty, ValidConfig, "public enum Raw : int { A = 0 } public partial struct Memory { [AibtMemoryField(\"a\", \"Int32\", 1u)] public Raw Value; }") }, new[] { contracts }, true, true);
        RequireDiagnostic(rawEnum, "AIBT5002"); RequireAtomicUnusableShard(rawEnum, "raw enum storage");
    }

    private static void VerifyAbiSurface()
    {
        var expectedPublicTypes = new[]
        {
            "AibtAsyncOperationBindingAttribute", "AibtBlackboardBindingAttribute", "AibtBurstNodeAttribute", "AibtBurstValueAttribute",
            "AibtCatalogSetAttribute", "AibtCatalogShardAttribute", "AibtCommandBindingAttribute", "AibtCompletionBindingAttribute",
            "AibtConfigFieldAttribute", "AibtMemoryFieldAttribute", "AibtNodeDocumentationAttribute", "AibtObserverConditionAttribute",
            "AibtRandomStreamAttribute", "AibtSnapshotBindingAttribute", "AibtValueFieldAttribute", "AsyncOperationHandle`2",
            "BlackboardReadHandle`1", "BlackboardReadWriteHandle`1", "BlackboardWriteHandle`1", "BurstAbortContext",
            "BurstBlackboardAccess", "BurstCallbackPhase", "BurstCancellationMode", "BurstCatalogFingerprint", "BurstCatalogHandshake",
            "BurstCatalogValidationCode", "BurstCatalogValidationResult", "BurstCompletionOutcome", "BurstConfigurationReader",
            "BurstContextResult", "BurstDispatchFrame", "BurstEnterContext", "BurstExecutionBatch", "BurstExecutionCode",
            "BurstExecutionResult", "BurstExitContext", "BurstGeneratedRuntimeBridge", "BurstHash256", "BurstMemoryAccessor", "BurstNodeAbortReason",
            "BurstNodeCost", "BurstNodeExitReason", "BurstNodeKind", "BurstNodeStatusMask", "BurstObserverContext", "BurstTickContext",
            "BurstValueReader", "BurstValueWriter", "CommandHandle`1", "CompletionHandle`1", "ConditionResult", "SnapshotReadHandle`1"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualPublicTypes = typeof(BurstContextResult).Assembly.GetTypes().Where(type => type.IsPublic && type.Namespace == "AIBT.Burst")
            .Select(type => type.Name).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Require(actualPublicTypes.SequenceEqual(expectedPublicTypes, StringComparer.Ordinal),
            "closed public AIBT.Burst type manifest differs: " + string.Join(",", actualPublicTypes));
        var handles = new[]
        {
            typeof(BlackboardReadHandle<int>), typeof(BlackboardWriteHandle<int>), typeof(BlackboardReadWriteHandle<int>),
            typeof(SnapshotReadHandle<int>), typeof(CommandHandle<int>), typeof(AsyncOperationHandle<int, uint>),
            typeof(CompletionHandle<int>)
        };
        foreach (var handle in handles)
        {
            var layout = handle.StructLayoutAttribute;
            Require(layout != null && layout.Value == LayoutKind.Sequential
                && layout.Pack == 4 && layout.Size == 8, handle.FullName + " layout differs from ABI v1");
            var fields = handle.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(fields.Length == 2 && fields.All(field => field.FieldType == typeof(uint))
                && fields.Any(field => field.Name == "_ordinal") && fields.Any(field => field.Name == "_accessToken"),
                handle.FullName + " must contain exactly the two private uint ABI words");
        }
        var expected = new[] { "Success", "InvalidHandle", "TypeMismatch", "PhaseViolation", "CapacityExceeded",
            "StaleCompletion", "Overflow", "InvalidEncoding", "IncompleteValue", "AlreadyCommitted", "InvalidStatus" };
        Require(Enum.GetNames(typeof(BurstContextResult)).SequenceEqual(expected, StringComparer.Ordinal)
            && Enum.GetValues(typeof(BurstContextResult)).Cast<BurstContextResult>().Select(value => (byte)value).SequenceEqual(Enumerable.Range(0, 11).Select(value => (byte)value)),
            "BurstContextResult must be the exact contiguous ABI v1 values 0..10");
        var validationLayout = typeof(BurstCatalogValidationResult).StructLayoutAttribute;
        Require(validationLayout != null && validationLayout.Value == LayoutKind.Sequential && validationLayout.Pack == 2 && validationLayout.Size == 4
            && Marshal.SizeOf<BurstCatalogValidationResult>() == 4
            && Marshal.OffsetOf<BurstCatalogValidationResult>("_codeWord").ToInt32() == 0
            && Marshal.OffsetOf<BurstCatalogValidationResult>("_diagnosticNumber").ToInt32() == 2,
            "BurstCatalogValidationResult must be exact Sequential/Pack2/Size4 with offsets 0,2");
        var executionLayout = typeof(BurstExecutionResult).StructLayoutAttribute;
        Require(executionLayout != null && executionLayout.Value == LayoutKind.Sequential && executionLayout.Pack == 4 && executionLayout.Size == 16
            && Marshal.SizeOf<BurstExecutionResult>() == 16
            && Marshal.OffsetOf<BurstExecutionResult>("_codeWord").ToInt32() == 0
            && Marshal.OffsetOf<BurstExecutionResult>("_diagnosticNumber").ToInt32() == 2
            && Marshal.OffsetOf<BurstExecutionResult>("_instancesVisited").ToInt32() == 4
            && Marshal.OffsetOf<BurstExecutionResult>("_segmentSteps").ToInt32() == 8,
            "BurstExecutionResult must be exact Sequential/Pack4/Size16 with offsets 0,2,4,8");
        foreach (var context in new[] { typeof(BurstEnterContext), typeof(BurstTickContext) })
        {
            var layout = context.StructLayoutAttribute;
            Require(layout != null && layout.Value == LayoutKind.Sequential && layout.Pack == 8 && layout.Size == 24
                && Marshal.SizeOf(context) == 24
                && Marshal.OffsetOf(context, "_validationToken").ToInt32() == 0
                && Marshal.OffsetOf(context, "_randomState").ToInt32() == 8
                && Marshal.OffsetOf(context, "_randomIncrement").ToInt32() == 16,
                context.Name + " must be exact Sequential/Pack8/Size24 with offsets 0,8,16");
        }
        var configMethods = typeof(BurstGeneratedRuntimeBridge).GetMethods().Where(method => method.IsPublic && method.Name.StartsWith("TryRead", StringComparison.Ordinal)
            && method.GetParameters().Length > 0 && method.GetParameters()[0].ParameterType.IsByRef
            && method.GetParameters()[0].ParameterType.GetElementType() == typeof(BurstConfigurationReader)).Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var exactConfigMethods = new[] { "TryReadAsyncOperationHandle", "TryReadBlackboardReadHandle", "TryReadBlackboardReadWriteHandle", "TryReadBlackboardWriteHandle", "TryReadBoolean", "TryReadCommandHandle", "TryReadCompletionHandle", "TryReadSnapshotHandle", "TryReadUInt32", "TryReadUInt64" };
        Require(configMethods.SequenceEqual(exactConfigMethods, StringComparer.Ordinal), "configuration reader public scalar/handle surface differs from ABI v1: " + string.Join(",", configMethods));
        var exactBridgeNames = new[]
        {
            "TryAcquireDispatchFrame", "TryCommitBlackboardWrite", "TryCommitCancel", "TryCommitConsume", "TryCommitEffect", "TryCommitMemory", "TryCommitStart",
            "TryCompleteAbort", "TryCompleteEnter", "TryCompleteExit", "TryCompleteObserver", "TryCompleteTick", "TryCompleteValueRead",
            "TryCreateAbortContext", "TryCreateConfigurationReader", "TryCreateEnterContext", "TryCreateExitContext", "TryCreateMemoryAccessor", "TryCreateObserverContext", "TryCreateTickContext",
            "TryFailDispatch", "TryGetAbortReason", "TryGetCatalogHandshake", "TryGetExecutionRequest", "TryGetExecutionResult", "TryGetExitReason", "TryPrepareSchedule", "TryRejectBatch",
            "TryReadAsyncOperationHandle", "TryReadBlackboardReadHandle", "TryReadBlackboardReadWriteHandle", "TryReadBlackboardWriteHandle", "TryReadBoolean", "TryReadCommandHandle", "TryReadCompletionHandle",
            "TryReadMemoryBoolean", "TryReadMemoryFloat32", "TryReadMemoryFloat64", "TryReadMemoryInt16", "TryReadMemoryInt32", "TryReadMemoryInt64", "TryReadMemoryInt8", "TryReadMemoryUInt16", "TryReadMemoryUInt32", "TryReadMemoryUInt64", "TryReadMemoryUInt8",
            "TryReadSnapshotHandle", "TryReadUInt32", "TryReadUInt64", "TryReadValue",
            "TryWriteMemoryBoolean", "TryWriteMemoryFloat32", "TryWriteMemoryFloat64", "TryWriteMemoryInt16", "TryWriteMemoryInt32", "TryWriteMemoryInt64", "TryWriteMemoryInt8", "TryWriteMemoryUInt16", "TryWriteMemoryUInt32", "TryWriteMemoryUInt64", "TryWriteMemoryUInt8", "TryWriteValue"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var actualBridgeNames = typeof(BurstGeneratedRuntimeBridge).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(method => method.Name).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Require(actualBridgeNames.SequenceEqual(exactBridgeNames, StringComparer.Ordinal), "closed public bridge member-name manifest differs: " + string.Join(",", actualBridgeNames));
        var bridge = typeof(BurstGeneratedRuntimeBridge);
        Require(bridge.GetMethod("TryPrepareSchedule", new[] { typeof(BurstExecutionBatch).MakeByRefType(), typeof(BurstExecutionBatch).MakeByRefType() })?.ReturnType == typeof(BurstContextResult), "TryPrepareSchedule exact ref/out signature missing");
        Require(bridge.GetMethod("TryCompleteEnter", new[] { typeof(BurstExecutionBatch).MakeByRefType(), typeof(BurstDispatchFrame).MakeByRefType(), typeof(BurstEnterContext).MakeByRefType() })?.ReturnType == typeof(BurstContextResult), "TryCompleteEnter exact ref/in/ref signature missing");
        Require(bridge.GetMethod("TryCompleteTick", new[] { typeof(BurstExecutionBatch).MakeByRefType(), typeof(BurstDispatchFrame).MakeByRefType(), typeof(BurstTickContext).MakeByRefType(), typeof(AIBT.NodeStatus) })?.ReturnType == typeof(BurstContextResult), "TryCompleteTick exact ref/in/ref/status signature missing");
        Require(bridge.GetMethods().Count(method => method.Name == "TryReadValue") == 11 && bridge.GetMethods().Count(method => method.Name == "TryWriteValue") == 11,
            "value bridge must expose exactly the closed Bool+Int8..Float64 scalar overloads");
        var memberManifest = BuildPublicAbiManifest();
        using var expectedStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ExpectedPublicAbiV1.txt") ?? throw new InvalidOperationException("retained expected public ABI manifest resource missing");
        using var expectedReader = new StreamReader(expectedStream, new UTF8Encoding(false, true));
        var expectedManifest = expectedReader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!expectedManifest.EndsWith("\n", StringComparison.Ordinal)) expectedManifest += "\n";
        Require(memberManifest == expectedManifest, "closed public AIBT.Burst signature records differ line-for-line: " + FirstManifestDifference(expectedManifest, memberManifest));
        var memberManifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(memberManifest))).ToLowerInvariant();
        Require(memberManifestHash == "f7f5b62fb2c26194011a2654fb50d78ba1991268022291612f9ae5a616711348",
            "closed public AIBT.Burst member-signature manifest differs; sha256=" + memberManifestHash + "\n" + memberManifest);
    }

    private static string FirstManifestDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n'); var actualLines = actual.Split('\n'); var count = Math.Max(expectedLines.Length, actualLines.Length);
        for (var index = 0; index < count; index++) { var left = index < expectedLines.Length ? expectedLines[index] : "<missing>"; var right = index < actualLines.Length ? actualLines[index] : "<missing>"; if (!string.Equals(left, right, StringComparison.Ordinal)) return "line " + (index + 1).ToString(CultureInfo.InvariantCulture) + " expected '" + left + "' actual '" + right + "'"; }
        return "unknown difference";
    }

    private static string BuildPublicAbiManifest()
    {
        var lines = new List<string>();
        var types = typeof(BurstContextResult).Assembly.GetTypes()
            .Where(type => type.IsPublic && type.Namespace == "AIBT.Burst")
            .OrderBy(type => ReflectionTypeName(type), StringComparer.Ordinal);
        foreach (var type in types)
        {
            var kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class";
            var typeLine = "T|" + kind + "|" + ReflectionTypeName(type) + "|abstract=" + type.IsAbstract + "|sealed=" + type.IsSealed;
            if (type.IsGenericTypeDefinition) typeLine += "|" + GenericConstraintManifest(type.GetGenericArguments());
            var layout = type.StructLayoutAttribute;
            if (layout != null) typeLine += "|layout=" + layout.Value + ",pack=" + layout.Pack + ",size=" + layout.Size + ",charset=" + layout.CharSet;
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
                lines.Add("V|" + (IsStatic(@event.AddMethod) ? "static|" : "instance|") + @event.Name + "|" + ReflectionTypeName(@event.EventHandlerType!));
        }
        return string.Join("\n", lines) + "\n";
    }

    private static string FieldManifest(FieldInfo field) => "F|" + (field.IsStatic ? "static|" : "instance|")
        + (field.IsLiteral ? "const|" : field.IsInitOnly ? "readonly|" : "mutable|") + field.Name + "|" + ReflectionTypeName(field.FieldType)
        + (field.IsLiteral ? "|" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : string.Empty);
    private static string ConstructorManifest(ConstructorInfo ctor) => "C|" + ParameterManifest(ctor.GetParameters());
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
        if (type.IsByRef) type = type.GetElementType()!;
        return modifier + ":" + ReflectionTypeName(type) + ":" + parameter.Name + ":optional=" + parameter.IsOptional
            + (parameter.HasDefaultValue ? ":default=" + Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) : string.Empty);
    }));
    private static string GenericConstraintManifest(IEnumerable<Type> arguments) => "generic=" + string.Join(";", arguments.Select(argument =>
        argument.Name + ":" + argument.GenericParameterAttributes + ":" + string.Join("&", argument.GetGenericParameterConstraints().Select(ReflectionTypeName).OrderBy(value => value, StringComparer.Ordinal))));
    private static string ReflectionTypeName(Type type)
    {
        if (type.IsByRef) return ReflectionTypeName(type.GetElementType()!) + "&";
        if (type.IsArray) return ReflectionTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return "!" + type.Name;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        var definition = type.GetGenericTypeDefinition();
        var name = definition.FullName ?? definition.Name;
        return name[..name.IndexOf('`')] + "<" + string.Join(",", type.GetGenericArguments().Select(ReflectionTypeName)) + ">";
    }
    private static bool IsStatic(MethodInfo? method) => method?.IsStatic == true;

    private static void VerifyGeneratedFacade(Compilation compilation)
    {
        var facade = compilation.GetTypeByMetadataName("Consumer.Catalog.GeneratedCatalog") ?? throw new InvalidOperationException("generated facade type missing");
        Require(facade.DeclaredAccessibility == Accessibility.Public && facade.IsStatic, "generated facade must be public static");
        var members = facade.GetMembers().Where(member => !member.IsImplicitlyDeclared && member.DeclaredAccessibility == Accessibility.Public
                && (member is not IMethodSymbol method || method.AssociatedSymbol == null))
            .Select(SymbolMemberManifest).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expected = new[]
        {
            "F|IsUsable|System.Boolean|const=True",
            "M|ExecuteImmediate|AIBT.Burst.BurstExecutionResult|ref:AIBT.Burst.BurstExecutionBatch",
            "M|Schedule|Unity.Jobs.JobHandle|ref:AIBT.Burst.BurstExecutionBatch,value:Unity.Jobs.JobHandle",
            "M|Validate|AIBT.Burst.BurstCatalogValidationResult|in:AIBT.Burst.BurstCatalogHandshake",
            "P|Fingerprint|AIBT.Burst.BurstCatalogFingerprint|get=True|set=False"
        }.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Require(members.SequenceEqual(expected, StringComparer.Ordinal), "exact generated facade surface differs: " + string.Join(" | ", members));
    }

    private static string SymbolMemberManifest(ISymbol symbol)
    {
        static string TypeName(ITypeSymbol type) => type.SpecialType == SpecialType.System_Boolean ? "System.Boolean"
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
        static string RefName(RefKind kind) => kind == RefKind.None ? "value" : kind.ToString().ToLowerInvariant();
        return symbol switch
        {
            IFieldSymbol field => "F|" + field.Name + "|" + TypeName(field.Type) + (field.HasConstantValue ? "|const=" + Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture) : string.Empty),
            IPropertySymbol property => "P|" + property.Name + "|" + TypeName(property.Type) + "|get=" + (property.GetMethod != null) + "|set=" + (property.SetMethod != null),
            IMethodSymbol method => "M|" + method.Name + "|" + TypeName(method.ReturnType) + "|" + string.Join(",", method.Parameters.Select(parameter => RefName(parameter.RefKind) + ":" + TypeName(parameter.Type))),
            _ => symbol.Kind + "|" + symbol.Name
        };
    }

    private static void VerifyRandomContexts()
    {
        const ulong catalog = 0x123456789abcdef0UL;
        var defaultContext = default(BurstTickContext); var defaultState = defaultContext.RandomState;
        Require(defaultContext.TryNextUInt32(0, out var defaultValue) == BurstContextResult.InvalidHandle && defaultValue == 0 && defaultContext.RandomState == defaultState,
            "default+bound0 RNG precedence/state changed");
        var inert = BurstContractTestSeam.Batch(catalog, 1, 0, 0, 0, 0, 0, 0, 0, true);
        BurstDispatchFrame inertFrame = default; BurstTickContext inertContext = default;
        Require(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref inert, 0, 0, 0, BurstCallbackPhase.Tick, out inertFrame) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCreateTickContext(in inertFrame, out inertContext) == BurstContextResult.Success,
            "non-random Tick context creation failed");
        var inertState = inertContext.RandomState;
        Require(inertContext.RandomIncrement == 1 && inertState == 0
            && inertContext.TryNextUInt32(0, out _) == BurstContextResult.PhaseViolation
            && inertContext.RandomState == inertState
            && BurstGeneratedRuntimeBridge.TryCompleteTick(ref inert, in inertFrame, ref inertContext, AIBT.NodeStatus.Success) == BurstContextResult.PhaseViolation
            && BurstContractTestSeam.RandomState(in inert) == 0,
            "ignored non-random RNG failure did not latch or consumed/published RNG");
        var inertSuccess = BurstContractTestSeam.Batch(catalog, 1, 0, 0, 0, 0, 0, 0, 0, true);
        BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref inertSuccess, 0, 0, 0, BurstCallbackPhase.Tick, out var inertSuccessFrame);
        BurstGeneratedRuntimeBridge.TryCreateTickContext(in inertSuccessFrame, out var inertSuccessContext);
        Require(BurstGeneratedRuntimeBridge.TryCompleteTick(ref inertSuccess, in inertSuccessFrame, ref inertSuccessContext, AIBT.NodeStatus.Success) == BurstContextResult.Success,
            "non-random inert 0/1 context could not complete without RNG use");

        var semanticHash = new AIBT.BurstAbi.Canary.DeterministicSemanticHashCanary(0x0123456789abcdefUL, 0xfedcba9876543210UL, 0x1122334455667788UL, 0x8877665544332211UL);
        var random = BurstContractTestSeam.Batch(catalog, 1, 0, 0, 0, 0, 0, 0, 0, true);
        BurstDispatchFrame randomFrame = default; BurstTickContext randomContext = default;
        Require(BurstContractTestSeam.SetRandom(ref random, 0x1234UL, in semanticHash, 7UL, 3u, true)
            && BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref random, 0, 0, 0, BurstCallbackPhase.Tick, out randomFrame) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCreateTickContext(in randomFrame, out randomContext) == BurstContextResult.Success,
            "random-capable Tick context creation failed");
        var initial = randomContext.RandomState; var copy = randomContext;
        Require(randomContext.TryNextUInt32(out _) == BurstContextResult.Success && randomContext.RandomState != initial,
            "context TryNextUInt32 did not advance the retained PCG state");
        var advanced = randomContext.RandomState;
        Require(BurstGeneratedRuntimeBridge.TryCompleteTick(ref random, in randomFrame, ref randomContext, AIBT.NodeStatus.Success) == BurstContextResult.Success
            && BurstContractTestSeam.RandomState(in random) == advanced
            && BurstGeneratedRuntimeBridge.TryCompleteTick(ref random, in randomFrame, ref copy, AIBT.NodeStatus.Success) == BurstContextResult.InvalidHandle,
            "copied context did not implement first-claim-wins/stale semantics");
        Require(copy.TryNextUInt32(out var staleValue) == BurstContextResult.InvalidHandle && staleValue == 0 && copy.RandomState == initial,
            "stale copied context advanced RNG after another copy claimed completion");

        var rejected = BurstContractTestSeam.Batch(catalog, 1, 0, 0, 0, 0, 0, 0, 0, true);
        BurstContractTestSeam.SetRandom(ref rejected, 0x1234UL, in semanticHash, 7UL, 3u, true);
        BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref rejected, 0, 0, 0, BurstCallbackPhase.Tick, out var rejectedFrame);
        BurstGeneratedRuntimeBridge.TryCreateTickContext(in rejectedFrame, out var rejectedContext);
        var beforeBound = rejectedContext.RandomState;
        Require(rejectedContext.TryNextUInt32(0, out _) == BurstContextResult.InvalidStatus && rejectedContext.RandomState == beforeBound,
            "zero-bound RNG failure consumed state");
        var even = BurstContractTestSeam.ForgeTick(in rejectedFrame, beforeBound, 0);
        Require(BurstGeneratedRuntimeBridge.TryCompleteTick(ref rejected, in rejectedFrame, ref even, AIBT.NodeStatus.Success) == BurstContextResult.InvalidStatus
            && BurstContractTestSeam.RandomState(in rejected) == beforeBound,
            "zero/even increment forged context published RNG");
        var tampered = BurstContractTestSeam.Batch(catalog, 1, 0, 0, 0, 0, 0, 0, 0, true);
        BurstContractTestSeam.SetRandom(ref tampered, 0x1234UL, in semanticHash, 7UL, 3u, true);
        BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref tampered, 0, 0, 0, BurstCallbackPhase.Tick, out var tamperedFrame);
        var tamperedContext = BurstContractTestSeam.ForgeTick(in tamperedFrame, BurstContractTestSeam.RandomState(in tampered), 0);
        var tamperedState = tamperedContext.RandomState;
        Require(tamperedContext.TryNextUInt32(0, out var tamperedValue) == BurstContextResult.PhaseViolation && tamperedValue == 0 && tamperedContext.RandomState == tamperedState,
            "tampered-increment+bound0 RNG precedence/state changed");
    }

    private static void VerifyStandaloneRandomVectors()
    {
        var hash = new AIBT.BurstAbi.Canary.DeterministicSemanticHashCanary(0, 0, 0, 0);
        Require(AIBT.BurstAbi.Canary.DeterministicRandomCanary.TryCreate(0, in hash, 1, 0, out var random), "zero RNG vector construction failed");
        var initialState = random.State; var increment = random.Increment;
        Require(random.InitialStateWord == 0x114fca7ce1cd0d61UL && random.StreamWord == 0x9b20a16d47a2f685UL,
            $"published RNG seed intermediates changed: initialWord=0x{random.InitialStateWord:x16}, streamWord=0x{random.StreamWord:x16}, seeded=0x{initialState:x16}");
        var expected = new[] { 0x650f0350u, 0x19bf2775u, 0x93792ebdu, 0xf8d15448u, 0x80f1bd3cu, 0x1312f9f2u };
        foreach (var value in expected) Require(random.TryNextUInt32(out var actual) && actual == value, "published zero RNG vector changed");
        Require(initialState == 0xcd0663b1aab38607UL && increment == 0x364142da8f45ed0bUL && random.State == 0x6f21375a82efe66dUL,
            $"published RNG intermediate state invalid: initial=0x{initialState:x16}, increment=0x{increment:x16}, final=0x{random.State:x16}");
        var bounded = default(AIBT.BurstAbi.Canary.DeterministicRandomCanary);
        Require(AIBT.BurstAbi.Canary.DeterministicRandomCanary.TryCreate(0, in hash, 1, 0, out bounded)
            && bounded.TryNextUInt32(0x90000000u, out var boundedValue) && boundedValue == 0x03792ebdu
            && bounded.TryNextUInt32(out var afterBounded) && afterBounded == 0xf8d15448u,
            "bounded rejection/advancement vector changed");
    }

    private static async Task VerifyGlobalCatalogCollision(MetadataReference contracts, MetadataReference runtimeBuiltins, MetadataReference shardA, MetadataReference shardB)
    {
        Require(BurstNodeGenerator.NumericIdentityForTests(string.Empty) == 0xcbf29ce484222325UL
            && BurstNodeGenerator.NumericIdentityForTests("hello") == 0xa430d84680aabd0bUL
            && BurstNodeGenerator.NumericIdentityForTests("aibt.canary.action") == 0xcc620a073e6c90afUL,
            "production FNV-1a 64 vectors changed");

        BurstNodeGenerator.ForceNumericIdentityCollisionForTests = true;
        try
        {
            var collision = await CompileAsync("Negative.GlobalCatalogCollision", new[] { Catalog },
                new[] { contracts, runtimeBuiltins, shardA, shardB }, true, true);
            RequireDiagnostic(collision, "AIBT5010");
            RequireDiagnostic(collision, "AIBT5011");
            Require(!collision.LogicallyUsable, "global numeric collision left a usable catalog assembly");
            Require(collision.GeneratedSource.Contains("IsUsable = false", StringComparison.Ordinal)
                && !collision.GeneratedSource.Contains("static global::AIBT.Burst.BurstContextResult Execute", StringComparison.Ordinal)
                && !collision.GeneratedSource.Contains("case 0u:", StringComparison.Ordinal)
                && !collision.GeneratedSource.Contains("CatalogToken", StringComparison.Ordinal),
                "global numeric collision emitted a usable facade");
        }
        finally
        {
            BurstNodeGenerator.ForceNumericIdentityCollisionForTests = false;
        }
    }

    private static async Task VerifyCompileTimeHandshake(MetadataReference contracts)
    {
        const string mismatchedShard = "using AIBT.Burst; namespace External { [AibtCatalogShard(\"external.shard\", 1u)] public partial struct Shard { public const bool IsUsable=true; public const uint AbiVersion=2u; } }";
        var shard = await CompileAsync("External.MismatchedShard", new[] { mismatchedShard }, new[] { contracts }, false, false);
        RequireClean(shard, "external mismatched-ABI shard fixture");
        const string catalog = "using AIBT.Burst; namespace External.Consumer { [AibtCatalogSet(\"external.catalog\", 1u, typeof(External.Shard))] public static partial class Catalog { } }";
        var result = await CompileAsync("External.MismatchedCatalog", new[] { catalog }, new[] { contracts, Reference(shard) }, true, true);
        var diagnostics = result.Diagnostics.Where(item => item.Id == "AIBT5012" && item.Severity == DiagnosticSeverity.Error).ToArray();
        Require(diagnostics.Length == 1 && diagnostics[0].Location == Location.None,
            "compile-time shard ABI mismatch did not emit one Location.None AIBT5012");
        Require(result.GeneratedSource.Contains("IsUsable = false", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("ExecuteImmediate", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("Schedule", StringComparison.Ordinal),
            "compile-time shard ABI mismatch emitted usable CatalogSet members");
        var suppressed = await CompileAsync("External.MismatchedCatalog.Suppressed", new[] { catalog }, new[] { contracts, Reference(shard) }, true, true, true);
        var suppressedDiagnostics = suppressed.Diagnostics.Where(item => item.Id == "AIBT5012" && item.Severity == DiagnosticSeverity.Error).ToArray();
        Require(suppressedDiagnostics.Length == 1 && suppressedDiagnostics[0].Location == Location.None
            && suppressedDiagnostics[0].Descriptor.IsEnabledByDefault
            && suppressedDiagnostics[0].Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal),
            "AIBT5012 suppression changed severity/location/NotConfigurable contract");
        RequireAtomicUnusableCatalog(suppressed, "suppressed compile-time handshake mismatch");
    }

    private static void VerifyBridgeAndAsyncOwnership()
    {
        const ulong catalog = 0x123456789abcdef0UL;
        var defaultBatch = default(BurstExecutionBatch);
        Require(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref defaultBatch, 0, 0, 0, BurstCallbackPhase.Tick, out _) == BurstContextResult.InvalidHandle, "default batch accepted");
        var forged = BurstContractTestSeam.Batch(catalog, 2, 0, 0, 0, 0, 0, 0, 0, true);
        Require(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref forged, 0, 0, 1, BurstCallbackPhase.Tick, out _) == BurstContextResult.InvalidHandle, "forged case accepted");
        var badPadding = BurstContractTestSeam.Batch(catalog, 2, 0, 0, 0, 0, 0, 0, 0, false);
        Require(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref badPadding, 0, 0, 0, BurstCallbackPhase.Tick, out _) == BurstContextResult.InvalidHandle, "nonzero padding accepted");
        var scheduled = BurstContractTestSeam.RuntimeBatch(catalog, 2, 0, 0, 0, 0, 0, 0, 0, true);
        BurstContractTestSeam.SetWorkCount(ref scheduled, 2u);
        var preScheduleHostCopy = scheduled;
        Require(BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref scheduled, out var scheduledView) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref scheduled, out _) == BurstContextResult.PhaseViolation
            && BurstGeneratedRuntimeBridge.TryPrepareSchedule(ref preScheduleHostCopy, out _) == BurstContextResult.PhaseViolation
            && BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in scheduled, out _, out _, out _, out _, out _) == BurstContextResult.PhaseViolation
            && BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in preScheduleHostCopy, out _, out _, out _, out _, out _) == BurstContextResult.PhaseViolation
            && BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref scheduled, 0, 0, 0, BurstCallbackPhase.Tick, out _) == BurstContextResult.PhaseViolation,
            "shared schedule claim was not atomic across host copies");
        var scheduledViewCopy = scheduledView;
        Require(BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in scheduledView, out var requestInstance, out var requestNode, out var requestCase, out var requestPhase, out var scheduledHasWork) == BurstContextResult.Success
            && scheduledHasWork
            && BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref scheduledView, requestInstance, requestNode, requestCase, requestPhase, out var scheduledFrame0) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCreateTickContext(in scheduledFrame0, out var scheduledContext0) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCompleteTick(ref scheduledView, in scheduledFrame0, ref scheduledContext0, AIBT.NodeStatus.Running) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryGetExecutionRequest(in scheduledView, out requestInstance, out requestNode, out requestCase, out requestPhase, out scheduledHasWork) == BurstContextResult.Success
            && scheduledHasWork
            && BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref scheduledView, requestInstance, requestNode, requestCase, requestPhase, out var scheduledFrame1) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCreateTickContext(in scheduledFrame1, out var scheduledContext1) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCompleteTick(ref scheduledView, in scheduledFrame1, ref scheduledContext1, AIBT.NodeStatus.Success) == BurstContextResult.Success,
            "shared multi-request cursor did not advance through both requests");
        Require(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduledView, out _) == BurstContextResult.InvalidHandle
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduledViewCopy, out _) == BurstContextResult.InvalidHandle
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduled, out var scheduledResult) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in preScheduleHostCopy, out var copiedHostResult) == BurstContextResult.Success
            && scheduledResult.Code == BurstExecutionCode.Success && scheduledResult.InstancesVisited == 2u && scheduledResult.SegmentSteps == 2u
            && copiedHostResult.Code == scheduledResult.Code && copiedHostResult.InstancesVisited == scheduledResult.InstancesVisited
            && copiedHostResult.SegmentSteps == scheduledResult.SegmentSteps,
            "terminal job views or repeatable host result ownership differed across copies");
        BurstContractTestSeam.Release(ref scheduled);
        Require(BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduled, out _) == BurstContextResult.InvalidHandle
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in preScheduleHostCopy, out _) == BurstContextResult.InvalidHandle
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduledView, out _) == BurstContextResult.InvalidHandle
            && BurstGeneratedRuntimeBridge.TryGetExecutionResult(in scheduledViewCopy, out _) == BurstContextResult.InvalidHandle,
            "feasibility release did not invalidate every host/job view copy");

        var batch = BurstContractTestSeam.Batch(catalog, 2, 0, 0x00000000fffffffeUL, 0xfedcba9876543210UL,
            1UL, ((ulong)unchecked((uint)-7) << 32) | 42u, ((ulong)9u << 32) | 4u, 11UL, true);
        Require(BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref batch, 0, 0, 0, BurstCallbackPhase.Tick, out var frame) == BurstContextResult.Success, "valid frame rejected");
        BurstGeneratedRuntimeBridge.TryCreateTickContext(in frame, out var tickContext);
        Require(BurstGeneratedRuntimeBridge.TryCompleteTick(ref batch, in frame, ref tickContext, AIBT.NodeStatus.Running) == BurstContextResult.Success
            && BurstContractTestSeam.PublishedStatus(in batch) == AIBT.NodeStatus.Running, "Tick status was not published");
        var invalidBatch = BurstContractTestSeam.Batch(catalog, 2, 0, 0, 0, 0, 0, 0, 0, true);
        BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref invalidBatch, 0, 0, 0, BurstCallbackPhase.Tick, out var invalidFrame);
        BurstGeneratedRuntimeBridge.TryCreateTickContext(in invalidFrame, out var invalidTickContext);
        Require(BurstGeneratedRuntimeBridge.TryCompleteTick(ref invalidBatch, in invalidFrame, ref invalidTickContext, (AIBT.NodeStatus)255) == BurstContextResult.PhaseViolation
            && BurstContractTestSeam.PublishedStatus(in invalidBatch) == default, "invalid Tick status was published");
        Require(BurstGeneratedRuntimeBridge.TryCreateConfigurationReader(in frame, out var reader) == BurstContextResult.Success, "reader rejected");
        Require(BurstGeneratedRuntimeBridge.TryReadUInt32(ref reader, 0, 0, out var uintValue) == BurstContextResult.Success && uintValue == 0xfffffffeu, "LE uint32 decode failed");
        Require(BurstGeneratedRuntimeBridge.TryReadUInt64(ref reader, 1, 0, out var ulongValue) == BurstContextResult.Success && ulongValue == 0xfedcba9876543210UL, "LE uint64 decode failed");
        Require(BurstGeneratedRuntimeBridge.TryReadBoolean(ref reader, 2, 0, out var boolValue) == BurstContextResult.Success && boolValue, "boolean decode failed");
        Require(BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(in frame, out var memory) == BurstContextResult.Success, "memory accessor rejected");
        Require(BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(ref memory, 0, 0, -10) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryWriteMemoryUInt32(ref memory, 1, 0, 20) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryWriteMemoryUInt64(ref memory, 2, 0, 30) == BurstContextResult.Success
            && BurstContractTestSeam.MemoryWord0(in memory) == (((ulong)20u << 32) | unchecked((uint)-10))
            && BurstContractTestSeam.MemoryWord1(in memory) == 30UL, "multi-field memory writeback failed");

        var rollbackBatch = BurstContractTestSeam.Batch(catalog, 2, 0, 0, 0, 0, 0, 0, 0, true);
        BurstGeneratedRuntimeBridge.TryAcquireDispatchFrame(ref rollbackBatch, 0, 0, 0, BurstCallbackPhase.Tick, out var rollbackFrame);
        BurstGeneratedRuntimeBridge.TryCreateMemoryAccessor(in rollbackFrame, out var rollbackMemory);
        var firstWrite = BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(ref rollbackMemory, 0, 0, 41);
        var invalidWrite = BurstGeneratedRuntimeBridge.TryWriteMemoryInt32(ref rollbackMemory, 99, 0, 42);
        var invalidCommit = BurstGeneratedRuntimeBridge.TryCommitMemory(ref rollbackMemory);
        var failedDispatch = BurstGeneratedRuntimeBridge.TryFailDispatch(ref rollbackBatch, in rollbackFrame, BurstContextResult.InvalidHandle);
        BurstGeneratedRuntimeBridge.TryCreateTickContext(in rollbackFrame, out var rollbackTickContext);
        var expiredCompletion = BurstGeneratedRuntimeBridge.TryCompleteTick(ref rollbackBatch, in rollbackFrame, ref rollbackTickContext, AIBT.NodeStatus.Success);
        Require(firstWrite == BurstContextResult.Success && invalidWrite == BurstContextResult.InvalidHandle
            && invalidCommit == BurstContextResult.InvalidHandle && failedDispatch == BurstContextResult.InvalidHandle
            && expiredCompletion == BurstContextResult.InvalidHandle,
            $"late invalid memory write did not roll back and expire dispatch: {firstWrite}/{invalidWrite}/{invalidCommit}/{failedDispatch}/{expiredCompletion}");

        var handle = BurstContractTestSeam.AsyncOperation<int, uint>(3, catalog);
        var tick = BurstContractTestSeam.Tick(catalog, 5, 2, 8);
        AIBT.OperationId operationId = default;
        Require(tick.TryBeginStart(handle, out var startWriter, out var faultCancelWriter) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryWriteValue(ref startWriter, 0, 0, 7) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryWriteValue(ref faultCancelWriter, 0, 0, 9u) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCommitStart(ref startWriter, ref faultCancelWriter, out operationId) == BurstContextResult.Success
            && operationId.IsValid, "atomic runtime operation allocation failed");
        var completion = BurstContractTestSeam.Completion<int>(4, catalog);
        var consume = BurstContractTestSeam.TickWithCompletion(catalog, operationId);
        Require(consume.TryBeginConsume(completion, operationId, out _, out var completionReader) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryReadValue(ref completionReader, 0, 0, out int _) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCommitConsume(ref completionReader) == BurstContextResult.Success, "owned completion rejected");
        var wrongId = new AIBT.OperationId(new AIBT.TreeInstanceId(5), new AIBT.RuntimeNodeIndex(3), 8, operationId.Sequence);
        Require(consume.TryBeginConsume(completion, wrongId, out _, out _) == BurstContextResult.StaleCompletion, "foreign completion accepted");
        var abort = BurstContractTestSeam.Abort(catalog, operationId);
        Require(abort.TryBeginCancel(handle, operationId, out var cancelWriter) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryWriteValue(ref cancelWriter, 0, 0, 9u) == BurstContextResult.Success
            && BurstGeneratedRuntimeBridge.TryCommitCancel(ref cancelWriter) == BurstContextResult.Success
            && abort.TryBeginCancel(handle, wrongId, out _) == BurstContextResult.StaleCompletion, "async cancellation ownership failed");
    }

    private static async Task<CompilationResult> CompileAsync(string assemblyName, IEnumerable<string> sources,
        IEnumerable<MetadataReference> extraReferences, bool generator, bool analyzer, bool suppressAibt = false)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, assemblyName + index + ".cs", Encoding.UTF8));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false, deterministic: true, nullableContextOptions: NullableContextOptions.Enable);
        if (suppressAibt) options = options.WithSpecificDiagnosticOptions(Enumerable.Range(5001, 12).ToImmutableDictionary(value => "AIBT" + value.ToString(CultureInfo.InvariantCulture), _ => ReportDiagnostic.Suppress));
        Compilation compilation = CSharpCompilation.Create(assemblyName, trees, PlatformReferences.Concat(extraReferences), options);
        var diagnostics = new List<Diagnostic>(); var generated = string.Empty;
        if (generator)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new BurstNodeGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorDiagnostics);
            var result = driver.GetRunResult();
            diagnostics.AddRange(generatorDiagnostics); diagnostics.AddRange(result.Diagnostics);
            generated = string.Join("\n", result.Results.SelectMany(item => item.GeneratedSources).OrderBy(item => item.HintName, StringComparer.Ordinal).Select(item => item.SourceText.ToString()));
        }
        if (analyzer) diagnostics.AddRange(await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new BurstNodeUsageAnalyzer())).GetAnalyzerDiagnosticsAsync());
        diagnostics.AddRange(compilation.GetDiagnostics());
        using var stream = new MemoryStream(); var emit = compilation.Emit(stream); diagnostics.AddRange(emit.Diagnostics);
        return new CompilationResult(emit.Success ? stream.ToArray() : Array.Empty<byte>(), generated, compilation,
            diagnostics.Distinct(DiagnosticComparer.Instance).OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Location.SourceSpan.Start).ToArray());
    }

    private static MetadataReference Reference(CompilationResult value) => MetadataReference.CreateFromImage(ImmutableArray.Create(value.Image));
    private static void RequireClean(CompilationResult value, string label) { var errors = value.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray(); if (errors.Length != 0 || value.Image.Length == 0) throw new InvalidOperationException(label + ": " + string.Join(" | ", errors.Select(Format))); }
    private static void RequireDiagnostic(CompilationResult value, string id) => Require(value.Diagnostics.Any(item => item.Id == id && item.Severity == DiagnosticSeverity.Error), "Expected " + id + ", got " + string.Join(" | ", value.Diagnostics.Select(Format)));
    private static void RequireDiagnosticSpan(CompilationResult value, string id, string expectedText, int expectedCount, int expectedAdditional, bool atomicShard = true)
    {
        var diagnostics = value.Diagnostics.Where(item => item.Id == id && item.Severity == DiagnosticSeverity.Error).ToArray();
        Require(diagnostics.Length == expectedCount && diagnostics.All(item => DiagnosticText(item) == expectedText && item.AdditionalLocations.Count == expectedAdditional
            && item.Descriptor.IsEnabledByDefault && item.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable, StringComparer.Ordinal)),
            id + " exact primary span/count/tag differs: " + string.Join(" | ", diagnostics.Select(item => DiagnosticText(item) + ":additional=" + item.AdditionalLocations.Count)));
        if (atomicShard) RequireAtomicUnusableShard(value, id + " exact-location case");
    }
    private static string DiagnosticText(Diagnostic diagnostic, bool additional = false)
    {
        var location = additional ? diagnostic.AdditionalLocations[0] : diagnostic.Location;
        return location.SourceTree == null ? string.Empty : location.SourceTree.GetText().ToString(location.SourceSpan);
    }
    private static void RequireNoDiagnostic(CompilationResult value, params string[] ids) => Require(!value.Diagnostics.Any(item => ids.Contains(item.Id, StringComparer.Ordinal)), "Unexpected diagnostics: " + string.Join(" | ", value.Diagnostics.Select(Format)));
    private static void Require(bool condition, string message) { assertionCount++; if (!condition) throw new InvalidOperationException(message); }
    private static void ForbidGenerated(string source) { foreach (var token in new[] { "System.Reflection", "delegate", "interface ", "virtual ", "object ", "unsafe", "void*", "byte*", "int*", "string", "Task", "IEnumerator", "UnityEngine.Object", "UnityEngine.GameObject" }) Require(source.IndexOf(token, StringComparison.Ordinal) < 0, "forbidden generated token: " + token); }
    private static string Sha256(string value) { using var hash = SHA256.Create(); return string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2", CultureInfo.InvariantCulture))); }

    private static void VerifyBuiltInCanonicalCodecCanaries()
    {
        var fixedString = new byte[32];
        var utf8 = new UTF8Encoding(false, true).GetBytes("A\u20ac");
        fixedString[0] = (byte)utf8.Length;
        fixedString[1] = (byte)(utf8.Length >> 8);
        Array.Copy(utf8, 0, fixedString, 2, utf8.Length);
        Require(TryDecodeFixedString32(fixedString, out var decoded) && decoded == "A\u20ac", "FixedString32 strict UTF-8 canonical decode failed");
        var trailing = (byte[])fixedString.Clone(); trailing[31] = 1;
        Require(!TryDecodeFixedString32(trailing, out _), "FixedString32 accepted nonzero trailing bytes");
        var invalidUtf8 = new byte[32]; invalidUtf8[0] = 2; invalidUtf8[2] = 0xc0; invalidUtf8[3] = 0x80;
        Require(!TryDecodeFixedString32(invalidUtf8, out _), "FixedString32 accepted invalid UTF-8");

        var asset = new byte[32];
        WriteU64(asset, 0, 0x0102030405060708UL); WriteU64(asset, 8, 0x1112131415161718UL);
        Require(TryDecodeAssetId(asset, out var high, out var low, out var local, out var present)
            && high == 0x0102030405060708UL && low == 0x1112131415161718UL && local == 0 && !present,
            "AssetId absent-local canonical decode failed");
        var absentWithLocal = (byte[])asset.Clone(); WriteU64(absentWithLocal, 16, 7);
        Require(!TryDecodeAssetId(absentWithLocal, out _, out _, out _, out _), "AssetId accepted nonzero absent local-file ID");
        var invalidPresence = (byte[])asset.Clone(); invalidPresence[24] = 2;
        Require(!TryDecodeAssetId(invalidPresence, out _, out _, out _, out _), "AssetId accepted noncanonical Bool8-present");
        var nonzeroPadding = (byte[])asset.Clone(); nonzeroPadding[31] = 1;
        Require(!TryDecodeAssetId(nonzeroPadding, out _, out _, out _, out _), "AssetId accepted nonzero trailing padding");
    }

    private static bool TryDecodeFixedString32(byte[] bytes, out string value)
    {
        value = string.Empty;
        if (bytes.Length != 32) return false;
        var length = bytes[0] | (bytes[1] << 8);
        if (length > 30) return false;
        for (var index = 2 + length; index < bytes.Length; index++) if (bytes[index] != 0) return false;
        try { value = new UTF8Encoding(false, true).GetString(bytes, 2, length); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    private static bool TryDecodeAssetId(byte[] bytes, out ulong high, out ulong low, out long local, out bool present)
    {
        high = 0; low = 0; local = 0; present = false;
        if (bytes.Length != 32 || bytes[24] > 1) return false;
        for (var index = 25; index < bytes.Length; index++) if (bytes[index] != 0) return false;
        high = ReadU64(bytes, 0); low = ReadU64(bytes, 8); local = unchecked((long)ReadU64(bytes, 16)); present = bytes[24] != 0;
        return present || local == 0;
    }

    private static ulong ReadU64(byte[] bytes, int offset)
    {
        ulong value = 0;
        for (var index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (index * 8);
        return value;
    }

    private static void WriteU64(byte[] bytes, int offset, ulong value)
    {
        for (var index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (index * 8));
    }
    private static string ExtractCatalogFingerprint(string generated)
    {
        const string marker = "public static global::AIBT.Burst.BurstCatalogFingerprint Fingerprint";
        var start = generated.IndexOf(marker, StringComparison.Ordinal);
        Require(start >= 0, "generated catalog fingerprint member missing");
        var end = generated.IndexOf("public static global::AIBT.Burst.BurstCatalogValidationResult Validate", start, StringComparison.Ordinal);
        Require(end > start, "generated catalog fingerprint member malformed");
        return generated.Substring(start, end - start);
    }

    private static byte[] IndependentCatalogFingerprint()
    { using var sha = SHA256.Create(); return sha.ComputeHash(IndependentCatalogBytes(0, out _)); }

    private static byte[] IndependentCatalogBytes(byte canaryCapability, out int canaryCapabilityOffset)
    {
        var canaryConfig = IndependentStorage("AIBT-CONFIG-LAYOUT-V1\0", "aibt.canary.action", 24, 8,
            new IndependentField("a-count", "UInt32", 0, 4, 4, 6), new IndependentField("b-limit", "UInt64", 8, 8, 8, 8), new IndependentField("c-enabled", "Bool", 16, 1, 1, 0));
        var canaryMemory = IndependentStorage("AIBT-MEMORY-LAYOUT-V1\0", "aibt.canary.action", 16, 8,
            new IndependentField("a-count", "Int32", 0, 4, 4, 5), new IndependentField("b-flags", "UInt32", 4, 4, 4, 6), new IndependentField("c-total", "UInt64", 8, 8, 8, 8));
        var observerConfig = IndependentStorage("AIBT-CONFIG-LAYOUT-V1\0", "aibt.observer.condition", 4, 4, new IndependentField("a-threshold", "UInt32", 0, 4, 4, 6));
        var observerMemory = IndependentStorage("AIBT-MEMORY-LAYOUT-V1\0", "aibt.observer.condition", 4, 4, new IndependentField("a-last", "Int32", 0, 4, 4, 5));
        var runtimeConfig = IndependentStorage("AIBT-CONFIG-LAYOUT-V1\0", "aibt.fixture.runtime.builtin", 0, 1);
        var runtimeMemory = IndependentStorage("AIBT-MEMORY-LAYOUT-V1\0", "aibt.fixture.runtime.builtin", 0, 1);
        var canaryAccess = IndependentAccess("aibt.canary.action"); var observerAccess = IndependentAccess("aibt.observer.condition"); var runtimeAccess = IndependentAccess("aibt.fixture.runtime.builtin");
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        Raw(writer, "AIBT-CATALOG-V1\0"); U32(writer, 1); S(writer, "consumer.catalog"); U32(writer, 1); U32(writer, 3);
        ShardPrefix(writer, "canary.nodes", "aibt.canary.action", 1, 1, 1, 0, 1, 3, 0, 15); canaryCapabilityOffset = checked((int)stream.Position); writer.Write(canaryCapability); writer.Write(canaryConfig); writer.Write(canaryMemory); writer.Write(canaryAccess);
        Shard(writer, "observer.nodes", "aibt.observer.condition", 1, 0, 1, 0, 0, 3, 0, 31, 0, observerConfig, observerMemory, observerAccess);
        Shard(writer, "runtime-builtins.fixture", "aibt.fixture.runtime.builtin", 1, 1, 1, 0, 0, 1, 0, 15, 0, runtimeConfig, runtimeMemory, runtimeAccess);
        return stream.ToArray();
    }

    private static byte[] IndependentRegistryBytes()
    {
        var nodes = new[]
        {
            new IndependentRegistryNode("aibt.canary.action", "Canary", "action", "Use", "Avoid", new[] { new IndependentRegistryParameter("a-count", "uint32", 0, 4, 4), new IndependentRegistryParameter("b-limit", "uint64", 8, 8, 8), new IndependentRegistryParameter("c-enabled", "boolean", 16, 1, 1) }, new[] { "success", "failure" }, 16, 8, "low", "canary"),
            new IndependentRegistryNode("aibt.fixture.runtime.builtin", "Feasibility-only Runtime built-in metadata fixture.", "action", "P2-001 isolated registry hashing.", "Production behavior.", Array.Empty<IndependentRegistryParameter>(), new[] { "success" }, 0, 1, "trivial", "runtime-fixture"),
            new IndependentRegistryNode("aibt.observer.condition", "Observer", "condition", "Use", "Avoid", new[] { new IndependentRegistryParameter("a-threshold", "uint32", 0, 4, 4) }, new[] { "success", "failure" }, 4, 4, "trivial", "observer")
        }.OrderBy(node => node.TypeId, Utf8Comparer.Instance).ToArray();
        var builder = new StringBuilder(4096); builder.AppendLine("{"); RegistryLine(builder, 1, "\"format\": \"aibt-node-registry\","); RegistryLine(builder, 1, "\"formatVersion\": 1,"); RegistryLine(builder, 1, "\"manifests\": [");
        for (var index = 0; index < nodes.Length; index++) { AppendIndependentRegistryManifest(builder, nodes[index], 2); if (index + 1 < nodes.Length) builder.Append(','); builder.AppendLine(); }
        RegistryLine(builder, 1, "]"); builder.Append("}\n"); return new UTF8Encoding(false, true).GetBytes(builder.ToString());
    }

    private static void AppendIndependentRegistryManifest(StringBuilder builder, IndependentRegistryNode node, int indent)
    {
        RegistryIndent(builder, indent); builder.AppendLine("{"); RegistryProperty(builder, indent + 1, "typeId", node.TypeId, true); RegistryNumber(builder, indent + 1, "version", 1, true);
        RegistryProperty(builder, indent + 1, "summary", node.Summary, true); RegistryProperty(builder, indent + 1, "category", "Tests", true); RegistryProperty(builder, indent + 1, "kind", node.Kind, true);
        RegistryProperty(builder, indent + 1, "whenToUse", node.Use, true); RegistryProperty(builder, indent + 1, "whenNotToUse", node.Avoid, true);
        RegistryIndent(builder, indent + 1); builder.Append("\"parameters\": ");
        if (node.Parameters.Length == 0) builder.AppendLine("{},"); else { builder.AppendLine("{"); for (var i = 0; i < node.Parameters.Length; i++) { var parameter = node.Parameters[i]; RegistryIndent(builder, indent + 2); RegistryString(builder, parameter.Id); builder.AppendLine(": {"); RegistryProperty(builder, indent + 3, "type", parameter.Type, true); RegistryBoolean(builder, indent + 3, "required", true, true); RegistryIndent(builder, indent + 3); builder.AppendLine("\"packing\": {"); RegistryNumber(builder, indent + 4, "offset", parameter.Offset, true); RegistryNumber(builder, indent + 4, "size", parameter.Size, true); RegistryNumber(builder, indent + 4, "alignment", parameter.Alignment, false); RegistryIndent(builder, indent + 3); builder.AppendLine("}"); RegistryIndent(builder, indent + 2); builder.Append('}'); if (i + 1 < node.Parameters.Length) builder.Append(','); builder.AppendLine(); } RegistryIndent(builder, indent + 1); builder.AppendLine("},"); }
        RegistryIndent(builder, indent + 1); builder.AppendLine("\"childPolicy\": {"); RegistryNumber(builder, indent + 2, "minimum", 0, true); RegistryNumber(builder, indent + 2, "maximum", 0, true); RegistryBoolean(builder, indent + 2, "ordered", true, false); RegistryIndent(builder, indent + 1); builder.AppendLine("},");
        RegistryLine(builder, indent + 1, "\"reads\": [],"); RegistryLine(builder, indent + 1, "\"writes\": [],"); RegistryLine(builder, indent + 1, "\"sideEffects\": [],");
        RegistryIndent(builder, indent + 1); builder.AppendLine("\"possibleStatuses\": ["); for (var i = 0; i < node.Statuses.Length; i++) { RegistryIndent(builder, indent + 2); RegistryString(builder, node.Statuses[i]); if (i + 1 < node.Statuses.Length) builder.Append(','); builder.AppendLine(); } RegistryIndent(builder, indent + 1); builder.AppendLine("],");
        RegistryIndent(builder, indent + 1); builder.AppendLine("\"memory\": {"); RegistryNumber(builder, indent + 2, "size", node.MemorySize, true); RegistryNumber(builder, indent + 2, "alignment", node.MemoryAlignment, true); RegistryProperty(builder, indent + 2, "lifetime", "activation", false); RegistryIndent(builder, indent + 1); builder.AppendLine("},");
        var configSize = node.Parameters.Length == 0 ? 0u : node.TypeId == "aibt.canary.action" ? 24u : 4u; var configAlignment = node.Parameters.Length == 0 ? 1u : node.TypeId == "aibt.canary.action" ? 8u : 4u;
        RegistryIndent(builder, indent + 1); builder.AppendLine("\"configuration\": {"); RegistryNumber(builder, indent + 2, "size", configSize, true); RegistryNumber(builder, indent + 2, "alignment", configAlignment, false); RegistryIndent(builder, indent + 1); builder.AppendLine("},");
        RegistryProperty(builder, indent + 1, "cancellation", "not-applicable", true); RegistryProperty(builder, indent + 1, "executionDomain", "burst", true); RegistryBoolean(builder, indent + 1, "deterministic", true, true); RegistryProperty(builder, indent + 1, "costHint", node.Cost, true);
        RegistryIndent(builder, indent + 1); builder.AppendLine("\"examples\": ["); RegistryIndent(builder, indent + 2); builder.AppendLine("{"); RegistryProperty(builder, indent + 3, "title", node.Example, true); RegistryIndent(builder, indent + 3); builder.Append("\"parameters\": ");
        if (node.Parameters.Length == 0) builder.AppendLine("{},"); else { builder.AppendLine("{"); for (var i = 0; i < node.Parameters.Length; i++) { RegistryIndent(builder, indent + 4); RegistryString(builder, node.Parameters[i].Id); builder.Append(node.Parameters[i].Type == "boolean" ? ": false" : ": 0"); if (i + 1 < node.Parameters.Length) builder.Append(','); builder.AppendLine(); } RegistryIndent(builder, indent + 3); builder.AppendLine("},"); }
        RegistryProperty(builder, indent + 3, "expectedBehavior", node.Summary, false); RegistryIndent(builder, indent + 2); builder.AppendLine("}"); RegistryIndent(builder, indent + 1); builder.AppendLine("]"); RegistryIndent(builder, indent); builder.Append('}');
    }

    private static void RegistryIndent(StringBuilder builder, int indent) => builder.Append(' ', indent * 2);
    private static void RegistryLine(StringBuilder builder, int indent, string text) { RegistryIndent(builder, indent); builder.AppendLine(text); }
    private static void RegistryProperty(StringBuilder builder, int indent, string name, string value, bool comma) { RegistryIndent(builder, indent); RegistryString(builder, name); builder.Append(": "); RegistryString(builder, value); if (comma) builder.Append(','); builder.AppendLine(); }
    private static void RegistryNumber(StringBuilder builder, int indent, string name, uint value, bool comma) { RegistryIndent(builder, indent); RegistryString(builder, name); builder.Append(": ").Append(value.ToString(CultureInfo.InvariantCulture)); if (comma) builder.Append(','); builder.AppendLine(); }
    private static void RegistryBoolean(StringBuilder builder, int indent, string name, bool value, bool comma) { RegistryIndent(builder, indent); RegistryString(builder, name); builder.Append(value ? ": true" : ": false"); if (comma) builder.Append(','); builder.AppendLine(); }
    private static void RegistryString(StringBuilder builder, string value) { builder.Append('"'); foreach (var character in value) { if (character == '"') builder.Append("\\\""); else if (character == '\\') builder.Append("\\\\"); else builder.Append(character); } builder.Append('"'); }

    private static void VerifyIndependentRegisteredFingerprints(string generated)
    {
        var inner = IndependentRegisteredSchema("aibt.padded-inner", "aibt.schema.padded-inner.v1", 16, 8,
            new IndependentSchemaField("a-flag", "UInt8", 0, 1, 1, 2, new byte[32]), new IndependentSchemaField("b-value", "Int64", 8, 8, 8, 7, new byte[32]));
        var outer = IndependentRegisteredSchema("aibt.padded-payload", "aibt.schema.padded-payload.v1", 24, 8,
            new IndependentSchemaField("a-inner", "aibt.padded-inner", 0, 16, 8, 13, inner), new IndependentSchemaField("b-value", "Int64", 16, 8, 8, 7, new byte[32]));
        Require(inner.Any(value => value != 0) && outer.Any(value => value != 0) && !inner.SequenceEqual(outer), "registered schema H32 vectors must be nonzero and nested");
        var config = IndependentStorageWithSchemas("AIBT-CONFIG-LAYOUT-V1\0", "aibt.padded.node", 4, 4,
            new IndependentSchemaField("effect", "GeneratedHandle", 0, 4, 4, 12, new byte[32]));
        var memory = IndependentStorageWithSchemas("AIBT-MEMORY-LAYOUT-V1\0", "aibt.padded.node", 24, 8,
            new IndependentSchemaField("a-payload", "aibt.padded-payload", 0, 24, 8, 13, outer));
        var access = IndependentPaddedAccess(outer);
        config = IndependentCatalogLayout("AIBT-CATALOG-CONFIG-LAYOUT-V1\0", config);
        memory = IndependentCatalogLayout("AIBT-CATALOG-MEMORY-LAYOUT-V1\0", memory);
        access = IndependentCatalogLayout("AIBT-CATALOG-ACCESS-LAYOUT-V1\0", access);
        Require(generated.Contains(IndependentHashLiteral(config), StringComparison.Ordinal), "independent padded configuration H32 missing from generated catalog");
        Require(generated.Contains(IndependentHashLiteral(memory), StringComparison.Ordinal), "independent nested registered memory H32 missing from generated catalog");
        Require(generated.Contains(IndependentHashLiteral(access), StringComparison.Ordinal), "independent registered access H32 missing from generated catalog");
    }

    private static byte[] IndependentRegisteredSchema(string typeId, string schemaId, uint size, byte alignment, params IndependentSchemaField[] fields)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        Raw(writer, "AIBT-VALUE-SCHEMA-V1\0"); U32(writer, 1); S(writer, typeId); U64(writer, IndependentFnv(typeId)); U32(writer, 1); S(writer, schemaId); U64(writer, IndependentFnv(schemaId)); U32(writer, size); writer.Write(alignment); U32(writer, (uint)fields.Length);
        foreach (var field in fields) { S(writer, field.Id); U64(writer, IndependentFnv(field.Id)); S(writer, field.TypeId); U64(writer, IndependentFnv(field.TypeId)); U32(writer, 1); writer.Write(field.Schema); U32(writer, field.Offset); U32(writer, field.Size); writer.Write(field.Alignment); writer.Write(field.Encoding); }
        using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray());
    }

    private static byte[] IndependentStorageWithSchemas(string domain, string nodeId, uint totalSize, byte alignment, params IndependentSchemaField[] fields)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        Raw(writer, domain); U32(writer, 1); S(writer, nodeId); U32(writer, 1); U32(writer, totalSize); writer.Write(alignment); U32(writer, (uint)fields.Length);
        foreach (var field in fields) { S(writer, field.Id); U64(writer, IndependentFnv(field.Id)); S(writer, field.TypeId); U32(writer, 1); writer.Write(field.Schema); U32(writer, field.Offset); U32(writer, field.Size); writer.Write(field.Alignment); writer.Write(field.Encoding); }
        var padding = new List<(uint Offset, uint Size)>(); uint cursor = 0; foreach (var field in fields.OrderBy(field => field.Offset)) { if (field.Offset > cursor) padding.Add((cursor, field.Offset - cursor)); cursor = field.Offset + field.Size; } if (cursor < totalSize) padding.Add((cursor, totalSize - cursor));
        U32(writer, (uint)padding.Count); foreach (var range in padding) { U32(writer, range.Offset); U32(writer, range.Size); }
        using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray());
    }

    private static byte[] IndependentPaddedAccess(byte[] payloadSchema)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        Raw(writer, "AIBT-ACCESS-LAYOUT-V1\0"); U32(writer, 1); S(writer, "aibt.padded.node"); U32(writer, 1); U32(writer, 1);
        S(writer, "effect"); U64(writer, IndependentFnv("effect")); writer.Write((byte)4); writer.Write((byte)0xff); writer.Write((byte)1); U32(writer, 0); U32(writer, 1);
        writer.Write((byte)1); S(writer, "aibt.padded-payload"); U64(writer, IndependentFnv("aibt.padded-payload")); U32(writer, 1); writer.Write(payloadSchema);
        using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray());
    }

    private static byte[] IndependentCatalogLayout(string domain, byte[] nodeHash)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        Raw(writer, domain); U32(writer, 1); S(writer, "padded.catalog"); U32(writer, 1); U32(writer, 1); S(writer, "aibt.padded.node"); U32(writer, 1); writer.Write(nodeHash);
        using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray());
    }

    private static string IndependentHashLiteral(byte[] hash)
    {
        var words = new string[8]; for (var index = 0; index < words.Length; index++) { var offset = index * 4; var value = (uint)(hash[offset] | hash[offset + 1] << 8 | hash[offset + 2] << 16 | hash[offset + 3] << 24); words[index] = "0x" + value.ToString("x8", CultureInfo.InvariantCulture) + "u"; }
        return "new global::AIBT.Burst.BurstHash256(" + string.Join(", ", words) + ")";
    }

    private static byte[] IndependentStorage(string domain, string nodeId, uint totalSize, byte alignment, params IndependentField[] fields)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true); Raw(writer, domain); U32(writer, 1); S(writer, nodeId); U32(writer, 1); U32(writer, totalSize); writer.Write(alignment); U32(writer, (uint)fields.Length);
        foreach (var field in fields) { S(writer, field.Id); U64(writer, IndependentFnv(field.Id)); S(writer, field.TypeId); U32(writer, 1); writer.Write(new byte[32]); U32(writer, field.Offset); U32(writer, field.Size); writer.Write(field.Alignment); writer.Write(field.Encoding); }
        var padding = new List<(uint Offset, uint Size)>(); uint cursor = 0; foreach (var field in fields.OrderBy(value => value.Offset)) { if (field.Offset > cursor) padding.Add((cursor, field.Offset - cursor)); cursor = field.Offset + field.Size; } if (totalSize > cursor) padding.Add((cursor, totalSize - cursor));
        U32(writer, (uint)padding.Count); foreach (var range in padding) { U32(writer, range.Offset); U32(writer, range.Size); }
        using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray());
    }
    private static byte[] IndependentAccess(string nodeId) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true); Raw(writer, "AIBT-ACCESS-LAYOUT-V1\0"); U32(writer, 1); S(writer, nodeId); U32(writer, 1); U32(writer, 0); using var sha = SHA256.Create(); return sha.ComputeHash(stream.ToArray()); }
    private static void Shard(BinaryWriter writer, string shardId, string nodeId, uint nodeVersion, byte kind, byte deterministic, byte cancellation, byte cost, byte statuses, byte memoryLifetime, byte callbackMask, byte capabilityMask, byte[] config, byte[] memory, byte[] access)
    { ShardPrefix(writer, shardId, nodeId, nodeVersion, kind, deterministic, cancellation, cost, statuses, memoryLifetime, callbackMask); writer.Write(capabilityMask); writer.Write(config); writer.Write(memory); writer.Write(access); }
    private static void ShardPrefix(BinaryWriter writer, string shardId, string nodeId, uint nodeVersion, byte kind, byte deterministic, byte cancellation, byte cost, byte statuses, byte memoryLifetime, byte callbackMask)
    { S(writer, shardId); U32(writer, 1); U32(writer, 1); S(writer, nodeId); U64(writer, IndependentFnv(nodeId)); U32(writer, nodeVersion); writer.Write(kind); writer.Write(deterministic); writer.Write(cancellation); writer.Write(cost); writer.Write(statuses); writer.Write(memoryLifetime); writer.Write(callbackMask); }
    private static ulong IndependentFnv(string value) { var hash = 14695981039346656037UL; foreach (var octet in Encoding.UTF8.GetBytes(value)) { hash ^= octet; hash *= 1099511628211UL; } return hash; }
    private static void Raw(BinaryWriter writer, string value) => writer.Write(Encoding.UTF8.GetBytes(value));
    private static void S(BinaryWriter writer, string value) { var bytes = new UTF8Encoding(false, true).GetBytes(value); U32(writer, (uint)bytes.Length); writer.Write(bytes); }
    private static void U32(BinaryWriter writer, uint value) { writer.Write((byte)value); writer.Write((byte)(value >> 8)); writer.Write((byte)(value >> 16)); writer.Write((byte)(value >> 24)); }
    private static void U64(BinaryWriter writer, ulong value) { U32(writer, (uint)value); U32(writer, (uint)(value >> 32)); }
    private static void RequireAtomicUnusableShard(CompilationResult result, string label)
        => Require(result.GeneratedSource.Contains("IsUsable = false", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("BurstAccess", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("BurstCodec", StringComparison.Ordinal),
            label + " emitted usable shard members after an error");
    private static void RequireAtomicUnusableCatalog(CompilationResult result, string label)
        => Require(result.GeneratedSource.Contains("IsUsable = false", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("IsUsable = true", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("ExecuteImmediate", StringComparison.Ordinal)
            && !result.GeneratedSource.Contains("Schedule", StringComparison.Ordinal), label + " emitted usable catalog members after an error");
    private static string Format(Diagnostic value) => value.Id + ":" + value.Severity + ":" + value.GetMessage(CultureInfo.InvariantCulture);

    private const string ValidConfig = "public partial struct Config { [AibtConfigField(\"a\", \"UInt32\", 1u)] public uint Value; }";
    private const string ValidMemory = "public partial struct Memory { [AibtMemoryField(\"a\", \"Int32\", 1u)] public int Value; }";
    private const string BindingConfig = "public partial struct Config { [AibtConfigField(\"input\", \"GeneratedHandle\", 1u), AibtBlackboardBinding(\"input\", BurstBlackboardAccess.Read, AIBT.BlackboardScope.Tree, \"Int32\", 1u)] public BlackboardReadHandle<int> Value; }";
    private const string WrongHandleConfig = "public partial struct Config { [AibtConfigField(\"input\", \"GeneratedHandle\", 1u), AibtBlackboardBinding(\"input\", BurstBlackboardAccess.Read, AIBT.BlackboardScope.Tree, \"Int32\", 1u)] public BlackboardWriteHandle<int> Value; }";
    private const string SharedWriteConfig = "public partial struct Config { [AibtConfigField(\"output\", \"GeneratedHandle\", 1u), AibtBlackboardBinding(\"output\", BurstBlackboardAccess.Write, AIBT.BlackboardScope.Shared, \"Int32\", 1u)] public BlackboardWriteHandle<int> Value; }";
    private const string UnityApiStub = "namespace UnityEngine { public class GameObject { public static GameObject Find(string value) => null; } }";
    private const string ExternalCollisionShard = @"
using AIBT; using AIBT.Burst;
namespace External {
public partial struct ConfigA { [AibtConfigField(""a"", ""UInt32"", 1u)] public uint Value; }
public partial struct MemoryA { [AibtMemoryField(""a"", ""Int32"", 1u)] public int Value; }
public partial struct ConfigB { [AibtConfigField(""b"", ""UInt32"", 1u)] public uint Value; }
public partial struct MemoryB { [AibtMemoryField(""b"", ""Int32"", 1u)] public int Value; }
[AibtCatalogShard(""external.collision"", 1u)] public partial struct CollisionShard { public const bool IsUsable = true; public const uint AbiVersion = 1u; }
[AibtBurstNode(""aibt.external.duplicate"", 1u, BurstNodeKind.Action, typeof(ConfigA), typeof(MemoryA), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Low, BurstNodeStatusMask.Success)]
[AibtNodeDocumentation(""First"", ""Tests"", ""Use"", ""Avoid"", ""first"")] public partial struct FirstNode {
public static void Enter(in ConfigA config, ref MemoryA memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in ConfigA config, ref MemoryA memory, ref BurstTickContext context) { return NodeStatus.Success; }
public static void Abort(in ConfigA config, ref MemoryA memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in ConfigA config, ref MemoryA memory, ref BurstExitContext context, BurstNodeExitReason reason) { } }
[AibtBurstNode(""aibt.external.duplicate"", 1u, BurstNodeKind.Action, typeof(ConfigB), typeof(MemoryB), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Low, BurstNodeStatusMask.Success)]
[AibtNodeDocumentation(""Second"", ""Tests"", ""Use"", ""Avoid"", ""second"")] public partial struct SecondNode {
public static void Enter(in ConfigB config, ref MemoryB memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in ConfigB config, ref MemoryB memory, ref BurstTickContext context) { return NodeStatus.Success; }
public static void Abort(in ConfigB config, ref MemoryB memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in ConfigB config, ref MemoryB memory, ref BurstExitContext context, BurstNodeExitReason reason) { } }
}";

    private static string Node(string nodeFields, string config, string memory, string nodeName = "ProbeNode", string typeId = "aibt.probe.node", string kind = "BurstNodeKind.Action", string docs = "[AibtNodeDocumentation(\"Probe\", \"Tests\", \"Use\", \"Avoid\", \"example\")]", string? tickSignature = null, string configName = "Config", string memoryName = "Memory", string nodeAttributes = "", string cancellation = "BurstCancellationMode.NotApplicable", string statuses = "BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure") => $@"
using AIBT; using AIBT.Burst;
namespace Probe {{ {config} {memory}
[AibtCatalogShard(""probe.shard"", 1u)] public partial struct ProbeShard {{ }}
[AibtBurstNode(""{typeId}"", 1u, {kind}, typeof({configName}), typeof({memoryName}), NodeMemoryLifetime.Activation, true, {cancellation}, BurstNodeCost.Low, {statuses})] {docs}
{nodeAttributes} public partial struct {nodeName} {{ {nodeFields}
public static void Enter(in {configName} config, ref {memoryName} memory, ref BurstEnterContext context) {{ }}
{tickSignature ?? $"public static NodeStatus Tick(in {configName} config, ref {memoryName} memory, ref BurstTickContext context) {{ return NodeStatus.Success; }}"}
public static void Abort(in {configName} config, ref {memoryName} memory, ref BurstAbortContext context, BurstNodeAbortReason reason) {{ }}
public static void Exit(in {configName} config, ref {memoryName} memory, ref BurstExitContext context, BurstNodeExitReason reason) {{ }} }} }}";

    private const string NodeA = @"
using AIBT; using AIBT.Burst;
namespace Canary.Nodes {
public partial struct CanaryConfig { [AibtConfigField(""a-count"", ""UInt32"", 1u)] public uint Count; [AibtConfigField(""b-limit"", ""UInt64"", 1u)] public ulong Limit; [AibtConfigField(""c-enabled"", ""Bool"", 1u)] public bool Enabled; }
public partial struct CanaryMemory { [AibtMemoryField(""a-count"", ""Int32"", 1u)] public int Count; [AibtMemoryField(""b-flags"", ""UInt32"", 1u)] public uint Flags; [AibtMemoryField(""c-total"", ""UInt64"", 1u)] public ulong Total; }
[AibtCatalogShard(""canary.nodes"", 1u)] public partial struct CanaryShard { }
[AibtBurstNode(""aibt.canary.action"", 1u, BurstNodeKind.Action, typeof(CanaryConfig), typeof(CanaryMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Low, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
[AibtNodeDocumentation(""Canary"", ""Tests"", ""Use"", ""Avoid"", ""canary"")]
public partial struct CanaryNode {
public static void Enter(in CanaryConfig config, ref CanaryMemory memory, ref BurstEnterContext context) { memory.Count = unchecked((int)config.Count); }
public static NodeStatus Tick(in CanaryConfig config, ref CanaryMemory memory, ref BurstTickContext context) { memory.Count += unchecked((int)config.Count); memory.Flags++; memory.Total += config.Limit; return config.Enabled ? NodeStatus.Success : NodeStatus.Failure; }
public static void Abort(in CanaryConfig config, ref CanaryMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { memory.Flags++; }
public static void Exit(in CanaryConfig config, ref CanaryMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { memory.Total++; }
} }";

    private const string NodeB = @"
using AIBT; using AIBT.Burst;
namespace Observer.Nodes {
public partial struct ObserverConfig { [AibtConfigField(""a-threshold"", ""UInt32"", 1u)] public uint Threshold; }
public partial struct ObserverMemory { [AibtMemoryField(""a-last"", ""Int32"", 1u)] public int Last; }
[AibtCatalogShard(""observer.nodes"", 1u)] public partial struct ObserverShard { }
[AibtBurstNode(""aibt.observer.condition"", 1u, BurstNodeKind.Condition, typeof(ObserverConfig), typeof(ObserverMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, BurstNodeStatusMask.Success | BurstNodeStatusMask.Failure)]
[AibtNodeDocumentation(""Observer"", ""Tests"", ""Use"", ""Avoid"", ""observer"")] [AibtObserverCondition]
public partial struct ObserverNode {
public static void Enter(in ObserverConfig config, ref ObserverMemory memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in ObserverConfig config, ref ObserverMemory memory, ref BurstTickContext context) { return NodeStatus.Success; }
public static void Abort(in ObserverConfig config, ref ObserverMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in ObserverConfig config, ref ObserverMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
public static ConditionResult Evaluate(in ObserverConfig config, ref BurstObserverContext context) { return config.Threshold > 0 ? ConditionResult.Success : ConditionResult.Failure; }
} }";

    private const string Catalog = @"using AIBT.Burst; namespace Consumer.Catalog { [AibtCatalogSet(""consumer.catalog"", 1u, typeof(AIBT.BurstAbi.RuntimeBuiltins.RuntimeBuiltinsShard), typeof(Canary.Nodes.CanaryShard), typeof(Observer.Nodes.ObserverShard))] public static partial class GeneratedCatalog { } }";
    private const string CatalogReversed = @"using AIBT.Burst; namespace Consumer.Catalog { [AibtCatalogSet(""consumer.catalog"", 1u, typeof(Observer.Nodes.ObserverShard), typeof(Canary.Nodes.CanaryShard), typeof(AIBT.BurstAbi.RuntimeBuiltins.RuntimeBuiltinsShard))] public static partial class GeneratedCatalog { } }";

    private const string RuntimeBuiltins = @"
using AIBT; using AIBT.Burst;
namespace AIBT.BurstAbi.RuntimeBuiltins {
public partial struct RuntimeBuiltinConfig { }
public partial struct RuntimeBuiltinMemory { }
[AibtCatalogShard(""runtime-builtins.fixture"", 1u)] public partial struct RuntimeBuiltinsShard { }
[AibtBurstNode(""aibt.fixture.runtime.builtin"", 1u, BurstNodeKind.Action, typeof(RuntimeBuiltinConfig), typeof(RuntimeBuiltinMemory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Trivial, BurstNodeStatusMask.Success)]
[AibtNodeDocumentation(""Feasibility-only Runtime built-in metadata fixture."", ""Tests"", ""P2-001 isolated registry hashing."", ""Production behavior."", ""runtime-fixture"")]
public partial struct RuntimeBuiltinNode {
public static void Enter(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstTickContext context) { return NodeStatus.Success; }
public static void Abort(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in RuntimeBuiltinConfig config, ref RuntimeBuiltinMemory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
} }";

    private const string PaddedPayloadNode = @"
using AIBT; using AIBT.Burst;
namespace Padded.Probe {
[AibtBurstValue(""aibt.padded-inner"", 1u, ""aibt.schema.padded-inner.v1"")] public partial struct PaddedInner { [AibtValueField(""a-flag"", ""UInt8"", 1u)] public byte Flag; [AibtValueField(""b-value"", ""Int64"", 1u)] public long Value; }
[AibtBurstValue(""aibt.padded-payload"", 1u, ""aibt.schema.padded-payload.v1"")] public partial struct PaddedPayload { [AibtValueField(""a-inner"", ""aibt.padded-inner"", 1u)] public PaddedInner Inner; [AibtValueField(""b-value"", ""Int64"", 1u)] public long Value; public long Computed => Value + Inner.Flag; public long Twice() { return Value * 2; } }
public partial struct Config { [AibtConfigField(""effect"", ""GeneratedHandle"", 1u), AibtCommandBinding(""effect"", ""aibt.padded-payload"", 1u)] public CommandHandle<PaddedPayload> Output; }
public partial struct Memory { [AibtMemoryField(""a-payload"", ""aibt.padded-payload"", 1u)] public PaddedPayload Value; }
[AibtCatalogShard(""padded.shard"", 1u)] public partial struct ProbeShard { }
[AibtBurstNode(""aibt.padded.node"", 1u, BurstNodeKind.Action, typeof(Config), typeof(Memory), NodeMemoryLifetime.Activation, true, BurstCancellationMode.NotApplicable, BurstNodeCost.Low, BurstNodeStatusMask.Success)]
[AibtNodeDocumentation(""Padded"", ""Tests"", ""Use"", ""Avoid"", ""padded"")] public partial struct PaddedNode {
public static void Enter(in Config config, ref Memory memory, ref BurstEnterContext context) { }
public static NodeStatus Tick(in Config settings, ref Memory memory, ref BurstTickContext context) { var payload = new PaddedPayload { Inner = new PaddedInner { Flag = 1, Value = 7 }, Value = 9 }; ProbeShard.BurstAccess.TryEmit(ref context, settings.Output, in payload); memory.Value = payload; return NodeStatus.Success; }
public static void Abort(in Config config, ref Memory memory, ref BurstAbortContext context, BurstNodeAbortReason reason) { }
public static void Exit(in Config config, ref Memory memory, ref BurstExitContext context, BurstNodeExitReason reason) { }
} }";

    private sealed class Negative { internal Negative(string id, string source, bool includeUnity = false) { Id = id; Source = source; IncludeUnity = includeUnity; } internal string Id { get; } internal string Source { get; } internal bool IncludeUnity { get; } }
    private readonly struct IndependentField { internal IndependentField(string id, string typeId, uint offset, uint size, byte alignment, byte encoding) { Id = id; TypeId = typeId; Offset = offset; Size = size; Alignment = alignment; Encoding = encoding; } internal string Id { get; } internal string TypeId { get; } internal uint Offset { get; } internal uint Size { get; } internal byte Alignment { get; } internal byte Encoding { get; } }
    private readonly struct IndependentSchemaField { internal IndependentSchemaField(string id, string typeId, uint offset, uint size, byte alignment, byte encoding, byte[] schema) { Id=id; TypeId=typeId; Offset=offset; Size=size; Alignment=alignment; Encoding=encoding; Schema=schema; } internal string Id { get; } internal string TypeId { get; } internal uint Offset { get; } internal uint Size { get; } internal byte Alignment { get; } internal byte Encoding { get; } internal byte[] Schema { get; } }
    private sealed class IndependentRegistryNode { internal IndependentRegistryNode(string typeId,string summary,string kind,string use,string avoid,IndependentRegistryParameter[] parameters,string[] statuses,uint memorySize,uint memoryAlignment,string cost,string example) { TypeId=typeId;Summary=summary;Kind=kind;Use=use;Avoid=avoid;Parameters=parameters;Statuses=statuses;MemorySize=memorySize;MemoryAlignment=memoryAlignment;Cost=cost;Example=example; } internal string TypeId{get;} internal string Summary{get;} internal string Kind{get;} internal string Use{get;} internal string Avoid{get;} internal IndependentRegistryParameter[] Parameters{get;} internal string[] Statuses{get;} internal uint MemorySize{get;} internal uint MemoryAlignment{get;} internal string Cost{get;} internal string Example{get;} }
    private readonly struct IndependentRegistryParameter { internal IndependentRegistryParameter(string id,string type,uint offset,uint size,uint alignment) { Id=id;Type=type;Offset=offset;Size=size;Alignment=alignment; } internal string Id{get;} internal string Type{get;} internal uint Offset{get;} internal uint Size{get;} internal uint Alignment{get;} }
    private sealed class Utf8Comparer : IComparer<string> { internal static readonly Utf8Comparer Instance=new Utf8Comparer(); public int Compare(string? left,string? right) { if (ReferenceEquals(left,right)) return 0; if (left==null) return -1; if (right==null) return 1; var a=Encoding.UTF8.GetBytes(left); var b=Encoding.UTF8.GetBytes(right); var length=Math.Min(a.Length,b.Length); for(var i=0;i<length;i++) if(a[i]!=b[i]) return a[i].CompareTo(b[i]); return a.Length.CompareTo(b.Length); } }
    private sealed class CompilationResult { internal CompilationResult(byte[] image, string generatedSource, Compilation compilation, IReadOnlyList<Diagnostic> diagnostics) { Image = image; GeneratedSource = generatedSource; Compilation = compilation; Diagnostics = diagnostics; } internal byte[] Image { get; } internal string GeneratedSource { get; } internal Compilation Compilation { get; } internal IReadOnlyList<Diagnostic> Diagnostics { get; } internal bool LogicallyUsable => Image.Length != 0 && !Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error); }
    private sealed class DiagnosticComparer : IEqualityComparer<Diagnostic> { internal static readonly DiagnosticComparer Instance = new DiagnosticComparer(); public bool Equals(Diagnostic? x, Diagnostic? y) => x?.Id == y?.Id && x?.Location.SourceSpan == y?.Location.SourceSpan && x?.GetMessage() == y?.GetMessage(); public int GetHashCode(Diagnostic value) => HashCode.Combine(value.Id, value.Location.SourceSpan, value.GetMessage()); }
}
