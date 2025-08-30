using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public interface ICadastralNumberExtractor
{
    public List<string> Extract(string description);
}

public class CadastralNumberExtractor : ICadastralNumberExtractor
{
    /// <summary>
    /// Извлекает все кадастровые номера из строки описания.
    /// </summary>
    /// <param name="description">Текстовое описание лота.</param>
    /// <returns>Список найденных кадастровых номеров.</returns>
    public List<string> Extract(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return new List<string>();
        }

        const string pattern = @"\d+:\d+:\d+:\d+";

        var matches = Regex.Matches(description, pattern);

        var cadastralNumbers = matches.Cast<Match>()
                                      .Select(m => m.Value)
                                      .ToList();

        return cadastralNumbers;
    }
}
