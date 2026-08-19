using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AIBT.Editor.Layout
{
    /// <summary>
    /// Canonical serializer for <see cref="LayoutDocument"/>, implementing editor-layout-v1.md's
    /// encoding rules (UTF-8 no BOM, LF, two-space indent, ordinal key order, Float32
    /// shortest-round-trip formatting). Mirrors AIBT.Authoring.CanonicalTreeJsonWriter's
    /// structure; duplicated rather than shared because AIBT.Authoring does not grant
    /// InternalsVisibleTo to AIBT.Editor and this card's allowed changes are scoped to
    /// Editor/Layout/ only.
    /// </summary>
    public static class CanonicalLayoutJsonWriter
    {
        public static byte[] Write(LayoutDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var writer = new Writer();
            writer.WriteDocument(document);
            return writer.ToUtf8();
        }

        private sealed class Writer
        {
            private readonly StringBuilder _builder = new StringBuilder();
            private int _depth;

            public byte[] ToUtf8()
            {
                _builder.Append('\n');
                return new UTF8Encoding(false, true).GetBytes(_builder.ToString());
            }

            public void WriteDocument(LayoutDocument document)
            {
                BeginObject();
                var first = true;
                Property(ref first, "format", () => String(LayoutDocument.CurrentFormat));
                Property(ref first, "formatVersion", () => Integer(LayoutDocument.CurrentFormatVersion));
                Property(ref first, "treeId", () => String(document.TreeId.Value));
                Property(ref first, "direction", () => String(DirectionText(document.Direction)));
                Property(ref first, "nodes", () => WriteNodes(document.Nodes));
                if (document.Groups.Count > 0)
                {
                    Property(ref first, "groups", () => WriteGroups(document.Groups));
                }

                if (document.Notes.Count > 0)
                {
                    Property(ref first, "notes", () => WriteNotes(document.Notes));
                }

                if (document.Reroutes.Count > 0)
                {
                    Property(ref first, "reroutes", () => WriteReroutes(document.Reroutes));
                }

                EndObject(first);
            }

            private void WriteNodes(IReadOnlyDictionary<NodeId, LayoutNodePlacement> nodes)
            {
                var keys = new List<NodeId>(nodes.Keys);
                // NodeId values are constrained to ASCII by the authoring identity grammar
                // (identity-and-hashing-v1.md), so ordinal UTF-16 comparison of the string
                // values is equivalent to ordinal UTF-8 byte comparison here.
                keys.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));

                BeginObject();
                var first = true;
                foreach (var key in keys)
                {
                    var placement = nodes[key];
                    Property(ref first, key.Value, () => WritePlacement(placement));
                }

                EndObject(first);
            }

            private void WritePlacement(LayoutNodePlacement placement)
            {
                BeginObject();
                var first = true;
                Property(ref first, "position", () => WritePoint(placement.Position));
                if (placement.Pinned)
                {
                    Property(ref first, "pinned", () => Boolean(true));
                }

                EndObject(first);
            }

            private void WritePoint(LayoutPoint point)
            {
                BeginObject();
                var first = true;
                Property(ref first, "x", () => Number(point.X));
                Property(ref first, "y", () => Number(point.Y));
                EndObject(first);
            }

            private void WriteGroups(IReadOnlyDictionary<string, LayoutGroup> groups)
            {
                var keys = new List<string>(groups.Keys);
                keys.Sort(string.CompareOrdinal);

                BeginObject();
                var first = true;
                foreach (var key in keys)
                {
                    var group = groups[key];
                    Property(ref first, key, () => WriteGroup(group));
                }

                EndObject(first);
            }

            private void WriteGroup(LayoutGroup group)
            {
                BeginObject();
                var first = true;
                Property(ref first, "title", () => String(group.Title));
                if (group.Description != null)
                {
                    Property(ref first, "description", () => String(group.Description));
                }

                if (group.Color != null)
                {
                    Property(ref first, "color", () => String(group.Color));
                }

                if (group.Locked)
                {
                    Property(ref first, "locked", () => Boolean(true));
                }

                Property(ref first, "memberNodeIds", () => WriteNodeIdArray(group.MemberNodeIds));
                EndObject(first);
            }

            private void WriteNodeIdArray(IReadOnlyList<NodeId> ids)
            {
                _builder.Append('[');
                _depth++;
                for (var index = 0; index < ids.Count; index++)
                {
                    if (index > 0)
                    {
                        _builder.Append(',');
                    }

                    _builder.Append('\n');
                    Indent();
                    String(ids[index].Value);
                }

                _depth--;
                if (ids.Count > 0)
                {
                    _builder.Append('\n');
                    Indent();
                }

                _builder.Append(']');
            }

            private void WriteNotes(IReadOnlyDictionary<string, LayoutNote> notes)
            {
                var keys = new List<string>(notes.Keys);
                keys.Sort(string.CompareOrdinal);

                BeginObject();
                var first = true;
                foreach (var key in keys)
                {
                    var note = notes[key];
                    Property(ref first, key, () => WriteNote(note));
                }

                EndObject(first);
            }

            private void WriteNote(LayoutNote note)
            {
                BeginObject();
                var first = true;
                Property(ref first, "text", () => String(note.Text));
                Property(ref first, "position", () => WritePoint(note.Position));
                Property(ref first, "size", () => WritePoint(note.Size));
                if (note.Color != null)
                {
                    Property(ref first, "color", () => String(note.Color));
                }

                EndObject(first);
            }

            private void WriteReroutes(IReadOnlyDictionary<LayoutEdgeKey, LayoutReroute> reroutes)
            {
                var keys = new List<LayoutEdgeKey>(reroutes.Keys);
                keys.Sort((left, right) => string.CompareOrdinal(left.ToKeyString(), right.ToKeyString()));

                BeginObject();
                var first = true;
                foreach (var key in keys)
                {
                    var reroute = reroutes[key];
                    Property(ref first, key.ToKeyString(), () => WriteReroute(reroute));
                }

                EndObject(first);
            }

            private void WriteReroute(LayoutReroute reroute)
            {
                BeginObject();
                var first = true;
                Property(ref first, "waypoints", () => WritePointArray(reroute.Waypoints));
                EndObject(first);
            }

            private void WritePointArray(IReadOnlyList<LayoutPoint> points)
            {
                _builder.Append('[');
                _depth++;
                for (var index = 0; index < points.Count; index++)
                {
                    if (index > 0)
                    {
                        _builder.Append(',');
                    }

                    _builder.Append('\n');
                    Indent();
                    WritePoint(points[index]);
                }

                _depth--;
                if (points.Count > 0)
                {
                    _builder.Append('\n');
                    Indent();
                }

                _builder.Append(']');
            }

            private static string DirectionText(LayoutDirection direction)
            {
                switch (direction)
                {
                    case LayoutDirection.TopToBottom:
                        return "topToBottom";
                    case LayoutDirection.LeftToRight:
                        return "leftToRight";
                    default:
                        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
                }
            }

            private void Property(ref bool first, string name, Action writeValue)
            {
                if (!first)
                {
                    _builder.Append(',');
                }

                _builder.Append('\n');
                Indent();
                String(name);
                _builder.Append(": ");
                writeValue();
                first = false;
            }

            private void BeginObject()
            {
                _builder.Append('{');
                _depth++;
            }

            private void EndObject(bool empty)
            {
                _depth--;
                if (!empty)
                {
                    _builder.Append('\n');
                    Indent();
                }

                _builder.Append('}');
            }

            private void Indent() => _builder.Append(' ', _depth * 2);

            private void Boolean(bool value) => _builder.Append(value ? "true" : "false");

            private void Integer(int value) => _builder.Append(value.ToString(CultureInfo.InvariantCulture));

            private void Number(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                RawNumber(value == 0f ? "0" : value.ToString("R", CultureInfo.InvariantCulture));
            }

            private void RawNumber(string value)
            {
                var exponent = value.IndexOfAny(new[] { 'E', 'e' });
                if (exponent < 0)
                {
                    _builder.Append(value);
                    return;
                }

                _builder.Append(value, 0, exponent);
                _builder.Append('e');
                var index = exponent + 1;
                if (index < value.Length && value[index] == '+')
                {
                    index++;
                }

                if (index < value.Length && value[index] == '-')
                {
                    _builder.Append('-');
                    index++;
                }

                while (index + 1 < value.Length && value[index] == '0')
                {
                    index++;
                }

                _builder.Append(value, index, value.Length - index);
            }

            private void String(string value)
            {
                _builder.Append('"');
                for (var index = 0; index < value.Length; index++)
                {
                    var character = value[index];
                    switch (character)
                    {
                        case '"':
                            _builder.Append("\\\"");
                            break;
                        case '\\':
                            _builder.Append("\\\\");
                            break;
                        case '\b':
                            _builder.Append("\\b");
                            break;
                        case '\f':
                            _builder.Append("\\f");
                            break;
                        case '\n':
                            _builder.Append("\\n");
                            break;
                        case '\r':
                            _builder.Append("\\r");
                            break;
                        case '\t':
                            _builder.Append("\\t");
                            break;
                        default:
                            if (character < 0x20)
                            {
                                _builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                _builder.Append(character);
                            }

                            break;
                    }
                }

                _builder.Append('"');
            }
        }
    }
}
