using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Unity.Collections;

namespace StopYap;

public class StopYap
{
    private const string MESSAGE_PREFIX = "<color=orange><b>StopYap</b></color>";
    private const string HELP_MESSAGE = $"commands:\n"
        + "* <b>/mute [name/number]</b> - Mute a player."
        + "* <b>/unmute [name/number]</b> - Unmute a player.";

    private static readonly HashSet<FixedString32Bytes> mutedPlayers = [];

    private static void sendMessage(UIChat uiChat, object message)
    {
        uiChat.AddChatMessage($"{MESSAGE_PREFIX} {message}");
    }

    private static List<Player> MatchingPlayers(string name = null, int number = -1)
    {
        List<Player> matching = [];

        PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
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

    [HarmonyPatch(typeof(UIChat), nameof(UIChat.Client_SendClientChatMessage))]
    public static class UIChatSendClientChatMessagePatch
    {
        [HarmonyPrefix]
        public static void Client_SendClientChatMessage(UIChat __instance, string message)
        {
            UIChat uiChat = __instance;

            if (uiChat == null) return;
            if (message == null) return;
            if (!message.StartsWith("/")) return;

            string name;
            int number;
            string[] words = message.Split();
            switch (words)
            {
                case ["/help"]:
                    sendMessage(uiChat, HELP_MESSAGE);
                    return;
                case ["/mute" or "/unmute", string nameOrNumber]:
                    name = nameOrNumber;
                    if (int.TryParse(words[1], out number)) { name = null; } else { number = -1; }
                    break;
                case ["/mute" or "/unmute"]:
                    sendMessage(uiChat, "Missing a name or number.");
                    return;
                default:
                    return;
            }

            List<Player> matching = MatchingPlayers(name, number);

            if (matching.Count == 0) { sendMessage(uiChat, "There is no matching player."); return; }
            if (matching.Count > 1) { sendMessage(uiChat, "That matches more than one player."); return; }

            Player theOne = matching[0];
            name = theOne.Username.Value.ToString();
            number = theOne.Number.Value;
            FixedString32Bytes steamId = theOne.SteamId.Value;

            switch (words[0].Equals("/mute"), mutedPlayers.Contains(steamId))
            {
                case (false, false):
                    sendMessage(uiChat, $"<b>#{number} {name}</b> is not muted.");
                    break;
                case (false, true):
                    mutedPlayers.Remove(steamId);
                    sendMessage(uiChat, $"Unmuted <b>#{number} {name}</b>.");
                    Mod.LogDebug($"Unmuted player: number={number}, name={name}, steamid={steamId}");
                    break;
                case (true, false):
                    mutedPlayers.Add(steamId);
                    sendMessage(uiChat, $"Muted <b>#{number} {name}</b>.");
                    Mod.LogDebug($"Muted player: number={number}, name={name}, steamid={steamId}");
                    break;
                case (true, true):
                    sendMessage(uiChat, $"<b>#{number} {name}</b> is already muted.");
                    break;
            }
        }
    }

    static readonly Regex regex = new(@"^(?:\[TEAM\] )?(?:<.+>)?<noparse>#(\d\d?) ([\w\d\s]+)<\/noparse>(?:<.+>)?: <noparse>.*<\/noparse>$");

    [HarmonyPatch(typeof(UIChat), nameof(UIChat.AddChatMessage))]
    public static class UIChatClientAddChatMessage
    {
        [HarmonyPrefix]
        public static bool AddChatMessage(string message)
        {
            Mod.LogDebug($"UIChat.AddChatMessage({message})");

            MatchCollection matches = regex.Matches(message); // TODO: Maybe replace this with Unity's RichTextParser.
            Match match = ((IEnumerable<Match>)matches).FirstOrDefault();
            if (match == null) return true;

            int number = int.Parse(match.Groups[1].Value);
            string name = match.Groups[2].Value;
            List<Player> matching = MatchingPlayers(name, number);

            if (matching.Where(player => mutedPlayers.Contains(player.SteamId.Value)).Any()) return false;
            return true;
        }
    }
}
