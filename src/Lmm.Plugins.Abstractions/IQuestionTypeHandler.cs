using Lmm.Core.Model;

namespace Lmm.Plugins.Abstractions;

/// <summary>
/// Per-question-type rendering contract. The host only ever sees <see cref="object"/>;
/// handlers that produce a <see cref="IQuestionCardViewModel"/> get the built-in
/// card renderer, anything else falls back to a plain text panel.
/// </summary>
public interface IQuestionTypeHandler
{
    string QuestionTypeId { get; }

    /// <summary>ViewModel for the review (讲评) panel.</summary>
    object CreateReviewViewModel(Question question);

    /// <summary>ViewModel for the correction (试卷修正) panel.</summary>
    object CreateEditorViewModel(Question question);
}
