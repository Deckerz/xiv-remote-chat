using System;

namespace XIVRemoteChat.Models;

public record SocketChatMessageDto(string Channel, string SenderName, string Message, bool Self, DateTime PostedDate);
