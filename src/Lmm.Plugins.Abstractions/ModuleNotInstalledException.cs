namespace Lmm.Plugins.Abstractions;

/// <summary>
/// Thrown by <see cref="IModuleCatalog.Resolve"/> when a .lmm file declares a
/// moduleId this machine doesn't have. This is a normal failure case once
/// modules are distributed separately, so it carries a message the host can
/// show verbatim ("这个文件需要模块 X，但没安装").
/// </summary>
public sealed class ModuleNotInstalledException(string moduleId)
    : Exception($"这个文件需要模块 \"{moduleId}\"，但本机没有安装该模块。请先安装对应的学科插件再打开。")
{
    public string ModuleId { get; } = moduleId;
}
