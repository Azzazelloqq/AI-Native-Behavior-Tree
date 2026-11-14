using UnityEngine;

namespace AIBT.Samples.FirstAgent
{
    /// <summary>
    /// Game-side half of the sample: it tells the tree how far the goal is (a fact) and moves when
    /// the tree asks (an effect). <see cref="NativeTreeAgent"/> owns the program, host and world.
    /// </summary>
    [RequireComponent(typeof(NativeTreeAgent))]
    public sealed class FirstAgent : MonoBehaviour
    {
        [SerializeField] private Transform _goal;
        private NativeTreeAgent _agent;

        private void Awake()
        {
            _agent = GetComponent<NativeTreeAgent>();
            _agent.OnEffect("move-speed", (float speed) =>
                transform.position = Vector3.MoveTowards(transform.position, _goal.position, speed * Time.deltaTime));
            _agent.Completed += result =>
            {
                if (result != NodeStatus.Success) return;
                _goal.position = Vector3.Scale(_goal.position, new Vector3(-1f, 1f, 1f)); // reached: send the goal to the other side
                ReportDistance();
            };
        }

        private void Update() => ReportDistance();

        private void ReportDistance() => _agent.SetFact("distance", Vector3.Distance(transform.position, _goal.position));
    }
}
