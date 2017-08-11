// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    using System;

    // These represent the rooms a user can join. They should be consistent through stack, which means also in the JS Enum equivalent
    // as well as having a corresponding image file in the sites static content.
    public enum RoomTypes
    {
        Office,
        Garden,
        Cafe
    }

    // Players in the PlayerManager stores either do not exist, which means they do not have an account, or are in one of these two states.
    // These manage what is the priority data for this player.
    public enum LogState
    {
        LoggedIn,
        LoggedOut
    }

    // Used as a vessle for transporting player statistics through stack. Must be consistent through stack.
    public struct PlayerStats
    {
        public long NumAccounts; // Number of accounts in the entire application
        public long NumLoggedIn; // Number of those accounts actively marked as logged in
        public float AvgNumLogins; // Avg. # logins of all these accounts
        public double AvgAccountAge; //Avg account age, in seconds

        public PlayerStats(long numAccounts, long numLoggedIn, float avgNumLogins, double avgAccountAge)
        {
            this.NumAccounts = numAccounts;
            this.NumLoggedIn = numLoggedIn;
            this.AvgNumLogins = avgNumLogins;
            this.AvgAccountAge = avgAccountAge;
        }
    }

    // The wrapper for a Player in the cold player store
    public struct PlayerPackage
    {
        public Player Player; // Player game data
        public LogState State; // Whether they are currently logged in or not
        public int NumLogins; // Number of times they have logged in
        public DateTime FirstLogin; // Stored in UTC
        public string RoomId; // If they are in LoggedIn, this points to where they are logged in, otherwise its value is meaningless.

        public PlayerPackage(Player player, LogState state, int numLogins, DateTime firstLogin, string roomid)
        {
            this.Player = player;
            this.State = state;
            this.NumLogins = numLogins;
            this.FirstLogin = firstLogin;
            this.RoomId = roomid;
        }
    }

    //The wrapper for the player in the hot player store
    public struct ActivePlayer
    {
        public Player Player; // Player game data
        public DateTime LastUpdated; // Last time that player logged in or updated their game, in UTC

        public ActivePlayer(Player player, DateTime lastUpdated)
        {
            this.Player = player;
            this.LastUpdated = lastUpdated;
        }
    }

    //The game-relevant info tied to players
    public struct Player
    {
        public int XPos; // their x position
        public int YPos; // their y position
        public string Color; // their color hex code

        public Player(int xpos, int ypos, string color)
        {
            this.XPos = xpos;
            this.YPos = ypos;
            this.Color = color;
        }
    }

    //This is the struct that contains relevant room data.
    public struct Room
    {
        public int NumPlayers; // Number of players active in the room
        public string RoomType; // What type of room it is


        public Room(int numplayers, string roomtype)
        {
            this.RoomType = roomtype;
            this.NumPlayers = numplayers;
        }
    }
}