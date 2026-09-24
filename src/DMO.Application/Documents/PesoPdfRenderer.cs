using System.Globalization;
using System.Text;

namespace DMO.Application.Documents;

/// <summary>
/// The Peso PDF renderer contract: pure byte generation from the composed document model.
/// </summary>
/// <remarks>
/// The renderer is a pure function of the supplied model and generation timestamp: it performs no
/// file IO, no reads, no calculations and no state. It never prints a local filesystem path
/// (<c>file:///</c> or otherwise) — the document carries only record facts and document dates.
/// The deterministic filename convention is NOT printed either: it is an output convention, not a
/// record fact.</remarks>
public interface IPesoPdfRenderer
{
    /// <summary>Renders the Peso PDF bytes (multi-page when the reading table overflows).</summary>
    byte[] Render(PesoPdfDocumentModel model, DateTimeOffset generatedAt);
}

/// <summary>
/// The dependency-free PDF 1.4 renderer (A4, Helvetica, WinAnsiEncoding = Latin-1) over the
/// composed <see cref="PesoPdfDocumentModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// Document structure (fixed desktop geometry of the paper, not the browser):
/// title → identification block in the fixed reading order (Referência, Produção, Máquina, Data,
/// CM, Processo, Estado, Lote — no Boquilha, lot as a plain value) → calculation inputs (the
/// water MASS per reading is listed as input inside the readings table, never as a comparison) →
/// readings table (N rows, all of them — nothing is fixed at 4 lines) with the functional
/// comparison per reading = <b>Volume de água</b> and <b>Peso</b> plus the per-reading deviation
/// against THIS sheet's own average (per-CM values, never a global average) → per-CM summary →
/// footer with the record identity and the generation timestamp.</para>
/// <para>
/// The long table is paginated automatically: continuation pages repeat the table header; every
/// page carries the same footer. No filesystem path, no base directory and no machine code of any
/// storage location ever appears in the produced bytes.</para>
/// </remarks>
public sealed class PesoPdfRenderer : IPesoPdfRenderer
{
    // A4 geometry (points). Content band is tracked top-down; PDF coordinates are bottom-up.
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double LeftMargin = 48;
    private const double RightMargin = 48;
    private const double TopMargin = 56;
    private const double BottomLimit = 800;
    private const double FooterY = 818;

    private const double TitleHeight = 34;
    private const double SectionHeight = 20;
    private const double IdentificationRowHeight = 21;
    private const double InputRowHeight = 17;
    private const double CaptionHeight = 14;
    private const double TableHeaderHeight = 18;
    private const double TableRowHeight = 15;
    private const double SummaryRowHeight = 16;
    private const double ContinuationHeadingHeight = 26;

    private static readonly (string Heading, double X, double Width, bool Numeric)[] TableColumns =
    [
        ("Leitura", 48, 45, false),
        ("Peso em água (g)", 93, 90, true),
        ("Volume de água (cm³)", 183, 102, true),
        ("Desvio (cm³)", 285, 72, true),
        ("Desvio (%)", 357, 72, true),
        ("Peso (g)", 429, 118, true),
    ];

    /// <inheritdoc />
    public byte[] Render(PesoPdfDocumentModel model, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(model);

        var pages = new List<PageBuilder>();
        var page = StartPage(pages, continuation: false);

        DrawTitle(page, model);

        // ---- Identification (fixed reading order; one fact per row; no Boquilha) ------------
        DrawSectionHeading(page, "Identificação");
        DrawIdentification(page, model);

        // ---- Calculation inputs (the water mass itself belongs to the readings table below) --
        DrawSectionHeading(page, "Entradas de cálculo");
        DrawInputs(page, model);

        // ---- Readings table: N rows; the comparison per reading is Volume de água + Peso -----
        DrawSectionHeading(page, "Leituras");
        page.Text(
            "O Peso em água é entrada de cálculo. A comparação funcional por leitura utiliza o " +
            "Volume de água e o Peso — valores deste CM, nunca médias globais.",
            x: LeftMargin,
            y: page.Cursor,
            font: Font.Regular,
            size: 8);
        page.Cursor += CaptionHeight;
        DrawTableHeader(page);

        foreach (var row in model.Rows)
        {
            if (!page.Fits(TableRowHeight))
            {
                page = StartPage(pages, continuation: true);
                DrawTableHeader(page);
            }

            DrawTableRow(page, row, model.AverageCapacityCm3, model.AverageGlassWeightG);
        }

        // ---- Per-CM summary (this sheet's own values; never a global average) ----------------
        if (!page.Fits(SectionHeight + SummaryRowHeight * 4 + 8))
        {
            page = StartPage(pages, continuation: true);
        }

        DrawSectionHeading(page, "Resumo — valores deste controlo (CM)");
        DrawSummary(page, model);

        // ---- Footer on every page ------------------------------------------------------------
        for (var index = 0; index < pages.Count; index++)
        {
            pages[index].DrawFooter(
                model,
                pageNumber: index + 1,
                pageCount: pages.Count,
                generatedAt);
        }

        return Serialize(pages, generatedAt);
    }

