using System.Collections.Concurrent;
using Lots.Data.Entities;

namespace FedresursScraper.Services
{
    public interface IRosreestrService
    {
        /// <summary>
        /// Находит координаты для ПЕРВОГО валидного кадастрового номера из списка.
        /// Используется для лотов с несколькими объектами, где достаточно одной точки на карте.
        /// </summary>
        Task<Coordinates?> FindFirstCoordinatesAsync(IEnumerable<string> cadastralNumbers);

        /// <summary>
        /// Пытается получить координаты для указанного кадастрового номера через HTTP-клиент.
        /// <para>
        /// Если клиент исчерпает лимит попыток (Retry Policy) и выбросит исключение, 
        /// номер будет добавлен в очередь <see cref="_retryQueue"/> для отложенной обработки.
        /// </para>
        /// </summary>
        /// <param name="cadastralNumber">Кадастровый номер участка.</param>
        /// <returns>
        /// Объект <see cref="Coordinates"/>, если запрос успешен. 
        /// Возвращает <c>null</c>, если координаты не найдены (404), доступ запрещен (403) или произошел сбой (номер добавлен в очередь).
        /// </returns>
        Task<Coordinates?> FindCoordinatesAsync(string cadastralNumber);

        /// <summary>
        /// Запускает принудительную повторную обработку всех кадастровых номеров из очереди сбоев.
        /// </summary>
        /// <returns>
        /// Объект <see cref="RosreestrReprocessingResult"/>, содержащий списки успешных и неуспешных обработок.
        /// </returns>
        Task<RosreestrReprocessingResult> ReprocessRetryQueueAsync();

        /// <summary>
        /// Обрабатывает пакетную передачу кадастровых номеров, последовательно запрашивая координаты для каждого из них.
        /// </summary>
        /// <param name="numbers">Коллекция кадастровых номеров для обработки.</param>
        /// <remarks>
        /// Для каждого номера вызывается метод <see cref="FindCoordinatesAsync"/>. 
        /// Ошибки при обработке отдельных номеров не прерывают обработку всего пакета (сбойные номера попадают в очередь ретраев).
        /// </remarks>
        Task ProcessBatchAsync(IEnumerable<string> numbers);

        /// <summary>
        /// Возвращает текущее количество кадастровых номеров, находящихся в очереди на повторную обработку (_retryQueue).
        /// </summary>
        /// <returns>Целое число, показывающее размер очереди.</returns>
        int GetQueueSize();

        Task<List<CadastralInfo>> FindAllCadastralInfosAsync(IEnumerable<string> cadastralNumbers);
    }

    /// <summary>
    /// Сервис-оркестратор для работы с данными Росреестра. 
    /// Отвечает за бизнес-логику получения координат, обработку критических сбоев и управление 
    /// очередью отложенной обработки (Fallback).
    /// </summary>
    public class RosreestrService : IRosreestrService
    {
        private readonly IRosreestrServiceClient _client;
        private readonly ILogger<RosreestrService> _logger;

        private static readonly ConcurrentQueue<string> _retryQueue = new();

