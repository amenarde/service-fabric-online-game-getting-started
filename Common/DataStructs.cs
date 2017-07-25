using System;

namespace Common
{

    public enum LogState { LoggedIn, LoggedOut, InTransit }

    public struct PlayerStats
    {
        public long numAccounts;
        public long numLoggedIn;
        public float avgNumLogins;
        public double avgAccountAge; //seconds

        public PlayerStats(long numAccounts, long numLoggedIn, float avgNumLogins, double avgAccountAge)
        {
            this.numAccounts = numAccounts;
            this.numLoggedIn = numLoggedIn;
            this.avgNumLogins = avgNumLogins;
            this.avgAccountAge = avgAccountAge;
        }
    }


    public struct PlayerPackage
    {
        public Player player;
        public LogState state;
        public int numLogins;
        public DateTime firstLogin; //UTC
        public string roomid;

        public PlayerPackage(Player player, LogState state, int numLogins, DateTime firstLogin, string roomid)
        {
            this.player = player;
            this.state = state;
            this.numLogins = numLogins;
            this.firstLogin = firstLogin;
            this.roomid = roomid;
        }
    }

    public struct ActivePlayer
    {
        public Player player;
        public DateTime lastUpdated; //UTC
        public LogState state;

        public ActivePlayer(Player player, DateTime lastUpdated, LogState state)
        {
            this.player = player;
            this.lastUpdated = lastUpdated;
            this.state = state;
        }
    }


    public struct Player
    {
        public int xpos;
        public int ypos;
        public string color;

        public Player(int xpos, int ypos, string color)
        {
            this.xpos = xpos;
            this.ypos = ypos;
            this.color = color;

        }
    }

    /// <summary>
    /// This is the struct that contains relevant room data: the number of players and the room type
    /// </summary>
    public struct Room
    {

        public int numplayers;
        public string roomtype;


        public Room(int numplayers, string roomtype)
        {
            this.roomtype = roomtype;
            this.numplayers = numplayers;
        }
    }
}
