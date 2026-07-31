namespace Infrastructure.Helpers;

public static class JsonTextCleaner
{
    public static string StripCodeFence(string raw)
    {
        var result = raw.Trim();

        if (result.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            result = result["```json".Length..];
        else if (result.StartsWith("```"))
            result = result["```".Length..];

        if (result.EndsWith("```"))
            result = result[..^"```".Length];

        result = result.Trim();

        return ExtractBalancedJson(result) ?? result;
    }

    public static string? ExtractBalancedJson(string text)
    {
        text = text.Trim();

        if (text.Length == 0)
            return null;

        int start = text[0] is '[' or '{' ? 0 : FindFirstOpeningBracket(text);

        if (start < 0)
            return null;

        char open = text[start];
        char close = open == '[' ? ']' : '}';
        int? end = FindBalancedEnd(text, open, close, start);
        
        return end is null ? null : text[start..(end.Value + 1)];
    }

    public static string? TruncateToLastCompleteObject(string text)
    {
        if (!text.StartsWith("[") || text.Length < 2)
            return null;

        int lastObjectEnd = -1;
        bool inString = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length) i++;
                continue;
            }

            if (c != '{') continue;

            int? end = FindBalancedEnd(text, '{', '}', i);
            
            if (end is null)
                break;

            int next = end.Value + 1;
            
            if (next >= text.Length || text[next] is ',' or ']')
                lastObjectEnd = end.Value;

            i = end.Value;
        }

        return lastObjectEnd == -1 ? null : text[..(lastObjectEnd + 1)] + "]";
    }

    private static int FindFirstOpeningBracket(string text)
    {
        int array = text.IndexOf('[');
        int brace = text.IndexOf('{');
        
        if (array == -1)
            return brace;
        
        if (brace == -1)
            return array;
        
        return Math.Min(array, brace);
    }

    private static int? FindBalancedEnd(string text, char open, char close, int start)
    {
        int depth = 0;
        bool inString = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length)
                    i++;
                continue;
            }

            if (c == open)
                depth++;
            else if (c == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return null;
    }
}