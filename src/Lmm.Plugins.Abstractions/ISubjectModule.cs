using Lmm.Core.Model;

namespace Lmm.Plugins.Abstractions;

/// <summary>
/// One installed subject (English today, possibly maths later). The shell stays
/// subject-agnostic: question vocabularies, validation rules and the
/// review/editor view models all live behind this contract.
/// </summary>
public interface ISubjectModule
{
    /// <summary>Stable id written into manifest.moduleId, e.g. "cn.examreview.subject.english.v1".</summary>
    string ModuleId { get; }

    /// <summary>Shown to the user when picking/creating a paper.</summary>
    string DisplayName { get; }

    SubjectCapabilities Capabilities { get; }

    /// <summary>Question type ids this module can resolve a handler for.</summary>
    IReadOnlyList<string> SupportedQuestionTypeIds { get; }

    /// <summary>
    /// Returns the handler for a question type id. Unknown ids fall back to a
    /// generic handler instead of throwing: a .lmm file written by a newer
    /// version of the module must still open read-only rather than crash.
    /// </summary>
    IQuestionTypeHandler ResolveHandler(string questionTypeId);

    /// <summary>
    /// Section-level content check (duplicate numbers, answer letters that
    /// aren't among the options, spans pointing past the paragraph text, ...).
    /// The host calls this before saving and shows the issues; it never
    /// silently auto-repairs the paper.
    /// </summary>
    ValidationResult Validate(Section section);
}
