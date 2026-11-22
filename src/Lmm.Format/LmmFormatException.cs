namespace Lmm.Format;

/// <summary>
/// A .lmm container is malformed, unreadable, or (for writing) cannot be
/// produced. Deliberately NOT thrown for "I don't have a module that can
/// handle this moduleId" — that is the host's IModuleCatalog decision, not
/// this layer's; Lmm.Format only knows how to get the manifest's moduleId
/// string out of the container, not what to do with it.
/// </summary>
public sealed class LmmFormatException(string message, Exception? inner = null) : Exception(message, inner);
