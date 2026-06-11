using Mail2SNMP.Web;

namespace Mail2SNMP.Tests.Web;

/// <summary>
/// Peer-review: CsvCell is the spreadsheet formula-injection defence for the
/// Events/AuditLog/DeadLetters CSV exports. It was structurally untestable until
/// the test project referenced Mail2SNMP.Web. These tests pin both the
/// formula-neutralization and the RFC4180 quoting.
/// </summary>
public class CsvCellTests
{
    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1:A9)", "'@SUM(A1:A9)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    public void Quote_NeutralizesLeadingFormulaTrigger(string input, string expected)
    {
        // Cells with no comma/quote/newline are not RFC-wrapped, so the result is
        // just the single-quote-prefixed literal.
        Assert.Equal(expected, CsvCell.Quote(input));
    }

    [Fact]
    public void Quote_FormulaTriggerWithComma_IsPrefixedThenRfcWrapped()
    {
        // Leading '=' → prefix '\'' ; embedded comma → RFC4180 double-quote wrap.
        Assert.Equal("\"'=1,2\"", CsvCell.Quote("=1,2"));
    }

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("user@example.com is fine mid-cell", "user@example.com is fine mid-cell")]
    public void Quote_BenignText_PassesThrough(string input, string expected)
    {
        Assert.Equal(expected, CsvCell.Quote(input));
    }

    [Fact]
    public void Quote_EmbeddedComma_IsWrapped()
    {
        Assert.Equal("\"a,b\"", CsvCell.Quote("a,b"));
    }

    [Fact]
    public void Quote_EmbeddedQuote_IsDoubledAndWrapped()
    {
        Assert.Equal("\"he said \"\"hi\"\"\"", CsvCell.Quote("he said \"hi\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Quote_NullOrEmpty_ReturnsEmpty(string? input)
    {
        Assert.Equal(string.Empty, CsvCell.Quote(input));
    }
}
