using UnityEngine;

namespace AIBT.Samples.NativeGuard
{
    /// <summary>
    /// Game-side half of the Native Guard sample. The tree is built only from package nodes
    /// (aibt.core.* and aibt.native.*); this component writes facts (where the intruder is, whether
    /// the gate is open) and turns aibt.native.signal codes into something visible.
    /// </summary>
    [RequireComponent(typeof(NativeTreeAgent))]
    public sealed class NativeGuard : MonoBehaviour
    {
        public const int Zap = 1, Flare = 2, OpenGate = 3, SweepAndClose = 4;

        [SerializeField] private Transform _intruder;
        [SerializeField] private Transform _gate;
        [SerializeField] private float _seenRange = 8f;
        [SerializeField] private float _closeRange = 3f;
        [SerializeField] private float _intruderSpeed = 2.5f;

        private NativeTreeAgent _agent;
        private Renderer _tower;
        private Transform _flare;
        private float _flash, _flareTime = -1f, _gateTarget, _direction = 1f;
        private bool _seen, _close, _gateOpen;
        private string _log = string.Empty;

        private void Awake()
        {
            _agent = GetComponent<NativeTreeAgent>();
            _tower = GetComponent<Renderer>();
            _agent.OnEffect("aibt.native.signal", (int code) => OnSignal(code));
        }

        private void OnSignal(int code)
        {
            _log = (code == Zap ? "1 zap" : code == Flare ? "2 flare" : code == OpenGate ? "3 open gate" : code == SweepAndClose ? "4 sweep + close" : code.ToString())
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
            }
        }

        private void Start()
        {
            _flare = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            _flare.name = "Flare";
            _flare.localScale = Vector3.one * 0.6f;
            _flare.GetComponent<Renderer>().material.color = Color.yellow;
            _flare.gameObject.SetActive(false);
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
        }
    }
}
