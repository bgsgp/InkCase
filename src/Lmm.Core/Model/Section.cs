namespace Lmm.Core.Model;

/// <summary>
/// One section of a paper (e.g. Reading A/B/C/D, 七选五, 完形填空, ...).
/// Field names map 1:1 onto the legacy app's real `Section` object shape
/// (tab/meta/title/gist/article/structure/qs/bi/drill), not a generalized guess.
/// </summary>
public sealed class Section
{
    public string Id { get; set; } = "";

    /// <summary>Old "tab": which kind of section this is (module-defined vocabulary, e.g. "reading-c").</summary>
    public string TypeId { get; set; } = "";

    /// <summary>Old "title".</summary>
    public string Title { get; set; } = "";

    /// <summary>Old "meta": short descriptor line (e.g. word count / difficulty tag).</summary>
    public string Meta { get; set; } = "";

    /// <summary>Old "gist": one-sentence summary of the passage/section.</summary>
    public string Gist { get; set; } = "";

    /// <summary>Old "article", now structured instead of pre-rendered HTML.</summary>
    public Article? Article { get; set; }

    /// <summary>Old "structure": [{p,f,c,q}] paragraph-function table.</summary>
    public List<StructureRow> Structure { get; set; } = [];

    /// <summary>Old "qs".</summary>
    public List<Question> Questions { get; set; } = [];

    /// <summary>Old "bi": [{en,zh}] bilingual sentence pairs.</summary>
    public List<BilingualPair> Bilingual { get; set; } = [];

    /// <summary>Old "drill": [{h,q,a}] in-class practice drills.</summary>
    public List<Drill> Drills { get; set; } = [];
}
