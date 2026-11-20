using System.Globalization;
using System.Text.RegularExpressions;

namespace Z10.Core.Model;

/// <summary>
/// Ersetzt Platzhalter in Formatvorlagen:
///   {value}            Rohwert
///   {value:0.0}        .NET-Zahlenformat
///   {value/1000:0.0}   vorher umrechnen (+ - * / mit einer Zahl)
/// Ohne Wert (Sensor fehlt) wird "--" eingesetzt.
/// </summary>
public static partial class ValueFormatter
{
    [GeneratedRegex(@"\{value(?:\s*([*/+\-])\s*([0-9]+(?:[.,][0-9]+)?))?(?::([^}]*))?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    public static string Format(string template, double? value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return Placeholder().Replace(template ?? "", m =>
        {
            if (value is not double v || double.IsNaN(v)) return "--";
            if (m.Groups[1].Success)
            {
                double operand = double.Parse(m.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                v = m.Groups[1].Value switch
                {
                    "*" => v * operand,
                    "/" => operand == 0 ? double.NaN : v / operand,
                    "+" => v + operand,
                    "-" => v - operand,
                    _ => v
                };
                if (double.IsNaN(v)) return "--";
            }
            var fmt = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? m.Groups[3].Value : "0.#";
            return v.ToString(fmt, culture);
        });
    }
}
