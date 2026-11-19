using System;
using System.Collections.Generic;
using System.Linq;
using AIBT.Authoring;
using AIBT.Editor.Editing;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AIBT.Editor.Graph
{
    public sealed partial class BehaviorTreeGraphWindow
    {
        private static readonly BlackboardValueType[] EditableKeyTypes =
        {
            BlackboardValueType.Bool, BlackboardValueType.Int32, BlackboardValueType.Int64, BlackboardValueType.Float32,
            BlackboardValueType.Float64, BlackboardValueType.Float2, BlackboardValueType.Float3,
        };

        private readonly Dictionary<string, Label> _liveValueLabels = new Dictionary<string, Label>();

        private void RefreshInspector()
        {
            if (_inspector == null) return;
            _inspector.Clear();
            _liveValueLabels.Clear();
            var nodeSection = Section("NODE", "aibt-node-inspector");
            _inspector.Add(nodeSection);
            BuildNodeInspector(nodeSection);
            if (Document != null)
            {
                var blackboard = Section("BLACKBOARD", "aibt-blackboard");
                _inspector.Add(blackboard);
                BuildBlackboard(blackboard);
            }
        }

        private static VisualElement Section(string title, string name)
        {
            var section = new VisualElement { name = name };
            section.AddToClassList("aibt-section");
            var label = new Label(title);
            label.AddToClassList("aibt-section__title");
            section.Add(label);
            return section;
        }

        private static Label Text(VisualElement parent, string text, string className, string name = null)
        {
            var label = new Label(text) { name = name };
            label.AddToClassList(className);
            parent.Add(label);
            return label;
        }

        // ---------------------------------------------------------------- node

        private void BuildNodeInspector(VisualElement section)
        {
            var view = SelectedNode;
            var node = view == null || Document == null ? null : GraphDocumentEdits.Find(Document, view.NodeId);
            if (node == null)
            {
                Text(section, Document == null ? "Open a tree to inspect it." : "Select a node to edit it. Right-click the canvas or press Space to add one.",
                    "aibt-inspector__empty");
                return;
            }

            var editable = _session != null;
            if (editable)
            {
                var name = new TextField { name = "aibt-node-name", isDelayed = true, tooltip = "Display name. Leave empty to show the node type." };
                name.SetValueWithoutNotify(view.title);
                name.AddToClassList("aibt-inspector__name");
                name.RegisterValueChangedCallback(evt =>
                {
                    var readable = BehaviorTreeNode.ReadableTitle(node.TypeId);
                    var value = evt.newValue == readable ? null : evt.newValue;
                    ApplyEdit(document => GraphDocumentEdits.SetDisplayName(document, node.Id, value));
                });
                section.Add(name);
            }
            else Text(section, view.title, "aibt-inspector__name");

            if (node.Subtree != null)
            {
                BuildReferenceInspector(section, node, view);
                return;
            }

            Text(section, view.KindText + "\n" + node.TypeId + "  ·  id " + node.Id.Value, "aibt-inspector__type");
            if (view.Manifest == null)
            {
                Text(section, "This node's contract is not registered, so its parameters cannot be edited here.", "aibt-inspector__summary");
                return;
            }

            if (!string.IsNullOrEmpty(view.Manifest.Summary)) Text(section, view.Manifest.Summary, "aibt-inspector__summary");
            if (view.ProblemText != null) Text(section, view.ProblemText, "aibt-field-help").style.color = new Color(1f, 0.55f, 0.5f);
            if (view.Manifest.Parameters.Count == 0) Text(section, "No parameters.", "aibt-inspector__empty");
            foreach (var parameter in view.Manifest.Parameters) section.Add(ParameterRow(node, parameter, editable));
            AddBindingInspector(section, node);
        }

        private VisualElement ParameterRow(NodeDocument node, NodeParameterContract parameter, bool editable)
        {
            node.Parameters.TryGetValue(parameter.Name, out var value);
            var shown = value ?? parameter.DefaultValue;
            var row = new VisualElement { name = "aibt-param-row-" + parameter.Name };
            row.AddToClassList("aibt-field-row");
            var field = ParameterField(node.Id, parameter, shown);
            field.name = "aibt-param-" + parameter.Name;
            field.SetEnabled(editable);
            field.EnableInClassList("aibt-field--default", value == null);
            row.Add(field);
            if (value == null)
            {
                var tag = new Label(parameter.Required ? "required" : parameter.DefaultValue != null ? "default" : "unset")
                    { tooltip = parameter.Required ? "This parameter must be set." : "Not set in the tree; the shown value is what the node uses." };
                tag.AddToClassList("aibt-field-tag");
                tag.EnableInClassList("aibt-field-tag--required", parameter.Required);
                row.Add(tag);
            }

            if (editable)
            {
                row.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    evt.menu.AppendAction("Reset to default", _ => ResetParameter(node.Id, parameter.Name),
                        value != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                }));
            }

            if (parameter.Type == NodeParameterType.BlackboardKey && parameter.BlackboardValueType != null)
            {
                var wrapper = new VisualElement();
                wrapper.Add(row);
                Text(wrapper, "Blackboard key of type " + parameter.BlackboardValueType, "aibt-field-help");
                return wrapper;
            }

            return row;
        }

        private VisualElement ParameterField(NodeId node, NodeParameterContract parameter, SemanticValue shown)
        {
            var label = parameter.Name;
            switch (parameter.Type)
            {
                case NodeParameterType.Boolean:
                {
                    var field = new Toggle(label);
                    field.SetValueWithoutNotify(shown != null && shown.TryGetBoolean(out var flag) && flag);
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromBoolean(evt.newValue)));
                    return field;
                }
                case NodeParameterType.Float32:
                {
                    var field = new FloatField(label) { isDelayed = true };
                    if (TryNumber(shown, out var number)) field.SetValueWithoutNotify((float)number);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue)) { _view.schedule.Execute(RefreshInspector); return; }
                        SetParameter(node, parameter.Name, SemanticValue.FromNumber(ShortestDouble(evt.newValue)));
                    });
                    return field;
                }
                case NodeParameterType.Int32:
                {
                    var field = new IntegerField(label) { isDelayed = true };
                    if (TryNumber(shown, out var number)) field.SetValueWithoutNotify((int)number);
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromInt64(evt.newValue)));
                    return field;
                }
                case NodeParameterType.UInt32:
                {
                    var field = new UnsignedIntegerField(label) { isDelayed = true };
                    if (TryNumber(shown, out var number) && number >= 0) field.SetValueWithoutNotify((uint)Math.Min(number, uint.MaxValue));
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromUInt64(evt.newValue)));
                    return field;
                }
                case NodeParameterType.UInt64:
                {
                    var field = new UnsignedLongField(label) { isDelayed = true };
                    if (shown != null && shown.TryGetUInt64(out var unsigned)) field.SetValueWithoutNotify(unsigned);
                    else if (TryNumber(shown, out var number) && number >= 0) field.SetValueWithoutNotify((ulong)number);
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromUInt64(evt.newValue)));
                    return field;
                }
                case NodeParameterType.StringEnum:
                {
                    var choices = parameter.AllowedValues.ToList();
                    var current = shown != null && shown.TryGetString(out var text) && choices.Contains(text) ? text : choices[0];
                    var field = new PopupField<string>(label, choices, choices.IndexOf(current));
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromString(evt.newValue)));
                    return field;
                }
                case NodeParameterType.String:
                {
                    var field = new TextField(label) { isDelayed = true };
                    if (shown != null && shown.TryGetString(out var text)) field.SetValueWithoutNotify(text);
                    field.RegisterValueChangedCallback(evt => SetParameter(node, parameter.Name, SemanticValue.FromString(evt.newValue ?? string.Empty)));
                    return field;
                }
                case NodeParameterType.BlackboardKey:
                {
                    var choices = new List<string> { string.Empty };
                    choices.AddRange(Document.Blackboard
                        .Where(key => key.Scope == BlackboardScope.Tree && (parameter.BlackboardValueType == null
                            || key.Type.CanonicalTypeId == parameter.BlackboardValueType))
                        .Select(key => key.Id));
                    var current = shown != null && shown.TryGetString(out var keyId) ? keyId : string.Empty;
                    if (!choices.Contains(current)) choices.Add(current);
                    var field = new PopupField<string>(label, choices, choices.IndexOf(current),
                        KeyChoiceLabel, KeyChoiceLabel);
                    field.RegisterValueChangedCallback(evt =>
                    {
                        if (string.IsNullOrEmpty(evt.newValue)) ResetParameter(node, parameter.Name);
                        else SetParameter(node, parameter.Name, SemanticValue.FromString(evt.newValue));
                    });
                    return field;
                }
                default:
                    return new Label(label + ": unsupported parameter type " + parameter.Type);
            }
        }

        private string KeyChoiceLabel(string choice)
        {
            if (string.IsNullOrEmpty(choice)) return "(none)";
            return Document != null && Document.Blackboard.Any(key => key.Id == choice) ? choice : choice + " (missing)";
        }

        // 0.8f must be stored as 0.8, not 0.800000011920929.
        private static double ShortestDouble(float value)
            => value == 0f ? 0d : double.Parse(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

        private static bool TryNumber(SemanticValue value, out double number)
        {
            number = 0d;
            if (value == null) return false;
            if (value.TryGetNumber(out number)) return true;
            if (value.TryGetInt64(out var signed)) { number = signed; return true; }
            if (value.TryGetUInt64(out var unsigned)) { number = unsigned; return true; }
            return false;
        }

        private void SetParameter(NodeId node, string name, SemanticValue value)
        {
            // The field may still be dispatching its change event; rebuild the inspector after it completes.
            ApplyEditDeferred(document => SemanticEditOperations.SetParameter(document, node, name, value));
        }

        private void ResetParameter(NodeId node, string name)
            => ApplyEditDeferred(document => SemanticEditOperations.RemoveParameter(document, node, name));

        private void ApplyEditDeferred(Func<TreeDocument, TreeDocument> edit)
        {
            if (_session == null) return;
            _inspectorEditDepth++;
            try { ApplyEdit(edit); }
            finally { _inspectorEditDepth--; }
        }

        private int _inspectorEditDepth;

        // ---------------------------------------------------------------- blackboard

        private void BuildBlackboard(VisualElement section)
        {
            var live = _live != null && _live.IsAttached;
            if (Document.Blackboard.Count == 0) Text(section, "No blackboard keys yet.", "aibt-inspector__empty");
            foreach (var key in Document.Blackboard)
            {
                var row = new VisualElement { name = "aibt-bb-" + key.Id };
                row.AddToClassList("aibt-bb-row");
                var head = new VisualElement();
                head.AddToClassList("aibt-bb-row__head");
                Text(head, key.Id, "aibt-bb-row__name");
                Text(head, key.Type.CanonicalTypeId + (key.Scope == BlackboardScope.Tree ? string.Empty : " · " + key.Scope), "aibt-bb-row__type");
                row.Add(head);
                if (live)
                {
                    var value = Text(row, "live: …", "aibt-bb-row__live", "aibt-bb-live-" + key.Id);
                    _liveValueLabels[key.Id] = value;
                }
                if (key.Scope == BlackboardScope.Tree) AddDefaultField(row, key);
                if (_session != null && key.Scope == BlackboardScope.Tree)
                {
                    var id = key.Id;
                    row.AddManipulator(new ContextualMenuManipulator(evt =>
                        evt.menu.AppendAction("Remove key " + id, _ => ApplyEdit(document => SemanticEditOperations.RemoveBlackboardKey(document, id)))));
                }
                section.Add(row);
            }

            if (_session == null) return;
            var add = new VisualElement { name = "aibt-bb-add" };
            add.AddToClassList("aibt-bb-add");
            var idField = new TextField { name = "aibt-bb-new-id", value = "new-key", tooltip = "Lowercase letters, digits, hyphens and dots." };
            var typeField = new PopupField<BlackboardValueType>(EditableKeyTypes.ToList(), BlackboardValueType.Bool) { name = "aibt-bb-new-type" };
            add.Add(idField);
            add.Add(typeField);
            add.Add(new Button(() => AddBlackboardKey(idField.value, typeField.value)) { text = "Add", name = "aibt-bb-add-button" });
            section.Add(add);
            Text(section, "Right-click a key to remove it.", "aibt-field-help");
        }

        internal bool AddBlackboardKey(string id, BlackboardValueType type)
        {
            if (!ValidBlackboardId(id) || Document.Blackboard.Any(key => key.Id == id))
            {
                Notify("Use a unique lowercase key ID: letters, digits, hyphens or dots.");
                return false;
            }
            var key = new BlackboardKeyDefinition(id, id, BlackboardTypeReference.BuiltIn(type), defaultValue: DefaultFor(type));
            return ApplyEdit(document => SemanticEditOperations.SetBlackboardKey(document, key));
        }

        private static bool ValidBlackboardId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 255) return false;
            return id.Split('.').All(segment => segment.Length > 0 && segment[0] != '-' && segment[segment.Length - 1] != '-' &&
                segment.All(character => character >= 'a' && character <= 'z' || character >= '0' && character <= '9' || character == '-'));
        }

        private void AddDefaultField(VisualElement row, BlackboardKeyDefinition key)
        {
            var fallback = DefaultFor(key.Type.ValueType);
            var source = key.DefaultValue ?? fallback;
            if (source == null || !source.TryGetRuntimeValue(out var value)) return;
            VisualElement field;
            switch (key.Type.ValueType)
            {
                case BlackboardValueType.Bool:
                    value.TryGetBool(out var b);
                    var toggle = new Toggle("Default");
                    toggle.SetValueWithoutNotify(b);
                    toggle.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Bool(evt.newValue)));
                    field = toggle; break;
                case BlackboardValueType.Int32:
                    value.TryGetInt32(out var i);
                    var integer = new IntegerField("Default") { isDelayed = true };
                    integer.SetValueWithoutNotify(i);
                    integer.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Int32(evt.newValue)));
                    field = integer; break;
                case BlackboardValueType.Int64:
                    value.TryGetInt64(out var l);
                    var longField = new LongField("Default") { isDelayed = true };
                    longField.SetValueWithoutNotify(l);
                    longField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Int64(evt.newValue)));
                    field = longField; break;
                case BlackboardValueType.Float32:
                    value.TryGetFloat32(out var f);
                    var floatField = new FloatField("Default") { isDelayed = true };
                    floatField.SetValueWithoutNotify(f);
                    floatField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float32(evt.newValue)));
                    field = floatField; break;
                case BlackboardValueType.Float64:
                    value.TryGetFloat64(out var d);
                    var doubleField = new DoubleField("Default") { isDelayed = true };
                    doubleField.SetValueWithoutNotify(d);
                    doubleField.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float64(evt.newValue)));
                    field = doubleField; break;
                case BlackboardValueType.Float2:
                    value.TryGetFloat2(out var v2);
                    var vector2 = new Vector2Field("Default");
                    vector2.SetValueWithoutNotify(new Vector2(v2.X, v2.Y));
                    vector2.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float2(evt.newValue.x, evt.newValue.y)));
                    field = vector2; break;
                case BlackboardValueType.Float3:
                    value.TryGetFloat3(out var v3);
                    var vector3 = new Vector3Field("Default");
                    vector3.SetValueWithoutNotify(new Vector3(v3.X, v3.Y, v3.Z));
                    vector3.RegisterValueChangedCallback(evt => SetBlackboardDefault(key.Id, BlackboardDefaultValue.Float3(evt.newValue.x, evt.newValue.y, evt.newValue.z)));
                    field = vector3; break;
                default:
                    return;
            }
            field.name = "aibt-bb-default-" + key.Id;
            field.SetEnabled(_session != null);
            field.EnableInClassList("aibt-field--default", key.DefaultValue == null);
            row.Add(field);
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
            ApplyEditDeferred(document =>
            {
                var key = document.Blackboard.First(candidate => candidate.Id == id);
                return SemanticEditOperations.SetBlackboardKey(document,
                    new BlackboardKeyDefinition(key.Id, key.Name, key.Type, key.Scope, value, key.Description, key.Reduction));
            });
        }

        private void RefreshLiveBlackboard()
        {
            if (_live == null || !_live.IsAttached) return;
            foreach (var pair in _liveValueLabels)
                pair.Value.text = _live.TryFormatValue(LiveKeyName(pair.Key), out var text) ? "live: " + text : "live: —";
        }

        // ---------------------------------------------------------------- generated bindings

        private void AddBindingInspector(VisualElement section, NodeDocument node)
        {
            if (!(_projectCatalog ?? Registry.ProjectNodeCatalogCache.Current).Generated.TryGetValue(node.TypeId, out var descriptor)) return;
            var bindings = descriptor.Bindings.Where(binding => binding.IsBlackboard).ToArray();
            if (bindings.Length == 0) return;
            Text(section, "Blackboard bindings", "aibt-section__title");
            if (Document.FormatVersion < TreeDocument.LatestFormatVersion)
            {
                Text(section, "Bindings require a version 2 tree; this tree has not been migrated.", "aibt-field-help");
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
                var labels = new List<string> { "(unbound)" };
                foreach (var key in keys) { ids.Add(key.Id); labels.Add(key.Name + " (" + key.Id + ")"); }
                var index = ids.IndexOf(selected);
                if (index < 0) { ids.Add(selected); labels.Add(selected + " (missing or incompatible)"); index = ids.Count - 1; }
                var field = new PopupField<string>(binding.BindingId + " (" + binding.Kind + ")", labels, index) { name = "aibt-binding-" + binding.BindingId };
                field.SetEnabled(_session != null);
                field.RegisterValueChangedCallback(evt =>
                {
                    var keyId = ids[labels.IndexOf(evt.newValue)];
                    ApplyEditDeferred(document => SemanticEditOperations.SetBinding(document, node.Id, binding.BindingId, keyId));
                });
                section.Add(field);
            }
        }
    }
}
