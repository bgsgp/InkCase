namespace Lmm.Plugins.Abstractions;

public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>One problem found in a section, addressed at a human-readable path.</summary>
public sealed record ValidationIssue(ValidationSeverity Severity, string Message, string? Path = null)
{
    public override string ToString() => string.IsNullOrEmpty(Path) ? Message : $"{Path}: {Message}";
}

/// <summary>
/// Result of <see cref="ISubjectModule.Validate"/>. Warnings are shown but let
/// the save go through; errors block the save and point at the offending path.
/// </summary>
public sealed class ValidationResult
{
    public static ValidationResult Valid { get; } = new([]);

    public ValidationResult(IReadOnlyList<ValidationIssue> issues) => Issues = issues;

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool IsValid => Issues.All(i => i.Severity != ValidationSeverity.Error);

    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

    public IEnumerable<string> ErrorMessages =>
        Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.ToString());

    public static ValidationResult FromIssues(IEnumerable<ValidationIssue> issues)
    {
        var list = issues.ToList();
        return list.Count == 0 ? Valid : new ValidationResult(list);
    }
}
