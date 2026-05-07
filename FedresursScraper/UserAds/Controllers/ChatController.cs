using FedresursScraper.UserAds;
using Lots.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FedresursScraper.UserAds.Hubs;
using Microsoft.AspNetCore.SignalR;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ChatController : ControllerBase
{
    private readonly LotsDbContext _dbContext;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatController(LotsDbContext dbContext, IHubContext<ChatHub> hubContext)
    {
        _dbContext = dbContext;
        _hubContext = hubContext;
    }

    // Получить историю переписки по конкретному объявлению
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] Guid adId)
    {
        // Получаем ID текущего авторизованного пользователя
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdStr, out Guid currentUserId))
            return Unauthorized("Некорректный ID пользователя.");

        // Ищем комнату, где текущий пользователь является либо покупателем, либо продавцом по этому объявлению
        var room = await _dbContext.ChatRooms
            .Include(r => r.Messages)
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(r => r.AdId == adId && (r.BuyerId == currentUserId || r.SellerId == currentUserId));

        if (room == null)
        {
            // Чата еще нет, возвращаем пустой список
            return Ok(new List<ChatMessageDto>());
        }

        var messages = room.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ChatMessageDto
            {
                Id = m.Id,
                RoomId = m.ChatRoomId,
                SenderId = m.SenderId == currentUserId ? "me" : "seller", // Маскируем ID для удобства фронта
                Text = m.Text,
                CreatedAt = m.CreatedAt,
                IsRead = m.IsRead
            }).ToList();

        return Ok(messages);
    }

    // Отправить сообщение
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Сообщение не может быть пустым.");

        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdStr, out Guid currentUserId))
            return Unauthorized("Некорректный ID пользователя.");

        var ad = await _dbContext.UserAds.FirstOrDefaultAsync(a => a.Id == request.AdId);
        if (ad == null) return NotFound("Объявление не найдено");

        bool isSeller = ad.UserId == currentUserId;
        UserAdChatRoom? room = null;

        if (isSeller)
        {
            // ЛОГИКА ПРОДАВЦА:
            // Продавец не может создать новый чат на своем объявлении.
            // Он может только ответить в уже существующий чат с покупателем.
            room = await _dbContext.ChatRooms
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(r => r.AdId == request.AdId && r.SellerId == currentUserId);

            if (room == null)
                return BadRequest("У вас еще нет диалогов с покупателями по этому объявлению.");
        }
        else
        {
            // ЛОГИКА ПОКУПАТЕЛЯ:
            // Ищем существующий чат покупателя, если нет — создаем новый.
            room = await _dbContext.ChatRooms
                .FirstOrDefaultAsync(r => r.AdId == request.AdId && r.BuyerId == currentUserId);

            if (room == null)
            {
                room = new UserAdChatRoom
                {
                    AdId = request.AdId,
                    BuyerId = currentUserId,
                    SellerId = ad.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.ChatRooms.Add(room);
            }
        }

        var newMessage = new UserAdChatMessage
        {
            ChatRoomId = room.Id,
            SenderId = currentUserId,
            Text = request.Text.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ChatMessages.Add(newMessage);
        await _dbContext.SaveChangesAsync();

        // Отправляем уведомление получателю через SignalR
        var receiverId = isSeller ? room.BuyerId : room.SellerId;
        var dto = new ChatMessageDto
        {
            Id = newMessage.Id,
            RoomId = room.Id,
            SenderId = currentUserId.ToString(), // Для получателя это будет реальный ID отправителя
            Text = newMessage.Text,
            CreatedAt = newMessage.CreatedAt,
            IsRead = false
        };
        await _hubContext.Clients.Group(receiverId.ToString()).SendAsync("ReceiveMessage", dto);

        dto.SenderId = "me"; // Для фронта отправитель всегда "я"
        return Ok(dto);
    }

    // Получить список диалогов
    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox()
    {
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdStr, out Guid currentUserId))
            return Unauthorized("Некорректный ID пользователя.");

        var rooms = await _dbContext.ChatRooms
            .Include(r => r.Ad)
                .ThenInclude(a => a.Images)
            .Include(r => r.Messages)
            .Where(r => r.BuyerId == currentUserId || r.SellerId == currentUserId)
            .ToListAsync();

        var inboxItems = new List<InboxItemDto>();

        foreach (var room in rooms)
        {
            var lastMessage = room.Messages.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
            if (lastMessage == null) continue;

            var companionId = room.BuyerId == currentUserId ? room.SellerId : room.BuyerId;
            var companion = await _dbContext.Users.FindAsync(companionId);

            var unreadCount = room.Messages.Count(m => m.SenderId != currentUserId && !m.IsRead);

            var companionName = !string.IsNullOrWhiteSpace(companion?.Name) 
                ? companion.Name 
                : "Пользователь";

            inboxItems.Add(new InboxItemDto
            {
                RoomId = room.Id,
                AdId = room.AdId,
                AdTitle = room.Ad.Title,
                AdImageUrl = room.Ad.Images.FirstOrDefault(i => i.IsMain)?.Url ?? room.Ad.Images.FirstOrDefault()?.Url ?? "",
                CompanionName = companionName,
                LastMessageText = lastMessage.Text,
                LastMessageDate = lastMessage.CreatedAt,
                UnreadCount = unreadCount
            });
        }

        return Ok(inboxItems.OrderByDescending(i => i.LastMessageDate));
    }

    // Пометить сообщения в комнате как прочитанные
    [HttpPost("read")]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadRequest request)
    {
        var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserIdStr, out Guid currentUserId))
            return Unauthorized("Некорректный ID пользователя.");

        var unreadMessages = await _dbContext.ChatMessages
            .Where(m => m.ChatRoomId == request.RoomId && m.SenderId != currentUserId && !m.IsRead)
            .ToListAsync();

        if (unreadMessages.Any())
        {
            var senderId = unreadMessages.First().SenderId;
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
            }
            await _dbContext.SaveChangesAsync();

            // Уведомляем отправителя о том, что его сообщения прочитаны
            await _hubContext.Clients.Group(senderId.ToString()).SendAsync("MessagesRead", new { RoomId = request.RoomId });
        }

        return Ok();
    }
}