namespace Lmm.Core.Ink;

/// <summary>
/// A plain 2D point used internally by the ink geometry algorithms.
/// Unlike <see cref="Lmm.Core.Model.InkPoint"/> (always fractional 0..1
/// surface coordinates, the persisted/document representation), a Pt here
/// is whatever coordinate space the calling code is working in — usually
/// pixels, matching the legacy JS algorithm's `[x, y]` tuples.
/// </summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double s) => new(a.X * s, a.Y * s);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public static double Distance(Pt a, Pt b) => (b - a).Length;
}
