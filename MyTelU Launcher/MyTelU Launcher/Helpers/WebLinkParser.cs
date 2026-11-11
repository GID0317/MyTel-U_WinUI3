using System.Text.RegularExpressions;

namespace MyTelU_Launcher.Helpers;

internal readonly record struct WebTextPart(string Text, Uri? Link);

internal static partial class WebLinkParser
{
    [GeneratedRegex("\\b(?:https?://|www\\.)[^\\s<>\"\\x00-\\x1f]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebAddress();

    internal static IEnumerable<WebTextPart> Parse(string text)
    {
        int position = 0;
        foreach (Match match in WebAddress().Matches(text))
        {
            if (match.Index > position) yield return new(text[position..match.Index], null);
            string address = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"');
            foreach (var (open, close) in new[] { ('(', ')'), ('[', ']'), ('{', '}') })
                while (address.EndsWith(close) && address.Count(c => c == close) > address.Count(c => c == open))
                    address = address[..^1];

            string target = address.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + address : address;
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.Host.Length != 0 && uri.UserInfo.Length == 0)
            {
                yield return new(address, uri);
                position = match.Index + address.Length;
            }
            else
            {
                yield return new(match.Value, null);
                position = match.Index + match.Length;
            }
        }
        if (position < text.Length) yield return new(text[position..], null);
    }
}
