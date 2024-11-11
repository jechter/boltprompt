namespace boltprompt;
using SkiaSharp;

public static class FontUtility
{
    private static void FontNameToFamilyNameAndStyle(string fontName, out string familyName, out SKFontStyle style)
    {
        if (fontName.Contains('-'))
        {
            var split = fontName.Split('-');
            fontName = split[0];
            style = split[1].ToLower() switch
            {
                "italic" => SKFontStyle.Italic,
                "bold" => SKFontStyle.Bold,
                "bolditalic" => SKFontStyle.BoldItalic,
                _ => SKFontStyle.Normal
            };
        }
        else
            style = SKFontStyle.Normal;

        familyName = fontName switch
        {
            "0xProtoNF" => "0xProto Nerd Font",
            "0xProtoNFM" => "0xProto Nerd Font Mono",
            _ => fontName
        };
    }
    
    public static bool FontHasGlyph(string? terminalFont, char character)
    {
        if (terminalFont == null)
            return false;
        FontNameToFamilyNameAndStyle(terminalFont, out var fontName, out var style); 
        using var typeface = SKTypeface.FromFamilyName(fontName, style);
        if (typeface == null)
            return false;
        
        // Check if the character has a glyph index in the font
        var glyph = typeface.GetGlyph(character);
        return glyph != 0;
    }
}