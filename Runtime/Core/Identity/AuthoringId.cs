using System;

namespace AIBT
{
    internal static class AuthoringId
    {
        public static bool IsValid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsFirstCharacter(value[0]))
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                if (!IsSubsequentCharacter(value[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public static string Parse(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!IsValid(value))
            {
                throw new FormatException(
                    "Authoring IDs must match ^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$.");
            }

            return value;
        }

        private static bool IsFirstCharacter(char value)
        {
            return value >= 'A' && value <= 'Z'
                || value >= 'a' && value <= 'z'
                || value >= '0' && value <= '9';
        }

        private static bool IsSubsequentCharacter(char value)
        {
            return IsFirstCharacter(value)
                || value == '.'
                || value == '_'
                || value == ':'
                || value == '-';
        }
    }
}
