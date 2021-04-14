using DSharpPlus.Entities;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Localization;
using Eco.Plugins.DiscordLink.Utilities;
using System;
using System.Collections.Generic;

using StoreOfferList = System.Collections.Generic.IEnumerable<System.Linq.IGrouping<string, System.Tuple<Eco.Gameplay.Components.StoreComponent, Eco.Gameplay.Components.TradeOffer>>>;
using DSharpPlus;

namespace Eco.Plugins.DiscordLink
{
    public class EcoCommands : IChatCommandHandler
    {
        #region Commands Base

        private delegate void EcoCommand(User callingUser, params string[] parameters);

        private static void ExecuteCommand<TRet>(EcoCommand command, User callingUser, params string[] parameters)
        {
            // Trim the arguments since they often have a space at the beginning
            for (int i = 0; i < parameters.Length; ++i)
            {
                parameters[i] = parameters[i].Trim();
            }

            string commandName = command.Method.Name;
            try
            {
                Logger.Debug($"{MessageUtil.StripTags(callingUser.Name)} invoked Eco command \"/{command.Method.Name}\"");
                command(callingUser, parameters);
            }
            catch (Exception e)
            {
                ChatManager.ServerMessageToPlayer(new LocString("Error occurred while attempting to run that command. Error message: " + e), callingUser);
                Logger.Error($"An exception occured while attempting to execute a command.\nCommand name: \"{commandName}\"\nCalling user: \"{MessageUtil.StripTags(callingUser.Name)}\"\nError message: {e}");
            }
        }

        [ChatCommand("Commands for the Discord integration plugin.", "DL", ChatAuthorizationLevel.User)]
#pragma warning disable IDE0060 // Remove unused parameter - callingUser parameter required
        public static void DiscordLink(User callingUser) { }
#pragma warning restore IDE0060

        #endregion

        #region User Feedback

        public static void ReportCommandError(User callingUser, string message)
        {
            callingUser.Player.Error(Localizer.NotLocalizedStr(message));
        }

        public static void ReportCommandInfo(User callingUser, string message)
        {
            callingUser.Player.InfoBox(Localizer.NotLocalizedStr(message));
        }

        public static void DisplayCommandData(User callingUser, string title, string data)
        {
            callingUser.Player.OpenInfoPanel(title, data, "DiscordLink");
        }

        #endregion

        #region Plugin Management

        [ChatSubCommand("Restart", "Restarts the plugin.", "DL-Restart", ChatAuthorizationLevel.Admin)]
        public static void Restart(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.Restart(SharedCommands.CommandSource.Eco, callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Resets world data as if a new world had been created.", "dl-resetdata", ChatAuthorizationLevel.Admin)]
        public static void ResetWorldData(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                ChatManager.ServerMessageToPlayer(new LocString(SharedCommands.ResetWorldData()), callingUser);
            }, callingUser);
        }

        #endregion

        #region Meta

        [ChatSubCommand("DiscordLink", "Displays information about the DiscordLink plugin.", "dl-about", ChatAuthorizationLevel.User)]
        public static void About(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                EcoUtil.SendAnnouncementMessage($"About DiscordLink {Plugins.DiscordLink.DiscordLink.Obj.PluginVersion}", MessageBuilder.Shared.GetAboutMessage(), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Shows the plugin status.", "dl-status", ChatAuthorizationLevel.Admin)]
        public static void PluginStatus(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                EcoUtil.SendAnnouncementMessage("DiscordLink Status", MessageBuilder.Shared.GetDisplayString(verbose: false));
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Shows the plugin status including verbose debug level information.", "dl-statusverbose", ChatAuthorizationLevel.Admin)]
        public static void PluginStatusVerbose(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                EcoUtil.SendAnnouncementMessage("DiscordLink Status Verbose", MessageBuilder.Shared.GetDisplayString(verbose: true));
            }, callingUser);
        }

        #endregion

        #region Discord Bot Info Fetching

        [ChatSubCommand("DiscordLink", "Lists Discord servers the bot is in.", ChatAuthorizationLevel.Admin)]
        public static void ListGuilds(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                if (plugin == null) return;

                var joinedNames = string.Join(", ", plugin.GuildNames);

                ChatManager.ServerMessageToPlayer(new LocString("Servers: " + joinedNames), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Lists channels available to the bot in a specific server.", ChatAuthorizationLevel.Admin)]
        public static void ListChannels(User callingUser, string guildName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                if (plugin == null) return;

                var guild = string.IsNullOrEmpty(guildName)
                    ? plugin.DefaultGuild
                    : plugin.GuildByName(guildName);

                // Can happen if DefaultGuild is not configured.
                if (guild == null)
                {
                    ChatManager.ServerMessageToPlayer(new LocString("Unable to find that guild, perhaps the name was misspelled?"), callingUser);
                }

                var joinedGames = string.Join(", ", guild.TextChannelNames());
                ChatManager.ServerMessageToAll(new LocString("Channels: " + joinedGames));
            }, callingUser);
        }

        #endregion

        #region Message Relaying

        [ChatSubCommand("DiscordLink", "Sends a message to a specific server and channel.", ChatAuthorizationLevel.Admin)]
        public static void SendMessageToDiscordChannel(User callingUser, string guild, string channel, string outerMessage)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                if (plugin == null) return;

                plugin.SendDiscordMessageAsUser(outerMessage, callingUser, channel, guild).ContinueWith(result =>
                {
                    ChatManager.ServerMessageToPlayer(new LocString(result.Result), callingUser);
                });
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco server message to a specified user.", "dl-servermessage", ChatAuthorizationLevel.Admin)]
        public static void SendServerMessage(User callingUser, string message, string recipientUserName, string persistanceType = "temporary")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendServerMessage(message, callingUser.Name, recipientUserName, persistanceType);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco server message to all online users.", "dl-broadcastservermessage", ChatAuthorizationLevel.Admin)]
        public static void BroadcastServerMessage(User callingUser, string message, string persistanceType = "temporary")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendServerMessage(message, callingUser.Name, string.Empty, persistanceType);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }


