using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AIBT.PatrolResearch
{
    public sealed class ResearchScene : MonoBehaviour
    {
        public ResearchActor[] Actors;
        public Camera View;
        public TextMesh Status;
        public LineRenderer Sight;
        private ProductionTreeWorld _world;
        private readonly Report _report = new Report();
        private readonly HashSet<int> _captured = new HashSet<int>();
        private string _output;
        private float _sampleAt;
        private bool _probing, _finished;
        private int _captureMode;
        private Vector3 _wanderStart;

        [Serializable] public sealed class Event
        {
            public float time; public string actor, mode; public int target;
            public float distance; public Vector3 position;
        }
        [Serializable] public sealed class Snapshot
        {
            public float time; public Vector3 patrol, runner, wander;
            public string mode; public bool alarm; public int target; public float distance; public string[] roots, failures;
        }
        [Serializable] public sealed class Report
        {
            public string unity, worldFailure;
            public bool completedCycle, alarmConsistent = true, allHostsRunning = true;
            public float elapsed, wanderDistance;
            public int wanderArrivals;
            public List<Event> events = new List<Event>();
            public List<Snapshot> samples = new List<Snapshot>();
        }

        private void Start()
        {
            _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/PatrolEvidence"));
            _probing = File.Exists(Path.Combine(Application.dataPath, "../Temp/research-probe.request"));
            if (_probing) Directory.CreateDirectory(_output);
            _world = gameObject.AddComponent<ProductionTreeWorld>();
            foreach (var actor in Actors) actor.Initialize(_world, this);
            _wanderStart = Actors[2].transform.position;
            _report.unity = Application.unityVersion;
        }

        public void Record(ResearchActor actor, int previous, int mode)
        {
            if (_probing) _report.events.Add(new Event { time = Time.timeSinceLevelLoad, actor = actor.Kind.ToString(), mode = actor.ModeName,
                target = actor.PursuedTarget, distance = actor.PursuitDistance, position = actor.transform.position });
            if (actor.Kind != ResearchKind.Patroller) return;
            if (previous == 3 && mode == 1) { _report.completedCycle = true; if (!_captured.Contains(4)) _captureMode = 4; }
            else if (!_captured.Contains(mode)) _captureMode = mode;
        }

        private void LateUpdate()
        {
            if (_world == null || _finished) return;
            var patrol = Actors[0];
            Status.text = "AIBT / PATROL RESEARCH\n" + patrol.ModeName + "   |   " + Time.timeSinceLevelLoad.ToString("0.0") + " s\n"
                + "BLUE: patrol  /  ORANGE: runner  /  GREEN: random walk\nSight 5.2 m / 130 deg    Give up: 9 m";
            foreach (var actor in Actors)
            {
                actor.Label.text = actor.Kind.ToString().ToUpperInvariant() + "\n" + actor.ModeName;
                actor.Label.transform.rotation = View.transform.rotation;
                if (actor.Alert != null) actor.Alert.transform.rotation = View.transform.rotation;
                _report.allHostsRunning &= actor.Host != null && actor.Host.LastFailure.Code == NativeRuntimeDiagnosticCodeV1.None
                    && actor.Host.LastRootResult == null && (actor.Host.TotalUpdates > 0 || Time.timeSinceLevelLoad < 1f);
            }
            _report.alarmConsistent &= patrol.AlarmShown == (patrol.Mode == 2);
            Sight.positionCount = 34;
            var origin = patrol.transform.position; origin.y = 0.06f;
            Sight.SetPosition(0, origin);
            for (var i = 0; i <= 31; i++)
                Sight.SetPosition(i + 1, origin + Quaternion.AngleAxis(-65f + 130f * i / 31f, Vector3.up) * patrol.transform.forward * patrol.SightRange);
            Sight.SetPosition(33, origin);
            if (_probing && Time.timeSinceLevelLoad >= _sampleAt)
            {
                _sampleAt += 1f;
                _report.samples.Add(new Snapshot { time = Time.timeSinceLevelLoad, patrol = patrol.transform.position,
                    runner = Actors[1].transform.position, wander = Actors[2].transform.position, mode = patrol.ModeName,
                    alarm = patrol.AlarmShown, target = patrol.PursuedTarget, distance = patrol.PursuitDistance,
                    roots = Array.ConvertAll(Actors, a => a.Host.LastRootResult?.ToString() ?? "NoTerminalResult; updates=" + a.Host.TotalUpdates),
                    failures = Array.ConvertAll(Actors, a => a.Host.LastFailure.Code.ToString()) });
            }
            if (_probing && _captureMode != 0)
            {
                Capture(_captureMode); _captured.Add(_captureMode); _captureMode = 0;
            }
            if (_probing && Time.timeSinceLevelLoad >= 45f) Finish();
        }

        private void Capture(int mode)
        {
            var texture = new RenderTexture(1600, 1000, 24);
            var previous = RenderTexture.active;
            var target = View.targetTexture;
            var aspect = View.aspect;
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            View.targetTexture = texture; View.aspect = 1.6f; View.Render(); RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(_output, "scene-" + mode + ".png"), image.EncodeToPNG());
            View.targetTexture = target; View.aspect = aspect; RenderTexture.active = previous;
            Destroy(image); texture.Release(); Destroy(texture);
        }

        private void Finish()
        {
            _finished = true;
            _report.elapsed = Time.timeSinceLevelLoad;
            _report.worldFailure = _world.LastWorldFailure.Code.ToString();
            _report.wanderDistance = Vector3.Distance(_wanderStart, Actors[2].transform.position);
            _report.wanderArrivals = Actors[2].Arrivals;
            File.WriteAllText(Path.Combine(_output, "report.json"), JsonUtility.ToJson(_report, true));
            File.Delete(Path.Combine(Application.dataPath, "../Temp/research-probe.request"));
            Debug.Log("PATROL_RESEARCH_COMPLETE " + _report.completedCycle);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
