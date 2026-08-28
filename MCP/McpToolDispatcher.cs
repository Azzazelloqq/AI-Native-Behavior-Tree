using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using Newtonsoft.Json.Linq;

namespace AIBT.Mcp
{
    /// <summary>
    /// Dispatches one relayed tool request from the external MCP~/Server/ process to the real
    /// AIBT.Authoring query layer (P6-003), enforcing McpPermissionEnforcer first. The external
    /// server holds no logic of its own -- this is where every real decision is made, and where
    /// it is tested.
    /// </summary>
    public static class McpToolDispatcher
    {
        public static string Dispatch(string requestLine, string projectRoot)
        {
            JObject request;
            try
            {
                request = JObject.Parse(requestLine);
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                return Error(McpDiagnostics.UnknownTool.Value, "Malformed request: " + ex.Message);
            }

            var tool = (string)request["tool"];
            var args = request["args"] as JObject ?? new JObject();
            var granted = ParseGranted(request["grantedCategories"] as JArray);

            switch (tool)
            {
                case "get_project_manifest":
                    return WithPermission(granted, McpPermissionCategory.Read, () => GetProjectManifest(projectRoot));
                case "search_nodes":
                    return WithPermission(granted, McpPermissionCategory.Read, () => SearchNodes(args));
                case "get_node_contract":
                    return WithPermission(granted, McpPermissionCategory.Read, () => GetNodeContract(args));
                case "get_static_resource":
                    return WithPermission(granted, McpPermissionCategory.Read, () => GetStaticResource(projectRoot, args));
                default:
                    return Error(McpDiagnostics.UnknownTool.Value, "Unknown tool: " + tool);
            }
        }

        private static string WithPermission(HashSet<McpPermissionCategory> granted, McpPermissionCategory required, Func<JObject> handler)
        {
            if (!McpPermissionEnforcer.Require(granted, required, out var denial))
            {
                return Error(denial.Code.Value, denial.Message);
            }

            return Result(handler());
        }

        private static JObject GetProjectManifest(string projectRoot)
        {
            var registryResult = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            var scan = AibtTreeDiscovery.Scan(projectRoot);

            ProjectPolicySnapshot policy;
            var policyPath = System.IO.Path.Combine(ProjectRootParent(projectRoot), ".aibt", "policy.json");
            if (!ProjectPolicySnapshot.TryReadFile(policyPath, out policy, out var policyError))
            {
                policy = null;
            }

            var manifestJson = policy != null
                ? new ProjectManifestQuery(registryResult.Registry, policy).Build(scan.Trees)
                : new JObject
                {
                    ["format"] = "aibt-project-manifest",
                    ["formatVersion"] = 1,
                    ["error"] = "Project policy could not be read: " + policyPath,
                };

            manifestJson["skippedTreeFiles"] = new JArray(scan.SkippedFiles);
            return manifestJson;
        }

        private static JObject SearchNodes(JObject args)
        {
            var registryResult = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            var query = new NodeCatalogQuery(registryResult.Registry);
            var keyword = (string)args["keyword"];
            var offset = (int?)args["offset"] ?? 0;
            var count = (int?)args["count"] ?? 50;

            var matches = query.Search(keyword);
            var page = matches.Skip(offset).Take(count).ToArray();
            var entries = new JArray();
            foreach (var entry in page)
            {
                if (query.TryGetContract(entry.Manifest.TypeId, out var contract))
                {
                    entries.Add(contract);
                }
            }

            return new JObject
            {
                ["totalCount"] = matches.Count,
                ["entries"] = entries,
            };
        }

        private static JObject GetNodeContract(JObject args)
        {
            var registryResult = NodeRegistryBuilder.CreateWithBuiltIns().Build();
            var query = new NodeCatalogQuery(registryResult.Registry);
            var typeId = (string)args["typeId"];

            return query.TryGetContract(typeId, out var contract)
                ? new JObject { ["found"] = true, ["manifest"] = contract }
                : new JObject { ["found"] = false };
        }

        // Fixed allowlist -- the request supplies only a key, never a path, so there is no
        // path-traversal surface. This assumes AIBT lives at "<Assets>/AIBT" (true for this
        // repository's own embedded-package layout; a real Package Manager registry install
        // would need a different root resolution -- disclosed as a known simplification, not
        // silently generalized).
        private static readonly Dictionary<string, string> StaticResourceAllowlist = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ai-and-mcp"] = "Documentation~/ai-and-mcp.md",
            ["schema.behavior-case"] = "Schemas~/behavior-case.schema.json",
            ["schema.layout"] = "Schemas~/layout.schema.json",
            ["schema.node-manifest"] = "Schemas~/node-manifest.schema.json",
            ["schema.policy"] = "Schemas~/policy.schema.json",
            ["schema.tree"] = "Schemas~/tree.schema.json",
            ["schema.work-item-index"] = "Schemas~/work-item-index.schema.json",
        };

        private static JObject GetStaticResource(string projectRoot, JObject args)
        {
            var key = (string)args["key"];
            if (key == null || !StaticResourceAllowlist.TryGetValue(key, out var relativePath))
            {
                return new JObject { ["found"] = false, ["availableKeys"] = new JArray(StaticResourceAllowlist.Keys) };
            }

            // projectRoot is Application.dataPath (".../Assets"); AIBT lives at "Assets/AIBT" in
            // this repository's embedded-package layout (unlike .aibt/policy.json, which is a
            // per-consuming-project file expected at the project root, sibling to Assets/).
            var fullPath = System.IO.Path.Combine(projectRoot, "AIBT", relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(fullPath))
            {
                return new JObject { ["found"] = false };
            }

            return new JObject { ["found"] = true, ["key"] = key, ["content"] = System.IO.File.ReadAllText(fullPath) };
        }

        private static string ProjectRootParent(string assetsPath)
        {
            return System.IO.Directory.GetParent(assetsPath)?.FullName ?? assetsPath;
        }

        private static HashSet<McpPermissionCategory> ParseGranted(JArray array)
        {
            var result = new HashSet<McpPermissionCategory>();
            if (array == null)
            {
                return result;
            }

            foreach (var token in array)
            {
                if (Enum.TryParse<McpPermissionCategory>((string)token, out var category))
                {
                    result.Add(category);
                }
            }

            return result;
        }

        private static string Result(JObject payload)
        {
            return new JObject { ["result"] = payload }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string Error(string code, string message)
        {
            return new JObject
            {
                ["error"] = new JObject { ["code"] = code, ["message"] = message },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
