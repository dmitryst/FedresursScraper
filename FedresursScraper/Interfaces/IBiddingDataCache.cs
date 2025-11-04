using FedresursScraper.Services.Models;

/// <summary>
/// Кэш для хранения первичных данных о торгах, ожидающих детального парсинга.
/// </summary>
public interface IBiddingDataCache
{
    /// <summary>
    /// Получает данные о торгах, которые нужно распарсить.
    /// </summary>
    /// <returns>Коллекция данных о торгах, ожидающих обработки.</returns>
    IReadOnlyCollection<BiddingData> GetDataToParse();

    /// <summary>
    /// Отмечает торги как успешно обработанные (или взятые в работу).
    /// </summary>
    /// <param name="biddingId">ID торгов, которые были обработаны.</param>
    void MarkAsCompleted(Guid biddingId);

    /// <summary>
    /// Добавляет новые данные о торгах в кэш.
    /// Реализация должна обеспечивать уникальность добавляемых записей.
    /// </summary>
    /// <param name="newBiddings">Коллекция новых данных о торгах.</param>
    /// <returns>Количество фактически добавленных (уникальных) записей.</returns>
    int AddMany(IEnumerable<BiddingData> newBiddings);

    /// <summary>
    /// Удаляет из кэша старые записи, которые уже были обработаны.
    /// </summary>
    void PruneCompleted();
}