        [ChatSubCommand("DiscordLink", "Sends an Eco popup message to a specified user.", "dl-popup", ChatAuthorizationLevel.Admin)]
        public static void SendPopup(User callingUser, string message, string recipientUserName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendPopup(message, callingUser.Name, recipientUserName);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco popup message to all online users.", "dl-broadcastpopup", ChatAuthorizationLevel.Admin)]
        public static void BroadcastPopup(User callingUser, string message)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendPopup(message, callingUser.Name, string.Empty);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco announcement message to a specified user.", "dl-announcement", ChatAuthorizationLevel.Admin)]
        public static void SendAnnouncement(User callingUser, string title, string message, string recipientUserName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendAnnouncement(title, message, callingUser.Name, recipientUserName);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Sends an Eco announcement message to a specified user.", "dl-broadcastannouncement", ChatAuthorizationLevel.Admin)]
        public static void BroadcastAnnouncement(User callingUser, string title, string message)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.SendAnnouncement(title, message, callingUser.Name, string.Empty);
                ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
            }, callingUser);
        }

        #endregion

        #region Invites

        [ChatSubCommand("DiscordLink", "Posts the Discord invite message to the target user.", "DL-Invite", ChatAuthorizationLevel.User)]
        public static void Invite(User callingUser, string targetUserName = "")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = string.Empty;
                User targetUser = callingUser;
                if (!string.IsNullOrEmpty(targetUserName))
                {
                    targetUser = UserManager.FindUserByName(targetUserName);
                    if (targetUser != null)
                    {
                        result = SharedCommands.DiscordInvite(targetUser);
                    }
                    else
                    {
                        User offlineUser = EcoUtil.GetUserbyName(targetUserName);
                        if (offlineUser != null)
                            result = $"{MessageUtil.StripTags(offlineUser.Name)} is not online";
                        else
                            result = $"Could not find user with name {targetUserName}";
                    }
                    ChatManager.ServerMessageToPlayer(new LocString(result), callingUser);
                }
                else
                {
                    SharedCommands.DiscordInvite(targetUser);
                }
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Posts the Discord invite message to the Eco chat.", "DL-BroadcastInvite", ChatAuthorizationLevel.User)]
        public static void BroadcastInvite(User user)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                string result = SharedCommands.BroadcastDiscordInvite();
                ChatManager.ServerMessageToPlayer(new LocString(result), user);
            },
            user);
        }

        #endregion

        #region Account Linking

        [ChatSubCommand("DiscordLink", "Links the calling user account to a Discord account.", "DL-Link", ChatAuthorizationLevel.User)]
        public static void LinkDiscordAccount(User callingUser, string discordName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                if (plugin == null) return;

                if (!DiscordUtil.BotHasIntent(DiscordIntents.GuildMembers))
                {
                    ChatManager.ServerMessageToPlayer(new LocString($"This server is not configured to use account linking as the bot lacks the elevated Guild Members Intent."), callingUser);
                    return;
                }

                // Find the Discord user
                DiscordMember matchingMember = null;
                foreach (DiscordGuild guild in plugin.DiscordClient.Guilds.Values)
                {
                    IReadOnlyCollection<DiscordMember> guildMembers = DiscordUtil.GetGuildMembersAsync(guild).Result;
                    if (guildMembers == null)
                        continue;

                    foreach (DiscordMember member in guildMembers)
                    {
                        if (member.HasNameOrID(discordName))
                        {
                            matchingMember = member;
                            break;
                        }
                    }
                }

                if (matchingMember == null)
                {
                    ChatManager.ServerMessageToPlayer(new LocString($"No Discord account with the ID or name \"{discordName}\" could be found."), callingUser);
                    return;
                }

                // Make sure that the accounts aren't already linked to any account
                foreach (LinkedUser linkedUser in DLStorage.PersistentData.LinkedUsers)
                {
                    if (callingUser.SlgId == linkedUser.SlgID || callingUser.SteamId == linkedUser.SteamID)
                    {
                        if (linkedUser.DiscordID == matchingMember.Id.ToString())
                            ChatManager.ServerMessageToPlayer(new LocString("Eco account is already linked to this Discord account.\nUse /dl-unlink to remove the existing link."), callingUser);
                        else
                            ChatManager.ServerMessageToPlayer(new LocString("Eco account is already linked to a different Discord account.\nUse /dl-unlink to remove the existing link."), callingUser);
                        return;
                    }
                    else if (linkedUser.DiscordID == matchingMember.Id.ToString())
                    {
                        ChatManager.ServerMessageToPlayer(new LocString("Discord account is already linked to a different Eco account."), callingUser);
                        return;
                    }
                }

                // Create a linked user from the combined Eco and Discord info
                LinkedUserManager.AddLinkedUser(callingUser, matchingMember.Id.ToString(), matchingMember.Guild.Id.ToString());

                // Notify the Discord account that a link has been made and ask for verification
                _ = DiscordUtil.SendDMAsync(matchingMember, null, MessageBuilder.Discord.GetVerificationDM(callingUser));

                // Notify the Eco user that the link has been created and that verification is required
                ChatManager.ServerMessageToPlayer(new LocString($"Your account has been linked.\nThe link requires verification before becoming active.\nInstructions have been sent to the linked Discord account."), callingUser);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Unlinks the calling user account from a linked Discord account.", "DL-Unlink", ChatAuthorizationLevel.User)]
        public static void UnlinkDiscordAccount(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                bool result = LinkedUserManager.RemoveLinkedUser(callingUser);
                if (result)
                    ChatManager.ServerMessageToPlayer(new LocString($"Discord account unlinked."), callingUser);
                else
                    ChatManager.ServerMessageToPlayer(new LocString($"No linked Discord account could be found."), callingUser);
            }, callingUser);
        }

        #endregion

        #region Trades

        [ChatSubCommand("DiscordLink", "Displays available trades by player or by item.", "DL-Trades", ChatAuthorizationLevel.User)]
        public static void Trades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.Trades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName, out _);
            }, callingUser);
        }

        // Wrapper for the Trades command in order to facilitate more command aliases
        [ChatSubCommand("DiscordLink", "Displays available trades by player or by item.", "dlt", ChatAuthorizationLevel.User)]
        public static void Trade(User user, string userOrItemName)
        {
            Trades(user, userOrItemName);
        }

        [ChatSubCommand("DiscordLink", "Creates a live updated display of available trades by player or item.", "DL-TrackTrades", ChatAuthorizationLevel.User)]
        public static void TrackTrades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.TrackTrades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Removes the live updated display of available trades for the player or item.", "DL-StopTrackTrades", ChatAuthorizationLevel.User)]
        public static void StopTrackTrades(User callingUser, string userOrItemName)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.StopTrackTrades(SharedCommands.CommandSource.Eco, callingUser, userOrItemName);
            }, callingUser);
        }

        [ChatSubCommand("DiscordLink", "Lists all tracked trades for the calling user.", "DL-ListTrackedTrades", ChatAuthorizationLevel.User)]
        public static void ListTrackedTrades(User callingUser)
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                SharedCommands.ListTrackedTrades(SharedCommands.CommandSource.Eco, callingUser);
            }, callingUser);
        }

        #endregion

        #region Snippets

        [ChatSubCommand("DiscordLink", "Post a predefined snippet from Discord.", "DL-Snippet", ChatAuthorizationLevel.User)]
        public static void Snippet(User callingUser, string snippetKey = "")
        {
            ExecuteCommand<object>((lUser, args) =>
            {
                var plugin = Plugins.DiscordLink.DiscordLink.Obj;
                if (plugin == null) return;

                var snippets = DLStorage.Instance.Snippets;
                string response;
                if (string.IsNullOrWhiteSpace(snippetKey)) // List all snippets if no key is given
                {
                    response = (snippets.Count > 0 ? $"Available snippets:\n{string.Join("\n", snippets.Keys)}" : "There are no registered snippets.");
                    ChatManager.ServerMessageToPlayer(new LocString(response), callingUser);
                }
                else
                {
                    // Find and post the snippet requested by the user
                    if (snippets.TryGetValue(snippetKey, out string snippetText))
                    {
                        response = $"{callingUser.Name} invoked snippet \"{snippetKey}\"\n- - -\n{ snippetText}\n- - -";
                        EcoUtil.SendServerMessage(response, permanent: true);
                    }
                    else
                    {
                        response = $"No snippet with key \"{snippetKey}\" could be found.";
                        ChatManager.ServerMessageToPlayer(new LocString(response), callingUser);
                    }
                }
            }, callingUser);
        }

        #endregion
    }
}