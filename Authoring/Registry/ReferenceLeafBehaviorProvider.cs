namespace AIBT.Authoring
{
    /// <summary>Provides a project leaf contract and creates independent reference behaviors.</summary>
    public interface IReferenceLeafBehaviorProvider
    {
        /// <summary>The stable contract for this leaf type.</summary>
        NodeManifest Manifest { get; }

        /// <summary>Creates a fresh behavior instance for one node in one reference tree instance.</summary>
        IReferenceLeafBehavior CreateBehavior();
    }
}
