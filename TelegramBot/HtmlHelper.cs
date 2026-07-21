using System.Web;

namespace TelegramBot;

public static class HtmlHelper
{
    public static string Escape(string text) => HttpUtility.HtmlEncode(text);
}
