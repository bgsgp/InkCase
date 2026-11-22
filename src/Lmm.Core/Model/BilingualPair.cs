namespace Lmm.Core.Model;

/// <summary>Old "bi" entry: [{en,zh}] — one sentence shown in the bilingual (中英对照) panel.</summary>
public sealed class BilingualPair
{
    public string En { get; set; } = "";
    public string Zh { get; set; } = "";
}
