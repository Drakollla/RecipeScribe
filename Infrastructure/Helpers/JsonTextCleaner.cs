namespace Infrastructure.Helpers
{
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

            char open = text[0] == '[' ? '[' : text[0] == '{' ? '{' : '\0';

            if (open == '\0')
            {
                int firstArray = text.IndexOf('[');
                int firstBrace = text.IndexOf('{');

                if (firstArray != -1 && (firstBrace == -1 || firstArray < firstBrace))
                    open = '[';
                else if (firstBrace != -1)
                    open = '{';
                else
                    return null;
            }

            char close = open == '[' ? ']' : '}';
            int start = text.IndexOf(open);
            
            if (start == -1)
                return null;

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
                    
                    if (depth == 0) 
                        return text.Substring(start, i - start + 1); 
                }
            }

            return null;
        }

        public static string? TruncateToLastCompleteObject(string text)
        {
            if (!text.StartsWith("[") || text.Length < 2)
                return null;

            int depth = 0;
            bool inString = false;
            int lastRecipeEnd = -1;

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

                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 1 && c == '}')
                    {
                        char next = i + 1 < text.Length ? text[i + 1] : '\0';
                        if (next == ',' || next == ']' || next == '\0')
                            lastRecipeEnd = i;
                    }
                }
            }

            if (lastRecipeEnd == -1)
                return null;

            var truncated = text.Substring(0, lastRecipeEnd + 1) + "]";
            return truncated;
        }
    }
}
