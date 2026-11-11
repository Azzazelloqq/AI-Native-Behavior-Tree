// Runtime-only smoke component for the disposable consumer project.
using System;
using System.Collections;
using System.IO;
using AIBT;
using UnityEngine;

public sealed class AgentPlayerSmoke : MonoBehaviour
{
    public float TargetX;
    public float Radius;
    public string ExpectedProgramHash;

    [Serializable]
    private sealed class Report
    {
        public bool success;
        public string rootStatus;
        public string failure;
        public string programHash;
        public float x;
    }

    private IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        var started = Time.realtimeSinceStartup;
        var agent = GameObject.Find("First Agent");
        ProductionTreeHost host = null;
        while (Time.realtimeSinceStartup - started < 15f)
        {
            host = agent.GetComponent<ProductionTreeHost>();
            if (host != null && (host.LastRootResult.HasValue || host.LastFailure.Code != NativeRuntimeDiagnosticCodeV1.None)) break;
            yield return null;
        }
        var report = new Report { rootStatus = host?.LastRootResult?.ToString(), failure = host?.LastFailure.Code.ToString(),
            programHash = host?.Program?.Header.CompiledContentHash.HexadecimalValue, x = agent.transform.position.x };
        report.success = report.rootStatus == "Success" && report.failure == "None" && report.programHash == ExpectedProgramHash
            && Mathf.Abs(report.x - TargetX) <= Radius + 0.0001f;
        var json = JsonUtility.ToJson(report);
        var arguments = Environment.GetCommandLineArgs();
        for (var index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index] == "-aibtReport") File.WriteAllText(arguments[index + 1], json);
        Debug.Log("AIBT_AGENT_PLAYER|" + json);
        Application.Quit(report.success ? 0 : 1);
    }
}
