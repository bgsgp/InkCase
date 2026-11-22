namespace Lmm.Core.Model;

/// <summary>Old "qs" entry: one question within a Section.</summary>
public sealed class Question
{
    /// <summary>Old "n": display number within the section.</summary>
    public int Number { get; set; }

    /// <summary>Stable id, independent of display Number (which can be renumbered on edit).</summary>
    public string Id { get; set; } = "";

    /// <summary>Old "key": the skill/knowledge-point tag (module-defined vocabulary).</summary>
    public string SkillTag { get; set; } = "";

    /// <summary>Old "stem".</summary>
    public string Stem { get; set; } = "";

    public List<Option> Options { get; set; } = [];

    /// <summary>Old "ans": the correct option letter (or free-text answer for non-MC question types).</summary>
    public string CorrectAnswer { get; set; } = "";

    /// <summary>Old "body": the explanation shown during review.</summary>
    public string Explanation { get; set; } = "";

    /// <summary>Old "wrong": analysis of why each distractor is wrong.</summary>
    public string DistractorAnalysis { get; set; } = "";

    /// <summary>References into Paper.Assets (manifest-level), e.g. a figure this question depends on.</summary>
    public List<string> AssetRefs { get; set; } = [];
}

/// <summary>Old option entry: [{l,t}].</summary>
public sealed class Option
{
    /// <summary>Old "l": option letter (A/B/C/D...).</summary>
    public string Letter { get; set; } = "";

    /// <summary>Old "t": option text.</summary>
    public string Text { get; set; } = "";
}
