using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private void AddBlackboardInspector()
        {
            var panel = new Foldout { text = "Blackboard", value = true, name = "aibt-blackboard" };
            _inspector.Add(panel);
            foreach (var key in Document.Blackboard)
            {
                var row = new VisualElement();
                panel.Add(row);
                row.Add(new Label(key.Id + " • " + key.Scope + " • " + key.Type.CanonicalTypeId)
                    { style = { whiteSpace = WhiteSpace.Normal } });
                if (key.Scope != BlackboardScope.Tree) continue;
                AddDefaultField(row, key);
                row.Add(new Button(() =>
                {
                    if (!EditorUtility.DisplayDialog("Remove variable", "Remove this variable? Existing bindings will need to be repaired.", "Remove", "Cancel")) return;
                    _session.Apply(document => SemanticEditOperations.RemoveBlackboardKey(document, key.Id));
                    RefreshSession();
                }) { text = "Remove variable" });
            }
            var newId = new TextField("New variable ID") { value = "new-variable", isDelayed = true };
            panel.Add(newId);
            panel.Add(new Button(() =>
            {
                var id = newId.value;
                if (!ValidBlackboardId(id) || Document.Blackboard.Any(key => key.Id == id))
                {
                    EditorUtility.DisplayDialog("Invalid variable ID", "Use a unique lowercase ID with letters, digits, hyphens or dots. Segments cannot begin or end with a hyphen.", "OK");
                    return;
                }
                var menu = new GenericMenu();
                foreach (var type in new[] { BlackboardValueType.Bool, BlackboardValueType.Int32, BlackboardValueType.Int64,
                             BlackboardValueType.Float32, BlackboardValueType.Float64, BlackboardValueType.Float2, BlackboardValueType.Float3 })
                {
                    var selected = type;
                    menu.AddItem(new GUIContent(type.ToString()), false, () =>
                    {
                        var key = new BlackboardKeyDefinition(id, id,
                            BlackboardTypeReference.BuiltIn(selected), defaultValue: DefaultFor(selected));
                        _session.Apply(document => SemanticEditOperations.SetBlackboardKey(document, key));
                        RefreshSession();
                    });
                }
                menu.ShowAsContext();
            }) { text = "Add variable" });
        }

        private static bool ValidBlackboardId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 255) return false;
            return id.Split('.').All(segment => segment.Length > 0 && segment[0] != '-' && segment[segment.Length - 1] != '-' &&
                segment.All(character => character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '-'));
        }

        private void AddDefaultField(VisualElement row, BlackboardKeyDefinition key)
        {
            if (key.DefaultValue == null)
            {
                if (DefaultFor(key.Type.ValueType) != null)
                    row.Add(new Button(() => SetBlackboardDefault(key.Id, DefaultFor(key.Type.ValueType))) { text = "Set default" });
                return;
            }
            if (!key.DefaultValue.TryGetRuntimeValue(out var value)) return;
            switch (key.Type.ValueType)
            {
                case BlackboardValueType.Bool:
                    value.TryGetBool(out var b);
                    var toggle = new Toggle("Default") { value = b };
                    toggle.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Bool(evt.newValue)));
                    row.Add(toggle); break;
                case BlackboardValueType.Int32:
                    value.TryGetInt32(out var i);
                    var integer = new IntegerField("Default") { value = i, isDelayed = true };
                    integer.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Int32(evt.newValue)));
                    row.Add(integer); break;
                case BlackboardValueType.Int64:
                    value.TryGetInt64(out var l);
                    var longField = new LongField("Default") { value = l, isDelayed = true };
                    longField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Int64(evt.newValue)));
                    row.Add(longField); break;
                case BlackboardValueType.Float32:
                    value.TryGetFloat32(out var f);
                    var floatField = new FloatField("Default") { value = f, isDelayed = true };
                    floatField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float32(evt.newValue)));
                    row.Add(floatField); break;
                case BlackboardValueType.Float64:
                    value.TryGetFloat64(out var d);
                    var doubleField = new DoubleField("Default") { value = d, isDelayed = true };
                    doubleField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float64(evt.newValue)));
                    row.Add(doubleField); break;
                case BlackboardValueType.Float2:
                    value.TryGetFloat2(out var v2);
                    var vector2 = new Vector2Field("Default") { value = new Vector2(v2.X, v2.Y) };
                    vector2.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float2(evt.newValue.x, evt.newValue.y)));
                    row.Add(vector2); break;
                case BlackboardValueType.Float3:
                    value.TryGetFloat3(out var v3);
                    var vector3 = new Vector3Field("Default") { value = new Vector3(v3.X, v3.Y, v3.Z) };
                    vector3.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float3(evt.newValue.x, evt.newValue.y, evt.newValue.z)));
                    row.Add(vector3); break;
                default: return;
            }
            row.Add(new Button(() => SetBlackboardDefault(key.Id, null)) { text = "Clear default" });
        }

        private static BlackboardDefaultValue DefaultFor(BlackboardValueType type)
        {
            switch (type)
            {
                case BlackboardValueType.Bool: return BlackboardDefaultValue.Bool(false);
                case BlackboardValueType.Int32: return BlackboardDefaultValue.Int32(0);
                case BlackboardValueType.Int64: return BlackboardDefaultValue.Int64(0);
                case BlackboardValueType.Float32: return BlackboardDefaultValue.Float32(0);
                case BlackboardValueType.Float64: return BlackboardDefaultValue.Float64(0);
                case BlackboardValueType.Float2: return BlackboardDefaultValue.Float2(0, 0);
                case BlackboardValueType.Float3: return BlackboardDefaultValue.Float3(0, 0, 0);
                default: return null;
            }
        }

        private void SetBlackboardDefault(string id, BlackboardDefaultValue value)
        {
            if (value != null && !value.IsCanonical) { _view.schedule.Execute(RefreshInspector); return; }
            UpdateBlackboardKey(id, key => new BlackboardKeyDefinition(key.Id, key.Name, key.Type, key.Scope, value, key.Description, key.Reduction));
        }

        private void UpdateBlackboardKey(string id, Func<BlackboardKeyDefinition, BlackboardKeyDefinition> update)
        {
            _session.Apply(document => SemanticEditOperations.SetBlackboardKey(document, update(document.Blackboard.First(key => key.Id == id))));
            _view.schedule.Execute(RefreshSession);
        }

        private void AddBindingInspector(NodeDocument node)
        {
            if (!(_projectCatalog ?? Registry.ProjectNodeCatalogCache.Current).Generated.TryGetValue(node.TypeId, out var descriptor)) return;
            var bindings = descriptor.Bindings.Where(binding => binding.IsBlackboard).ToArray();
            if (bindings.Length == 0) return;
            _inspector.Add(new Label("Blackboard bindings") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            if (Document.FormatVersion < TreeDocument.LatestFormatVersion)
            {
                _inspector.Add(new Label("Bindings require a version 2 tree; this tree has not been migrated.") { style = { whiteSpace = WhiteSpace.Normal } });
                return;
            }
            foreach (var binding in bindings)
            {
                var type = binding.Types[0];
                var keys = Document.Blackboard.Where(key => ValidBlackboardId(key.Id) && key.Scope == binding.Scope &&
                    key.Type.CanonicalTypeId == type.CanonicalTypeId && key.Type.RuntimeDescriptor.Version == type.Version &&
                    key.Type.RegisteredDescriptor.Equals(type.RegisteredDescriptor)).ToArray();
                string selected = null;
                node.Bindings?.Values.TryGetValue(binding.BindingId, out selected);
                var ids = new List<string> { null };
                var labels = new List<string> { "<unbound>" };
                foreach (var key in keys) { ids.Add(key.Id); labels.Add(key.Name + " (" + key.Id + ")"); }
                var index = ids.IndexOf(selected);
                if (index < 0) { ids.Add(selected); labels.Add("<missing or incompatible: " + selected + ">"); index = ids.Count - 1; }
                var field = new PopupField<string>(binding.BindingId + " (" + binding.Kind + ")", labels, index);
                field.RegisterValueChangedCallback(evt =>
                {
                    var keyId = ids[labels.IndexOf(evt.newValue)];
                    _session.Apply(document => SemanticEditOperations.SetBinding(document, node.Id, binding.BindingId, keyId));
                    _view.schedule.Execute(RefreshSession);
                });
                _inspector.Add(field);
            }
        }
    }
}
