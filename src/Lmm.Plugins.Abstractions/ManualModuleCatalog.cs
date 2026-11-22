namespace Lmm.Plugins.Abstractions;

/// <summary>
/// Hand-registered catalog: with exactly one shipped module, assembly scanning
/// / MEF would be over-engineering. The shell only depends on
/// <see cref="IModuleCatalog"/>, so the swap later is a one-class change.
/// </summary>
public sealed class ManualModuleCatalog : IModuleCatalog
{
    private readonly Dictionary<string, ISubjectModule> _byId;

    public ManualModuleCatalog(IEnumerable<ISubjectModule> modules)
    {
        var list = modules.ToList();
        _byId = new Dictionary<string, ISubjectModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in list)
        {
            if (string.IsNullOrWhiteSpace(module.ModuleId))
                throw new ArgumentException("模块的 ModuleId 不能为空。", nameof(modules));
            if (!_byId.TryAdd(module.ModuleId, module))
                throw new ArgumentException($"重复注册了同一个模块：{module.ModuleId}", nameof(modules));
        }
        Modules = list;
    }

    public IReadOnlyList<ISubjectModule> Modules { get; }

    public ISubjectModule? Find(string moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId) && _byId.TryGetValue(moduleId, out var module) ? module : null;

    public ISubjectModule Resolve(string moduleId) =>
        Find(moduleId) ?? throw new ModuleNotInstalledException(moduleId);
}
