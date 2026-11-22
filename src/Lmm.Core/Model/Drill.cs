namespace Lmm.Core.Model;

/// <summary>Old "drill" entry: [{h,q,a}] — one in-class practice item (课堂小练).</summary>
public sealed class Drill
{
    /// <summary>Old "h": heading/label for the drill.</summary>
    public string Heading { get; set; } = "";

    /// <summary>Old "q": the drill's prompt/question.</summary>
    public string Question { get; set; } = "";

    /// <summary>Old "a": the drill's answer.</summary>
    public string Answer { get; set; } = "";
}