    // -----------------------------------------------------------------------------------------
    // Layout primitives
    // -----------------------------------------------------------------------------------------

    private static void DrawTitle(PageBuilder page, PesoPdfDocumentModel model)
    {
        page.Text("Peso", x: LeftMargin, y: page.Cursor, font: Font.Bold, size: 16);
        page.Cursor += TitleHeight;
        page.Line(fromX: LeftMargin, toX: PageWidth - RightMargin, y: page.Cursor, heavy: true);
        page.Cursor += 10;

        if (!page.Fits(SectionHeight + IdentificationRowHeight * 8))
        {
            throw new InvalidOperationException(
                "The identification block cannot fit a single page; the layout is broken.");
        }
    }

    private static void DrawSectionHeading(PageBuilder page, string heading)
    {
        page.Text(heading, x: LeftMargin, y: page.Cursor, font: Font.Bold, size: 10);
        page.Cursor += SectionHeight;
    }

    private static void DrawIdentification(PageBuilder page, PesoPdfDocumentModel model)
    {
        var entries = new (string Label, string Value)[]
        {
            ("Referência", model.Reference),
            ("Produção", model.ProductionNumber),
            ("Máquina", model.Machine),
            ("Data", model.Date),
            ("CM", model.CmReference),
            ("Processo", model.Processo ?? "—"),
            ("Estado", model.EstadoLabel),
            ("Lote", model.Lot),
        };

        foreach (var entry in entries)
        {
            page.Text(entry.Label, x: LeftMargin, y: page.Cursor, font: Font.Bold, size: 9);
            page.Text(entry.Value, x: LeftMargin + 140, y: page.Cursor, font: Font.Regular, size: 10.5);
            page.Cursor += IdentificationRowHeight;
        }
    }

    private static void DrawInputs(PageBuilder page, PesoPdfDocumentModel model)
    {
        var inputs = new (string Label, string Value)[]
        {
            ("Temperatura da água (°C)", Fmt(model.WaterTemperature)),
            ("Volume Marisa/BQ (cm³)", Fmt(model.VolumeMarisaBq)),
            ("Volume Punção/PU (cm³)", Fmt(model.VolumePuncaoPu)),
            ("Densidade do vidro (g/cm³)", Fmt(model.GlassDensityGCm3)),
            ("Fim da produção anterior (SAP)", model.PreviousProductionEndReference ?? "—"),
            ("Peso médio anterior (SAP)", model.PreviousAverageWeightReference ?? "—"),
        };

        foreach (var input in inputs)
        {
            page.Text(input.Label, x: LeftMargin, y: page.Cursor, font: Font.Bold, size: 9);
            page.Text(input.Value, x: LeftMargin + 220, y: page.Cursor, font: Font.Regular, size: 10);
            page.Cursor += InputRowHeight;
        }
    }

    private static void DrawTableHeader(PageBuilder page)
    {
        var y = page.Cursor;

        foreach (var column in TableColumns)
        {
            DrawCell(page, column.Heading, column: column, y: y, alignRight: column.Numeric);
        }

        page.Cursor += TableHeaderHeight;
        page.Line(fromX: LeftMargin, toX: LeftMargin + TableColumns.Sum(column => column.Width), y: page.Cursor, heavy: true);
    }

