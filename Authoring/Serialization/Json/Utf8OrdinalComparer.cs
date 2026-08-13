using System;
using System.Collections.Generic;
using System.Text;

namespace AIBT.Authoring
{
    internal sealed class Utf8OrdinalComparer : IComparer<string>
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static Utf8OrdinalComparer Instance { get; } = new Utf8OrdinalComparer();

        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var leftBytes = Utf8.GetBytes(left);
            var rightBytes = Utf8.GetBytes(right);
            var count = Math.Min(leftBytes.Length, rightBytes.Length);
            for (var index = 0; index < count; index++)
            {
                var comparison = leftBytes[index].CompareTo(rightBytes[index]);
                if (comparison != 0) return comparison;
            }

            return leftBytes.Length.CompareTo(rightBytes.Length);
        }
    }
}