        public RosreestrService(IRosreestrServiceClient client, ILogger<RosreestrService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<Coordinates?> FindFirstCoordinatesAsync(IEnumerable<string> cadastralNumbers)
        {
            if (cadastralNumbers == null) return null;

            foreach (var number in cadastralNumbers)
            {
                if (string.IsNullOrWhiteSpace(number)) continue;

                // Пытаемся получить координаты для очередного номера.
                // Если сервис вернет ошибку, номер уйдет в _retryQueue (через FindCoordinatesAsync),
                // мы получим null и просто пойдем к следующему номеру.
                var coords = await FindCoordinatesAsync(number);

                if (coords != null)
                {
                    // Остальные номера проверять нет смысла
                    return coords;
                }
            }

            // Если прошли весь список и ничего не нашли (или все упало с ошибками)
            return null;
        }

        public async Task<Coordinates?> FindCoordinatesAsync(string cadastralNumber)
        {
            try
            {
                // Клиент внутри себя уже сделает 3 попытки с паузами
                var coordsArray = await _client.GetCoordinatesAsync(cadastralNumber);

                if (coordsArray != null && coordsArray.Length == 2)
                {
                    return new Coordinates { Latitude = coordsArray[0], Longitude = coordsArray[1] };
                }
            }
            catch (Exception ex)
            {
                // ЭТО НАШ FALLBACK
                // Если мы здесь, значит Retry Policy в клиенте исчерпала лимит (3 раза) и сдалась.
                _logger.LogError(ex, "Все попытки получения координат для {Number} исчерпаны. Добавляем в очередь репроцессинга.", cadastralNumber);

                // Добавляем в очередь (только если это не 404/403, которые мы обработали внутри клиента возвратом null)
                _retryQueue.Enqueue(cadastralNumber);
            }

            return null;
        }

        public async Task ProcessBatchAsync(IEnumerable<string> numbers)
        {
            foreach (var number in numbers)
            {
                await FindCoordinatesAsync(number);
            }
        }

        public async Task<RosreestrReprocessingResult> ReprocessRetryQueueAsync()
        {
            var result = new RosreestrReprocessingResult();

            if (_retryQueue.IsEmpty)
            {
                return result;
            }

            // Выгружаем текущий снимок очереди, чтобы не зациклиться
            var snapshot = new List<string>();
            while (_retryQueue.TryDequeue(out var number))
            {
                // Убираем дубликаты прямо на этапе вычитки
                if (!snapshot.Contains(number))
                {
                    snapshot.Add(number);
                }
            }

            _logger.LogInformation("Начинаем репроцессинг. Извлечено {Count} уникальных номеров из очереди.", snapshot.Count);

            // Проходим по снимку
            foreach (var number in snapshot)
            {
                // Вызываем основной метод получения координат.
                // Он сам решит: если успех -> вернет координаты.
                // Если провал (500) -> вернет null и сам добавит номер ОБРАТНО в _retryQueue.
                // Если 404/403 -> вернет null (в очередь не добавит, но для нас это тоже "неуспех" получения координат в данном контексте).
                var coords = await FindCoordinatesAsync(number);

                if (coords != null)
                {
                    result.Succeeded[number] = coords;
                    _logger.LogInformation("Репроцессинг: номер {Number} успешно обработан.", number);
                }
                else
                {
                    result.Failed.Add(number);
                    // Логировать ошибку тут не обязательно, т.к. FindCoordinatesAsync уже залогировал причину (500 или 404).
                }
            }

            return result;
        }

        public int GetQueueSize() => _retryQueue.Count;

        public async Task<List<CadastralInfo>> FindAllCadastralInfosAsync(IEnumerable<string> cadastralNumbers)
        {
            var results = new List<CadastralInfo>();
            if (cadastralNumbers == null) return results;

            foreach (var number in cadastralNumbers)
            {
                if (string.IsNullOrWhiteSpace(number)) continue;

                try
                {
                    var infoDto = await _client.GetCadastralInfoAsync(number);
                    if (infoDto != null)
                    {
                        results.Add(new CadastralInfo
                        {
                            CadastralNumber = number,
                            RawGeoJson = infoDto.RawGeoJson,
                            Area = infoDto.Area,
                            CadastralCost = infoDto.CadastralCost,
                            Category = infoDto.Category,
                            PermittedUse = infoDto.PermittedUse,
                            Address = infoDto.Address
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка получения инфо для {Number}", number);
                    _retryQueue.Enqueue(number);
                }
            }
            return results;
        }

    }
}


// Модель для хранения координат
public class Coordinates
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class RosreestrReprocessingResult
{
    public Dictionary<string, Coordinates> Succeeded { get; set; } = new();
    public List<string> Failed { get; set; } = new();

    public int TotalProcessed => Succeeded.Count + Failed.Count;
}