    private static void DrawTableRow(
        PageBuilder page,
        PesoPdfRowModel row,
        decimal? averageCapacity,
        decimal? averageGlass)
    {
        var y = page.Cursor;
        var deviationCm3 = averageCapacity is { } average ? row.CapacityCm3 - average : (decimal?)null;
        var deviationPercent = averageCapacity is { } avg && avg != 0
            ? (row.CapacityCm3 - avg) / avg * 100
            : (decimal?)null;

        var cells = new (string Value, int ColumnIndex)[]
        {
            (row.Position.ToString(CultureInfo.InvariantCulture), 0),
            (Fmt(row.WaterWeightG), 1),
            (Fmt(row.CapacityCm3), 2),
            (Fmt(deviationCm3), 3),
            (deviationPercent is null ? "—" : Fmt(deviationPercent) + "%", 4),
            (Fmt(row.GlassWeightG), 5),
        };

        foreach (var cell in cells)
        {
            var column = TableColumns[cell.ColumnIndex];
            DrawCell(page, cell.Value, column, y, alignRight: column.Numeric);
        }

        page.Cursor += TableRowHeight;
        page.Line(
            fromX: LeftMargin,
            toX: LeftMargin + TableColumns.Sum(column => column.Width),
            y: page.Cursor,
            heavy: false);
    }

    private static void DrawCell(
        PageBuilder page,
        string value,
        (string Heading, double X, double Width, bool Numeric) column,
        double y,
        bool alignRight)
    {
        var x = alignRight ? column.X + column.Width - 4 : column.X + 4;
        page.Text(value, x, y, Font.Regular, 9, alignRight);
    }

    private static void DrawSummary(PageBuilder page, PesoPdfDocumentModel model)
    {
        var summary = new (string Label, string Value)[]
        {
            ("Média em água (g)", Fmt(model.AverageWaterWeightG)),
            ("Volume de água médio (cm³)", Fmt(model.AverageCapacityCm3)),
            ("Peso médio (g)", Fmt(model.AverageGlassWeightG)),
            ("Densidade usada (g/cm³)", Fmt(model.GlassDensityGCm3)),
        };

        foreach (var entry in summary)
        {
            page.Text(entry.Label, x: LeftMargin, y: page.Cursor, font: Font.Bold, size: 9);
            page.Text(entry.Value, x: LeftMargin + 220, y: page.Cursor, font: Font.Regular, size: 10);
            page.Cursor += SummaryRowHeight;
        }
    }

    private static string Fmt(decimal? value) =>
        value is null ? "—" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    private static PageBuilder StartPage(List<PageBuilder> pages, bool continuation)
    {
        var page = new PageBuilder();
        pages.Add(page);

        if (continuation)
        {
            page.Text(
                "Peso — continuação",
                x: LeftMargin,
                y: page.Cursor,
                font: Font.Bold,
                size: 10);
            page.Cursor += ContinuationHeadingHeight;
        }

        return page;
    }

    /// <summary>
    /// Windows-1252 (the PDF <c>WinAnsiEncoding</c>) covers the Portuguese labels and the
    /// punctuation (°, ³, —) of the document; Latin-1 would silently replace them with '?'. The
    /// <c>CodePagesEncodingProvider</c> ships inside the shared .NET framework — no package is
    /// needed.
    /// </summary>
    private static readonly Encoding WinAnsi = CreateWinAnsi();

    private static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    // -----------------------------------------------------------------------------------------
    // Serialization (PDF 1.4 objects + xref)
    // -----------------------------------------------------------------------------------------

    private static byte[] Serialize(IReadOnlyList<PageBuilder> pages, DateTimeOffset generatedAt)
    {
        // Object numbering: 1 catalog, 2 pages, then (page, contents) pairs, then fonts and info.
        var fontRegularNumber = 3 + pages.Count * 2;
        var fontBoldNumber = fontRegularNumber + 1;
        var infoNumber = fontRegularNumber + 2;
        var objectCount = infoNumber;

        using var output = new MemoryStream();
        var offsets = new long[objectCount + 1];

        // The PDF header — mandatory first line of every PDF file.
        var header = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        output.Write(header, 0, header.Length);

        void WriteObject(int number, string body)
        {
            offsets[number] = output.Position;
            var text = $"{number} 0 obj\n{body}\nendobj\n";
            var bytes = WinAnsi.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");

        var kids = string.Join(
            " ",
            Enumerable.Range(0, pages.Count).Select(index => $"{3 + index * 2} 0 R"));
        WriteObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageNumber = 3 + index * 2;
            var contentsNumber = pageNumber + 1;
            var content = pages[index].Content.ToString();
            var contentBytes = WinAnsi.GetBytes(content);

            WriteObject(
                pageNumber,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(PageWidth)} {F(PageHeight)}] " +
                $"/Resources << /Font << /F1 {fontRegularNumber} 0 R /F2 {fontBoldNumber} 0 R >> >> " +
                $"/Contents {contentsNumber} 0 R >>");

