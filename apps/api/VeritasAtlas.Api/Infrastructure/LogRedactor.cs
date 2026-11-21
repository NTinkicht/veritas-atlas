using System.Text;
using System.Text.RegularExpressions;

namespace VeritasAtlas.Api.Infrastructure;

/// <summary>
/// VER-P0-19: exception text can carry connection strings, bearer tokens or API keys.
/// Failed-request logs use <see cref="Describe"/> instead of handing the raw exception to the log sink.
/// </summary>
public static partial class LogRedactor
{
    public const string Mask = "[REDACTED]";

    // key=value / key: value pairs for secret-looking keys. An unquoted value runs to ';' or end of line
    // (connection-string semantics: a password may contain '&', ',' or spaces). Inside a query string
    // (key preceded by '?' or '&') it stops at '&', whitespace, ',' or a quote so later parameters survive.
    [GeneratedRegex(
        @"(?<key>\b[\w.\-]*(?:password|passwd|pwd|secret|token|api[_\-]?key|apikey|access[_\-]?key|private[_\-]?key|credential)[\w.\-]*)(?<sep>\s*[=:]\s*)(?<val>""(?:[^""]|"""")*""|'(?:[^']|'')*'|[^;\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValuePattern();

    // Authorization: Bearer xxx / Basic xxx, or a bare "Bearer xxx".
    [GeneratedRegex(@"\b(?<scheme>Bearer|Basic)\s+(?<val>[A-Za-z0-9\-._~+/]+=*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthSchemePattern();

    // Credentials embedded in URLs: scheme://user:pass@host.
    [GeneratedRegex(@"(?<pre>\b[a-z][a-z0-9+.\-]*://[^/\s:@]+:)(?<val>[^@\s/]+)(?=@)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentialPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = RedactKeyValues(text);
        result = AuthSchemePattern().Replace(result, m => m.Groups["scheme"].Value + " " + Mask);
        result = UrlCredentialPattern().Replace(result, m => m.Groups["pre"].Value + Mask);
        return result;
    }

    private static string RedactKeyValues(string text) => KeyValuePattern().Replace(text, m => MaskKeyValue(m, text));

    private static string MaskKeyValue(Match m, string text)
    {
        var prefix = m.Groups["key"].Value + m.Groups["sep"].Value;
        var value = m.Groups["val"].Value;
        var inQuery = m.Index > 0 && (text[m.Index - 1] == '?' || text[m.Index - 1] == '&');
        if (inQuery && value.Length > 0 && value[0] != '"' && value[0] != '\'')
        {
            var end = value.IndexOfAny(['&', ',', ' ', '\t', '"', '\'']);
            if (end >= 0)
            {
                // The restored tail may hold further secret parameters: scan it again.
                return prefix + Mask + RedactKeyValues(value[end..]);
            }
        }

        return prefix + Mask;
    }

    /// <summary>Type, redacted message and redacted stack for the exception and its inner chain.</summary>
    public static string Describe(Exception exception)
    {
        var sb = new StringBuilder();
        var depth = 0;
        for (var current = exception; current is not null && depth < 8; current = current.InnerException, depth++)
        {
            if (depth > 0)
            {
                sb.Append(" ---> ");
            }

            sb.Append(current.GetType().FullName).Append(": ").Append(Redact(current.Message));
            if (!string.IsNullOrEmpty(current.StackTrace))
            {
                sb.AppendLine().Append(Redact(current.StackTrace));
            }
        }

        return sb.ToString();
    }
}
