namespace FedresursScraper.UserAds;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid? RoomId { get; set; }
    public string SenderId { get; set; } = default!;
    public string Text { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public class SendMessageRequest
{
    public Guid AdId { get; set; }
    public string Text { get; set; } = default!;
}

public class InboxItemDto
{
    public Guid RoomId { get; set; }
    public Guid AdId { get; set; }
    public string AdTitle { get; set; } = default!;
    public string AdImageUrl { get; set; } = default!;
    public string CompanionName { get; set; } = default!;
    public string LastMessageText { get; set; } = default!;
    public DateTime LastMessageDate { get; set; }
    public int UnreadCount { get; set; }
}

public class MarkAsReadRequest
{
    public Guid RoomId { get; set; }
}