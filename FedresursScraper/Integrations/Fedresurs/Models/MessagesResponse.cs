using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FedresursScraper.Integrations.Fedresurs.Models;

/// <summary>
/// Возвращаемый объект со списком сообщений.
/// </summary>
public class MessagesResponse
{
    /// <summary>
    /// Количество найденных записей.
    /// </summary>
    [JsonPropertyName("total")]
    public int Total { get; set; }

    /// <summary>
    /// Список сообщений, каждое сообщение в своем объекте.
    /// </summary>
    [JsonPropertyName("pageData")]
    public List<MessageDto> PageData { get; set; } = new();
}

/// <summary>
/// Структура объекта с данными по сообщению.
/// </summary>
public class MessageDto
{
    /// <summary>
    /// GUID сообщения.
    /// </summary>
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; }

    /// <summary>
    /// GUID должника.
    /// </summary>
    [JsonPropertyName("bankruptGuid")]
    public Guid? BankruptGuid { get; set; }

    /// <summary>
    /// GUID сообщения об аннулировании.
    /// </summary>
    [JsonPropertyName("annulmentMessageGuid")]
    public Guid? AnnulmentMessageGuid { get; set; }

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
    /// Содержание (контент) сообщения. 
    /// Возвращается только если в запросе указан параметр includeContent=true.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Тип сообщения.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    /// <summary>
    /// Причина блокировки. Возвращается только если сообщение заблокировано.
    /// </summary>
    [JsonPropertyName("lockReason")]
    public string? LockReason { get; set; }

    /// <summary>
    /// Признак публикации с нарушением сроков.
    /// </summary>
    [JsonPropertyName("hasViolation")]
    public bool? HasViolation { get; set; }
}