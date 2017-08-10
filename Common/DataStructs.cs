// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    using System;

    public enum RoomTypes
    {
        Office,
        Garden,
        Cafe
    }

    public enum LogState
    {
        LoggedIn,
        LoggedOut
    }

    public struct PlayerStats
    {
        public long NumAccounts;
        public long NumLoggedIn;
        public float AvgNumLogins;
        public double AvgAccountAge; //seconds

        public PlayerStats(long numAccounts, long numLoggedIn, float avgNumLogins, double avgAccountAge)
        {
            this.NumAccounts = numAccounts;
            this.NumLoggedIn = numLoggedIn;
            this.AvgNumLogins = avgNumLogins;
            this.AvgAccountAge = avgAccountAge;
        }
    }


    public struct PlayerPackage
    {
        public Player Player;
        public LogState State;
        public int NumLogins;
        public DateTime FirstLogin; //UTC
        public string RoomId;

        public PlayerPackage(Player player, LogState state, int numLogins, DateTime firstLogin, string roomid)
        {
            this.Player = player;
            this.State = state;
            this.NumLogins = numLogins;
            this.FirstLogin = firstLogin;
            this.RoomId = roomid;
        }
    }

    public struct ActivePlayer
    {
        public Player Player;
        public DateTime LastUpdated; //UTC

        public ActivePlayer(Player player, DateTime lastUpdated)
        {
            this.Player = player;
            this.LastUpdated = lastUpdated;
        }
    }


    public struct Player
    {
        public int XPos;
        public int YPos;
        public string Color;

        public Player(int xpos, int ypos, string color)
        {
            this.XPos = xpos;
            this.YPos = ypos;
            this.Color = color;
        }
    }

    /// <summary>
    ///     This is the struct that contains relevant room data: the number of players and the room type
    /// </summary>
    public struct Room
    {
        public int NumPlayers;
        public string RoomType;


        public Room(int numplayers, string roomtype)
        {
            this.RoomType = roomtype;
            this.NumPlayers = numplayers;
        }
    }
}