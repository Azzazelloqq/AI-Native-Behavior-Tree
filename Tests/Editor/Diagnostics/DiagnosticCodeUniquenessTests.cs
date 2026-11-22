using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AIBT.Tests.Editor.Diagnostics
{
    /// <summary>
    /// P11-008 (review F11): a diagnostic code is a public contract users filter on, so one code must
    /// have one meaning. Every static <see cref="DiagnosticCode"/> field declared in a non-test AIBT
    /// assembly is one declaration; two declarations under different names with one code value are two
    /// meanings. AIBT2042 and AIBT2043 each had two until P11-008.
    /// </summary>
    public sealed class DiagnosticCodeUniquenessTests
    {
        [Test]
        public void NoDiagnosticCodeIsDeclaredWithTwoMeanings()
        {
            var declarations = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name.StartsWith("AIBT", StringComparison.Ordinal)
                                   && assembly.GetName().Name.IndexOf("Tests", StringComparison.Ordinal) < 0)
                .ToArray();
            Assert.That(assemblies.Select(assembly => assembly.GetName().Name),
                Is.SupersetOf(new[] { "AIBT.Runtime", "AIBT.Authoring", "AIBT.Agents", "AIBT.Editor", "AIBT.Mcp" }));

            foreach (var assembly in assemblies)
            foreach (var type in LoadableTypes(assembly))
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(DiagnosticCode) || field.IsLiteral) continue;
                var code = (DiagnosticCode)field.GetValue(null);
                if (code.Value == null) continue;
                if (!declarations.TryGetValue(code.Value, out var names)) declarations.Add(code.Value, names = new List<string>());
                names.Add(type.FullName + "." + field.Name);
            }

            Assert.That(declarations.Count, Is.GreaterThan(100), "the scan must see the package's catalogs");
            var twoMeanings = declarations
                .Where(pair => pair.Value.Select(name => name.Substring(name.LastIndexOf('.') + 1)).Distinct().Count() > 1)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + ": " + string.Join(", ", pair.Value.OrderBy(name => name, StringComparer.Ordinal)))
                .ToArray();
            Assert.That(twoMeanings, Is.Empty, "Codes declared with two meanings:\n" + string.Join("\n", twoMeanings));
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type != null); }
        }
    }
}
