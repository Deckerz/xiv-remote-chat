namespace XIVRemoteChat.Models;

public record ChatDto(string CharacterId, string SenderName, string Channel, string Message, bool Self);
