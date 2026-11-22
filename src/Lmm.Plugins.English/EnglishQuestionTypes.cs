namespace Lmm.Plugins.English;

/// <summary>
/// Vocabulary of the English module. These strings are what Section.TypeId and
/// Question ids in a .lmm file contain; adding a type means adding it here plus
/// a handler in <see cref="EnglishSubjectModule"/>.
/// </summary>
public static class EnglishQuestionTypes
{
    public const string ReadingChoice = "reading-choice";
    public const string ClozeChoice = "cloze-choice";
    public const string SevenOfFive = "seven-of-five";
    public const string GrammarFill = "grammar-fill";
    public const string Writing = "writing";

    /// <summary>Section type ids (Section.TypeId) the reading side knows about.</summary>
    public static IReadOnlyList<string> SectionTypes { get; } =
    [
        "reading-c", "cloze", "seven-of-five", "grammar-fill", "writing", "listening",
    ];

    public static IReadOnlyList<string> All { get; } =
    [
        ReadingChoice, ClozeChoice, SevenOfFive, GrammarFill, Writing,
    ];

    public static bool IsChoice(string questionTypeId) =>
        questionTypeId is ReadingChoice or ClozeChoice or SevenOfFive;

    public static bool RequiresArticle(string sectionTypeId) =>
        sectionTypeId is "reading-c" or "cloze" or "seven-of-five";
}
