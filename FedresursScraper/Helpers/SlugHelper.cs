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
        {' ', "-"}, {'.', ""}, {',', ""}, {':', ""}, {'/', "-"},
        {'№', "n"}, {'×', "x"}
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
        }

        var slug = sb.ToString();

        // Заменяем множественные дефисы на один
        slug = Regex.Replace(slug, @"-+", "-");
        
        // Убираем дефисы по краям перед проверкой длины
        slug = slug.Trim('-');

        // Увеличиваем лимит для большей информативности (оптимально 80-90 для SEO)
        int maxLength = 85; 

        if (slug.Length > maxLength)
        {
            // Ищем последний дефис в пределах лимита, чтобы не резать слово пополам
            int lastDashIndex = slug.LastIndexOf('-', maxLength);

            if (lastDashIndex > 0)
            {
                slug = slug.Substring(0, lastDashIndex);
            }
            else
            {
                // Если дефисов почему-то нет (одно гигантское слово), режем жестко
                slug = slug.Substring(0, maxLength); 
            }
        }

        // Еще раз убираем дефисы с конца (на случай, если обрезка пришлась на дефис)
        slug = slug.Trim('-');

        // убираем висячие предлоги на конце (i, v, s, k, o, u, na, po, za, do)
        slug = Regex.Replace(slug, @"-(i|v|s|k|o|u|na|po|za|do)$", "");

        return slug;
    }
}
