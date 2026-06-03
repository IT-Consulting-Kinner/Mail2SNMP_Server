namespace Mail2SNMP.Web;

/// <summary>
/// V5: CSV cell quoting with spreadsheet formula-injection neutralization.
///
/// The Events / AuditLog / DeadLetters exports write untrusted, attacker-
/// influenced text (email subject/sender, audit details) into CSV files that
/// operators open in Excel/LibreOffice. RFC4180 quoting alone is not enough:
/// a cell beginning with '=', '+', '-', '@', TAB or CR is interpreted as a
/// formula by spreadsheet apps, enabling data exfiltration (HYPERLINK /
/// WEBSERVICE) or, on DDE-enabled setups, command execution. This helper
/// prefixes such cells with a single quote so the spreadsheet treats them as
/// literal text, then applies RFC4180 quoting.
/// </summary>
public static class CsvCell
{
    private static readonly char[] FormulaTriggers = { '=', '+', '-', '@', '\t', '\r' };
    private static readonly char[] QuoteTriggers = { ',', '"', '\r', '\n' };

    /// <summary>
    /// Returns a CSV-safe, formula-injection-safe representation of the value.
    /// </summary>
    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Formula-injection guard: neutralize a leading formula trigger by
        // prefixing a single quote, which spreadsheets render as literal text.
        if (Array.IndexOf(FormulaTriggers, value[0]) >= 0)
            value = "'" + value;

        // RFC4180 quoting: double embedded quotes, wrap if the cell contains
        // a comma, quote, or line break.
        if (value.IndexOfAny(QuoteTriggers) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
