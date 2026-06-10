using System.Collections.Generic;

namespace XIVRemoteChat.Models;

public record CreateChatsDto(IEnumerable<ChatDto> Chats);
