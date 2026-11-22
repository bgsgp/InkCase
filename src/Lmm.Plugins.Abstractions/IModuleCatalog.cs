namespace Lmm.Plugins.Abstractions;

/// <summary>
/// The host's only view of installed subject modules. Today it is backed by
/// <see cref="ManualModuleCatalog"/>; a later assembly-scanning catalog can be
/// swapped in without touching the shell.
/// </summary>
public interface IModuleCatalog
{
    IReadOnlyList<ISubjectModule> Modules { get; }

    /// <summary>Returns null when the module isn't installed.</summary>
    ISubjectModule? Find(string moduleId);

    /// <summary>Returns the module, or throws <see cref="ModuleNotInstalledException"/>.</summary>
    ISubjectModule Resolve(string moduleId);
}
