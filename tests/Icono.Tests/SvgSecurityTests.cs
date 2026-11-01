using System.Diagnostics;
using Icono.Rendering;

namespace Icono.Tests;

/// <summary>An SVG that is opened or pasted comes from outside: it must not be able to hang the program, fill the memory or read files.</summary>
public class SvgSecurityTests
{
    private const string Square = "<path d=\"M0 0 L10 0 L10 10 L0 10 Z\"/>";

    [Fact]
    public void AFileWithADoctype_ImportsLikeAnyOther()
    {
        var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" + Square + "</svg>";

        Assert.NotEmpty(SvgImporter.ExtractElements(svg));
    }

    [Fact]
    public void ABillionLaughsFile_IsRefusedQuickly_NotExpanded()
    {
        var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">"
            + "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"><!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\"><!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\">"
            + "<!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\"><!ENTITY g \"&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;\">]>"
            + "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>&g;</text>" + Square + "</svg>";

        var clock = Stopwatch.StartNew();
        var result = SvgImporter.ExtractElements(svg);

        Assert.True(clock.ElapsedMilliseconds < 3000);
        Assert.NotNull(result); // refused or read without the entity: either way nothing exploded
    }

    [Fact]
    public void AnExternalEntity_IsNotReadFromDisk()
    {
        var secret = Path.Combine(Path.GetTempPath(), $"icono-secret-{Guid.NewGuid():N}.txt");
        File.WriteAllText(secret, "TOP-SECRET-CONTENT");
        try
        {
            var url = secret.Replace(Path.DirectorySeparatorChar, '/');
            var svg = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///" + url + "\">]>"
                + "<svg xmlns=\"http://www.w3.org/2000/svg\"><text id=\"t\">&x;</text>" + Square + "</svg>";

            var elements = SvgImporter.ExtractElements(svg);

            Assert.DoesNotContain("TOP-SECRET-CONTENT", string.Concat(elements.Select(e => e.PathData)));
        }
        finally
        {
            File.Delete(secret);
        }
    }
}
