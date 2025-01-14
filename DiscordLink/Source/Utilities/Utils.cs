﻿namespace Eco.Plugins.DiscordLink.Utilities
{
    public static class Utils
    {
        public enum DiscordReactionChange
        {
            Added,
            Removed
        }

        public static bool TryParseSnowflakeID(string nameOrID, out ulong ID)
        {
            return ulong.TryParse(nameOrID, out ID) && ID > 0xFFFFFFFFFFFFFUL;
        }
    }
}
