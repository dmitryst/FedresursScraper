using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FedresursScraper.Integrations.Fedresurs.Models;

/// <summary>
/// Структура объекта с данными по связанному сообщению.
/// </summary>
public class LinkedMessageDto
{
    /// <summary>
    /// GUID сообщения.
    /// </summary>
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    /// <summary>
    /// Номер сообщения.
    /// </summary>
    [JsonPropertyName("number")]
    public string Number { get; set; } = null!;

    /// <summary>
    /// Дата публикации сообщения.
    /// </summary>
    [JsonPropertyName("datePublish")]
    public DateTime DatePublish { get; set; }

    /// <summary>
    /// Тип сообщения.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    /// <summary>
    /// GUID сообщения об аннулировании.
    /// </summary>
    [JsonPropertyName("annulmentMessageGuid")]
    public Guid? AnnulmentMessageGuid { get; set; }

    /// <summary>
    /// Причина блокировки.
    /// </summary>
    [JsonPropertyName("lockReason")]
    public string? LockReason { get; set; }

    /// <summary>
    /// Guid-ы сообщений, на которое ссылается текущее.
    /// </summary>
    [JsonPropertyName("contentMessageGuids")]
    public List<Guid> ContentMessageGuids { get; set; } = new();
}