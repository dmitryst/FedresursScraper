using System.Globalization;
using System.Text.RegularExpressions;
using Lots.Application.Services.DebtScoring.Models;
using Lots.Data.Entities.DebtScoring;

namespace Lots.Application.Services.DebtScoring;

public partial class CourtActEntityExtractor : ICourtActEntityExtractor
{
    private static readonly (string Pattern, string Label, double Confidence)[] DebtBasisPatterns =
    [
        (@"(?:признан(?:а|о|ы)?\s+)?недействительн(?:ой|ым|ыми)?\s+(?:сделк(?:а|и|ой)|договор)", "Признание сделки недействительной", 0.85),
        (@"(?:договор\s+)?(?:займа|за\s?ёма|заема)", "Договор займа", 0.9),
        (@"(?:неосновательн(?:ое|ого)\s+обогащени(?:е|я))", "Неосновательное обогащение", 0.9),
        (@"(?:коммунальн(?:ые|ых)\s+(?:платеж(?:и|ей)|услуг))", "Коммунальные платежи", 0.85),
        (@"(?:кредитн(?:ый|ого)\s+договор)", "Кредитный договор", 0.85),
        (@"(?:право\s+требовани(?:я|е))", "Право требования", 0.8),
    ];

    public CourtActExtractionResult Extract(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CourtActExtractionResult();
        }

        var normalized = Regex.Replace(text, @"\s+", " ");
        var entities = new List<ExtractedEntityResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMatches(entities, seen, ExtractedEntityType.CaseNumber, CaseNumberRegex(), normalized, 0.95);
        AddMatches(entities, seen, ExtractedEntityType.CaseNumber, CaseNumberByDeluRegex(), normalized, 0.93);
        AddMatches(entities, seen, ExtractedEntityType.Inn, InnRegex(), normalized, 0.9);
        AddMatches(entities, seen, ExtractedEntityType.Snils, SnilsRegex(), normalized, 0.92);
        AddMatches(entities, seen, ExtractedEntityType.Ogrn, OgrnRegex(), normalized, 0.9);
        AddMatches(entities, seen, ExtractedEntityType.BirthDate, BirthDateRegex(), normalized, 0.75);

        var debtorName = ExtractDebtorName(normalized);
        if (debtorName != null)
        {
            AddEntity(entities, seen, ExtractedEntityType.DebtorName, debtorName, 0.85);
        }

        var debtBasisText = ExtractDebtBasisText(normalized);
        if (debtBasisText != null)
        {
            AddEntity(entities, seen, ExtractedEntityType.DebtBasis, debtBasisText, 0.88);
        }

        var address = ExtractRegistrationAddress(normalized);
        if (address != null)
        {
            AddEntity(entities, seen, ExtractedEntityType.RegistrationAddress, address, 0.65);
        }

        var debtBasisCategory = ExtractDebtBasisCategory(normalized);
        if (debtBasisCategory is { } basis && debtBasisText == null)
        {
            AddEntity(entities, seen, ExtractedEntityType.DebtBasis, basis.Label, basis.Confidence);
        }

        var debtNominal = ExtractDebtNominal(normalized);

        return new CourtActExtractionResult
        {
            Entities = entities,
            DebtNominal = debtNominal,
        };
    }

    private static void AddMatches(
        List<ExtractedEntityResult> entities,
        HashSet<string> seen,
        ExtractedEntityType type,
        Regex regex,
        string text,
        double confidence)
    {
        foreach (Match match in regex.Matches(text))
        {
            var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
            value = value.Trim(' ', '.', ',', ';', ':', '"', '\'', '«', '»');
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            AddEntity(entities, seen, type, value, confidence);
        }
    }

    private static void AddEntity(
        List<ExtractedEntityResult> entities,
        HashSet<string> seen,
        ExtractedEntityType type,
        string value,
        double confidence)
    {
        var key = $"{type}:{value}";
        if (!seen.Add(key))
        {
            return;
        }

        entities.Add(new ExtractedEntityResult
        {
            EntityType = type,
            Value = value,
            Confidence = confidence,
            Source = EntityExtractionSource.Regex,
        });
    }

    private static string? ExtractDebtorName(string text)
    {
        var patterns = new[]
        {
            @"(?:дебитор[:\s]+)([А-ЯЁ][а-яёA-Za-z]+(?:\s+[А-ЯЁ][а-яёA-Za-z]+){1,3})",
            @"(?:должник(?:а|у|ом)?[:\s]+)([А-ЯЁ][а-яёA-Za-z]+(?:\s+[А-ЯЁ][а-яёA-Za-z]+){1,3})",
            @"(?:в\s+отношении\s+)([А-ЯЁ][а-яёA-Za-z]+(?:\s+[А-ЯЁ][а-яёA-Za-z]+){1,3})",
            @"(?:ответчик[:\s]+)([А-ЯЁ][а-яёA-Za-z]+(?:\s+[А-ЯЁ][а-яёA-Za-z]+){1,3})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static string? ExtractRegistrationAddress(string text)
    {
        var match = Regex.Match(
            text,
            @"(?:адрес(?:\s+регистрации)?[:\s]+)(.{10,200}?)(?:\.|,|\s+ИНН|\s+СНИЛС|\s+паспорт|$)",
            RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractDebtBasisText(string text)
    {
        var match = Regex.Match(
            text,
            @"(?:основание\s+возникновения[:\s]+)(.+)$",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim().TrimEnd('.', ',');
        return value.Length >= 10 ? value : null;
    }

    private static (string Label, double Confidence)? ExtractDebtBasisCategory(string text)
    {
        foreach (var (pattern, label, confidence) in DebtBasisPatterns)
        {
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                return (label, confidence);
            }
        }

        return null;
    }

    private static decimal? ExtractDebtNominal(string text)
    {
        var match = Regex.Match(
            text,
            @"(?:дебиторск(?:ая|ой)\s+задолженност(?:ь|и)\s+)?(?:в\s+размере\s+)([\d\s]+(?:[,\.]\d{2})?)\s*(?:руб\.?|₽|RUB)?",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"(?:сумм(?:а|е|у|ой)?\s+(?:задолженности|долга|требований)?[:\s]*)([\d\s]+(?:[,\.]\d{2})?)\s*(?:руб\.?|₽|RUB)?",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"([\d\s]+(?:[,\.]\d{2})?)\s*(?:руб\.?|₽)\s*(?:\d{2})?",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
        {
            return null;
        }

        var raw = match.Groups[1].Value.Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    [GeneratedRegex(@"\b([АA]\d{2}-[\d]+(?:-\d+)?/\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CaseNumberRegex();

    [GeneratedRegex(@"(?:по\s+делу\s+)([АA]\d{2}-[\d]+(?:-\d+)?/\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex CaseNumberByDeluRegex();

    [GeneratedRegex(@"\b(?:ИНН[:\s]*)(\d{10}|\d{12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex InnRegex();

    [GeneratedRegex(@"\b(?:СНИЛС[:\s]*)(\d{3}[-\s]?\d{3}[-\s]?\d{3}[-\s]?\d{2}|\d{11})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SnilsRegex();

    [GeneratedRegex(@"\b(?:ОГРН[:\s]*)(\d{13}|\d{15})\b", RegexOptions.IgnoreCase)]
    private static partial Regex OgrnRegex();

    [GeneratedRegex(@"(?:дата\s+рождения[:\s]*)(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex BirthDateRegex();
}
