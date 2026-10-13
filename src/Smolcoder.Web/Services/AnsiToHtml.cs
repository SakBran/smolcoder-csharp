using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Smolcoder.Web.Services;

/// <summary>
/// Turns terminal color codes into HTML spans for the terminal panel. Everything else is escaped.
/// </summary>
public static class AnsiToHtml
{
    private static readonly Regex ColorCode = new Regex(@"\x1b\[([0-9;]*)m", RegexOptions.Compiled);

    public static string Convert(string text)
    {
        StringBuilder html = new StringBuilder();
        int position = 0;
        int openSpans = 0;
        foreach (Match match in ColorCode.Matches(text))
        {
            html.Append(Escape(text.Substring(position, match.Index - position)));
            position = match.Index + match.Length;
            string[] codes = match.Groups[1].Value.Split(';');
            foreach (string code in codes)
            {
                string? cssClass = ClassFor(code);
                if (code.Length == 0 || code == "0")
                {
                    while (openSpans > 0)
                    {
                        html.Append("</span>");
                        openSpans--;
                    }
                }
                else if (cssClass != null)
                {
                    html.Append("<span class=\"").Append(cssClass).Append("\">");
                    openSpans++;
                }
            }
        }
        html.Append(Escape(text.Substring(position)));
        while (openSpans > 0)
        {
            html.Append("</span>");
            openSpans--;
        }
        return html.ToString();
    }

    private static string Escape(string text)
    {
        return WebUtility.HtmlEncode(text.Replace("\r", string.Empty));
    }

    private static string? ClassFor(string code)
    {
        if (code == "1")
        {
            return "ansi-bold";
        }
        if (code == "2")
        {
            return "ansi-dim";
        }
        if (code == "31" || code == "91")
        {
            return "ansi-red";
        }
        if (code == "32" || code == "92")
        {
            return "ansi-green";
        }
        if (code == "33" || code == "93")
        {
            return "ansi-yellow";
        }
        if (code == "34" || code == "94")
        {
            return "ansi-blue";
        }
        if (code == "35" || code == "95")
        {
            return "ansi-magenta";
        }
        if (code == "36" || code == "96")
        {
            return "ansi-cyan";
        }
        if (code == "90")
        {
            return "ansi-gray";
        }
        return null;
    }
}
