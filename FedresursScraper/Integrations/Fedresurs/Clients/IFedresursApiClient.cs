using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FedresursScraper.Integrations.Fedresurs.Models;

namespace FedresursScraper.Integrations.Fedresurs.Clients;

public interface IFedresursApiClient
{
    /// <summary>
    /// Получает список сообщений за указанный период.
    /// </summary>
    Task<MessagesResponse?> GetMessagesAsync(
        DateTime dateBegin,
        DateTime dateEnd,
        string[] types,
        int offset = 0,
        int limit = 500,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получает связанные сообщения (цепочки).
    /// </summary>
    Task<List<LinkedMessageDto>?> GetLinkedMessagesAsync(
        Guid messageGuid,
        CancellationToken cancellationToken = default);
}