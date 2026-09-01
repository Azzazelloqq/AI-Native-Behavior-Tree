namespace AIBT.Authoring
{
    // Pairs a project-authored leaf's NodeManifest with its public IReferenceLeafBehavior factory
    // (P7-008, applying ADR-P6-017). A project implements this once per custom leaf type with a
    // public parameterless constructor -- mirrors ICustomMcpToolProvider's own shape
    // (MCP/CustomTools/, P6-010), which MCP/Authoring/ProjectLeafExtensionDiscovery.cs discovers
    // via UnityEditor.TypeCache the same way.
    public interface IReferenceLeafBehaviorProvider
    {
        NodeManifest Manifest { get; }

        IReferenceLeafBehavior CreateBehavior();
    }
}
