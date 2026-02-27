using Ardalis.Specification;
using Lots.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lots.Data.Specifications;

public class LotsFilterSpecification : Specification<Lot>
{
    public LotsFilterSpecification(
        string[]? categories,
        string? searchQuery = null,
        string? biddingType = null,
        decimal? priceFrom = null,
        decimal? priceTo = null,
        bool? isSharedOwnership = null,
        string[]? regions = null,
        bool onlyActive = true)
    {
        // Полнотекстовый поиск + Кадастровые номера
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            // Очищаем запрос от мусора для поиска по кадастру (оставляем только цифры)
            var cleanSearchQuery = new string(searchQuery.Where(char.IsDigit).ToArray());

            Query.Where(l =>
                // Поиск по заголовку и описанию (FTS), используем Hunspell словарь
                l.SearchVector.Matches(EF.Functions.WebSearchToTsQuery("russian_h", searchQuery)) ||

                // Поиск по точному вхождению фразы в Title (для коротких названий)
                EF.Functions.ILike(l.Title ?? "", $"%{searchQuery}%") ||

                // Поиск по кадастровым номерам (если в запросе есть цифры)
                (cleanSearchQuery.Length > 12 &&
                 l.CadastralNumbers.Any(cn => cn.CleanCadastralNumber.Contains(cleanSearchQuery)))
            );
        }

        // Фильтр по категориям
        if (categories != null && categories.Length > 0)
        {
            Query.Where(l => l.Categories.Any(lc => categories.Contains(lc.Name)));
        }

        // Фильтр по типу торгов
        if (!string.IsNullOrWhiteSpace(biddingType) && biddingType != "Все")
        {
            Query.Where(l => l.Bidding.Type == biddingType);
        }

        // Фильтр по начальной цене
        if (priceFrom.HasValue)
        {
            Query.Where(l => l.StartPrice >= priceFrom.Value);
        }

        if (priceTo.HasValue)
        {
            Query.Where(l => l.StartPrice <= priceTo.Value);
        }

        // Фильтр по долевой собственности
        // Если параметр не передан (null) - показываем всё (и доли, и не доли)
        // Если передан true - только доли
        // Если передан false - только НЕ доли
        if (isSharedOwnership.HasValue)
        {
            Query.Where(l => l.IsSharedOwnership == isSharedOwnership.Value);
        }

        // Фильтр по регионам (местонахождение имущества)
        if (regions != null && regions.Length > 0)
        {
            Query.Where(l => !string.IsNullOrEmpty(l.PropertyRegionName) && regions.Contains(l.PropertyRegionName));
        }

        // показываем только классифицированные лоты
        // Лот считается классифицированным, если у него есть Title
        Query.Where(l => !string.IsNullOrEmpty(l.Title));

        // Фильтрация по активности
        if (onlyActive)
        {
            Query.Where(Lot.IsActiveExpression);
        }
    }
}
