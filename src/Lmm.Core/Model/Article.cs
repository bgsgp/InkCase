namespace Lmm.Core.Model;

/// <summary>
/// A section's reading passage, as structured paragraphs rather than
/// pre-rendered HTML. The old app embedded
/// &lt;span class="hl" data-q="N"&gt; (evidence highlight) and
/// &lt;span class="blank" data-q="N"&gt; (cloze/七选五 blank) directly inside
/// an HTML string; here those become typed Spans so the WPF side decides
/// how to render them (Run/Inline styling) without re-implementing an HTML
/// parser.
/// </summary>
public sealed class Article
{
    public List<Paragraph> Paragraphs { get; set; } = [];
}

public sealed class Paragraph
{
    /// <summary>0-based paragraph index within the article.</summary>
    public int Index { get; set; }

    /// <summary>Full plain text of the paragraph (Spans below are offsets/markers into this text).</summary>
    public string Text { get; set; } = "";

    public List<Span> Spans { get; set; } = [];
}

/// <summary>A highlighted or blanked-out run of text within a Paragraph, referencing a Question.</summary>
public sealed class Span
{
    public SpanKind Kind { get; set; }

    /// <summary>Character offset into the owning Paragraph's Text.</summary>
    public int Start { get; set; }

    /// <summary>Character length of the span.</summary>
    public int Length { get; set; }

    /// <summary>Which question this span relates to (evidence for an answer, or a blank to fill in).</summary>
    public int QuestionRef { get; set; }
}

public enum SpanKind
{
    /// <summary>Old "hl": text that is evidence/support for an answer.</summary>
    Evidence,

    /// <summary>Old "blank": a cloze / 七选五 blank the student fills in.</summary>
    Blank,
}
