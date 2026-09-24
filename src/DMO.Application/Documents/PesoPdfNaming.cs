namespace DMO.Application.Documents;

/// <summary>
/// The deterministic Peso document naming convention (P2-T05 contract §12.1 / delta §7.4):
/// <c>&lt;reference&gt;/&lt;production-number&gt;/Peso_&lt;reference&gt;_&lt;machine&gt;.pdf</c>.
/// </summary>
/// <remarks>
/// The input facts (reference / production number / machine) come from the Job On traversal facts
/// of the SHARED Peso read model — this class never redefines them and never stores them; it only
/// derives the deterministic OUTPUT target from them. Validation FAILS CLOSED: a value that cannot
/// form a safe single path segment (blank, path separators, reserved filename characters, "." or
/// "..", surrounding whitespace, over-long) yields <c>false</c> — the target is never silently
/// altered to fit, and path traversal is impossible through the document target.</remarks>
public static class PesoPdfNaming
{
    /// <summary>Maximum length of one path segment (reference/production number/machine).</summary>
    public const int MaxSegmentLength = 120;

    /// <summary>
    /// Composes the deterministic target path from the supplied Job On traversal facts, or returns
    /// <c>false</c> (with <c>path</c> left <c>null</c>) when any segment cannot form a safe single
    /// path segment. The values are used verbatim when valid — never normalized or rewritten.
    /// </summary>
    public static bool TryCompose(
        string? reference,
        string? productionNumber,
        string? machine,
        out PesoPdfPath? path)
    {
        if (!IsValidSegment(reference)
            || !IsValidSegment(productionNumber)
            || !IsValidSegment(machine))
        {
            path = null;
            return false;
        }

        var fileName = $"Peso_{reference}_{machine}.pdf";
        var relativeDirectory = $"{reference}/{productionNumber}";

        path = new PesoPdfPath(
            relativeDirectory,
            fileName,
            $"{relativeDirectory}/{fileName}");

        return true;
    }

    /// <summary>
    /// Whether a value can be ONE safe path segment: non-blank, no surrounding whitespace, no path
    /// separators, no reserved filename characters anywhere in the platform set, not "." or "..",
    /// and within the length bound.
    /// </summary>
    private static bool IsValidSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length > MaxSegmentLength)
        {
            return false;
        }

        // Surrounding whitespace is refused verbatim (never silently trimmed into a different
        // folder/file name than the one the operator sees).
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        // Path separators are refused explicitly (portable across platforms) in addition to the
        // platform invalid-name set, so a traversal attempt can never escape the base directory.
        if (value.Contains('/') || value.Contains('\\'))
        {
            return false;
        }

        if (value is "." or "..")
        {
            return false;
        }

        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return true;
    }
}