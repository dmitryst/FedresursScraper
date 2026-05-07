using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Сущность отдельного сообщения
/// </summary>
public class UserAdChatMessage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChatRoomId { get; set; }
    [ForeignKey("ChatRoomId")]
    public UserAdChatRoom Room { get; set; } = default!;

    public Guid SenderId { get; set; } // Кто написал (BuyerId или SellerId)
    public string Text { get; set; } = default!;
    
    public bool IsRead { get; set; } = false; // На будущее (непрочитанные)

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}