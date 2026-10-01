using System;

namespace Conduit.Features.Articles;

public static class ReadingTime
{
    public static int MinutesFromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return 1;
        }

        var wordCount = body.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries
        ).Length;

        return Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));
    }
}
