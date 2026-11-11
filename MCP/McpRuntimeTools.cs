using System;
using System.IO;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Debugger;
using AIBT.Editor.Graph;
using AIBT.Editor.Registry;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AIBT.Mcp
{
    internal static class McpRuntimeTools
    {
        internal static JObject InspectTree(string projectRoot, JObject args)
        {
            var (document, path) = McpAuthoringToolDispatcher.LoadTreeOrThrow(projectRoot, args);
            var catalog = ProjectNodeCatalogCache.Current;
            return new JObject
            {
                ["tree"] = JObject.Parse(System.Text.Encoding.UTF8.GetString(CanonicalTreeJson.Serialize(document, catalog.RegisteredTypes).Utf8)),
                ["contentHash"] = McpAuthoringToolDispatcher.ComputeSemanticHash(document),
                ["sourcePath"] = path,
                ["registryProblems"] = new JArray(catalog.Problems),
                ["referenceAvailable"] = document.Nodes.All(node => catalog.Registry.TryGet(node.TypeId, out var entry) && entry.HasReferenceHandlerBinding),
            };
        }

        internal static JObject Catalogs() => new JObject
        {
            ["catalogs"] = new JArray(ProjectNodeCatalog.RuntimeCatalogTypes().Select(type =>
            {
                var catalog = ProjectNodeCatalog.ForCatalog(type);
                return new JObject { ["catalogId"] = type.AssemblyQualifiedName, ["typeName"] = type.FullName,
                    ["assemblyName"] = type.Assembly.GetName().Name, ["leafTypeIds"] = new JArray(catalog.Generated.Keys.OrderBy(id => id, StringComparer.Ordinal)),
                    ["problems"] = new JArray(catalog.Problems) };
            })),
            ["registryProblems"] = new JArray(ProjectNodeCatalogCache.Current.Problems),
        };

        internal static JObject Prepare(string projectRoot, JObject args)
        {
            var (document, sourcePath) = McpAuthoringToolDispatcher.LoadTreeOrThrow(projectRoot, args);
            var sourceHash = McpAuthoringToolDispatcher.ComputeSemanticHash(document);
            if ((string)args["expectedHash"] != sourceHash)
                throw new McpToolException(McpAuthoringDiagnostics.ContentHashMismatch, "Source changed. Inspect the tree and use its current contentHash.");
            var validation = Verification.McpVerificationToolDispatcher.Validate(projectRoot, args);
            if (!(bool)validation["valid"])
            {
                validation["success"] = false;
                validation["backend"] = "native-generated-v2";
                validation["contentHash"] = sourceHash;
                return validation;
            }
            var catalogId = (string)args["catalog"];
            var types = ProjectNodeCatalog.RuntimeCatalogTypes().Where(type => type.AssemblyQualifiedName == catalogId || type.FullName == catalogId).ToArray();
            if (types.Length != 1) throw Invalid("Select one exact catalog from runtime_catalogs.");
            var outputPath = ResolveAssetPath(projectRoot, (string)args["outputPath"]);
            var existingHash = File.Exists(outputPath) ? StableHash.Sha256Hex(File.ReadAllBytes(outputPath)) : null;
            var dryRun = (bool?)args["dryRun"] ?? false;
            var unityPath = UnityAssetPath(projectRoot, outputPath);
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unityPath);
            if (existingHash != null && !(existing is ProductionTreeAsset)) throw Invalid("Destination is not an AIBT runtime asset.");
            if (existing != null && EditorUtility.IsDirty(existing) && !dryRun) throw Invalid("Destination has unsaved Editor changes; save or revert that asset before preparing.");
            if (existingHash != null && !dryRun && ((bool?)args["overwrite"] != true || (string)args["expectedAssetHash"] != existingHash))
                throw Invalid("Overwrite requires overwrite=true and the current expectedAssetHash (obtain it with dryRun).");
            ProductionTreeAsset prepared = null;
            try
            {
                prepared = ProductionTreeAssetBaker.Create(document, UnityAssetPath(projectRoot, sourcePath), types[0], out var diagnostics);
                var result = new JObject
                {
                    ["success"] = prepared != null, ["backend"] = "native-generated-v2", ["contentHash"] = sourceHash,
                    ["outputPath"] = unityPath, ["existingAssetHash"] = existingHash, ["dryRun"] = dryRun,
                    ["catalog"] = types[0].AssemblyQualifiedName, ["diagnostics"] = McpDiagnosticJson.WriteDiagnostics(diagnostics),
                };
                if (prepared == null || dryRun) return result;
                if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw Invalid("Create the output directory inside Assets before preparing.");
                var latest = TreeDocumentPersistence.Load(sourcePath);
                if (!latest.Success || McpAuthoringToolDispatcher.ComputeSemanticHash(latest.Document) != sourceHash
                    || (File.Exists(outputPath) ? StableHash.Sha256Hex(File.ReadAllBytes(outputPath)) : null) != existingHash)
                    throw Invalid("Source or destination changed during preparation; no asset was written.");
                var saved = existing;
                if (existing != null) { EditorUtility.CopySerialized(prepared, existing); EditorUtility.SetDirty(existing); }
                else { AssetDatabase.CreateAsset(prepared, unityPath); saved = prepared; prepared = null; }
                AssetDatabase.SaveAssetIfDirty(saved);
                result["assetGuid"] = AssetDatabase.AssetPathToGUID(unityPath);
                result["assetHash"] = StableHash.Sha256Hex(File.ReadAllBytes(outputPath));
                return result;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is System.Reflection.TargetInvocationException)
            { throw Invalid(exception.InnerException?.Message ?? exception.Message); }
            finally { if (prepared != null) UnityEngine.Object.DestroyImmediate(prepared); }
        }

        internal static string ResolveAssetPath(string assetsPath, string requestedPath)
        {
            if (string.IsNullOrWhiteSpace(requestedPath) || Path.IsPathRooted(requestedPath)
                || !requestedPath.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal)
                || !requestedPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) throw Invalid("outputPath must be an Assets-relative .asset path.");
            if (requestedPath.IndexOfAny(new[] { ':', '"', '<', '>', '|', '*', '?' }) >= 0 || requestedPath.Any(char.IsControl)
                || requestedPath.Replace('\\', '/').Split('/').Any(part => part != "." && part != ".." && (part.EndsWith(" ") || part.EndsWith("."))))
                throw Invalid("Output contains an invalid or ambiguous path component.");
            var root = Path.GetFullPath(assetsPath).TrimEnd(Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(Directory.GetParent(root).FullName, requestedPath));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison)) throw Invalid("Output must remain inside Assets.");
            for (var current = path; current.Length >= root.Length; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw Invalid("Output must not traverse a link or reparse point.");
            return path;
        }

        internal static string UnityAssetPath(string assetsRoot, string absolutePath)
        {
            var relative = Path.GetRelativePath(Directory.GetParent(Path.GetFullPath(assetsRoot)).FullName, Path.GetFullPath(absolutePath)).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.Ordinal)) throw Invalid("Asset must remain inside the selected project's Assets directory.");
            return relative;
        }

        internal static JObject Hosts() => new JObject { ["hosts"] = new JArray(FindHosts().Select(HostInfo)) };

        internal static JObject Trace(JObject args)
        {
            var instanceId = (ulong?)args["instanceId"] ?? 0;
            var host = FindHosts().SingleOrDefault(candidate => candidate.InstanceId == instanceId);
            if (host == null || instanceId == 0) throw Invalid("Select a live instanceId from runtime_hosts.");
            var result = HostInfo(host);
            if (host.TraceChannelOwner == null) { result["available"] = false; return result; }
            var session = new NativeExecutionDebuggerSession();
            session.Attach(host.TraceChannelOwner);
            if (!session.TryReadTrace(out var view, out var failure))
            { result["available"] = false; result["retryable"] = host.TraceChannelOwner.State != NativeOwnerStateV1.Disposed; result["reason"] = failure.ToString(); return result; }
            var limit = (int?)args["limit"] ?? 128;
            if (limit < 1 || limit > 512) throw Invalid("limit must be 1..512.");
            var after = (ulong?)args["afterSequence"] ?? 0;
            var records = view.StepHistory.Where(record => record.Sequence > after).ToArray();
            var page = records.Take(limit).ToArray();
            var ids = host.Program?.DebugMap.ToDictionary(entry => entry.RuntimeNodeIndex, entry => entry.AuthoringNodeId.Value);
            result["available"] = true;
            result["droppedCount"] = view.DroppedCount;
            result["complete"] = view.DroppedCount == 0 && !view.IsFaulted;
            result["faulted"] = view.IsFaulted;
            result["activeNodeIndices"] = new JArray(view.ActiveNodeIndices);
            result["activeNodesReliable"] = view.DroppedCount == 0 && !view.IsFaulted;
            result["hasMore"] = records.Length > page.Length;
            result["nextSequence"] = page.Length == 0 ? after : page[page.Length - 1].Sequence;
            result["events"] = new JArray(page.Select(record => new JObject
            {
                ["sequence"] = record.Sequence, ["updateId"] = record.UpdateId, ["kind"] = record.Kind.ToString(),
                ["nodeIndex"] = record.RuntimeNodeIndex,
                ["nodeId"] = ids != null && ids.TryGetValue(record.RuntimeNodeIndex, out var id) ? id : null,
                ["status"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.Status) != 0 ? record.Status.ToString() : null,
                ["exitReason"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.ExitReason) != 0 ? record.ExitReason.ToString() : null,
                ["abortReason"] = (record.OptionalFields & NativeTraceOptionalFieldsV1.AbortReason) != 0 ? record.AbortReason.ToString() : null,
                ["diagnosticCodeNumber"] = record.DiagnosticCodeNumber,
            }));
            return result;
        }

        private static ProductionTreeHost[] FindHosts() => UnityEngine.Object.FindObjectsByType<ProductionTreeHost>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        private static JObject HostInfo(ProductionTreeHost host) => new JObject
        {
            ["instanceId"] = host.InstanceId, ["objectName"] = host.name, ["scenePath"] = host.gameObject.scene.path,
            ["active"] = host.isActiveAndEnabled, ["rootStatus"] = host.LastRootResult?.ToString(), ["failure"] = host.LastFailure.Code.ToString(),
            ["updates"] = host.TotalUpdates, ["programHash"] = host.Program?.Header.CompiledContentHash.HexadecimalValue,
            ["traceState"] = host.TraceChannelOwner?.State.ToString(),
            ["sourceSemanticHash"] = host.Program?.Header.CanonicalSemanticHash.HexadecimalValue,
        };

        private static McpToolException Invalid(string message) => new McpToolException(McpAuthoringDiagnostics.MalformedArguments, message);
    }
}
