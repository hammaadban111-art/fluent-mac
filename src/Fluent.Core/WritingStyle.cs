using System.Text;
using System.Text.RegularExpressions;

namespace Fluent.Core;

/// <summary>How a finished transcript is dressed before it is inserted. A style only ever changes
/// capitals, punctuation and (for email) line layout. The words themselves are never rewritten.</summary>
public enum WritingStyle { Formal, Casual, VeryCasual, Excited }

/// <summary>Where the user is writing, decided from the app in front.</summary>
public enum StyleCategory { Personal, Work, Email, Other }

public static class StyleInfo
{
    public static string Raw(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "FORMAL",
        WritingStyle.Casual => "CASUAL",
        WritingStyle.VeryCasual => "VERY_CASUAL",
        _ => "EXCITED",
    };

    public static WritingStyle? ParseStyle(string? raw) => raw switch
    {
        "FORMAL" => WritingStyle.Formal,
        "CASUAL" => WritingStyle.Casual,
        "VERY_CASUAL" => WritingStyle.VeryCasual,
        "EXCITED" => WritingStyle.Excited,
        _ => null,
    };

    public static string Title(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "Formal.",
        WritingStyle.Casual => "Casual",
        WritingStyle.VeryCasual => "very casual",
        _ => "Excited!",
    };

    public static string Rule(this WritingStyle s) => s switch
    {
        WritingStyle.Formal => "Caps + Punctuation",
        WritingStyle.Casual => "Caps + Less punctuation",
        WritingStyle.VeryCasual => "No caps + No punctuation",
        _ => "Caps + Exclamation marks",
    };

    public static string Raw(this StyleCategory c) => c.ToString().ToUpperInvariant();

    public static string Label(this StyleCategory c) => c.ToString();

    /// <summary>Finishes "How do you write your …?"</summary>
    public static string Question(this StyleCategory c) => c switch
    {
        StyleCategory.Personal => "personal messages",
        StyleCategory.Work => "work messages",
        StyleCategory.Email => "email",
        _ => "other",
    };

    public static IReadOnlyList<WritingStyle> Styles(this StyleCategory c) => c is StyleCategory.Personal or StyleCategory.Work
        ? [WritingStyle.Formal, WritingStyle.Casual, WritingStyle.VeryCasual]
        : [WritingStyle.Formal, WritingStyle.Casual, WritingStyle.Excited];

    public static WritingStyle DefaultStyle(this StyleCategory c) => c == StyleCategory.Personal ? WritingStyle.Casual : WritingStyle.Formal;

    /// <summary>A typical smart-mode transcript, run through the real formatter for the previews.</summary>
    public static string Sample(this StyleCategory c) => c switch
    {
        StyleCategory.Personal => "Hey, are you free for lunch tomorrow? Let's do 12 if that works for you.",
        StyleCategory.Work => "Hey, if you're free, let's chat about the great results.",
        StyleCategory.Email => "Hi Alex, it was great talking with you today. Looking forward to our next chat. Best, Mary.",
        _ => "So far, I am enjoying the new workout routine. I am excited for tomorrow's workout, especially after a full night of rest.",
    };
}

