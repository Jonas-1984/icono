using Icono.Core;
using Icono.Editor;
using Icono.Rendering;

namespace Icono.Tests;

public class EditorSettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "icono-settings-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void TheSettings_ComeBack_AfterASaveAndALoad()
    {
        var path = TempFile();
        try
        {
            var first = new EditorContext
            {
                PenColor = ArgbColor.FromRgb(200, 30, 60),
                PenWidth = 6,
                PenSmoothing = 3,
                PenPressure = false,
                PenPressureStrength = 0.4,
                PenSnapShapes = false,
                CurveSizeCm = 2.5,
                KeepProportions = false,
                ShowGrid = true,
                SnapToGrid = true,
                ShowRulers = false,
            };
            first.PointSnap.Enabled = false;
            first.PointSnap.Set(PointSnapKind.Tangent, true);
            first.PointSnap.Set(PointSnapKind.Endpoint, false);
            first.ObjectSnap.Walls = false;
            first.WallSnap.EqualLength = false;
            Assert.True(EditorSettings.Save(first, path));

            var second = new EditorContext();
            Assert.True(EditorSettings.Load(second, path));

            Assert.Equal(ArgbColor.FromRgb(200, 30, 60), second.PenColor);
            Assert.Equal(6, second.PenWidth, 6);
            Assert.Equal(3, second.PenSmoothing, 6);
            Assert.False(second.PenPressure);
            Assert.Equal(0.4, second.PenPressureStrength, 6);
            Assert.False(second.PenSnapShapes);
            Assert.Equal(2.5, second.CurveSizeCm, 6);
            Assert.False(second.KeepProportions);
            Assert.True(second.ShowGrid);
            Assert.True(second.SnapToGrid);
            Assert.False(second.ShowRulers);
            Assert.False(second.PointSnap.Enabled);
            Assert.True(second.PointSnap.IsOn(PointSnapKind.Tangent));
            Assert.False(second.PointSnap.IsOn(PointSnapKind.Endpoint));
            Assert.False(second.ObjectSnap.Walls);
            Assert.False(second.WallSnap.EqualLength);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingOrDamagedFile_LeavesTheUsualStart()
    {
        var path = TempFile();
        var context = new EditorContext();
        var width = context.PenWidth;

        Assert.False(EditorSettings.Load(context, path)); // no file

        File.WriteAllText(path, "{ this is not json");
        try
        {
            Assert.False(EditorSettings.Load(context, path));
            Assert.Equal(width, context.PenWidth);
            Assert.True(context.KeepProportions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValuesOutOfRange_OrMissing_AreKeptInRange_OrIgnored()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ \"PenPressureStrength\": 9, \"PenColor\": \"nonsense\", \"KeepProportions\": false }");
        try
        {
            var context = new EditorContext();
            var colour = context.PenColor;

            Assert.True(EditorSettings.Load(context, path));

            Assert.Equal(1, context.PenPressureStrength, 6); // the context keeps it in range
            Assert.Equal(colour, context.PenColor); // not a colour: ignored
            Assert.False(context.KeepProportions);
            Assert.True(context.PenPressure); // not in the file: the usual value
        }
        finally
        {
            File.Delete(path);
        }
    }
}
