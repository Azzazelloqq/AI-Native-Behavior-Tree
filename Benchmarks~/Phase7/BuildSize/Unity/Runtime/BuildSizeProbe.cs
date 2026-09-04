using System;
using System.IO;
using System.Linq;
using System.Text;
using AIBT.Authoring;
using UnityEngine;

namespace AIBT.Benchmarks.BuildSize
{
    public static class BuildSizeProbe
    {
        public static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException(name);
            return args[index + 1];
        }

        public static string Validate(TextAsset[] trees)
        {
            var registry = NodeRegistryBuilder.CreateWithBuiltIns().Build().Registry;
            var ids = new System.Collections.Generic.HashSet<string>();
            var hashInput = new StringBuilder();
            foreach (var tree in trees.OrderBy(t => t.name, StringComparer.Ordinal))
            {
                var read = CanonicalTreeJson.Parse(tree.bytes);
                if (!read.Success) throw new InvalidOperationException("Invalid tree " + tree.name);
                if (!ids.Add(read.Document.TreeId.Value)) throw new InvalidOperationException("Duplicate tree ID");
                var compiled = ReferenceCompiler.Compile(read.Document, registry,
                    new ReferenceCompilerOptions(tree.name, ReferenceCompilationPolicy.Phase1, new CompiledCompilerVersion(1, 0, 0, 0)));
                if (!compiled.Success) throw new InvalidOperationException("Compilation failed: " + tree.name + " " + string.Join(";", compiled.Diagnostics.Select(d => d.Message)));
                hashInput.Append(tree.name).Append('\n').Append(tree.text).Append('\n');
            }
            return StableHash.Sha256Hex(hashInput.ToString());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            try
            {
                var trees = Resources.LoadAll<TextAsset>("Trees");
                var expected = int.Parse(Argument("-expectedTrees"));
                if (trees.Length != expected) throw new InvalidOperationException("Wrong shipped tree count: " + trees.Length);
                var fingerprint = AIBT.Authoring.BuiltInLeaves.BuiltInLeafCatalog.Fingerprint.Value;
                var result = new ProbeResult { trees = trees.Length, payloadBytes = trees.Sum(t => (long)t.bytes.Length),
                    contentHash = Validate(trees), unityVersion = Application.unityVersion,
                    catalogFingerprint = string.Concat(new[] { fingerprint.Word0, fingerprint.Word1, fingerprint.Word2,
                        fingerprint.Word3, fingerprint.Word4, fingerprint.Word5, fingerprint.Word6, fingerprint.Word7 }
                        .Select(word => word.ToString("x8"))) };
                File.WriteAllText(Argument("-probeResult"), JsonUtility.ToJson(result, true));
                Debug.Log("AIBT_BUILD_SIZE_OK|" + result.contentHash);
                Application.Quit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Application.Quit(1); }
        }

        [Serializable] private sealed class ProbeResult
        {
            public int trees;
            public long payloadBytes;
            public string contentHash, unityVersion, catalogFingerprint;
        }
    }
}
