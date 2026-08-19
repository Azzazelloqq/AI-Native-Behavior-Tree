using System;
using System.Collections.Generic;

namespace AIBT.Editor.Validation
{
    /// <summary>
    /// Document-level view over a <see cref="DiagnosticCollection"/>: per-severity counts and a
    /// classified marker per diagnostic, ready for a graph editor to render. Always rebuilt fresh
    /// from the current diagnostics (never cached/mutated in place) -- fixing the underlying issue
    /// and recomputing diagnostics naturally drops the stale marker, satisfying this card's "clears
    /// without requiring a manual refresh action" acceptance criterion by construction.
    /// </summary>
    public sealed class DiagnosticGraphSummary
    {
        private readonly List<DiagnosticGraphLocation> _markers;

        private DiagnosticGraphSummary(List<DiagnosticGraphLocation> markers, int errorCount, int warningCount, int infoCount)
        {
            _markers = markers;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            InfoCount = infoCount;
        }

        public IReadOnlyList<DiagnosticGraphLocation> Markers => _markers;

        public int ErrorCount { get; }

        public int WarningCount { get; }

        public int InfoCount { get; }

        public int TotalCount => ErrorCount + WarningCount + InfoCount;

        public static DiagnosticGraphSummary Build(DiagnosticCollection diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var markers = new List<DiagnosticGraphLocation>(diagnostics.Count);
            var errorCount = 0;
            var warningCount = 0;
            var infoCount = 0;

            foreach (var diagnostic in diagnostics)
            {
                markers.Add(DiagnosticGraphLocation.Resolve(diagnostic));
                switch (diagnostic.Severity)
                {
                    case DiagnosticSeverity.Error:
                        errorCount++;
                        break;
                    case DiagnosticSeverity.Warning:
                        warningCount++;
                        break;
                    case DiagnosticSeverity.Info:
                        infoCount++;
                        break;
                }
            }

            return new DiagnosticGraphSummary(markers, errorCount, warningCount, infoCount);
        }

        /// <summary>Nodes referenced by at least one marker, in first-seen order -- the "jump-to-node" list.</summary>
        public IEnumerable<NodeId> NodesWithMarkers()
        {
            var seen = new HashSet<NodeId>();
            foreach (var marker in _markers)
            {
                if (marker.Kind == DiagnosticGraphLocationKind.Document)
                {
                    continue;
                }

                if (seen.Add(marker.NodeId))
                {
                    yield return marker.NodeId;
                }
            }
        }
    }
}
