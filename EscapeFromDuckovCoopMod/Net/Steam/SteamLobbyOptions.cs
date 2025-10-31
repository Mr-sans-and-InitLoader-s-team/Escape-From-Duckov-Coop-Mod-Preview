// Escape-From-Duckov-Coop-Mod-Preview
// Copyright (C) 2025  Mr.sans and InitLoader's team

using Steamworks;

namespace EscapeFromDuckovCoopMod.Net.Steam
{
    public enum SteamLobbyVisibility
    {
        FriendsOnly,
        Public
    }
    
    public class SteamLobbyOptions
    {
        public string LobbyName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int MaxPlayers { get; set; } = 2;
        public SteamLobbyVisibility Visibility { get; set; } = SteamLobbyVisibility.Public;
        
        public static SteamLobbyOptions CreateDefault()
        {
            var options = new SteamLobbyOptions
            {
                LobbyName = "Duckov Lobby",
                Password = string.Empty,
                MaxPlayers = 2,
                Visibility = SteamLobbyVisibility.Public
            };
            
            if (SteamManager.Initialized)
            {
                try
                {
                    var personaName = SteamFriends.GetPersonaName();
                    if (!string.IsNullOrWhiteSpace(personaName))
                    {
                        options.LobbyName = personaName + "'s Room";
                    }
                }
                catch
                {
                }
            }
            
            return options;
        }
    }
}

