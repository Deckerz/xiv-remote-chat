using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;

using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

using static FFXIVClientStructs.FFXIV.Client.UI.Info.InfoProxyCommonList.CharacterData;

namespace XIVRemoteChat;



public static class Helpers
{
    private static readonly string FLSignature = "40 53 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B D9 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 0F 84 ?? ?? ?? ?? 44 0F B6 43 ?? 33 C9";

    private static unsafe delegate* unmanaged<InfoProxyFriendList*, byte> friendsListScanSig = null;

    public static unsafe void RefreshPlayerFreindsList()
    {
        if (friendsListScanSig == null)
        {
            var sig = Plugin.SigScanner.ScanText(FLSignature);
            if (sig == IntPtr.Zero)
            {
                Plugin.Log.Error("Failed to find friend list refresh function signature");
                return;
            }

            friendsListScanSig = (delegate* unmanaged<InfoProxyFriendList*, byte>)sig;
        }

        friendsListScanSig(InfoProxyFriendList.Instance());
    }

    public static unsafe List<(string Name, string Status)> GetOnlineFriends()
    {
        var result = new List<(string, string)>();
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
        {
            return result;
        }

        for (var i = 0; i < proxy->InfoProxyCommonList.CharDataSpan.Length; i++)
        {
            var entry = proxy->CharData[i];
            if (entry.State == OnlineStatus.Offline || entry.State.HasFlag(OnlineStatus.AnotherWorld))
            {
                continue;
            }

            var worldName = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>()
                .GetRowOrDefault(entry.HomeWorld)?.Name.ExtractText() ?? entry.HomeWorld.ToString();
            result.Add(($"{entry.NameString}@{worldName}", entry.State.ToString()));
        }
        return result;
    }

    public static unsafe string GetLocalContentId() => PlayerState.Instance()->ContentId.ToString();

    public static unsafe string LookupContentId(string senderName)
    {
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.Name.TextValue != senderName)
            {
                continue;
            }

            var chara = (Character*)obj.Address;
            return chara->ContentId.ToString();
        }
        return "0";
    }

    public static string ReplacePuaChars(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= '' and <= '')
            {
                sb.Append($"<U+{(int)c:X4}>");
                continue;
            }

            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                var codePoint = char.ConvertToUtf32(c, text[i + 1]);
                if (codePoint is >= 0xF0000 and <= 0x10FFFF)
                {
                    sb.Append($"<U+{codePoint:X5}>");
                    i++;
                    continue;
                }

                sb.Append(c);
                sb.Append(text[++i]);
                continue;
            }

            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string ConvertSeString(SeString seString)
    {
        var sb = new StringBuilder();
        foreach (var payload in seString.Payloads)
        {
            switch (payload)
            {
                case TextPayload { Text: not null } text:
                    sb.Append(text.Text);
                    break;
                case AutoTranslatePayload auto:
                    sb.Append($"{auto.Text.ToString().Trim()}");
                    break;
            }
        }
        return ReplacePuaChars(sb.ToString());
    }

    public static string Encrypt(string text, string password, string salt)
    {
        byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
        byte[] iv = RandomNumberGenerator.GetBytes(16);

#pragma warning disable SYSLIB0041 // static Pbkdf2 uses Windows CNG which fails under Wine
        using var deriveBytes = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
#pragma warning restore SYSLIB0041
        byte[] key = deriveBytes.GetBytes(32);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] plaintext = Encoding.UTF8.GetBytes(text);
        byte[] cipher = aes.CreateEncryptor().TransformFinalBlock(plaintext, 0, plaintext.Length);

        byte[] output = iv.Concat(cipher).ToArray();
        return Convert.ToBase64String(output);
    }
}
