namespace Lmm.Core.Model;

/// <summary>Old "structure" row: [{p,f,c,q}] — one line of the paragraph-function breakdown table.</summary>
public sealed class StructureRow
{
    /// <summary>Old "p": paragraph label (e.g. "Para 1" / "第一段").</summary>
    public string ParaLabel { get; set; } = "";

    /// <summary>Old "f": the paragraph's rhetorical function (e.g. "点题" / "举例论证").</summary>
    public string Function { get; set; } = "";

    /// <summary>Old "c": a short content note for the paragraph.</summary>
    public string ContentNote { get; set; } = "";

    /// <summary>Old "q": question numbers this paragraph relates to.</summary>
    public List<int> QuestionRefs { get; set; } = [];
}
