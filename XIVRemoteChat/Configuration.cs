using System;
using System.Collections.Generic;

using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace XIVRemoteChat;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string? Token { get; set; } = null;
    public string? EncryptionPassword { get; set; } = null;
    public string? EncryptionSalt { get; set; } = null;

	public bool IHaveALargeFriendsList { get; set; }

    public bool HasEncryptionSettings => !string.IsNullOrEmpty(EncryptionPassword) && !string.IsNullOrEmpty(EncryptionSalt);

    public Dictionary<XivChatType, bool> EnabledChannels { get; set; } = new()
    {
        [XivChatType.Debug] = true,
        [XivChatType.Urgent] = false,
        [XivChatType.Say] = false,
        [XivChatType.Shout] = false,
        [XivChatType.Yell] = false,
        [XivChatType.Echo] = false,
        [XivChatType.TellIncoming] = true,
        [XivChatType.TellOutgoing] = true,
        [XivChatType.Party] = true,
        [XivChatType.Alliance] = false,
        [XivChatType.FreeCompany] = true,
        [XivChatType.PvPTeam] = false,
        [XivChatType.NoviceNetwork] = false,
        [XivChatType.CustomEmote] = true,
        [XivChatType.StandardEmote] = true,
        [XivChatType.Ls1] = false,
        [XivChatType.Ls2] = false,
        [XivChatType.Ls3] = false,
        [XivChatType.Ls4] = false,
        [XivChatType.Ls5] = false,
        [XivChatType.Ls6] = false,
        [XivChatType.Ls7] = false,
        [XivChatType.Ls8] = false,
        [XivChatType.CrossLinkShell1] = false,
        [XivChatType.CrossLinkShell2] = false,
        [XivChatType.CrossLinkShell3] = false,
        [XivChatType.CrossLinkShell4] = false,
        [XivChatType.CrossLinkShell5] = false,
        [XivChatType.CrossLinkShell6] = false,
        [XivChatType.CrossLinkShell7] = false,
        [XivChatType.CrossLinkShell8] = false,
    };

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
