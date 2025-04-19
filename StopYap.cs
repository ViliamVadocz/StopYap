using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using Unity.Collections;

namespace StopYap;

public class StopYap
{
    private const string messagePrefix = "<color=orange><b>StopYap</b></color>";
    private const string helpMessage = $"{messagePrefix} commands:\n"
        + "* <b>/mute [name/number]</b> - Mute a player."
        + "* <b>/unmute [name/number]</b> - Unmute a player.";

    private static HashSet<FixedString32Bytes> mutedPlayers = [];

    private static List<Player> matchingPlayers(string name = null, int number = -1)
    {
        List<Player> matching = [];

        PlayerManager playerManager = NetworkBehaviourSingleton<PlayerManager>.Instance;
        if (playerManager == null) return matching;

        Il2CppSystem.Collections.Generic.List<Player> players = playerManager.GetPlayers();
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
        public static void Client_SendClientChatMessage(string message)
        {
            if (message == null) return;
            if (!message.StartsWith("/")) return;
            string[] words = message.Split();
            if (words.Length < 1) return;

            UIChat uiChat = NetworkBehaviourSingleton<UIChat>.Instance;
            if (uiChat == null) return;

            if (message.Equals("/help")) { uiChat.AddChatMessage(helpMessage); return; }
            if (!(words[0].Equals("/mute") || words[0].Equals("/unmute"))) return;
            if (words.Length != 2) { uiChat.AddChatMessage($"{messagePrefix} Missing a name or number."); return; }

            string name = words[1];
            if (int.TryParse(words[1], out int number)) { name = null; } else { number = -1; }
            List<Player> matching = matchingPlayers(name, number);

            if (matching.Count == 0) { uiChat.AddChatMessage($"{messagePrefix} There is no matching player."); return; }
            if (matching.Count > 1) { uiChat.AddChatMessage($"{messagePrefix} That matches more than one player."); return; }

            Player theOne = matching[0];
            name = theOne.Username.Value.ToString();
            number = theOne.Number.Value;
            FixedString32Bytes steamId = theOne.SteamId.Value;

            // TODO: Change confirmations based on whether they were already muted or not.
            // TODO: Add mute times.
            // TODO: Refactor to avoid checking for mute/unmute twice.
            if (words[0].Equals("/mute"))
            {
                if (mutedPlayers.Contains(steamId))
                {
                    uiChat.AddChatMessage($"{messagePrefix} <b>#{number} {name}</b> is already muted.");
                }
                else
                {
                    mutedPlayers.Add(steamId);
                    uiChat.AddChatMessage($"{messagePrefix} Muted <b>#{number} {name}</b>.");
                    Plugin.Log.LogInfo($"Muted player: number={number}, name={name}, steamid={steamId}");
                }
            }
            else
            {
                if (mutedPlayers.Contains(steamId))
                {
                    mutedPlayers.Remove(steamId);
                    uiChat.AddChatMessage($"{messagePrefix} Unmuted <b>#{number} {name}</b>.");
                    Plugin.Log.LogInfo($"Unmuted player: number={number}, name={name}, steamid={steamId}");
                }
                else
                {
                    uiChat.AddChatMessage($"{messagePrefix} <b>#{number} {name}</b> is not muted.");
                }
            }
        }
    }

    static readonly Regex regex = new(@"^(?:<.+>)*<noparse>#(\d\d?) ([\w\d\s]+)<\/noparse>(?:<.+>)*:");

    [HarmonyPatch(typeof(UIChat), nameof(UIChat.AddChatMessage))]
    public static class UIChatClientAddChatMessage
    {
        [HarmonyPrefix]
        public static bool AddChatMessage(string content)
        {
            Plugin.Log.LogInfo($"UIChat.AddChatMessage: {content}");

            MatchCollection matches = regex.Matches(content); // TODO: Maybe replace this with Unity's RichTextParser.
            Match match = matches.FirstOrDefault();
            if (match == null) return true;

            int number = int.Parse(match.Groups[1].Value);
            string name = match.Groups[2].Value;
            List<Player> matching = matchingPlayers(name, number);

            if (matching.Where(player => mutedPlayers.Contains(player.SteamId.Value)).Any()) return false;
            return true;
        }
    }
}
