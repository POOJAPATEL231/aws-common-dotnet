using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Domain.Common.Converter
{
    public static class RegularExpressionsConstants
    {
        /// <summary>
        /// Matches strings containing upper-case letters, lower-case letters, and/or numbers.
        /// </summary>
        public static readonly string AlphaNumeric = "^[a-zA-Z0-9]*$";

        /// <summary>
        /// Removing characters that are unacceptable in alphanumeric regex.
        /// </summary>
        public static readonly string AlphaNumericPreventions = "[^a-zA-Z0-9-.' ]";

        /// <summary>
        /// Matches strings containing upper-case letters, lower-case letters, numbers, underscore, and/or hyphen.
        /// </summary>
        public static readonly string AlphaNumericExtended = "^[a-zA-Z0-9_-]*$";

        /// <summary>
        /// Matches strings containing numbers only.
        /// </summary>
        public static readonly string Numeric = "^[0-9]*$";

        /// <summary>
        /// Removing characters that are unacceptable in numeric regex.
        /// </summary>
        public static readonly string NumericPreventions = "[^0-9]";

        /// <summary>
        /// Matches strings with format: (999) 999-9999.
        /// </summary>
        public static readonly string PhoneNumber = @"^\(\d{3}\) \d{3}-\d{4}$";

        /// <summary>
        /// Matches strings containing a validly formatted email address.
        /// </summary>
        public static readonly string Email = @"^[\w\.\-\+]{1,}@{1}[\w\-]{1,}(\.{1}[\w\-]{1,}){1,}$";

        /// <summary>
        /// Matches strings containing a validly formatted web URL.
        /// </summary>
        public static readonly string Url = @"https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)";

        /// <summary>
        /// Matches strings with format: 99999 or 99999-9999.
        /// </summary>
        public static readonly string ZipCode = @"^\d{5}(-\d{4})?$";

        /// <summary>
        /// Removing characters that are unacceptable in zipcode regex.
        /// </summary>
        public static readonly string ZipCodePrevention = @"^\d{5}(-\d{4})";

        /// <summary>
        /// Matches strings with format: 999-99-9999.
        /// </summary>
        public static readonly string SocialSecurityNumber = @"^\d{3}-\d{2}-\d{4}$";

        /// <summary>
        /// Matches strings with format: 99-9999999.
        /// </summary>
        public static readonly string FederalTaxId = @"^\d{2}-\d{7}$";

        /// <summary>
        /// Matches a string format for a code e.g. for generating/validating a unique code
        /// </summary>
        public static readonly string Code = @"[^\w]*";

        /// <summary>
        /// Matches strings containing upper-case letters, lower-case letters, and/or numbers and begins with alphabet
        /// </summary>
        public static readonly string AlphaNumericBeginsAlpha = "^[a-zA-Z][a-zA-Z0-9]*$";

        /// <summary>
        /// Matches strings that do not contain alpha-numeric characters.
        /// </summary>
        public static readonly string NonAlphaNumeric = "[^a-zA-Z0-9]";

        /// <summary>
        /// Matches strings that only contain valid characters that should be used in personal names
        /// </summary>
        public static readonly string PersonName = """^[A-Za-z\x{00C0}-\x{00FF}][A-Za-z\x{00C0}-\x{00FF}\'\-]+([\ A-Za-z\x{00C0}-\x{00FF}][A-Za-z\x{00C0}-\x{00FF}\'\-]+)*$""";

        /// <summary>
        /// Matches strings that begin with a number and are optionally followed by a suffix.
        /// String may or may not contain a space between the number and suffix.
        /// </summary>
        public static readonly string NumberWithOptionalSuffix = @"^(\d*\.?\d*)\s*(\D*)$";

        /// <summary>
        /// Matches strings that begin with a capital first letter and contains period (.), dashes, apostrophe, alphanumeric .
        /// String may or may not contain a space between the alphabets.
        /// </summary>
        public static readonly string IndividualNames = @"^([A-Z]([A-Za-z0-9\s.'\-]?[A-Za-z0-9\s])*)$";

        /// <summary>
        /// Matches strings that begin with a capital first letter and contains period (.), dashes, apostrophe, alphanumeric, commas
        /// String may or may not contain a space between the alphabets.
        /// </summary>
        public static readonly string BusinessNames = @"^([A-Z0-9(]([A-Za-z0-9\s.',&()-\/]*?[A-Za-z0-9\s.)])*)$";

        /// <summary>
        /// Matches strings that contains period (.), dashes, apostrophe, alphanumeric, commas
        /// String may or may not contain a space between the alphabets.
        /// </summary>
        public static readonly string Addresses = @"^([A-Z0-9#]([(A-Za-z0-9)\s#.',-]?[A-Za-z0-9\s.])*)$";

        /// <summary>
        /// Removing characters that are unacceptable in addresses regex.
        /// </summary>
        public static readonly string AddressesPrevention = @"[^A-Za-z0-9-/\s#.',-]";

        /// <summary>
        /// Matches strings that contains dashes and only numeric values.
        /// </summary>
        public static readonly string TaxIdSSN = @"^([0-9]([0-9-]?[0-9\s])*)$";

        /// <summary>
        /// Matches strings that contains dashes, slash, alphanumeric and alphabets in upper case only.
        /// </summary>
        public static readonly string Ids = @"^([A-Z0-9]([A-Z0-9-/\s]?[A-Z0-9\s])*)$";

        /// <summary>
        /// Removing characters that are unacceptable in ids regex.
        /// </summary>
        public static readonly string IdsPrevention = @"[^A-Z0-9-/\s]";

        /// <summary>
        /// Removing characters that are unacceptable in ids regex.
        /// </summary>
        public static readonly string ModelPrevention = @"[^A-Z0-9-./\s]";

        /// <summary>
        /// Matches strings that contains numeric, slash and dashes.
        /// </summary>
        public static readonly string Dates = @"^\d{2}\/\d{2}\/\d{4}$";

        /// <summary>
        /// Matches strings that contains numeric and dollar sign.
        /// </summary>
        public static readonly string NumericValue = "^[0-9$]*$";

        /// <summary>
        /// Matches strings that begin with a capital first letter and contains period (.), dashes,slash,@, apostrophe, alphanumeric, commas
        /// String may or may not contain a space in it.
        /// </summary>
        public static readonly string Other = @"^([A-Za-z0-9@]([A-Za-z0-9\s./@,'-]?[A-Za-z0-9\s.])*)$";

        /// <summary>
        /// Removing characters that are unacceptable in other regex.
        /// </summary>
        public static readonly string OtherPrevention = @"(^?:[A-Z][A-Za-z0-9\s./@,'-])";

        /// <summary>
        /// Matches strings that begin with a capital first letter and contains period (.), dashes,slash,@,%,&,apostrophe, alphanumeric, commas
        /// String may or may not contain a space in it.
        /// </summary>
        public static readonly string StreetAddress = @"^([A-Z0-9#]([(A-Za-z0-9)\s&#.'/,\(\)-]?[A-Za-z0-9)\s.])*)$";

        /// <summary>
        /// Removing characters that are unacceptable in other regex.
        /// </summary>
        public static readonly string StreetAddressPrevention = @"(^?:[A-Z0-9#][(A-Za-z0-9)\s&#.'/,\(\)-])";

        /// <summary>
        /// Expression to check only alphabetic characters.
        /// </summary>
        public static readonly string AlphabeticCharacters = @"^[A-Za-z\s]*$";

        /// <summary>
        /// Put the space in camel case string.
        /// </summary>
        public static readonly string SpaceInString = @"((?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z]))";

        public static string? GetValidOrNull(string pattern, string? value)
        {
            if (value is not null && Regex.IsMatch(value, pattern, RegexOptions.NonBacktracking))
            {
                return value;
            }
            return null;
        }
    }
}
