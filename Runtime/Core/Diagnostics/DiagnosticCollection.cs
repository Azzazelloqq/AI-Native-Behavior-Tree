using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT
{
    public sealed class DiagnosticCollection : IReadOnlyList<Diagnostic>
    {
        private readonly ReadOnlyCollection<Diagnostic> _items;

        public DiagnosticCollection(IEnumerable<Diagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var sorted = new List<Diagnostic>();
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic == null)
                {
                    throw new ArgumentException("Diagnostic collections cannot contain null entries.", nameof(diagnostics));
                }

                sorted.Add(diagnostic);
            }

            sorted.Sort();
            var distinct = new List<Diagnostic>(sorted.Count);
            for (var index = 0; index < sorted.Count; index++)
            {
                if (index == 0 || !sorted[index].Equals(sorted[index - 1]))
                {
                    distinct.Add(sorted[index]);
                }
            }

            _items = distinct.AsReadOnly();
        }

        public static DiagnosticCollection Empty { get; } = new DiagnosticCollection(Array.Empty<Diagnostic>());

        public int Count => _items.Count;

        public Diagnostic this[int index] => _items[index];

        public IEnumerator<Diagnostic> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
