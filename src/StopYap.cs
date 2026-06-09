using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Unity.Collections;

namespace StopYap;

public class StopYap
{
    private const string MESSAGE_PREFIX = "<color=orange><b>StopYap</b></color>";
    private const string HELP_MESSAGE = $"commands:\n"
        + "* <b>/mute [name/number]</b> - Mute a player.\n"
        + "* <b>/unmute [name/number]</b> - Unmute a player.";

    private static readonly HashSet<FixedString32Bytes> mutedPlayers = [];

    private static UIChat uiChat = null;

    private static void sendMessage(object message)
    {
        if (uiChat == null)
        {
            Mod.LogError("A UIChat instance has not been saved yet.");
            return;
        }
        ChatMessage chatMessage = new()
        {
            SteamID = null,
            Username = null,
            Team = null,
            Content = $"{MESSAGE_PREFIX} {message}",
            Timestamp = Utils.GetTimestamp(),
            IsQuickChat = false,
            IsTeamChat = false,
            IsSystem = true,
        };
        uiChat.AddChatMessage(chatMessage, Units.Metric, false);
    }

    private static List<Player> MatchingPlayers(string name = null, int number = -1)
    {
        List<Player> matching = [];

        PlayerManager playerManager = MonoBehaviourSingleton<PlayerManager>.Instance;
        if (playerManager == null) return matching;

        List<Player> players = playerManager.GetPlayers();
        foreach (Player player in players)
        {
            if (player == null) continue;
            string playerName = player.Username.Value.ToString();
            int playerNumber = player.Number.Value;

            if (
                (name == null || playerName.Equals(name))
                && (number == -1 || playerNumber == number)
            )
            {
                matching.Add(player);
            }
        }
        return matching;
    }

    static readonly FieldInfo _uiChat = typeof(UIChatController).GetField("uiChat", BindingFlags.Instance | BindingFlags.NonPublic);

    [HarmonyPatch(typeof(ChatManager), nameof(ChatManager.Client_SendChatMessage))]
    public static class ChatManagerClientSendChatMessagePatch
    {
        [HarmonyPrefix]
        public static bool Client_SendChatMessage(string content)
        {
            if (content == null) return true;
            if (!content.StartsWith("/")) return true;

            string name;
            int number;
            string[] words = content.Split();
            switch (words)
            {
                case ["/help"]:
                    sendMessage(HELP_MESSAGE);
                    return true; // This should stop giving "Unknown command" once it is back.
                case ["/mute" or "/unmute", string nameOrNumber]:
                    name = nameOrNumber;
                    if (int.TryParse(words[1], out number)) { name = null; } else { number = -1; }
                    break;
                case ["/mute" or "/unmute"]:
                    sendMessage("Missing a name or number.");
                    return false;
                default:
                    return true;
            }

            List<Player> matching = MatchingPlayers(name, number);

            if (matching.Count == 0) { sendMessage("There is no matching player."); return false; }
            if (matching.Count > 1) { sendMessage("That matches more than one player."); return false; }

            Player theOne = matching[0];
            name = theOne.Username.Value.ToString();
            number = theOne.Number.Value;
            FixedString32Bytes steamId = theOne.SteamId.Value;

            switch (words[0].Equals("/mute"), mutedPlayers.Contains(steamId))
            {
                case (false, false):
                    sendMessage($"<b>#{number} {name}</b> is not muted.");
                    break;
                case (false, true):
                    mutedPlayers.Remove(steamId);
                    sendMessage($"Unmuted <b>#{number} {name}</b>.");
                    Mod.LogDebug($"Unmuted player: number={number}, name={name}, steamid={steamId}");
                    break;
                case (true, false):
                    mutedPlayers.Add(steamId);
                    sendMessage($"Muted <b>#{number} {name}</b>.");
                    Mod.LogDebug($"Muted player: number={number}, name={name}, steamid={steamId}");
                    break;
                case (true, true):
                    sendMessage($"<b>#{number} {name}</b> is already muted.");
                    break;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(UIChat), nameof(UIChat.AddChatMessage))]
    public static class UIChatAddChatMessagePatch
    {
        [HarmonyPrefix]
        public static bool AddChatMessage(UIChat __instance, ChatMessage chatMessage)
        {
            uiChat = __instance; // Save the instance for adding chat messages later.
            string escapedContent = Regex.Escape(chatMessage.Content.ToString());
            Mod.LogDebug($"ChatManager.AddChatMessage(ChatMessage {{SteamID={chatMessage.SteamID}, Username={chatMessage.Username}, Team={chatMessage.Team}, Content=\"{escapedContent}\", Timestamp={chatMessage.Timestamp}, IsQuickChat={chatMessage.IsQuickChat}, IsTeamChat={chatMessage.IsTeamChat}, IsSystem={chatMessage.IsSystem}}})");
            if (chatMessage.SteamID == null) return true;
            return !mutedPlayers.Contains(chatMessage.SteamID.Value);
        }
    }
}
