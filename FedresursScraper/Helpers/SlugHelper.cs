using System.Text;
using System.Text.RegularExpressions;

public static class SlugHelper
{
    private static readonly Dictionary<char, string> TranslitMap = new()
    {
        {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
        {'е', "e"}, {'ё', "e"}, {'ж', "zh"}, {'з', "z"}, {'и', "i"},
        {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"},
        {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"},
        {'у', "u"}, {'ф', "f"}, {'х', "h"}, {'ц', "ts"}, {'ч', "ch"},
        {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""},
        {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
        {' ', "-"}, {'.', ""}, {',', ""}, {':', ""}, {'/', "-"}
    };

    public static string GenerateSlug(string text)
    {
        if (string.IsNullOrEmpty(text)) return "lot";

        var sb = new StringBuilder();
        var lowerText = text.ToLowerInvariant();

        foreach (var c in lowerText)
        {
            if (TranslitMap.TryGetValue(c, out var replacement))
            {
                sb.Append(replacement);
            }
            else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-')
            {
                sb.Append(c);
            }
            // Все остальные символы игнорируются, как в TS: (/[a-z0-9\-]/.test(char) ? char : '')
        }

        var slug = sb.ToString();

        // Аналог .replace(/-+/g, '-')
        slug = Regex.Replace(slug, @"-+", "-");
        
        // Аналог .replace(/^-|-$/g, '')
        slug = slug.Trim('-');

        // Аналог .substring(0, 60)
        if (slug.Length > 60)
        {
            slug = slug.Substring(0, 60);
            // На всякий случай, если обрезка пришлась на дефис (в TS это не делается явно после substring, 
            // но для красоты можно, хотя для точного соответствия TS оставим как есть)
        }

        return slug;
    }
}
