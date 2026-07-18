using Domain.Common.Converter;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Utils.Common.Utils
{
    public static class StringUtil
    {
        private static readonly Regex _codeRegex = new(RegularExpressionsConstants.Code, RegexOptions.NonBacktracking);
        private static readonly Regex _isAlphaNumericRegex = new(RegularExpressionsConstants.AlphaNumericBeginsAlpha, RegexOptions.NonBacktracking);
        private static readonly Regex _toAlphaNumericRegex = new(RegularExpressionsConstants.NonAlphaNumeric, RegexOptions.NonBacktracking);
        private static readonly Regex _isUrlRegex = new(RegularExpressionsConstants.Url, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

        public static List<string>? SplitCsv(this string csvList, bool nullOrWhitespaceInputReturnsNull = false)
        {
            if (csvList.IsNullOrWhitespace())
            {
                return nullOrWhitespaceInputReturnsNull ? null : new List<string>();
            }

            return csvList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static List<string>? SplitToList(this string list, char seperator, bool nullOrWhitespaceInputReturnsNull = false)
        {
            if (list.IsNullOrWhitespace())
            {
                return nullOrWhitespaceInputReturnsNull ? null : new List<string>();
            }

            return list.Split(seperator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static string? Join(char seperator, params string?[] values)
        {
            if (Array.TrueForAll(values, s => s.IsNullOrWhitespace()))
            {
                return null;
            }
            return string.Join(seperator, values.Where(s => !string.IsNullOrEmpty(s)));
        }

        public static string? Join(string seperator, params string?[] values)
        {
            if (Array.TrueForAll(values, s => s.IsNullOrWhitespace()))
            {
                return null;
            }
            return string.Join(seperator, values.Where(s => !string.IsNullOrEmpty(s)));
        }

        public static string? Truncate(this string? s, int maxLength)
        {
            if (s?.Length > maxLength)
            {
                return s[..maxLength];
            }
            return s;
        }

        public static bool IsNullOrWhitespace(this string? s)
        {
            return string.IsNullOrWhiteSpace(s);
        }

        public static string? Segment(this string? s, char separator, int index)
        {
            if (s is not null)
            {
                var parts = s.Split(separator);
                if (index < parts.Length)
                {
                    return parts[index];
                }
            }
            return null;
        }

        public static Guid ToGuid(this string s)
        {
            return Guid.TryParse(s, out Guid g) ? g : Guid.Empty;
        }

        public static Guid? ToNullableGuid(this string s)
        {
            return Guid.TryParse(s, out Guid g) ? g : null;
        }

        public static int ToInt(this string? s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                return int.TryParse(s.DigitsOnly(), out int i) ? i : 0;
            }
            else
            {
                return 0;
            }
        }

        public static int? ToNullableInt(this string s)
        {
            return int.TryParse(s.DigitsOnly(), out int i) ? i : null;
        }

        public static short ToShort(this string? s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                return short.TryParse(s.DigitsOnly(), out short i) ? i : (short)0;
            }
            else
            {
                return 0;
            }
        }

        public static short? ToNullableShort(this string s)
        {
            return short.TryParse(s.DigitsOnly(), out short i) ? i : null;
        }

        public static decimal ToDecimal(this string? s)
        {
            return decimal.TryParse(s.DecimalOnly(), out decimal d) ? d : 0;
        }

        public static decimal? ToNullableDecimal(this string? s)
        {
            return decimal.TryParse(s.DecimalOnly(), out decimal d) ? d : null;
        }

        public static string ToBase64String(this string s)
        {
            var plainTextBytes = Encoding.UTF8.GetBytes(s);
            return Convert.ToBase64String(plainTextBytes);
        }

        public static byte[]? FromBase64String(this string? s)
        {
            return !string.IsNullOrEmpty(s) ? Convert.FromBase64String(s) : null;
        }

        public static string? DigitsOnly(this string? s)
        {
            return !string.IsNullOrEmpty(s) ? Regex.Replace(s, "[^0-9]", "", RegexOptions.NonBacktracking) : null;
        }

        public static string? DecimalOnly(this string? s)
        {
            return !string.IsNullOrEmpty(s) ? Regex.Replace(s, "[^0-9.]", "", RegexOptions.NonBacktracking) : null;
        }

        public static string ToCode(this string s)
        {
            string str = s;
            if (!string.IsNullOrEmpty(s))
            {
                str = _codeRegex.Replace(s, string.Empty).ToLower();
                str = str[..Math.Min(str.Length, 25)];
            }
            return str;
        }

        // https://stackoverflow.com/questions/286813/how-do-you-convert-html-to-plain-text
        public static string StripHtml(this string source)
        {
            string output = source;

            if (!string.IsNullOrEmpty(output))
            {
                //get rid of HTML tags
                output = Regex.Replace(source, "<[^>]*>", string.Empty, RegexOptions.NonBacktracking);
                //get rid of multiple blank lines
                output = Regex.Replace(output, @"^\s*$\n", string.Empty, RegexOptions.Multiline | RegexOptions.NonBacktracking);
            }

            return output;
        }

        public static bool ToBool(this string s)
        {
            return bool.TryParse(s, out bool b) && b;
        }

        public static bool IsAlphaNumeric(this string s)
        {
            return _isAlphaNumericRegex.IsMatch(s);
        }

        public static string ToAlphaNumeric(this string s)
        {
            string str = s;
            if (!string.IsNullOrEmpty(s))
            {
                str = _toAlphaNumericRegex.Replace(s, string.Empty);
            }
            return str;
        }

        public static byte[] ToByteArray(this string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        public static T? ConvertTo<T>(this string? s)
        {
            T? val = default;

            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    var converter = TypeDescriptor.GetConverter(typeof(T));
                    if (converter is null)
                    {
                        return val;
                    }

                    var value = converter.ConvertFromInvariantString(s);
                    if (value is not null)
                    {
                        return (T)value;
                    }
                    return val;
                }
                catch (NotSupportedException)
                {
                    return val;
                }
            }

            return val;
        }

        /// <summary>
        /// Formats a phone number to the format: (999) 999-9999
        /// </summary>
        /// <param name="phoneNumber">Phone number to format</param>
        /// <returns>Formatted phone number</returns>
        public static string? FormatPhoneNumber(string? phoneNumber)
        {
            if (!string.IsNullOrWhiteSpace(phoneNumber))
            {
                var unformatted = Regex.Replace(phoneNumber, "[^0-9.]", "", RegexOptions.NonBacktracking);
                if (unformatted.Length == 10)
                {
                    return Regex.Replace(unformatted, @"(\d{3})(\d{3})(\d{4})", "($1) $2-$3", RegexOptions.NonBacktracking);
                }
            }
            return null;
        }

        /// <summary>
        /// Returns if a given string is a match to a given regular expression
        /// </summary>
        /// <param name="s">The string to match</param>
        /// <param name="regex">The regular expression</param>
        /// <returns>Match or not</returns>
        public static bool IsRegexMatch(this string s, string regex)
        {
            return new Regex(regex, RegexOptions.NonBacktracking).IsMatch(s);
        }

        public static bool IsUrl(this string s)
        {
            return _isUrlRegex.IsMatch(s);
        }

        public static string? DecodeBase64String(this string? data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(data));
            }
            return null;
        }

        public static string? ProperCase(this string? s)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                return Regex.Replace(s, "([A-Z])", " $1", RegexOptions.NonBacktracking).Trim();
            }
            return null;
        }
    }
}
