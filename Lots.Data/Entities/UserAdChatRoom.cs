using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Lots.Data.Entities;

/// <summary>
/// Сущность комнаты чата
/// </summary>
public class UserAdChatRoom
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // Привязка к объявлению
    public Guid AdId { get; set; }
    [ForeignKey("AdId")]
    public UserAd Ad { get; set; } = default!;

    // ID покупателя (того, кто нажал "Связаться")

    public Guid BuyerId { get; set; } 
    
    // ID продавца (владельца объявления)
    public Guid SellerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserAdChatMessage> Messages { get; set; } = new List<UserAdChatMessage>();
}