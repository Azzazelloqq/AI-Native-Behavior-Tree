using UnityEngine;

namespace AIBT.Samples.NativeGuard
{
    /// <summary>
    /// Game-side half of the Native Guard sample. The tree is built only from package nodes
    /// (aibt.core.* and aibt.native.*); this component writes facts (where the intruder is, whether
    /// the gate is open) and turns aibt.native.signal codes into something visible. The patrol's
    /// random choices (which spot to light, whether to whistle, how long to rest) come from the
    /// tree's Native/Random nodes, seeded by the agent's Random Seed and Random Stream Id.
    /// </summary>
    [RequireComponent(typeof(NativeTreeAgent))]
    public sealed class NativeGuard : MonoBehaviour
    {
        public const int Zap = 1, Flare = 2, OpenGate = 3, SweepAndClose = 4, LightA = 5, LightB = 6, LightC = 7, Whistle = 8;
        private static readonly string[] SpotNames = { "A", "B", "C" };

        [SerializeField] private Transform _intruder;
        [SerializeField] private Transform _gate;
        [SerializeField] private float _seenRange = 8f;
        [SerializeField] private float _closeRange = 3f;
        [SerializeField] private float _intruderSpeed = 2.5f;

        private NativeTreeAgent _agent;
        private Renderer _tower;
        private Transform _flare;
        private readonly Renderer[] _spots = new Renderer[3];
        private float _flash, _flareTime = -1f, _gateTarget, _direction = 1f, _spotLit;
        private bool _seen, _close, _gateOpen;
        private string _log = string.Empty, _choices = string.Empty;
        private int _patrols, _whistles;

        private void Awake()
        {
            _agent = GetComponent<NativeTreeAgent>();
            _tower = GetComponent<Renderer>();
            _agent.OnEffect("aibt.native.signal", (int code) => OnSignal(code));
        }

        private void OnSignal(int code)
        {
            _log = (code == Zap ? "1 zap" : code == Flare ? "2 flare" : code == OpenGate ? "3 open gate" : code == SweepAndClose ? "4 sweep + close"
                    : code >= LightA && code <= LightC ? code + " spot " + SpotNames[code - LightA] : code == Whistle ? "8 whistle" : code.ToString())
                + "   " + _log;
            if (_log.Length > 90) _log = _log.Substring(0, 90);
            switch (code)
            {
                case Zap: // knock the intruder back; it retreats and comes back later
                    Flash(Color.red);
                    _direction = Mathf.Sign(_intruder.position.x - transform.position.x);
                    _intruder.position += new Vector3(_direction * 4f, 0f, 0f);
                    break;
                case Flare: // a flare rises above the intruder
                    Flash(Color.yellow);
                    _flare.position = _intruder.position + Vector3.up;
                    _flareTime = 0f;
                    break;
                case OpenGate:
                    _gateTarget = 2.5f;
                    break;
                case SweepAndClose:
                    Flash(Color.cyan);
                    _gateTarget = 0f;
                    _gateOpen = false;
                    _agent.SetFact("gate-open", false);
                    break;
                case LightA:
                case LightB:
                case LightC: // the tree picked a spot with aibt.native.random-int
                    for (var spot = 0; spot < _spots.Length; spot++) _spots[spot].material.color = spot == code - LightA ? Color.cyan : Color.gray;
                    _spotLit = 1.5f;
                    _patrols++;
                    _choices += (_choices.Length == 0 ? "" : " ") + SpotNames[code - LightA];
                    break;
                case Whistle: // aibt.native.random-chance said yes
                    Flash(Color.green);
                    _whistles++;
                    _choices += "+w";
                    break;
            }
            if (_choices.Length > 60) _choices = _choices.Substring(_choices.Length - 60);
        }

        private void Start()
        {
            _flare = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            _flare.name = "Flare";
            _flare.localScale = Vector3.one * 0.6f;
            _flare.GetComponent<Renderer>().material.color = Color.yellow;
            _flare.gameObject.SetActive(false);
            for (var spot = 0; spot < _spots.Length; spot++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Spot " + SpotNames[spot];
                marker.transform.position = new Vector3(-7f + 7f * spot, 0.05f, 4.5f);
                marker.transform.localScale = new Vector3(2f, 0.05f, 2f);
                _spots[spot] = marker.GetComponent<Renderer>();
                _spots[spot].material.color = Color.gray;
            }
        }

        private void Update()
        {
            // The intruder walks back and forth past the tower.
            var position = _intruder.position + new Vector3(_direction * _intruderSpeed * Time.deltaTime, 0f, 0f);
            if (Mathf.Abs(position.x) > 12f) _direction = -Mathf.Sign(position.x);
            _intruder.position = new Vector3(Mathf.Clamp(position.x, -12f, 12f), position.y, position.z);

            var distance = Vector3.Distance(transform.position, _intruder.position);
            _seen = distance < _seenRange;
            _close = distance < _closeRange;
            _agent.SetFact("intruder-seen", _seen);
            _agent.SetFact("intruder-close", _close);

            var gate = _gate.localPosition;
            gate.y = Mathf.MoveTowards(gate.y, _gateTarget, 2.5f * Time.deltaTime);
            _gate.localPosition = gate;
            if (_gateTarget > 0f && !_gateOpen && Mathf.Approximately(gate.y, _gateTarget))
            {
                _gateOpen = true;
                _agent.SetFact("gate-open", true);
            }

            _flash = Mathf.Max(0f, _flash - Time.deltaTime);
            if (_flash <= 0f) _tower.material.color = Color.gray;
            _spotLit = Mathf.Max(0f, _spotLit - Time.deltaTime);
            if (_spotLit <= 0f) foreach (var spot in _spots) if (spot != null) spot.material.color = Color.gray;
            _flare.gameObject.SetActive(_flareTime >= 0f && _flareTime < 1f);
            if (_flareTime >= 0f) { _flareTime += Time.deltaTime; _flare.position += Vector3.up * (2f * Time.deltaTime); }
        }

        private void Flash(Color color)
        {
            _tower.material.color = color;
            _flash = 0.4f;
        }

        private void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(16, 12, 1400, 34), "Signals (newest first): " + _log, style);
            GUI.Label(new Rect(16, 44, 1400, 34), "Facts: intruder-seen=" + _seen + "  intruder-close=" + _close + "  gate-open=" + _gateOpen, style);
            GUI.Label(new Rect(16, 76, 1400, 34), "Random Seed " + _agent.RandomSeed + ", stream id " + _agent.RandomStreamId + ", run " + _agent.RunIndex + ": spots " + _choices + "   (" + _patrols + " patrols, " + _whistles + " whistles)", style);
        }
    }
}
