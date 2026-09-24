using System.Text;
using System.Text.RegularExpressions;
using DMO.Application.ControloCreate;
using DMO.Application.Documents;

namespace DMO.UnitTests.Documents;

/// <summary>
/// Unit proofs of the dependency-free Peso PDF renderer: valid PDF 1.4 bytes, the fixed
/// identification reading order (Referência → Produção → Máquina → Data → CM → Processo → Estado →
/// Lote), no Boquilha in the main identification, the lot as a plain value, the water mass as
/// input and the functional comparison as Peso + Volume de água per reading, ALL N rows rendered
/// (auto-pagination, nothing fixed at 4 lines), per-CM deviations and averages, and no filesystem
/// path anywhere in the output.
/// </summary>
public sealed class PesoPdfRendererTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 22, 14, 5, 0, TimeSpan.Zero);

    private static readonly Encoding WinAnsi = CreateWinAnsi();

    /// <summary>The PDF WinAnsiEncoding (Windows-1252), used to decode the raw rendered bytes.</summary>
    private static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private readonly PesoPdfRenderer _renderer = new();

    [Fact]
    public void Render_ProducesAValidPdfContainer()
    {
        var bytes = Render(PesoPdfFixtures.DecidedSheet(), GeneratedAt);

        var text = WinAnsi.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF", text.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Page ", text, StringComparison.Ordinal);
        Assert.Contains("startxref", text, StringComparison.Ordinal);

        // Every stream has a matching endstream.
        Assert.Equal(
            Regex.Matches(text, @"\nstream\n").Count,
            Regex.Matches(text, @"\nendstream\n").Count);

        // The xref table is consistent: startxref points at the xref keyword and EVERY entry's
        // recorded offset resolves to exactly its "<n> 0 obj" declaration (a viewer can walk the
        // whole object graph).
        var startxref = Regex.Match(text, @"startxref\s+(\d+)");
        Assert.True(startxref.Success, "The trailer declares a startxref offset.");
        var xrefOffset = int.Parse(startxref.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var atOffset = WinAnsi.GetString(bytes, xrefOffset, Math.Min(24, bytes.Length - xrefOffset));
        Assert.StartsWith("xref", atOffset, StringComparison.Ordinal);

        var xrefLines = WinAnsi.GetString(bytes, xrefOffset, bytes.Length - xrefOffset)
            .Split('\n');
        var countTokens = xrefLines[1].Trim().Split(' ');
        Assert.Equal("0", countTokens[0]);
        var objectCount = int.Parse(countTokens[1], System.Globalization.CultureInfo.InvariantCulture);

        for (var number = 0; number < objectCount; number++)
        {
            var entry = xrefLines[2 + number].Trim().Split(' ');
            Assert.Equal(3, entry.Length);
            var entryOffset = long.Parse(entry[0], System.Globalization.CultureInfo.InvariantCulture);

            if (number == 0)
            {
                Assert.Equal("f", entry[2]); // the free head entry
                continue;
            }

            Assert.Equal("n", entry[2]);
            var atEntry = WinAnsi.GetString(bytes, (int)entryOffset, Math.Min(32, bytes.Length - (int)entryOffset));
            Assert.StartsWith($"{number} 0 obj", atEntry, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_IdentificationBlockFollowsTheFixedReadingOrder()
    {
        var text = ExtractStrings(PesoPdfFixtures.DecidedSheet());

        var expectedOrder = new[]
        {
            "Referência", "Produção", "Máquina", "Data", "CM", "Processo", "Estado", "Lote",
        };

        var indexes = expectedOrder
            .Select(label => Array.IndexOf(text, label))
            .ToList();

        Assert.All(indexes, index => Assert.True(index >= 0, "A label of the identification block is missing."));
        Assert.Equal(indexes.OrderBy(index => index), indexes);
    }

    [Fact]
    public void Render_IdentificationShowsTheExactFacts_NoBoquilha_PlainLot()
    {
        var text = ExtractStrings(PesoPdfFixtures.DecidedSheet());

        // The facts of the identification block, verbatim (frozen read-model facts).
        Assert.Contains("REF-X", text);
        Assert.Contains("2026-001", text);
        Assert.Contains("B1", text);
        Assert.Contains("2026-09-19", text); // Data = the Job On production date (never SubmittedAt)
        Assert.Contains("CM-1100", text);
        Assert.Contains("NNPB", text);
        Assert.Contains("Aprovado", text);

        // The lot comes right after the Lote label as the PLAIN frozen value — no "L" prefix.
        var loteIndex = Array.IndexOf(text, "Lote");
        Assert.Equal("07", text[loteIndex + 1]);

        // No Boquilha/nozzle fact in the MAIN identification block (between the Identificação
        // heading and the Entradas de cálculo heading): "BQ" elsewhere (e.g. the drawing input
        // "Volume Marisa/BQ") is a different fact and is not affected by this rule.
        var identificationStart = Array.IndexOf(text, "Identificação");
        var inputsStart = Array.IndexOf(text, "Entradas de cálculo");
        var identificationBlock = text[identificationStart..inputsStart];
        Assert.DoesNotContain(identificationBlock, value => value.Contains("Boquilha", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(identificationBlock, value => value.Contains("BQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Render_ShowsTheWaterMassAsInputAndPesoPlusVolumeDeAguaAsTheComparison()
    {
        var text = ExtractStrings(PesoPdfFixtures.DecidedSheet());

        Assert.Contains("Entradas de cálculo", text);
        Assert.Contains("Peso em água (g)", text);
        Assert.Contains("Volume de água (cm³)", text);
        Assert.Contains("Peso (g)", text);
        Assert.Contains(text, value => value.Contains("comparação funcional", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Render_PrintsEveryReading_WithThisSheetsDeviations()
    {
        var sheet = PesoPdfFixtures.DecidedSheet(rowCount: 4);
        var text = ExtractStrings(sheet);

        // All 4 reading positions are present as standalone cells — nothing fixed at 4 lines,
        // nothing dropped.
        for (var position = 1; position <= 4; position++)
        {
            Assert.Contains(position.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        }

        // Per-CM deviation: the FIRST row's deviation against THIS sheet's own average capacity
        // (the values are this CM's — never a global average).
        var expectedAverage = sheet.Rows.Average(row => row.CapacityCm3);
        var expectedDeviation = sheet.Rows[0].CapacityCm3 - expectedAverage;
        Assert.Contains(expectedDeviation.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), text);

        // This sheet's own averages are the summary facts.
        Assert.Contains(text, value => value.Contains("Resumo — valores deste controlo (CM)", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_PaginatesManyReadingsAndRepeatsTheTableHeader()
    {
        var sheet = PesoPdfFixtures.DecidedSheet(rowCount: 60);
        var bytes = Render(sheet, GeneratedAt);
        var text = WinAnsi.GetString(bytes);

        // 60 readings overflow one page: multiple pages + the continuation heading + a repeated
        // readings table header.
        var pageCount = Regex.Matches(text, @"/Type /Page ").Count;
        Assert.True(pageCount >= 2, $"Expected multiple pages, got {pageCount}.");

        Assert.Contains("Peso — continuação", text, StringComparison.Ordinal);

        // The readings header must repeat on the continuation page (counted on the UNESCAPED
        // extracted literals — the raw stream escapes the parentheses).
        var strings = ExtractStrings(sheet);
        Assert.True(
            strings.Count(value => value == "Volume de água (cm³)") >= 2,
            "The readings header must repeat on the continuation page.");

        // Every one of the 60 reading positions appears somewhere in the rendered output.
        for (var position = 1; position <= 60; position++)
        {
            Assert.Contains(position.ToString(System.Globalization.CultureInfo.InvariantCulture), strings);
        }
    }

    [Fact]
    public void Render_NeverPrintsAFilesystemPath()
    {
        var text = WinAnsi.GetString(Render(PesoPdfFixtures.DecidedSheet(), GeneratedAt));

        Assert.DoesNotContain("file:///", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\\", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IsDeterministicForTheSameModelAndTimestamp()
    {
        var document = PesoPdfComposer.Compose(PesoPdfFixtures.DecidedSheet());

        Assert.Equal(
            _renderer.Render(document, GeneratedAt),
            _renderer.Render(document, GeneratedAt));
    }

    // -----------------------------------------------------------------------------------------

    private byte[] Render(PesoSheetReadModel sheet, DateTimeOffset generatedAt) =>
        _renderer.Render(PesoPdfComposer.Compose(sheet), generatedAt);

    private string[] ExtractStrings(PesoSheetReadModel sheet) =>
        ExtractStrings(Render(sheet, GeneratedAt));

    /// <summary>Extracts every rendered string literal (WinAnsi-decoded, unescaped).</summary>
    private static string[] ExtractStrings(byte[] pdf)
    {
        var text = WinAnsi.GetString(pdf);
        var streams = Regex.Matches(text, @"stream\n(.*?)\nendstream", RegexOptions.Singleline)
            .Select(match => match.Groups[1].Value)
            .ToList();

        var strings = new List<string>();
        foreach (var stream in streams)
        {
            foreach (Match match in Regex.Matches(stream, @"\(((?:[^()\\]|\\.)*)\)"))
            {
                strings.Add(match.Groups[1].Value
                    .Replace("\\(", "(")
                    .Replace("\\)", ")")
                    .Replace("\\\\", "\\"));
            }
        }

        return strings.ToArray();
    }
}