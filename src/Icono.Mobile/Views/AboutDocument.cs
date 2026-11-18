using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Icono.Mobile.Views;

/// <summary>Turns one of the plain-text documents of About (test terms, privacy notice, licence notices) into a page that is easy to read: white
/// paper, dark text with room between the lines, every section with its own colour (number badge, heading, a bar along its text), bullets as
/// bullets. The texts stay plain .txt files (the Windows installer uses the same ones); this only reads their layout.</summary>
internal static partial class AboutDocument
{
    private static readonly Color Ink = Color.Parse("#FF1F2937");
    private static readonly Color Quiet = Color.Parse("#FF6B7280");

    /// <summary>The colours of the sections, one after the other: blue, green, orange, purple, teal, rose, amber, indigo.</summary>
    private static readonly Color[] Accents =
    [
        Color.Parse("#FF2563EB"), Color.Parse("#FF16A34A"), Color.Parse("#FFEA580C"), Color.Parse("#FF7C3AED"),
        Color.Parse("#FF0D9488"), Color.Parse("#FFE11D48"), Color.Parse("#FFB45309"), Color.Parse("#FF4F46E5"),
    ];

    [GeneratedRegex(@"^\s{0,2}(?<n>\d+)\.\s+(?<t>\S.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\s*[-*•]\s+(?<t>\S.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*(-{3,}|={3,})\s*$")]
    private static partial Regex Underline();

    private sealed record Section(string? Number, string? Title, List<Control> Body);

    /// <param name="numberedHeadings">Lines like "3. Your drawings" are headings (terms, privacy); in licence texts such lines are list items.</param>
    public static Control Build(string text, bool numberedHeadings)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿').Split('\n').Select(l => l.TrimEnd()).ToArray();
        var page = new StackPanel { Spacing = 14 };

        var i = 0;
        string? title = null;
        if (lines.Length > 1 && lines[0].Length > 0 && Underline().IsMatch(lines[1]) && lines[1].Trim()[0] == '=')
        {
            title = lines[0];
            i = 2;
        }

        if (title is not null)
        {
            page.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#FF0F172A")), TextWrapping = TextWrapping.Wrap });
            page.Children.Add(new Border { Height = 4, Width = 56, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Accents[0]) });
        }

        var sections = new List<Section> { new(null, null, []) };
        var accent = -1;
        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            // a heading: a line with ---- or ==== under it, or (terms, privacy) a numbered line at the left edge
            string? number = null;
            string? heading = null;
            if (i + 1 < lines.Length && !char.IsWhiteSpace(line[0]) && Underline().IsMatch(lines[i + 1]) && !Underline().IsMatch(line))
            {
                heading = line;
                i++;
            }
            else if (numberedHeadings && !char.IsWhiteSpace(line[0]) && Numbered().Match(line) is { Success: true } m)
            {
                number = m.Groups["n"].Value;
                heading = m.Groups["t"].Value;
            }

            if (heading is not null)
            {
                var numbered = Numbered().Match(heading);
                if (number is null && numbered.Success)
                {
                    number = numbered.Groups["n"].Value; // "1. Lucide icons" above a ---- line
                    heading = numbered.Groups["t"].Value;
                }

                sections.Add(new Section(number, heading, []));
                accent++;
                continue;
            }

            if (line.Trim() is "* * *" or "***" or "---")
            {
                sections[^1].Body.Add(new Border { Height = 1, Margin = new Thickness(0, 4), Background = new SolidColorBrush(Color.Parse("#FFE5E7EB")) });
                continue;
            }

            // a paragraph: the lines up to the next blank line or heading
            var block = new List<string>();
            while (i < lines.Length && lines[i].Length > 0
                   && !(i + 1 < lines.Length && !char.IsWhiteSpace(lines[i][0]) && Underline().IsMatch(lines[i + 1]) && !Underline().IsMatch(lines[i]))
                   && !(numberedHeadings && !char.IsWhiteSpace(lines[i][0]) && block.Count > 0 && Numbered().IsMatch(lines[i])))
            {
                block.Add(lines[i]);
                i++;
            }

            i--;
            AddParagraph(sections[^1].Body, block);
        }

        var index = 0;
        foreach (var section in sections)
        {
            if (section.Body.Count == 0 && section.Title is null)
            {
                continue;
            }

            var colour = section.Title is null ? Accents[0] : Accents[index++ % Accents.Length];
            page.Children.Add(Render(section, colour));
        }

        return page;
    }

    private static Control Render(Section section, Color colour)
    {
        var tint = Color.FromArgb(0x55, colour.R, colour.G, colour.B);
        var body = new StackPanel { Spacing = 8 };
        foreach (var piece in section.Body)
        {
            body.Children.Add(piece);
        }

        var card = new StackPanel { Spacing = 8 };
        if (section.Title is not null)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (section.Number is not null)
            {
                head.Children.Add(new Border
                {
                    Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = section.Number, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
            }
            else
            {
                head.Children.Add(new Border { Width = 6, Height = 22, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center });
            }

            head.Children.Add(new TextBlock
            {
                Text = section.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(colour), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 560,
            });
            card.Children.Add(head);
        }

        if (body.Children.Count > 0)
        {
            card.Children.Add(section.Title is null
                ? body
                : new Border
                {
                    BorderBrush = new SolidColorBrush(tint), BorderThickness = new Thickness(3, 0, 0, 0), Margin = new Thickness(13, 0, 0, 0), Padding = new Thickness(16, 2, 0, 2),
                    Child = body,
                });
        }

        return card;
    }

    private static TextBlock Text(string text, bool quiet = false) => new()
    {
        Text = text, FontSize = 14.5, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(quiet ? Quiet : Ink),
    };

    /// <summary>Hard-wrapped prose becomes one flowing paragraph; short lines (addresses, lists) keep their line breaks; bullets become bullets.</summary>
    private static void AddParagraph(List<Control> into, List<string> block)
    {
        var item = new List<string>();
        var itemIsBullet = false;

        void Flush()
        {
            if (item.Count == 0)
            {
                return;
            }

            var joined = Join(item);
            if (itemIsBullet)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                row.Children.Add(new TextBlock { Text = "•", FontSize = 15, LineHeight = 22, Margin = new Thickness(2, 0, 10, 0), Foreground = new SolidColorBrush(Quiet) });
                var text = Text(joined);
                Grid.SetColumn(text, 1);
                row.Children.Add(text);
                into.Add(row);
            }
            else
            {
                into.Add(Text(joined));
            }

            item.Clear();
        }

        foreach (var raw in block)
        {
            if (Bullet().Match(raw) is { Success: true } bullet)
            {
                Flush();
                itemIsBullet = true;
                item.Add(bullet.Groups["t"].Value.Trim());
            }
            else
            {
                item.Add(raw.Trim());
            }
        }

        Flush();
    }

    private static string Join(List<string> lines)
    {
        // wrapped prose: the lines are long (the text was broken at about 100 - 150 characters); lists and addresses are short
        var prose = lines.Count > 1 && lines.Take(lines.Count - 1).Average(l => l.Length) > 55;
        return prose ? Regex.Replace(string.Join(' ', lines), "[ \t]{2,}", " ") : string.Join('\n', lines);
    }
}