            WriteObject(
                contentsNumber,
                $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream");
        }

        WriteObject(
            fontRegularNumber,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        WriteObject(
            fontBoldNumber,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        WriteObject(
            infoNumber,
            $"<< /Title (Peso) /Producer (DMO) /CreationDate (D:{generatedAt:yyyyMMddHHmmss}Z) >>");

        var xrefOffset = output.Position;
        var xref = new StringBuilder();
        xref.Append("xref\n");
        xref.Append($"0 {objectCount + 1}\n");
        xref.Append("0000000000 65535 f \n");
        for (var number = 1; number <= objectCount; number++)
        {
            xref.Append($"{offsets[number]:0000000000} 00000 n \n");
        }

        xref.Append("trailer\n");
        xref.Append($"<< /Size {objectCount + 1} /Root 1 0 R /Info {infoNumber} 0 R >>\n");
        xref.Append("startxref\n");
        xref.Append($"{xrefOffset}\n");
        xref.Append("%%EOF");

        var xrefBytes = Encoding.ASCII.GetBytes(xref.ToString());
        output.Write(xrefBytes, 0, xrefBytes.Length);

        return output.ToArray();
    }

    private static string F(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private enum Font
    {
        Regular,
        Bold,
    }

    /// <summary>One page's content stream builder with the top-down cursor.</summary>
    private sealed class PageBuilder
    {
        public PageBuilder()
        {
            Cursor = TopMargin;
        }

        public StringBuilder Content { get; } = new();

        public double Cursor { get; set; }

        public bool Fits(double height) => Cursor + height <= BottomLimit;

        public void Text(string value, double x, double y, Font font, double size, bool alignRight = false)
        {
            var width = Measure(value, font, size);
            var drawX = alignRight ? x - width : x;
            var pdfY = PageHeight - y;

            Content.Append("BT ");
            Content.Append(font == Font.Bold ? "/F2 " : "/F1 ");
            Content.Append(F(size)).Append(" Tf ");
            Content.Append(F(drawX)).Append(' ').Append(F(pdfY)).Append(" Td ");
            Content.Append('(').Append(Escape(value)).Append(") Tj ET\n");
        }

        public void Line(double fromX, double toX, double y, bool heavy)
        {
            var pdfY = PageHeight - y;
            Content.Append(heavy ? "1 w " : "0.5 w ");
            Content.Append("0.45 0.45 0.45 RG ");
            Content.Append(F(fromX)).Append(' ').Append(F(pdfY)).Append(" m ");
            Content.Append(F(toX)).Append(' ').Append(F(pdfY)).Append(" l S\n");
        }

        public void DrawFooter(
            PesoPdfDocumentModel model,
            int pageNumber,
            int pageCount,
            DateTimeOffset generatedAt)
        {
            Line(LeftMargin, PageWidth - RightMargin, FooterY - 10, heavy: false);

            Text(
                $"Peso {model.PesoId} · versão {model.Version} · página {pageNumber} de {pageCount} · " +
                $"gerado em {generatedAt:yyyy-MM-dd HH:mm} UTC",
                x: LeftMargin,
                y: FooterY,
                font: Font.Regular,
                size: 8);
        }

        private static double Measure(string value, Font font, double size) =>
            value.Length * size * 0.5;

        private static string Escape(string value)
        {
            // Only the parentheses/backslash of the literal need escaping; every other character
            // of the Latin-1 text is a raw byte in the WinAnsiEncoding string.
            var builder = new StringBuilder(value.Length + 8);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '(':
                        builder.Append("\\(");
                        break;
                    case ')':
                        builder.Append("\\)");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}