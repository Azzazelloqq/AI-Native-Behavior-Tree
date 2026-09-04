using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AIBT.Mcp.Authoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AIBT.Mcp.NodeDevelopment
{
    internal enum CompileAttemptAction { None, Import, Compile }

    // One record for the existing single staging slot. File operations are safe from the TCP
    // thread; Unity APIs live exclusively in the Editor hook below.
    internal sealed class CompileAttemptStore
    {
        private static readonly object Gate = new object();
        private readonly string _assets;
        private readonly string _session;
        private readonly string _path;
        internal static string EditorSession
        {
            get
            {
                using (var process = Process.GetCurrentProcess())
                    return process.Id + ":" + process.StartTime.ToUniversalTime().Ticks;
            }
        }

        internal CompileAttemptStore(string assets, string session)
        {
            _assets = assets;
            _session = session;
            _path = Path.Combine(Path.GetFullPath(Path.Combine(assets, "..")), "Library", "AIBT", "node-compile-attempt.json");
        }

        internal JObject Start()
        {
            lock (Gate)
            {
                var state = new Attempt { Id = Guid.NewGuid().ToString("N"), Session = _session,
                    Hash = StagingSlot.ComputeContentHash(_assets), Phase = "queued" };
                Save(state);
                return new JObject { ["status"] = "pending", ["attemptId"] = state.Id };
            }
        }

        internal JObject Check(string id)
        {
            lock (Gate)
            {
                var state = Load();
                if (state == null || state.Session != _session || state.Id != id)
                    throw new McpToolException(McpNodeDevelopmentDiagnostics.CompileNotObserved,
                        "Unknown, superseded or expired compile attempt. Call start again.");
                if (state.Hash != StagingSlot.ComputeContentHash(_assets))
                    throw new McpToolException(McpNodeDevelopmentDiagnostics.NoPendingGeneration,
                        "Staged content changed after start. Call start again.");
                var result = new JObject { ["attemptId"] = id };
                if (state.Phase == "compiled")
                {
                    result["status"] = "compiled";
                    result["contentHash"] = state.Hash;
                }
                else if (state.Phase == "failed")
                {
                    result["status"] = "failed";
                    result["diagnostics"] = new JArray(state.Errors);
                }
                else result["status"] = state.Phase == "queued" || state.Phase == "importing" ? "pending" : "still-compiling";
                if (!string.IsNullOrEmpty(state.LogWarning)) result["logWarning"] = state.LogWarning;
                return result;
            }
        }

        internal CompileAttemptAction Advance(bool busy, string domain, string logPath)
        {
            lock (Gate)
            {
                var state = Load();
                if (state == null || state.Session != _session || state.Phase == "compiled" || state.Phase == "failed")
                    return CompileAttemptAction.None;
                if (!CheckHash(state)) return CompileAttemptAction.None;
                if (state.Phase == "awaiting-reload")
                {
                    if (!busy && state.Domain != domain) { state.Phase = "compiled"; Save(state); }
                    return CompileAttemptAction.None;
                }
                if ((state.Phase == "requested" || state.Phase == "compiling") && state.Domain != domain)
                {
                    Fail(state, "Domain reload interrupted compile observation. Call start again.");
                    return CompileAttemptAction.None;
                }
                if (busy) return CompileAttemptAction.None;
                if (state.Phase == "queued")
                {
                    state.Phase = "importing"; Save(state);
                    return CompileAttemptAction.Import;
                }
                if (state.Phase == "importing")
                {
                    state.Phase = "requested"; state.Domain = domain; state.LogPath = logPath;
                    state.LogPosition = EditorLogCompileWatcher.Capture(logPath, out state.LogWarning);
                    Save(state);
                    return CompileAttemptAction.Compile;
                }
                return CompileAttemptAction.None;
            }
        }

        internal void CompilationStarted()
        {
            lock (Gate)
            {
                var state = Load();
                if (state == null || state.Session != _session || state.Phase != "requested" || !CheckHash(state)) return;
                state.Phase = "compiling"; Save(state);
            }
        }

        internal void AssemblyFinished(string assemblyPath, IEnumerable<string> errors, bool rebuilt = true)
        {
            lock (Gate)
            {
                var state = Load();
                if (state == null || state.Session != _session || state.Phase != "compiling") return;
                state.Assemblies.Add(Path.GetFileNameWithoutExtension(assemblyPath));
                state.ReloadRequired |= rebuilt;
                state.Errors.AddRange(errors); Save(state);
            }
        }

        internal void CompilationFinished()
        {
            lock (Gate)
            {
                var state = Load();
                if (state == null || state.Session != _session || state.Phase != "compiling" || !CheckHash(state)) return;
                var tail = EditorLogCompileWatcher.ReadTail(state.LogPath, state.LogPosition, out var warning);
                if (warning != null) state.LogWarning = warning;
                if (EditorLogCompileWatcher.HasFailureMarker(tail))
                    state.Errors.AddRange(tail.Split('\n').Where(EditorLogCompileWatcher.HasFailureMarker));
                if (!state.Assemblies.Contains("AIBT.Generated.Staging") || !state.Assemblies.Contains("AIBT.Generated.Staging.Catalog"))
                    state.Errors.Add("Requested compilation did not verify both staging assemblies. Call start again.");
                state.Phase = state.Errors.Count != 0 ? "failed" : state.ReloadRequired ? "awaiting-reload" : "compiled";
                Save(state);
            }
        }

        internal void FailCurrent(string message)
        {
            lock (Gate)
            {
                var state = Load();
                if (state != null && state.Session == _session) Fail(state, message);
            }
        }

        private bool CheckHash(Attempt state)
        {
            if (state.Hash == StagingSlot.ComputeContentHash(_assets)) return true;
            Fail(state, "Staged content changed during compilation. Call start again.");
            return false;
        }

        private void Fail(Attempt state, string error)
        {
            state.Phase = "failed"; state.Errors.Add(error); Save(state);
        }

        private Attempt Load()
        {
            if (!File.Exists(_path)) return null;
            try { return JsonConvert.DeserializeObject<Attempt>(File.ReadAllText(_path)); }
            catch (JsonException)
            {
                throw new McpToolException(McpNodeDevelopmentDiagnostics.CompileNotObserved,
                    "Compile attempt state is unreadable. Call start again.");
            }
        }

        private void Save(Attempt state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(state));
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }

        private sealed class Attempt
        {
            public string Id, Session, Hash, Phase, Domain, LogPath, LogWarning;
            public long LogPosition;
            public bool ReloadRequired;
            public List<string> Errors = new List<string>();
            public List<string> Assemblies = new List<string>();
        }
    }

    [InitializeOnLoad]
    internal static class NodeCompileEditorHook
    {
        private static readonly string Domain = Guid.NewGuid().ToString("N");
        private static readonly CompileAttemptStore Store = new CompileAttemptStore(Application.dataPath, CompileAttemptStore.EditorSession);
        private static double _nextPoll;

        static NodeCompileEditorHook()
        {
            EditorApplication.update += Pump;
            CompilationPipeline.compilationStarted += _ => Store.CompilationStarted();
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
                Store.AssemblyFinished(path, messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message));
            CompilationPipeline.assemblyCompilationNotRequired += path =>
                Store.AssemblyFinished(path, Array.Empty<string>(), rebuilt: false);
            CompilationPipeline.compilationFinished += _ => Store.CompilationFinished();
        }

        private static void Pump()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 0.25;
            try
            {
                var action = Store.Advance(EditorApplication.isCompiling || EditorApplication.isUpdating, Domain, Application.consoleLogPath);
                if (action == CompileAttemptAction.Import) AssetDatabase.Refresh();
                // Request compiler verification even when Auto Refresh already built these files.
                // Unity explicitly reports up-to-date assemblies via assemblyCompilationNotRequired.
                if (action == CompileAttemptAction.Compile)
                    CompilationPipeline.RequestScriptCompilation();
            }
            catch (Exception exception)
            {
                // An inaccessible state file cannot be repaired by repeatedly logging from Update.
                // The next explicit start/check reports the underlying IO failure to the caller.
                try { Store.FailCurrent(exception.Message); } catch (Exception) { }
            }
        }
    }
}
