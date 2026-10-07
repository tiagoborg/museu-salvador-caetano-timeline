using System;
using UnityEngine;

// RangedFloat/RangedInt/MinMaxRangeAttribute live in EnigmaVirtual, in Attributes/. Duplicate copies
// used to sit at the bottom of THIS file, in the global namespace, so the members below resolved
// without an import - identical declarations that compiled only because neither side ever saw an
// ambiguity. The copies are gone as of v0.52.0; this import is what replaces them.
using EnigmaVirtual;

public static class MathExtensions
{
    /// <summary>
    /// Swap two reference values
    /// </summary>
    public static void Swap<T>(ref T a, ref T b) => (a, b) = (b, a);

    public static float Clamp(this float value, float min, float max) => Mathf.Clamp(value, min, max);
    public static int Clamp(this int value, int min, int max) => Mathf.Clamp(value, min, max);

    /// <summary>
    /// Snap to grid of "round" size
    /// </summary>
    public static float Snap(this float val, float round) => round * Mathf.Round(val / round);

    /// <summary>
    /// Returns if value it's and odd number
    /// </summary>
    /// <param name="number"></param>
    /// <returns></returns>
    public static bool IsOdd(int number)
    {
        return number % 2 != 0;
    }

    /// <summary>
    /// Returns if value it's and even number
    /// </summary>
    /// <param name="number"></param>
    /// <returns></returns>
    public static bool IsEven(int number)
    {
        return number % 2 == 0;
    }

    /// <summary>
    /// Round float value to nearest whole number
    /// </summary>
    public static float Round(this float val) => Mathf.Round(val);

    /// <summary>
    /// Round float value to nearest integer
    /// </summary>
    public static int RoundToInt(this float val) => Mathf.RoundToInt(val);

    /// <summary>
    /// Returns the sign 1/-1 evaluated at the given value.
    /// </summary>
    public static int Sign(IComparable x) => x.CompareTo(0);

    /// <summary>
    /// Shortcut for Mathf.Approximately
    /// </summary>
    public static bool Approximately(this float value, float compare) => Mathf.Approximately(value, compare);

    /// <summary>
    /// Value is in [0, 1) range.
    /// </summary>
    public static bool InRange01(this float value) => InRange(value, 0, 1);

    /// <summary>
    /// Value is in [closedLeft, openRight) range.
    /// </summary>
    public static bool InRange<T>(this T value, T closedLeft, T openRight) where T : IComparable =>
        value.CompareTo(closedLeft) >= 0 && value.CompareTo(openRight) < 0;

    /// <summary>
    /// Value is in a RangedFloat.
    /// </summary>
    public static bool InRange(this float value, RangedFloat range) => value.InRange(range.Min, range.Max);

    /// <summary>
    /// Value is in a RangedInt.
    /// </summary>
    public static bool InRange(this int value, RangedInt range) => value.InRange(range.Min, range.Max);

    /// <summary>
    /// Value is in [closedLeft, closedRight] range, max-inclusive.
    /// </summary>
    public static bool InRangeInclusive<T>(this T value, T closedLeft, T closedRight) where T : IComparable =>
        value.CompareTo(closedLeft) >= 0 && value.CompareTo(closedRight) <= 0;

    /// <summary>
    /// Value is in a RangedFloat, max-inclusive.
    /// </summary>
    public static bool InRangeInclusive(this float value, RangedFloat range) =>
        value.InRangeInclusive(range.Min, range.Max);

    /// <summary>
    /// Value is in a RangedInt, max-inclusive.
    /// </summary>
    public static bool InRangeInclusive(this int value, RangedInt range) =>
        value.InRangeInclusive(range.Min, range.Max);

    /// <summary>
    /// Clamp value to less than min or more than max
    /// </summary>
    public static float NotInRange(this float num, float min, float max)
    {
        if (min > max) (min, max) = (max, min);

        if (num < min || num > max) return num;

        float mid = (max - min) / 2;

        if (num > min) return num + mid < max ? min : max;
        return num - mid > min ? max : min;
    }

    /// <summary>
    /// Clamp value to less than min or more than max
    /// </summary>
    public static int NotInRange(this int num, int min, int max) => (int)((float)num).NotInRange(min, max);

    /// <summary>
    /// Return point A or B, closest to num
    /// </summary>
    public static float ClosestPoint(this float num, float pointA, float pointB)
    {
        if (pointA > pointB) (pointA, pointB) = (pointB, pointA);

        float middle = (pointB - pointA) / 2;
        float withOffset = num.NotInRange(pointA, pointB) + middle;
        return (withOffset >= pointB) ? pointB : pointA;
    }

    /// <summary>
    /// Check if pointA closer to num than pointB
    /// </summary>
    public static bool ClosestPointIsA(this float num, float pointA, float pointB)
    {
        var closestPoint = num.ClosestPoint(pointA, pointB);
        return Mathf.Approximately(closestPoint, pointA);
    }
}
