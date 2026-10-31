namespace Icono.Core;

/// <summary>A list of pages typed by the user, like in a print dialog: "1,3-5", "2-" (page 2 to the end), "-3" (up to page 3), "all".</summary>
public static class PageRange
{
    /// <summary>Reads the text into the pages it names (0-based, ascending, each once). False when the text is empty, not understood or names a
    /// page the project does not have; <paramref name="error"/> then says what is wrong.</summary>
    public static bool TryParse(string? text, int pageCount, out List<int> pages, out string error)
    {
        pages = [];
        error = string.Empty;
        var source = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (source.Length == 0)
        {
            error = "Type the pages, for example 1,3-5.";
            return false;
        }

        if (source == "all")
        {
            pages = Enumerable.Range(0, pageCount).ToList();
            return true;
        }

        var picked = new SortedSet<int>();
        foreach (var part in source.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            int from, to;
            if (dash < 0)
            {
                if (!int.TryParse(part, out from))
                {
                    error = $"\"{part}\" is not a page number.";
                    return false;
                }

                to = from;
            }
            else
            {
                var left = part[..dash].Trim();
                var right = part[(dash + 1)..].Trim();
                if ((left.Length > 0 && !int.TryParse(left, out from)) || (right.Length > 0 && !int.TryParse(right, out to)))
                {
                    error = $"\"{part}\" is not a range of pages.";
                    return false;
                }

                from = left.Length == 0 ? 1 : int.Parse(left);
                to = right.Length == 0 ? pageCount : int.Parse(right);
                if (left.Length == 0 && right.Length == 0)
                {
                    error = "A range needs a start or an end, for example 2-4.";
                    return false;
                }
            }

            if (from < 1 || to < 1 || from > pageCount || to > pageCount)
            {
                error = pageCount == 1 ? "The project has only page 1." : $"The project has the pages 1 to {pageCount}.";
                return false;
            }

            if (from > to)
            {
                (from, to) = (to, from);
            }

            for (var page = from; page <= to; page++)
            {
                picked.Add(page - 1);
            }
        }

        if (picked.Count == 0)
        {
            error = "Type the pages, for example 1,3-5.";
            return false;
        }

        pages = picked.ToList();
        return true;
    }
}
