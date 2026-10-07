using System;
using System.Collections.Generic;
using System.Text;

public static class IntExtensions
{
    /// <summary>
    /// Converts numbers to Roman numerals
    /// </summary>
    /// <param name="number"></param>
    /// <returns>Roman numeral</returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static string ToRoman(this int number)
    {
        if (number <= 0 || number > 3999)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Roman numerals are only defined for values between 1 and 3999");
        }

        var romanNumerals = new Dictionary<int, string>
        {
            { 1000, "M" },
            { 900, "CM" },
            { 500, "D" },
            { 400, "CD" },
            { 100, "C" },
            { 90, "XC" },
            { 50, "L" },
            { 40, "XL" },
            { 10, "X" },
            { 9, "IX" },
            { 5, "V" },
            { 4, "IV" },
            { 1, "I" }
        };

        var result = new StringBuilder();

        foreach (var kvp in romanNumerals)
        {
            while (number >= kvp.Key)
            {
                result.Append(kvp.Value);
                number -= kvp.Key;
            }
        }

        return result.ToString();
    }

    public static string ToHours(this int value) => ToHours((float)value);
    public static string ToMinutes(this int value) => ToMinutes((float)value);
    public static string ToSeconds(this int value) => ToSeconds((float)value);

    public static string ToHours(this float value)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(value);
        return timeSpan.Hours.ToString("D2");
    }

    public static string ToMinutes(this float value)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(value);
        return timeSpan.Minutes.ToString("D2");
    }

    public static string ToSeconds(this float value)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(value);
        return timeSpan.Seconds.ToString("D2");
    }

    /// <summary>
    /// Validate if T is a numeric type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsNumericType<T>()
    {
        TypeCode typeCode = Type.GetTypeCode(typeof(T));

        return typeCode == TypeCode.Int16 ||
               typeCode == TypeCode.Int32 ||
               typeCode == TypeCode.Int64 ||
               typeCode == TypeCode.UInt16 ||
               typeCode == TypeCode.UInt32 ||
               typeCode == TypeCode.UInt64 ||
               typeCode == TypeCode.Single ||
               typeCode == TypeCode.Double ||
               typeCode == TypeCode.Decimal ||
               typeCode == TypeCode.Byte ||
               typeCode == TypeCode.SByte;
    }

    public static string FormatTimeFull<T>(this T totalSeconds, bool omitZeroHours = false) where T : IConvertible
    {
        if (!IsNumericType<T>())
            throw new ArgumentException("FormatTimeFull can only be used with numeric types.");

        // Convert to total seconds as int
        int totalSecondsInt = Convert.ToInt32(totalSeconds);

        int hours = totalSecondsInt / 3600;
        int minutes = (totalSecondsInt % 3600) / 60;
        int seconds = totalSecondsInt % 60;

        if (omitZeroHours && hours == 0)
        {
            return $"{minutes:D2}:{seconds:D2}";
        }
        else
        {
            return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }
    }
}
