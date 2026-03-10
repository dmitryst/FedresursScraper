namespace Lots.Data.Entities;

/// <summary>
/// Статусы процесса классификации лота.
/// </summary>
public enum ClassificationStatus
{
    /// <summary>
    /// Лот ожидает начала обработки.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Лот захвачен воркером и находится в процессе классификации.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Лот успешно классифицирован (конечный статус).
    /// </summary>
    Success = 2,

    /// <summary>
    /// Произошла программная ошибка или таймаут при классификации.
    /// </summary>
    Failed = 3
}
