using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    /// <summary>The waypoints a guard walks, in order.</summary>
    public sealed class PatrolRoute : MonoBehaviour
    {
        public Vector3[] points = new Vector3[0];

        private int _next;

        public int Count => points.Length;

        public Vector3 Next()
        {
            var point = points[_next];
            _next = (_next + 1) % points.Length;
            return point;
        }
    }
}
