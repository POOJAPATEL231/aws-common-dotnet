using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Utils.Common.Utils
{
    public static class NumberUtil
    {
        public static int GenerateRandomNumber(int min, int max)
        {
            return RandomNumberGenerator.GetInt32(min, max);
        }

        public static short ValidateTerm(short term, short min = 0, short max = 0)
        {
            short retval = term;

            // check if term is in increments of 6
            var i = retval % 6;
            if (i > 0 && i < 3)
            {
                retval = (short)(retval - i);
            }
            else if (i > 2)
            {
                retval = (short)((retval - i) + 6);
            }

            if (min > 0 && retval < min)
            {
                retval = min;
            }

            if (max > 0 && retval > max)
            {
                retval = max;
            }

            return retval;
        }

        public static decimal ValidateDecimal(decimal val, decimal min = -1, decimal max = -1)
        {
            decimal retval = val;

            if (min > -1 && retval < min)
            {
                retval = min;
            }
            if (max > -1 && retval > max)
            {
                retval = max;
            }

            return retval;
        }

        public static decimal? ValueOrNull(this decimal value, decimal minValue, decimal maxValue)
        {
            if (value < minValue || value > maxValue)
            {
                return null;
            }
            return value;
        }

        public static decimal ValueOrZero(this decimal value, decimal minValue, decimal maxvalue)
        {
            if (value < minValue || value > maxvalue)
            {
                return 0;
            }
            return value;
        }

        public static int? ToNullableInt(dynamic? d)
        {
            if (d != null)
            {
                return int.TryParse(d.ToString(), out int i) ? i : default;
            }
            else
            {
                return default;
            }
        }
    }
}
