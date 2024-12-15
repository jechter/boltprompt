using System.Text;
using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;

namespace boltprompt;

internal class AnsiConsoleChatReplyFormatter : CodeColorizerBase
{
    private TextWriter _writer;
    private bool _bold;
    private bool _headline;
    private bool _monospace;
    private bool _codeBlock;
    private string _textBuffer = "";
    private ILanguage? _formattingLanguage;
    private readonly string _resetBoldAndUnderline = BufferedConsole.BoldEsc(false) + BufferedConsole.UnderlineEsc(false);
    private readonly BufferedConsole.RgbColor _bg, _bgDark, _text, _comment;

    public AnsiConsoleChatReplyFormatter(StyleDictionary? styles = null, ILanguageParser? languageParser = null) : base(styles, languageParser)
    {
        _writer = Console.Out;
        _bg = BufferedConsole.ParseHtmlColor(Configuration.Instance.SuggestionBackgroundColor);
        _text = BufferedConsole.ParseHtmlColor(Configuration.Instance.SuggestionTextColor);
        _bgDark = new((byte)(_bg.R * 0.8), (byte)(_bg.G * 0.8), (byte)(_bg.B * 0.8));
        _comment = new((byte)((_text.R + _bg.R) * 0.5), (byte)((_text.G + _bg.G) * 0.5), (byte)((_text.B + _bg.B) * 0.5));
    }

    protected override void Write(string parsedSourceCode, IList<Scope> scopes)
    {
        var scope = scopes.Count > 0 ? scopes.Last() : null;
        _writer.Write(scope?.Name switch
        {
            ScopeName.Keyword => BufferedConsole.BoldEsc(true),
            ScopeName.String => BufferedConsole.UnderlineEsc(true),
            ScopeName.Comment => BufferedConsole.ForegroundColorEsc(_comment),
            _ => ""
        });
        _writer.Write(parsedSourceCode);
        _writer.Write(scope?.Name switch
        {
            ScopeName.Keyword => BufferedConsole.BoldEsc(false),
            ScopeName.String => BufferedConsole.UnderlineEsc(false),
            ScopeName.Comment => BufferedConsole.ForegroundColorEsc(
                BufferedConsole.ParseHtmlColor(Configuration.Instance.SuggestionTextColor)),
            _ => ""
        });
    }

    public void PrintChatResponseFormatted(string text)
    {
        if (!text.Contains('\n') && !_textBuffer.Contains('\n'))
        {
            _textBuffer += text;
            return;
        }

        text = _textBuffer + text;
        var lineEnd = text.IndexOf('\n');
        _textBuffer = text[(lineEnd+1)..];
        text = text[..(lineEnd+1)];

        var pos = 0;
        const string codeBlockMarkdown = "```";
        if (text.Trim().StartsWith(codeBlockMarkdown))
        {
            if (_codeBlock)
            {
                ReplaceKey(codeBlockMarkdown, $"{BufferedConsole.ClearEndOfLineEsc()}{BufferedConsole.ResetColorEsc()}", ref pos);
            }
            else
            {
                var language = text.Trim()[codeBlockMarkdown.Length..].Trim();
                _formattingLanguage = Languages.FindById(language);
                ReplaceKey(codeBlockMarkdown,
                    $"{BufferedConsole.MoveToStartOfLineEsc()}{BufferedConsole.ForegroundColorEsc(_text)}{BufferedConsole.BackgroundColorEsc(_bgDark)}📜 ",
                    ref pos);
            }
            _codeBlock = !_codeBlock;
        }

        if (_codeBlock)
        {
            try
            {
                if (_formattingLanguage != null)
                    text = GetFormattedString(text, _formattingLanguage);
                pos = 0;
                if (_codeBlock)
                    ReplaceKey("\n", $"{BufferedConsole.ClearEndOfLineEsc()}\n{BufferedConsole.BackgroundColorEsc(_bg)}",
                        ref pos);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
            }
        }
        else
        {
            if (_headline)
            {
                if (ReplaceKey("\n", _resetBoldAndUnderline, ref pos))
                    _headline = false;
            }

            if (!_headline)
            {
                while (ReplaceKey("###", $"{BufferedConsole.BoldEsc(true)}###", ref pos))
                {
                    if (!ReplaceKey("\n", _resetBoldAndUnderline, ref pos))
                        _headline = true;
                }

                while (ReplaceKey("##", $"{BufferedConsole.BoldEsc(true)}##", ref pos))
                {
                    if (!ReplaceKey("\n", _resetBoldAndUnderline, ref pos))
                        _headline = true;
                }

                while (ReplaceKey("#", $"{BufferedConsole.BoldEsc(true)}{BufferedConsole.UnderlineEsc(true)}#",
                           ref pos))
                {
                    if (!ReplaceKey("\n", _resetBoldAndUnderline, ref pos))
                        _headline = true;
                }
            }

            pos = 0;
            while (ReplaceKey("**", BufferedConsole.BoldEsc(!_bold), ref pos))
                _bold = !_bold;
            pos = 0;
            while (ReplaceKey("__", BufferedConsole.BoldEsc(!_bold), ref pos))
                _bold = !_bold;
            pos = 0;
            while (ReplaceKey("`",
                       !_monospace
                           ? BufferedConsole.ForegroundColorEsc(_text)+BufferedConsole.BackgroundColorEsc(_bg)
                           : BufferedConsole.ResetColorEsc(), ref pos))
                _monospace = !_monospace;
        }
        Console.Write(text);
            
        if (!string.IsNullOrWhiteSpace(_textBuffer))
            PrintChatResponseFormatted("");
        return;

        bool ReplaceKey(string key, string value, ref int searchPos)
        {
            var i = text.IndexOf(key, searchPos, StringComparison.InvariantCulture);
            if (i == -1) return false;
            text = text[..i] + value + text[(i + key.Length)..];
            searchPos = i + value.Length;
            return true;
        }
    }

    private string GetFormattedString(string sourceCode, ILanguage language)
    {
        var buffer = new StringBuilder(sourceCode.Length * 2);

        using (TextWriter writer = new StringWriter(buffer))
        {
            _writer = writer;
            languageParser.Parse(sourceCode, language, Write);
            writer.Flush();
        }

        return buffer.ToString();
    }
}