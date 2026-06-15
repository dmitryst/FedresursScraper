using System.Globalization;
using System.Text;

namespace Lots.Application.Services;

public static class RussianMoneyFormatter
{
    private static readonly string[] Units =
    [
        "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять",
        "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать",
        "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"
    ];

    private static readonly string[] Tens =
    [
        "", "", "двадцать", "тридцать", "сорок", "пятьдесят",
        "шестьдесят", "семьдесят", "восемьдесят", "девяносто"
    ];

    private static readonly string[] Hundreds =
    [
        "", "сто", "двести", "триста", "четыреста", "пятьсот",
        "шестьсот", "семьсот", "восемьсот", "девятьсот"
    ];

    public static string FormatAmount(decimal amount)
    {
        var value = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        return value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")).Replace('\u00A0', ' ');
    }

    public static string ToWords(decimal amount)
    {
        var value = (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (value == 0) return "Ноль";

        var words = Convert(value);
        if (words.Length == 0) return "Ноль";

        return char.ToUpper(words[0]) + words[1..];
    }

    private static string Convert(long number)
    {
        if (number == 0) return "";

        var parts = new List<string>();

        AppendScale(parts, number / 1_000_000, ["миллион", "миллиона", "миллионов"], false);
        number %= 1_000_000;

        AppendScale(parts, number / 1_000, ["тысяча", "тысячи", "тысяч"], true);
        number %= 1_000;

        if (number > 0)
        {
            parts.Add(ConvertLessThanThousand(number, false));
        }

        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static void AppendScale(List<string> parts, long value, string[] forms, bool feminine)
    {
        if (value <= 0) return;

        parts.Add(ConvertLessThanThousand(value, feminine));
        parts.Add(Pluralize(value, forms));
    }

    private static string ConvertLessThanThousand(long number, bool feminine)
    {
        var sb = new StringBuilder();

        var hundreds = number / 100;
        var remainder = number % 100;

        if (hundreds > 0)
        {
            sb.Append(Hundreds[hundreds]);
        }

        if (remainder > 0)
        {
            if (sb.Length > 0) sb.Append(' ');

            if (remainder < 20)
            {
                sb.Append(Units[remainder]);
            }
            else
            {
                sb.Append(Tens[remainder / 10]);
                var unit = (int)(remainder % 10);
                if (unit > 0)
                {
                    sb.Append(' ');
                    sb.Append(feminine ? FeminineUnit(unit) : Units[unit]);
                }
            }
        }

        return sb.ToString();
    }

    private static string FeminineUnit(int unit) => unit switch
    {
        1 => "одна",
        2 => "две",
        _ => Units[unit]
    };

    private static string Pluralize(long number, string[] forms)
    {
        var n = Math.Abs(number) % 100;
        if (n is >= 11 and <= 14) return forms[2];

        n = Math.Abs(number) % 10;
        return n switch
        {
            1 => forms[0],
            >= 2 and <= 4 => forms[1],
            _ => forms[2]
        };
    }
}
