﻿using DSharpPlus.Entities;
using Eco.Core.Utils;
using Eco.Plugins.DiscordLink.Extensions;
using Eco.Plugins.Networking;
using Eco.Shared.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Eco.Plugins.DiscordLink.Utilities
{
    public static class MessageUtils
    {
        // Snippet matching regex: Match the (case insensitive) [Snippet] header and capture the the content of the following bracket pair
        public static readonly Regex SnippetRegex = new Regex("(?i)\\[snippet\\]\\s*\\[([^\\]]+)\\].*\\s([^$]*)");

        // Display tag matching regex: Match the [] header and the [] tag and capture them both. Will capture only the header if no tag exists.
        public static readonly Regex DisplayTagRegex = new Regex("(\\[[^\\]]+\\])(?:\\s*\\[([^\\]]+)\\])*");

        // Eco tag matching regex: Match all characters that are used to create HTML style tags
        private static readonly Regex HTMLTagRegex = new Regex("<[^>]*>");

        // Discord mention matching regex: Match all characters followed by a mention character(@ or #) character (including that character) until encountering any type of whitespace, end of string or a new mention character
        private static readonly Regex DiscordMentionRegex = new Regex("([@#].+?)(?=\\s|$|@|#)");

        // Discord @everyone and @here matching regex: Match all instances that would trigger a Discord tag to @everyone or @here and capture the tag so the @ can easily removed.
        private static readonly Regex DiscordGlobalMentionRegex = new Regex("@(everyone|here)");

        // Discord bold tag matching regex: Match all characters between ** pairs.
        private static readonly Regex DiscordBoldRegex = new Regex("\\*\\*(.*?)\\*\\*");

        // Excessive newline matching regex: Match all cases of (0+)\r followed by (2+)\n.
        private static readonly Regex ExcessiveNewLineRegex = new Regex("(\\\r)*(\\\n){2,}");

        #region General

        public static string FirstNonEmptyString(params string[] strings)
        {
            return strings.FirstOrDefault(str => !string.IsNullOrEmpty(str)) ?? "";
        }

        public static List<string> SplitStringBySize(string str, int chunkSize)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(str))
                return result;

            str = str.Replace("\r", null); // Remove all carriage returns so they don't cause extra newlines
            str = str.TrimStart();
            str = str.TrimEnd();
            string[] lines = str.Split('\n');
            string builder = lines.First();
            foreach (string line in lines.Skip(1)) // Skip the first line as it is the initial value for the builder
            {
                string test = $"{builder}\n{line}";
                if (test.Length > chunkSize)
                {
                    result.Add(builder);
                    builder = line;
                }
                else
                {
                    builder = test;
                }
            }
            result.Add(builder);
            return result;
        }

        public static string GetCommandTokenForContext(SharedCommands.CommandInterface context)
        {
            return context == SharedCommands.CommandInterface.Eco ? "/" : DLConfig.Data.DiscordCommandPrefix;
        }

        public static List<DiscordEmbed> BuildDiscordEmbeds(DiscordLinkEmbed fullEmbed)
        {
            List<DiscordEmbed> resultEmbeds = new List<DiscordEmbed>();

            if (fullEmbed == null)
                return resultEmbeds;

            // Conditionally add the standard footer
            if (string.IsNullOrWhiteSpace(fullEmbed.Footer) && fullEmbed.GetSize() <= DLConfig.Data.MinEmbedSizeForFooter)
            {
                fullEmbed.WithFooter(MessageBuilder.Discord.GetStandardEmbedFooter());
            }

            // Count chars needed for title and footer
            int titleFooterCharCount = 0;
            if (fullEmbed.Title != null)
                titleFooterCharCount += fullEmbed.Title.Length;
            if (fullEmbed.Footer != null)
                titleFooterCharCount += fullEmbed.Footer.Length;

            int totalCharsCount = titleFooterCharCount;

            // Count chars needed for fields and track fields that are too long
            List<bool> needsSplitFields = Enumerable.Repeat(false, fullEmbed.Fields.Count).ToList();
            for (int i = 0; i < fullEmbed.Fields.Count; ++i)
            {
                DiscordLinkEmbedField field = fullEmbed.Fields[i];
                int length = field.Title.Length + field.Text.Length;
                if (length > DLConstants.DISCORD_EMBED_FIELD_CHARACTER_LIMIT)
                    needsSplitFields[i] = true;

                totalCharsCount += length;
            }

            // Early escape if no splitting is needed
            if (totalCharsCount <= DLConstants.DISCORD_EMBED_TOTAL_CHARACTER_LIMIT && needsSplitFields.Count <= DLConstants.DISCORD_EMBED_FIELD_ALIGNED_COUNT_LIMIT && !needsSplitFields.Any(t => t == true))
            {
                resultEmbeds.Add(BuildDiscordEmbed(fullEmbed));
                return resultEmbeds;
            }

            // Split too long fields
            List<DiscordLinkEmbedField> splitFields = new List<DiscordLinkEmbedField>();
            for (int i = 0; i < fullEmbed.Fields.Count; ++i)
            {
                DiscordLinkEmbedField field = fullEmbed.Fields[i];
                if (needsSplitFields[i] == true)
                {
                    const int PartCountCharactersMax = 6; // Space + Parenthesis + Number (No more than 999 parts)
                    IEnumerable<string> splits = SplitStringBySize(field.Text, DLConstants.DISCORD_EMBED_FIELD_CHARACTER_LIMIT - (field.Title.Length + PartCountCharactersMax));
                    int partCount = 1;
                    foreach (string fieldSplit in splits)
                    {
                        splitFields.Add(new DiscordLinkEmbedField($"{field.Title} ({partCount})", fieldSplit, fullEmbed.Fields[i].AllowAutoLineBreak, fullEmbed.Fields[i].Inline));
                        ++partCount;
                    }
                }
                else
                {
                    splitFields.Add(new DiscordLinkEmbedField(fullEmbed.Fields[i].Title, fullEmbed.Fields[i].Text, fullEmbed.Fields[i].AllowAutoLineBreak, fullEmbed.Fields[i].Inline));
                }
            }

            // Create new embeds that fit within the char limits
            List<DiscordLinkEmbed> splitEmbeds = new List<DiscordLinkEmbed>();
            DiscordLinkEmbed splitEmbedBuilder = new DiscordLinkEmbed(fullEmbed);
            splitEmbedBuilder.WithFooter(string.Empty); // Remove the footer for now, we will add it back at the end
            splitEmbedBuilder.ClearFields();
            int characterCount = 0;
            int fieldCount = 0;
            foreach (DiscordLinkEmbedField field in splitFields)
            {
                // If adding the next field would bring us over a limit, split into new embeds
                if (characterCount + field.Text.Length > DLConstants.DISCORD_EMBED_TOTAL_CHARACTER_LIMIT || fieldCount + 1 > DLConstants.DISCORD_EMBED_FIELD_ALIGNED_COUNT_LIMIT)
                {
                    splitEmbedBuilder.WithTitle($"{fullEmbed.Title} ({splitEmbeds.Count() + 1})");
                    splitEmbeds.Add(new DiscordLinkEmbed(splitEmbedBuilder));
                    splitEmbedBuilder.ClearFields();
                    characterCount = 0;
                    fieldCount = 0;
                }

                splitEmbedBuilder.AddField(field.Title, field.Text, field.AllowAutoLineBreak, field.Inline);
                characterCount += field.Text.Length;
                ++fieldCount;
            }
            splitEmbeds.Add(splitEmbedBuilder);
            splitEmbeds.Last().WithTitle($"{fullEmbed.Title} ({splitEmbeds.Count()})"); // Add the split number to the title of the last split
            splitEmbeds.Last().WithFooter(fullEmbed.Footer); // Add back the footer only in the last split

            // Convert embeds to actual DSharp Discord embeds
            foreach (DiscordLinkEmbed embedData in splitEmbeds)
            {
                resultEmbeds.Add(BuildDiscordEmbed(embedData));
            }

            return resultEmbeds;
        }

        // Creates an actual Discord embed with the assumption that all fields in the input are within the character constraints
        private static DiscordEmbed BuildDiscordEmbed(DiscordLinkEmbed embedData)
        {
            DiscordEmbedBuilder builder = new DiscordEmbedBuilder();
            builder.WithTitle(embedData.Title);
            builder.WithDescription(embedData.Description);
            builder.WithFooter(embedData.Footer);
            builder.WithColor(DLConstants.DISCORD_EMBED_COLOR);

            if (!string.IsNullOrEmpty(embedData.Thumbnail))
            {
                try
                {
                    builder.WithThumbnail(embedData.Thumbnail);
                }
                catch (UriFormatException e)
                {
                    Logger.Debug("Failed to include thumbnail in Server Info embed. Error: " + e);
                }
            }

            foreach (DiscordLinkEmbedField field in embedData.Fields)
            {
                builder.AddField(field.Title, field.Text, field.Inline);
            }

            return builder.Build();
        }

        #endregion

        #region Eco --> Discord

        public static string StripTags(string toStrip)
        {
            if (toStrip == null)
                return string.Empty;

            return HTMLTagRegex.Replace(toStrip, string.Empty);
        }

        public static string StripGlobalMentions(string toStrip)
        {
            if (toStrip == null)
                return null;

            return DiscordGlobalMentionRegex.Replace(toStrip, "$1");
        }

        public static string FormatMessageForDiscord(string message, DiscordChannel channel, string username = "", bool allowGlobalMentions = false)
        {
            string formattedMessage = (username.IsEmpty() ? "" : $"**{username.Replace("@", "")}**: ") + StripTags(message); // All @ characters are removed from the name in order to avoid unintended mentions of the sender
            if (!allowGlobalMentions)
                formattedMessage = StripGlobalMentions(formattedMessage);

            return FormatDiscordMentions(formattedMessage, channel);
        }

        private static string FormatDiscordMentions(string message, DiscordChannel channel)
        {
            return DiscordMentionRegex.Replace(message, capture =>
            {
                string match = capture.ToString().Substring(1); // Strip the mention character from the match
                string FormatMention(string name, string mention)
                {
                    if (name.EqualsCaseInsensitive(match))
                        return mention;

                    string beforeMatch = "";
                    int matchStartIndex = match.IndexOf(name);
                    if (matchStartIndex > 0) // There are characters before @username
                        beforeMatch = match.Substring(0, matchStartIndex);

                    string afterMatch = "";
                    int matchStopIndex = matchStartIndex + name.Length - 1;
                    int numCharactersAfter = match.Length - 1 - matchStopIndex;
                    if (numCharactersAfter > 0) // There are characters after @username
                        afterMatch = match.Substring(matchStopIndex + 1, numCharactersAfter);

                    return beforeMatch + mention + afterMatch; // Add whatever characters came before or after the username when replacing the match in order to avoid changing the message context
                }

                ChatChannelLink link = !channel.IsPrivate ? DLConfig.ChatLinkForDiscordChannel(channel) : null;
                bool allowRoleMentions = (link == null || link.AllowRoleMentions);
                bool allowMemberMentions = (link == null || link.AllowUserMentions);
                bool allowChannelMentions = (link == null || link.AllowChannelMentions);

                if (capture.ToString()[0] == '@')
                {
                    if (allowRoleMentions)
                    {
                        foreach (var role in channel.Guild.Roles.Values) // Checking roles first in case a user has a name identiacal to that of a role
                        {
                            if (!role.IsMentionable) continue;

                            string name = role.Name;
                            if (match.ContainsCaseInsensitive(name))
                            {
                                return FormatMention(name, role.Mention);
                            }
                        }
                    }

                    if (allowMemberMentions)
                    {
                        foreach (var member in channel.Guild.Members.Values)
                        {
                            string name = member.DisplayName;
                            if (match.ContainsCaseInsensitive(name))
                            {
                                return FormatMention(name, member.Mention);
                            }
                        }
                    }
                }
                else if (capture.ToString()[0] == '#' && allowChannelMentions)
                {
                    foreach (var listChannel in channel.Guild.Channels.Values)
                    {
                        string name = listChannel.Name;
                        if (match.ContainsCaseInsensitive(name))
                        {
                            return FormatMention(name, listChannel.Mention);
                        }
                    }
                }

                return capture.ToString(); // No match found, just return the original string
            });
        }

        #endregion

        #region Discord -> Eco

        public static async Task<string> FormatMessageForEco(DiscordMessage message, string ecoChannel)
        {
            DiscordMember author = await message.GetChannel().Guild.GetMemberAsync(message.Author.Id);
            string nametag = author != null
                ? Text.Bold(Text.Color(DLConstants.ECO_NAME_TAG_COLOR, author.DisplayName))
                : message.Author.Username;
            return $"#{ecoChannel} {nametag}: {GetReadableContent(message)}";
        }

        public static string GetReadableContent(DiscordMessage message)
        {
            var content = message.Content;
            foreach (var user in message.MentionedUsers)
            {
                if (user == null) { continue; }
                DiscordMember member = message.GetChannel().Guild.Members.FirstOrDefault(m => m.Value?.Id == user.Id).Value;
                if (member == null) { continue; }
                string name = $"@{member.DisplayName}";
                content = content.Replace($"<@{user.Id}>", name).Replace($"<@!{user.Id}>", name);
            }
            foreach (var role in message.MentionedRoles)
            {
                if (role == null) continue;
                content = content.Replace($"<@&{role.Id}>", $"@{role.Name}");
            }
            foreach (var channel in message.MentionedChannels)
            {
                if (channel == null) continue;
                content = content.Replace($"<#{channel.Id}>", $"#{channel.Name}");
            }

            if (message.Attachments.Count > 0)
            {
                content += "\nAttachments:";
                foreach (DiscordAttachment attachment in message.Attachments)
                {
                    content += $"\n{attachment.FileName}";
                }
            }

            return content;
        }

        public static string FormatEmbedForEco(DiscordLinkEmbed embed)
        {
            string text = embed.AsText();
            text = text.Substring(text.IndexOf('\n') + 1);
            text = text.Replace(DiscordLinkEmbed.INVISIBLE_EMBED_CHAR, null);
            text = text.Replace("[", null);
            text = text.Replace("****", null);
            text = text.Replace("\r", null);
            text = ExcessiveNewLineRegex.Replace(text, "\n\n");
            text = DiscordBoldRegex.Replace(text, Text.Color(Color.Green, Text.Bold("$1")));
            return text;
        }

        public static string GetWebserverBaseURL()
        {
            string webServerAddress = DLConfig.Data.WebServerAddress;
            if (!DLConfig.Data.WebServerAddressEndsWithPort())
                webServerAddress += $":{NetworkManager.GetServerInfo().WebPort}";
            return webServerAddress;
        }

        #endregion
    }
}
