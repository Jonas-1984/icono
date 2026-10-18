using Icono.Core;
using SkiaSharp;

namespace Icono.Rendering;

/// <summary>Turns a shape's <see cref="GradientFill"/> into a Skia shader positioned like the shape's own geometry.</summary>
internal static class FillGradientShader
{
    public static SKShader? Create(Shape shape, byte alpha)
    {
        var g = shape.FillGradient;
        if (g is null || g.Stops.Count < 2)
        {
            return null;
        }

        // gradient space -> shape-local 24x24 space (carried by the gradient itself)
        var matrix = new SKMatrix
        {
            ScaleX = (float)g.A, SkewY = (float)g.B,
            SkewX = (float)g.C, ScaleY = (float)g.D,
            TransX = (float)g.E, TransY = (float)g.F,
            Persp0 = 0, Persp1 = 0, Persp2 = 1,
        };

        // ... -> document space, in exactly the order ShapeGeometryFactory.Build treats a path: flip about the local
        // centre, scale 24x24 -> Width x Height, move to X/Y, rotate about the shape centre.
        if (shape.FlipHorizontal)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateScale(-1, 1, 12, 12));
        }

        if (shape.FlipVertical)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateScale(1, -1, 12, 12));
        }

        matrix = matrix.PostConcat(SKMatrix.CreateScale((float)(shape.Width / 24.0), (float)(shape.Height / 24.0)));
        matrix = matrix.PostConcat(SKMatrix.CreateTranslation((float)shape.X, (float)shape.Y));
        if (shape.Rotation != 0)
        {
            matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees(
                (float)shape.Rotation, (float)(shape.X + shape.Width / 2), (float)(shape.Y + shape.Height / 2)));
        }

        var colors = new SKColor[g.Stops.Count];
        var positions = new float[g.Stops.Count];
        for (var i = 0; i < colors.Length; i++)
        {
            var sk = g.Stops[i].Color.ToSkColor();
            colors[i] = sk.WithAlpha((byte)(sk.Alpha * alpha / 255.0));
            positions[i] = (float)Math.Clamp(g.Stops[i].Offset, 0, 1);
        }

        if (g.Kind == GradientKind.Linear)
        {
            return SKShader.CreateLinearGradient(
                new SKPoint((float)g.X1, (float)g.Y1), new SKPoint((float)g.X2, (float)g.Y2),
                colors, positions, SKShaderTileMode.Clamp, matrix);
        }

        var centre = new SKPoint((float)g.Cx, (float)g.Cy);
        var radius = (float)Math.Max(g.R, 1e-6);
        if (Math.Abs(g.Fx - g.Cx) > 1e-6 || Math.Abs(g.Fy - g.Cy) > 1e-6)
        {
            return SKShader.CreateTwoPointConicalGradient(
                new SKPoint((float)g.Fx, (float)g.Fy), 0, centre, radius, colors, positions, SKShaderTileMode.Clamp, matrix);
        }

        return SKShader.CreateRadialGradient(centre, radius, colors, positions, SKShaderTileMode.Clamp, matrix);
    }
}
