namespace NFCBets.Utilities;

/// <summary>
/// Helper for consistent console output formatting
/// Fixes the C# composite formatting issue where alignment and format specifiers conflict
/// </summary>
public static class ConsoleFormatter
{
    // Number formatters with fixed width
    public static string Percent(double value, int width = 8) 
        => $"{value:P1}".PadLeft(width);
    
    public static string Percent0(double value, int width = 8) 
        => $"{value:P0}".PadLeft(width);
    
    public static string Percent2(double value, int width = 10) 
        => $"{value:P2}".PadLeft(width);

    public static string Decimal2(double value, int width = 8) 
        => $"{value:F2}".PadLeft(width);
    
    public static string Decimal3(double value, int width = 8) 
        => $"{value:F3}".PadLeft(width);
    
    public static string Decimal4(double value, int width = 8) 
        => $"{value:F4}".PadLeft(width);

    public static string Currency(decimal value, int width = 12) 
        => $"${value:N2}".PadLeft(width);
    
    public static string CurrencyInt(decimal value, int width = 10) 
        => $"${value:N0}".PadLeft(width);

    public static string Number(int value, int width = 6) 
        => value.ToString().PadLeft(width);
    
    public static string Number(double value, int width = 8) 
        => $"{value:N2}".PadLeft(width);

    public static string Time(TimeSpan value, int width = 8) 
        => $"{value.TotalSeconds:F1}s".PadLeft(width);

    public static string Text(string value, int width, bool leftAlign = true) 
        => leftAlign ? value.PadRight(width) : value.PadLeft(width);

    // Signed percentages (with + for positive)
    public static string SignedPercent(double value, int width = 10)
    {
        var sign = value >= 0 ? "+" : "";
        return $"{sign}{value:P1}".PadLeft(width);
    }

    public static string SignedPercent0(double value, int width = 8)
    {
        var sign = value >= 0 ? "+" : "";
        return $"{sign}{value:P0}".PadLeft(width);
    }

    // Table row helpers
    public static string Row(params (object Value, int Width, string Format)[] columns)
    {
        var parts = new List<string>();
        foreach (var (value, width, format) in columns)
        {
            parts.Add(FormatValue(value, width, format));
        }
        return string.Join(" ", parts);
    }

    private static string FormatValue(object value, int width, string format)
    {
        var formatted = format.ToUpper() switch
        {
            "P0" => $"{Convert.ToDouble(value):P0}",
            "P1" => $"{Convert.ToDouble(value):P1}",
            "P2" => $"{Convert.ToDouble(value):P2}",
            "F2" => $"{Convert.ToDouble(value):F2}",
            "F3" => $"{Convert.ToDouble(value):F3}",
            "F4" => $"{Convert.ToDouble(value):F4}",
            "N0" => $"{Convert.ToDouble(value):N0}",
            "N2" => $"{Convert.ToDouble(value):N2}",
            "$" => $"${Convert.ToDecimal(value):N2}",
            "$0" => $"${Convert.ToDecimal(value):N0}",
            "S" or "" => value?.ToString() ?? "",
            _ => string.Format($"{{0:{format}}}", value)
        };

        return format.StartsWith("-") 
            ? formatted.PadRight(width) 
            : formatted.PadLeft(width);
    }
}