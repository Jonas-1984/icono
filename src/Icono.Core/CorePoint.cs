namespace Icono.Core;

/// <summary>A shape-local anchor point for <see cref="Shape.Points"/>. Named to avoid clashing
/// with <c>System.Windows.Point</c>/<c>SkiaSharp.SKPoint</c>, both already in scope in the
/// App/Rendering projects.</summary>
public readonly record struct CorePoint(double X, double Y);
