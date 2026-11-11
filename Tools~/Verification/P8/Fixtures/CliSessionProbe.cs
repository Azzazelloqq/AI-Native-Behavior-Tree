// Copy only into a disposable project's Assets/CliProbe/Editor directory.
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CliSessionProbe
{
    static CliSessionProbe()
    {
        var count = SessionState.GetInt("CliProbeReloads", 0) + 1;
        SessionState.SetInt("CliProbeReloads", count);
        File.WriteAllText(Path.Combine(Application.dataPath, "../Library/cli-probe-loaded.txt"), count.ToString());
        EditorApplication.update += Pump;
    }

    public static void Open() => Debug.Log("CLI_PROBE_READY");

    private static void Pump()
    {
        var library = Path.Combine(Application.dataPath, "../Library");
        var reload = Path.Combine(library, "cli-probe-reload.request");
        if (File.Exists(reload)) { File.Delete(reload); EditorUtility.RequestScriptReload(); }
        var quit = Path.Combine(library, "cli-probe-quit.request");
        if (File.Exists(quit)) { File.Delete(quit); EditorApplication.Exit(0); }
    }
}
