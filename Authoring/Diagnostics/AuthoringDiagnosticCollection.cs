using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AIBT.Authoring
{
    public sealed class AuthoringDiagnosticCollection : IReadOnlyList<AuthoringDiagnostic>
    {
        private readonly ReadOnlyCollection<AuthoringDiagnostic> _items;

        public AuthoringDiagnosticCollection(IEnumerable<AuthoringDiagnostic> diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            var sorted = new List<AuthoringDiagnostic>();
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic == null)
                {
                    throw new ArgumentException("Diagnostic collections cannot contain null entries.", nameof(diagnostics));
                }

                sorted.Add(diagnostic);
            }

            sorted.Sort();
            var distinct = new List<AuthoringDiagnostic>(sorted.Count);
            for (var index = 0; index < sorted.Count; index++)
            {
                if (index == 0 || !sorted[index].Equals(sorted[index - 1]))
                {
                    distinct.Add(sorted[index]);
                }
            }

            _items = distinct.AsReadOnly();
        }

        public int Count => _items.Count;

        public AuthoringDiagnostic this[int index] => _items[index];

        public IEnumerator<AuthoringDiagnostic> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
