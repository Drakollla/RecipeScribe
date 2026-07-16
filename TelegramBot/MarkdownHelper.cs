using System.Text.RegularExpressions;

namespace TelegramBot;

public static partial class MarkdownHelper
{
    private static readonly Regex EscapePattern = MyRegex();

    public static string Escape(string text) =>
        EscapePattern.Replace(text, @"\$1");

    [GeneratedRegex(@"([_*\[\]()~`>#+\-=|{}.!])")]
    private static partial Regex MyRegex();
}