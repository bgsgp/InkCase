using Lmm.Core.Model;
using Lmm.Plugins.Abstractions;

namespace Lmm.Plugins.English;

/// <summary>
/// The English subject module. Everything English-specific (question type
/// vocabulary, validation, sample paper) hangs off this one class; the host
/// only talks to it through <see cref="ISubjectModule"/>.
/// </summary>
public sealed class EnglishSubjectModule : ISubjectModule
{
    public const string Id = "cn.examreview.subject.english.v1";

    private static readonly IQuestionTypeHandler[] Handlers =
    [
        new EnglishChoiceHandler(EnglishQuestionTypes.ReadingChoice),
        new EnglishChoiceHandler(EnglishQuestionTypes.ClozeChoice),
        new EnglishChoiceHandler(EnglishQuestionTypes.SevenOfFive),
    ];

    public string ModuleId => Id;

    public string DisplayName => "英语";

    public SubjectCapabilities Capabilities { get; } = new()
    {
        SupportsInk = true,
        SupportsBilingualPairs = true,
        SupportsStructureTable = true,
        SupportsDrills = true,
        SupportsAssetImages = true,
    };

    public IReadOnlyList<string> SupportedQuestionTypeIds => EnglishQuestionTypes.All;

    public IQuestionTypeHandler ResolveHandler(string questionTypeId) =>
        Handlers.FirstOrDefault(h => string.Equals(h.QuestionTypeId, questionTypeId, StringComparison.OrdinalIgnoreCase))
        ?? new EnglishFallbackHandler(questionTypeId);

    public ValidationResult Validate(Section section) => EnglishSectionValidator.Validate(section);

    /// <summary>Section type ids the English module can render.</summary>
    public IReadOnlyList<string> SupportedSectionTypeIds => EnglishQuestionTypes.SectionTypes;
}
