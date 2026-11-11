// Copy only into a disposable consumer project's Assets/CliProbe/Editor directory.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AIBT;
using AIBT.Editor.Graph;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AgentConsumerSession
{
    private static readonly BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static double _next;
    private static ulong _attached;
    private static bool _graphAttached;
    private static string Library => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library"));

    static AgentConsumerSession() => EditorApplication.update += Pump;

    public static void Open()
    {
        EditorSceneManager.OpenScene("Assets/AibtFirstAgentDemo/FirstAgent.unity");
        foreach (var component in Resources.FindObjectsOfTypeAll<MonoBehaviour>().Where(component => component != null && component.GetType().Name == "ConsumerProbe" && component.gameObject.scene.IsValid()))
            component.gameObject.SetActive(false);
        var editor = typeof(BehaviorTreeGraphWindow).Assembly;
        var catalogType = editor.GetType("AIBT.Editor.Registry.ProjectNodeCatalog");
        var cache = editor.GetType("AIBT.Editor.Registry.ProjectNodeCatalogCache");
        cache.GetField("_current", HiddenStatic).SetValue(null, catalogType.GetMethod("Initial", HiddenStatic).Invoke(null, null));
        var window = BehaviorTreeGraphWindow.ShowWindow();
        window.OpenFromPath("Assets/FirstAgent/first-agent.aibt.json");
        var before = CanCompile(window);
        cache.GetMethod("Refresh", HiddenStatic).Invoke(null, null);
        var after = CanCompile(window);
        var mcp = typeof(AIBT.Mcp.McpBridgeWindow).Assembly;
        mcp.GetType("AIBT.Mcp.Documentation.McpDocumentationRegenerateCommand").GetMethod("Regenerate", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        Write("agent-preflight.json", new JObject { ["initialNativeGraphValid"] = before, ["refreshedNativeGraphValid"] = after,
            ["projectDocsGenerated"] = File.Exists(Path.Combine(Application.dataPath, "AIBT-Documentation/node-catalog.md")) });
    }

    private static bool CanCompile(BehaviorTreeGraphWindow window)
    {
        var session = typeof(BehaviorTreeGraphWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        return (bool)session.GetType().GetProperty("CanCompile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
    }

    private static void Pump()
    {
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.1;
        var play = Path.Combine(Library, "agent-play.request");
        if (File.Exists(play) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(play);
            var config = JObject.Parse(File.ReadAllText(Path.Combine(Library, "agent-case.json")));
            SessionState.SetString("AgentConsumerRunId", (string)config["runId"] ?? "");
            var agent = GameObject.Find("First Agent");
            var component = agent.GetComponents<MonoBehaviour>().Single(value => value.GetType().Name == "FirstAgent");
            var asset = AssetDatabase.LoadAssetAtPath<ProductionTreeAsset>((string)config["asset"]);
            var serialized = new SerializedObject(component);
            serialized.FindProperty("_tree").objectReferenceValue = asset;
            serialized.FindProperty("_targetX").floatValue = (float)config["target"];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            agent.transform.position = Vector3.zero;
            GameObject.Find("Goal").transform.position = new Vector3((float)config["target"], 0f, 0f);
            var catalog = TypeCache.GetTypesWithAttribute<AIBT.Burst.AibtCatalogSetAttribute>().Single(type => type.FullName == (string)config["catalog"]);
            var factory = (ProductionTreeCatalogFactory)Delegate.CreateDelegate(typeof(ProductionTreeCatalogFactory), catalog.GetMethod("TryCreateRuntimeCatalog", BindingFlags.Static | BindingFlags.Public));
            if (!asset.TryCreateProgram(factory, out var program, out var failure)) throw new InvalidOperationException(failure.ToString());
            config["expectedProgramHash"] = program.Program.Header.CompiledContentHash.HexadecimalValue;
            program.Dispose();
            Write("agent-case.json", config);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            BehaviorTreeGraphWindow.ShowWindow().OpenFromPath("Assets/FirstAgent/first-agent.aibt.json");
            Time.timeScale = 1f;
            EditorApplication.isPlaying = true;
        }
        if (EditorApplication.isPlaying)
        {
            var agent = GameObject.Find("First Agent");
            var host = agent != null ? agent.GetComponent<ProductionTreeHost>() : null;
            if (host != null && host.InstanceId != 0)
            {
                if (_attached != host.InstanceId)
                {
                    _attached = host.InstanceId;
                    var arguments = new object[] { host, null };
                    _graphAttached = (bool)typeof(BehaviorTreeGraphWindow).GetMethod("TryAttachHost", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(BehaviorTreeGraphWindow.ShowWindow(), arguments);
                }
                Write("agent-play-state.json", new JObject { ["runId"] = SessionState.GetString("AgentConsumerRunId", ""), ["instanceId"] = host.InstanceId, ["x"] = agent.transform.position.x,
                    ["rootStatus"] = host.LastRootResult?.ToString(), ["failure"] = host.LastFailure.Code.ToString(),
                    ["programHash"] = host.Program?.Header.CompiledContentHash.HexadecimalValue, ["graphAttached"] = _graphAttached });
            }
        }
        var stop = Path.Combine(Library, "agent-stop.request");
        if (File.Exists(stop)) { File.Delete(stop); EditorApplication.isPlaying = false; }
        var quit = Path.Combine(Library, "agent-quit.request");
        if (File.Exists(quit)) { File.Delete(quit); EditorApplication.Exit(0); }
    }

    private static void Write(string file, JObject content)
    {
        var path = Path.Combine(Library, file);
        try
        {
            File.WriteAllText(path + ".tmp", content.ToString());
            if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
            else File.Move(path + ".tmp", path);
        }
        catch (IOException) { /* A short-lived verifier reader can prevent Windows replacement; retry on the next observation. */ }
    }
}
