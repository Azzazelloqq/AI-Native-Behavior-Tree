using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    /// <summary>Loads the graph editor stylesheets from <c>Editor/Graph/Styles/</c> wherever the package lives.</summary>
    internal static class GraphStyles
    {
        internal const string GraphSheetName = "AibtGraph";
        internal const string WindowSheetName = "AibtGraphWindow";

        private static StyleSheet s_graph;
        private static StyleSheet s_window;

        internal static StyleSheet Graph => s_graph != null ? s_graph : s_graph = Load(GraphSheetName);

        internal static StyleSheet Window => s_window != null ? s_window : s_window = Load(WindowSheetName);

        internal static void ApplyGraph(VisualElement element) => Add(element, Graph);

        internal static void ApplyWindow(VisualElement element)
        {
            Add(element, Graph);
            Add(element, Window);
        }

        private static void Add(VisualElement element, StyleSheet sheet)
        {
            if (sheet != null && !element.styleSheets.Contains(sheet)) element.styleSheets.Add(sheet);
        }

        private static StyleSheet Load(string name)
        {
            var suffix = "/Editor/Graph/Styles/" + name + ".uss";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GraphStyles).Assembly);
            if (package != null)
            {
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(package.assetPath + suffix);
                if (sheet != null) return sheet;
            }

            foreach (var guid in AssetDatabase.FindAssets(name + " t:StyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(suffix, StringComparison.Ordinal)) return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }

            return null;
        }
    }
}
