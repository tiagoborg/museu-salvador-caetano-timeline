using System;
using System.Globalization;

public class ComMessage
{
    // Type identifiers
    public const char INTEGER = 'i';
    public const char FLOAT = 'f';
    public const char LONG = 'h';
    public const char DOUBLE = 'd';
    public const char STRING = 's';
    public const char BYTE = 'b';
    public const char DEFAULT = ',';

    public int Id { get; private set; }
    public char Type { get; private set; }
    public object Value { get; private set; }

    private const char Separator = '#';
    private const char LineTerminator = '\n';

    public ComMessage(int id, char type, object value)
    {
        Id = id;
        Type = type;

        if (!IsSupportedType(type))
            throw new ArgumentException($"Unsupported type '{type}'");

        if (!IsValueMatchingType(type, value))
            throw new ArgumentException($"Value '{value}' does not match type '{type}'");

        Value = NormalizeValue(type, value);
    }

    #region Factory Methods

    /// <summary>
    /// Creates a new ComMessage instance representing an integer value message with the specified identifier and value.
    /// </summary>
    /// <param name="id">The unique identifier for the message. Must be a non-negative integer.</param>
    /// <param name="value">The integer value to include in the message.</param>
    /// <returns>A ComMessage instance containing the specified identifier and integer value.</returns>
    /// <example>
    /// var msg = ComMessage.Create(3, 1);
    /// serialPort.Write(msg.Pack());
    /// </example>
    public static ComMessage Create(int id, int value)
        => new ComMessage(id, INTEGER, value);

    public static ComMessage Create(int id, float value)
        => new ComMessage(id, FLOAT, value);

    public static ComMessage Create(int id, long value)
        => new ComMessage(id, LONG, value);

    public static ComMessage Create(int id, double value)
        => new ComMessage(id, DOUBLE, value);

    public static ComMessage Create(int id, string value)
        => new ComMessage(id, STRING, value);

    public static ComMessage Create(int id, byte value)
        => new ComMessage(id, BYTE, value);

    #endregion

    #region Packing

    public string Pack()
    {
        string valueString = ConvertToString(Value, Type);

        return $"{Id}{Separator}{Type}{Separator}{valueString}{LineTerminator}";
    }

    private static string ConvertToString(object value, char type)
    {
        switch (type)
        {
            case FLOAT:
                return ((float)value).ToString(CultureInfo.InvariantCulture);

            case DOUBLE:
                return ((double)value).ToString(CultureInfo.InvariantCulture);

            case INTEGER:
                return ((int)value).ToString();

            case LONG:
                return ((long)value).ToString();

            case BYTE:
                return ((byte)value).ToString();

            case STRING:
                return value.ToString();

            default:
                throw new InvalidOperationException("Unsupported type.");
        }
    }

    #endregion

    #region Parsing

    /// <summary>
    /// Attempts to parse the specified raw line into a <see cref="ComMessage"/> instance.
    /// </summary>
    /// <remarks>The input string must consist of three parts separated by the expected separator: an integer
    /// ID, a single character type, and a value. If the input does not match this format or contains invalid data,
    /// parsing fails and <paramref name="message"/> is set to <see langword="null"/>.</remarks>
    /// <param name="rawLine">The input string representing a message to parse. Must contain three parts separated by the defined separator
    /// character.</param>
    /// <param name="message">When this method returns, contains the parsed <see cref="ComMessage"/> if parsing succeeded; otherwise, <see
    /// langword="null"/>. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the input string was successfully parsed into a <see cref="ComMessage"/>; otherwise,
    /// <see langword="false"/>.</returns>
    /// <example>
    /// if (ComMessage.TryParse(rawMessage, out ComMessage msg))
    ///     Debug.Log($"ID: {msg.Id} Type: {msg.Type} Value: {msg.Value}");
    /// </example> 
    public static bool TryParse(string rawLine, out ComMessage message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(rawLine))
            return false;

        rawLine = rawLine.Trim(); // removes \n and \r

        string[] parts = rawLine.Split(Separator);

        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out int id))
            return false;

        if (parts[1].Length != 1)
            return false;

        char type = parts[1][0];
        string valuePart = parts[2];

        if (!TryConvertValue(type, valuePart, out object value))
            return false;

        message = new ComMessage(id, type, value);
        return true;
    }

    private static bool TryConvertValue(char type, string raw, out object value)
    {
        value = null;

        switch (type)
        {
            case INTEGER:
                if (int.TryParse(raw, out int i))
                {
                    value = i;
                    return true;
                }
                break;

            case FLOAT:
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                {
                    value = f;
                    return true;
                }
                break;

            case LONG:
                if (long.TryParse(raw, out long l))
                {
                    value = l;
                    return true;
                }
                break;

            case DOUBLE:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    value = d;
                    return true;
                }
                break;

            case BYTE:
                if (byte.TryParse(raw, out byte b))
                {
                    value = b;
                    return true;
                }
                break;

            case STRING:
                value = raw;
                return true;
        }

        return false;
    }

    private static bool IsSupportedType(char type)
    {
        return type == INTEGER ||
               type == FLOAT ||
               type == LONG ||
               type == DOUBLE ||
               type == STRING ||
               type == BYTE;
    }

    private static bool IsValueMatchingType(char type, object value)
    {
        return type switch
        {
            INTEGER => value is int,
            FLOAT => value is float,
            LONG => value is long,
            DOUBLE => value is double,
            STRING => value is string,
            BYTE => value is byte || value is bool,
            _ => false
        };
    }

    private static object NormalizeValue(char type, object value)
    {
        if (type == BYTE)
        {
            if (value is bool b)
                return (byte)(b ? 1 : 0);
        }

        return value;
    }

    public override string ToString()
    {
        return $"[ComMessage] Id={Id}, Type={Type}, Value={Value}";
    }

    #endregion

    #region Typed Getters

    public int AsInt() => (int)Value;
    public float AsFloat() => (float)Value;
    public long AsLong() => (long)Value;
    public double AsDouble() => (double)Value;
    public byte AsByte() => (byte)Value;
    public string AsString() => (string)Value;
    public bool AsBool()
    {
        if (Type != INTEGER && Type != BYTE)
            throw new InvalidOperationException("Value is not numeric.");

        int val = Convert.ToInt32(Value);
        return val != 0;
    }

    #endregion
}
