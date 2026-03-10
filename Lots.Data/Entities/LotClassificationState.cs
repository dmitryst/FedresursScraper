namespace Lots.Data.Entities;

/// <summary>
/// Инфраструктурная сущность для управления очередью и состоянием классификации лота.
/// Позволяет не загрязнять основную бизнес-сущность Lot служебными данными.
/// </summary>
public class LotClassificationState
{
    /// <summary>
    /// Идентификатор лота. Выступает одновременно как первичный ключ и внешний ключ к таблице Lots.
    /// </summary>
    public Guid LotId { get; set; }

    /// <summary>
    /// Текущий статус классификации лота.
    /// </summary>
    public ClassificationStatus Status { get; set; } = ClassificationStatus.Pending;

    /// <summary>
    /// Количество неудачных попыток классификации.
    /// </summary>
    public int Attempts { get; set; } = 0;

    /// <summary>
    /// Время следующей попытки (используется для таймаутов, зависаний и отложенных повторов).
    /// </summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>
    /// Навигационное свойство для связи с основной сущностью лота.
    /// </summary>
    public Lot Lot { get; set; } = null!;
}
