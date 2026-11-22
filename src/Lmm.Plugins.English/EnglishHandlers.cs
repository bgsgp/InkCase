using Lmm.Core.Model;
using Lmm.Plugins.Abstractions;

namespace Lmm.Plugins.English;

/// <summary>
/// Handler for the option-based English types (阅读理解选择题、完形填空、七选五).
/// Review and editor view models are the same card because the correction panel
/// edits the underlying <see cref="Question"/> directly.
/// </summary>
public sealed class EnglishChoiceHandler(string questionTypeId) : IQuestionTypeHandler
{
    public string QuestionTypeId { get; } = questionTypeId;

    public object CreateReviewViewModel(Question question) => new EnglishQuestionCard(question, QuestionTypeId);

    public object CreateEditorViewModel(Question question) => new EnglishQuestionCard(question, QuestionTypeId);
}

/// <summary>
/// Unknown type id from a newer module version: render what we can (stem,
/// answer, explanation) instead of refusing to open the paper.
/// </summary>
public sealed class EnglishFallbackHandler(string questionTypeId) : IQuestionTypeHandler
{
    public string QuestionTypeId { get; } = questionTypeId;

    public object CreateReviewViewModel(Question question) => new EnglishQuestionCard(question, QuestionTypeId);

    public object CreateEditorViewModel(Question question) => new EnglishQuestionCard(question, QuestionTypeId);
}