/// <summary>Pure text transforms behind <see cref="WritingStyle"/>. A line-for-line port of the Android
/// and Mac <c>StyleFormatter</c>, checked by the same test cases.</summary>
public static class StyleFormatter
{
    public static string Format(string text, WritingStyle style, StyleCategory category)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return trimmed;
        if (category == StyleCategory.Email && style != WritingStyle.VeryCasual && !trimmed.Contains('\n')
            && ParseEmail(trimmed) is { } parts)
            return FormatEmail(parts, style);
        return StyleBody(trimmed, style);
    }

    /// <summary>Applies <paramref name="style"/> line by line so any line breaks the transcript already has survive.</summary>
    public static string StyleBody(string text, WritingStyle style) =>
        string.Join("\n", text.Split('\n').Select(line =>
            string.IsNullOrWhiteSpace(line) ? line : StyleLine(TrimSpaces(line), style)));

    static string StyleLine(string line, WritingStyle style) => style switch
    {
        WritingStyle.Formal => line,
        WritingStyle.Casual => DropFinalPeriod(DropIntroCommas(line)),
        WritingStyle.VeryCasual => StripPunctuation(LowercaseSentenceStarts(line)),
        _ => ExclaimFinal(line),
    };

    // casual

    static readonly Regex IntroComma = new(
        @"(^|[.!?]\s+)(Hey|Hi|Hello|So|Well|Okay|OK|Ok|Oh|Yeah|Yes|No|Alright|Anyway|Also|" +
        @"Honestly|Actually|Basically|Plus|Um|Uh|Right|Sure),(?=\s)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"Hey, are you free" becomes "Hey are you free"; "Hi Alex," is left alone.</summary>
    static string DropIntroCommas(string line) => IntroComma.Replace(line, "$1$2");

    /// <summary>Dotted abbreviations such as "p.m." or "U.S." keep their final dot.</summary>
    static readonly Regex DottedAbbreviation = new(@"(?:\b[A-Za-z]\.){2,}$", RegexOptions.CultureInvariant);

    static string DropFinalPeriod(string line)
    {
        if (!line.EndsWith('.') || line.EndsWith("..")) return line;
        if (DottedAbbreviation.IsMatch(line)) return line;
        return line[..^1].TrimEnd();
    }

    // very casual

    static string LowercaseSentenceStarts(string line)
    {
        var o = line.ToCharArray();
        var atStart = true;
        var i = 0;
        while (i < o.Length)
        {
            var ch = o[i];
            if (atStart && char.IsLetter(ch))
            {
                var end = i;
                while (end < o.Length && (char.IsLetter(o[end]) || char.IsDigit(o[end]) || o[end] == '\'' || o[end] == '’')) end++;
                var word = new string(o, i, end - i);
                if (!KeepsCapital(word)) o[i] = char.ToLowerInvariant(ch);
                atStart = false;
                i = end;
                continue;
            }
            if (ch is '.' or '!' or '?')
                atStart = i + 1 >= o.Length || char.IsWhiteSpace(o[i + 1]);
            else if (!char.IsWhiteSpace(ch) && "\"'“‘(".IndexOf(ch) < 0)
                atStart = false;
            i++;
        }
        return new string(o);
    }

    /// <summary>"I", "I'm" and acronyms like "NASA" or "OK" stay as they are.</summary>
    static bool KeepsCapital(string word)
    {
        if (word == "I" || word.StartsWith("I'") || word.StartsWith("I’")) return true;
        return word.Count(char.IsUpper) >= 2;
    }

    /// <summary>Drops sentence punctuation but keeps question marks, apostrophes, and anything inside a
    /// token such as "12:30", "3.5", "1,000" or "fluent.app".</summary>
    static readonly Regex LooseMarks = new(@"(?<=\S)[.,;:!…]+(?=\s|$)", RegexOptions.CultureInvariant);
    static readonly Regex DoubleSpace = new(" {2,}", RegexOptions.CultureInvariant);

    static string StripPunctuation(string line) =>
        TrimSpaces(DoubleSpace.Replace(LooseMarks.Replace(line, ""), " "));

    // excited

    /// <summary>Turns the closing full stop into an exclamation mark. Questions stay questions.</summary>
    static string ExclaimFinal(string line)
    {
        if (!line.EndsWith('.') || line.EndsWith("..")) return line;
        if (DottedAbbreviation.IsMatch(line)) return line;
        return line[..^1] + "!";
    }

    // email

    public sealed record EmailParts(string? Greeting, string Body, string? Closing, string? Name);

    const string NameWord = @"(?!I\b)[A-Z][\w'’-]*\.?";

    static readonly Regex GreetingRegex = new(
        @"^((?:Hi|Hello|Hey|Dear|Hiya|Greetings|Good (?:morning|afternoon|evening))" +
        @"(?:\s+(?:there|all|everyone|team|folks|guys|" + NameWord + @")){0,3})\s*[,.!:]\s+",
        RegexOptions.CultureInvariant);

    static readonly Regex ClosingRegex = new(
        @"(?<=[.!?])\s+(Best regards|Best wishes|Kind regards|Warm regards|Regards|Many thanks|" +
        @"Thanks again|Thank you|Thanks|All the best|Yours sincerely|Yours truly|Yours faithfully|" +
        @"Sincerely|Cheers|Talk soon|Take care|Warmly|Best)\s*[,.!]?\s+" +
        "(" + NameWord + @"(?:\s+" + NameWord + @"){0,2})\s*[.!]?$",
        RegexOptions.CultureInvariant);

    /// <summary>Splits a one-paragraph email into greeting, body and sign-off. Null when it has neither.</summary>
    public static EmailParts? ParseEmail(string text)
    {
        var rest = text;
        string? greeting = null;
        var g = GreetingRegex.Match(rest);
        if (g.Success)
        {
            greeting = g.Groups[1].Value.TrimEnd('.');
            rest = rest[(g.Index + g.Length)..];
        }
        string? closing = null, name = null;
        var c = ClosingRegex.Match(rest);
        if (c.Success)
        {
            closing = c.Groups[1].Value;
            name = c.Groups[2].Value.TrimEnd('.');
            rest = rest[..c.Index];
        }
        if (greeting is null && closing is null) return null;
        return new EmailParts(greeting, rest.Trim(), closing, name);
    }

    static string FormatEmail(EmailParts parts, WritingStyle style)
    {
        var body = StyleBody(parts.Body, style);
        if (style == WritingStyle.Casual)
        {
            string opening = parts.Greeting is { } greeting
                ? (body.Length == 0 ? $"{greeting}," : $"{greeting}, {LowercaseFirst(body)}")
                : body;
            var signOff = parts.Closing is { } cl ? $"{cl}, {parts.Name ?? ""}" : null;
            return string.Join("\n\n", new[] { opening.Length == 0 ? null : opening, signOff }.Where(x => x is not null));
        }
        var capitalized = CapitalizeFirst(body);
        return string.Join("\n\n", new[]
        {
            parts.Greeting is { } gr ? $"{gr}," : null,
            capitalized.Length == 0 ? null : capitalized,
            parts.Closing is { } c ? $"{c},\n{parts.Name ?? ""}" : null,
        }.Where(x => x is not null));
    }

    static string CapitalizeFirst(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    static string LowercaseFirst(string s)
    {
        if (s.Length == 0) return s;
        var word = new string(s.TakeWhile(ch => !char.IsWhiteSpace(ch)).ToArray()).Trim(',', '.', '!', '?');
        return KeepsCapital(word) ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }

    /// <summary>Trims spaces and tabs only, like Swift's <c>.whitespaces</c> (newlines are kept).</summary>
    static string TrimSpaces(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && char.IsWhiteSpace(s[a]) && s[a] != '\n') a++;
        while (b > a && char.IsWhiteSpace(s[b - 1]) && s[b - 1] != '\n') b--;
        return s[a..b];
    }
}
