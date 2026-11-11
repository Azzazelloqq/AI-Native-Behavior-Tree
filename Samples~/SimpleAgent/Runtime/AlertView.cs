using UnityEngine;

namespace AIBT.Samples.SimpleAgent
{
    /// <summary>Toggles the marker shown while the guard chases.</summary>
    public sealed class AlertView : MonoBehaviour
    {
        public GameObject marker;

        public bool IsShown => marker != null && marker.activeSelf;

        public void Show(bool visible)
        {
            if (marker != null) marker.SetActive(visible);
        }
    }
}
