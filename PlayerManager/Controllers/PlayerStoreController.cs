// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PlayerManager.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Newtonsoft.Json;

    /// <summary>
    ///     The role of this controller is to manage the state of players that are offline. The dictionary in this service will
    ///     on average hold much more data than a room dictionary, but handle a lot less throughput (Cold Storage). It handles
    ///     starting and ending games by transfering data to and from cold storage, and maintaining longer term account information like
    ///     account age, settings, and how often an account is used.
    /// </summary>
    public class PlayerStoreController : Controller
    {
        private static readonly Uri PlayersDictionaryName = new Uri("store:/players");
        private readonly HttpClient httpClient;
        private readonly IReliableStateManager stateManager;
        private readonly ConfigSettings configSettings;
        private readonly StatefulServiceContext serviceContext;

        private readonly string[] startingColors;
        private PlayerManager cache;
        private string proxy;

        /// <summary>
        ///     This constructor will execute the first time a function in the controller is called.
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="configSettings"></param>
        /// <param name="playerManager"></param>
        public PlayerStoreController(
            HttpClient httpClient, ConfigSettings configSettings, PlayerManager playerManager)
        {
            this.stateManager = playerManager.StateManager;
            this.httpClient = httpClient;
            this.configSettings = configSettings;
            this.serviceContext = playerManager.Context;
            this.cache = playerManager;

            this.RenewProxy();

            this.startingColors = new[]
            {
                "ADD8E6",
                "99FFCC",
                "CCCC99",
                "CCCCCC",
                "CCCCFF",
                "CCFF99",
                "CCFFCC",
                "CCFFFF",
                "FFCC99",
                "FFCCCC",
                "FFCCFF",
                "FFFF99",
                "FFFFCC"
            };
        }

        private static ContentResult exceptionHandler(Exception e)
        {
            //Reresolve proxy and retry
            if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                return new ContentResult { StatusCode = 503, Content = e.Message };

            //Retry the transaction
            if (e is FabricTransientException || e is TimeoutException || e is TransactionFaultedException)
                return new ContentResult { StatusCode = 503, Content = e.Message };

            if (e is FabricException)
                return new ContentResult { StatusCode = 500, Content = e.Message };

            return new ContentResult { StatusCode = 500, Content = e.InnerException.Message };
        }

        /// <summary>
        ///     Coordinates the new game process. This entails either gathering the player data or creating a new player, and
        ///     handing that data off to an active room. It coordinates the current state of the player by using the LogState 
        ///     of the player here and the existence of the player in the room.
        /// </summary>
        /// <param name="playerid"></param>
        /// <param name="roomid"></param>
        /// <param name="roomtype"></param>
        /// <returns>200 if successful, 503 if requesting a retry, 500 if failure, and 400 if the user is already logged in</returns>
        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string playerid, string roomid, string roomtype)
        {
            try
            {
                if (!PlayerManager.IsActive)
                    return new ContentResult {StatusCode = 500, Content = "Service is still starting up. Please retry."};

                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                PlayerPackage playerPackage; //for handing up player information if login is needed in scenario 2 

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<PlayerPackage> playerOption = await playdict.TryGetValueAsync(tx, playerid, LockMode.Update);

                    /////////////////////////////////////////////////
                    // SCENARIO 1: PLAYER DOES NOT HAVE AN ACCOUNT //
                    /////////////////////////////////////////////////

                    if (!playerOption.HasValue)
                    {
                        //State: Player does not exist / Cannot be in a game
                        Random rand = new Random(Environment.TickCount);
                        //Generate a new player with a random position
                        Player newPlayer = new Player(
                            rand.Next() % 100 - 6,
                            rand.Next() % 96 - 6,
                            this.startingColors[rand.Next() % this.startingColors.Length]);

                        //Package the new player with its baseline statistics
                        PlayerPackage newPlayerPackage = new PlayerPackage(newPlayer, LogState.LoggedIn, 1, DateTime.UtcNow, roomid);
                        await playdict.AddAsync(tx, playerid, newPlayerPackage);
                        await tx.CommitAsync();


                        return await this.NewGameRequestHelper(roomid, playerid, roomtype, newPlayer);
                    }

                    //////////////////////////////////////////////////////
                    // SCENARIO 2: PLAYER HAS ACCOUNT AND IS LOGGED OUT //
                    //////////////////////////////////////////////////////

                    if (playerOption.Value.State == LogState.LoggedOut)
                    {
                        /*
                         * Scenario: We think player is logged out (LO-N), in which case this is normal functionality. 
                         * The state could also be (LO-LI), which could happen if an EndGame failed halfway through. 
                         * If this is the case, there are two scenarios: The first is that the room we are about to log into was 
                         * the room that failed to log out, in which case we will override that data since we have the most updated 
                         * data and the situation is resolved. The second case is that we are trying to log into a different room. 
                         * In this case we trust that the protocol has removed that clients access to the player, which means the
                         * player will eventually be cleaned up by the timeout, keeping the game consistent.
                         */

                        //Grab our player data and update the package
                        PlayerPackage updatedPlayerPackage = playerOption.Value;
                        updatedPlayerPackage.State = LogState.LoggedIn;
                        updatedPlayerPackage.RoomId = roomid;
                        updatedPlayerPackage.NumLogins++;
                        await playdict.SetAsync(tx, playerid, updatedPlayerPackage);

                        //finish our transaction
                        await tx.CommitAsync();

                        // Request a newgame in the room we want to join
                        return await this.NewGameRequestHelper(roomid, playerid, roomtype, playerOption.Value.Player);
                    }

                    await tx.CommitAsync();
                    playerPackage = playerOption.Value;
                } // end of tx

                /////////////////////////////////////////////////////
                // SCENARIO 3: PLAYER HAS ACCOUNT AND IS LOGGED IN //
                /////////////////////////////////////////////////////

                if (playerPackage.State == LogState.LoggedIn)
                {
                    // Scenario: This state will generally be the success state, where the player thinks they are logged in and the
                    // appropriate room has the game. However, during login, it is possible that the process crashed between the time 
                    // that the login transaction marked the data as logged in and that data being put in the room. We must check to
                    // verify that this is not the state we are in.

                    int key = Partitioners.GetRoomPartition(playerPackage.RoomId);

                    // We first ask if the room has the data to determine which of the above states we are in.
                    string url = this.proxy + $"Exists/?playerid={playerid}&roomid={playerPackage.RoomId}&PartitionKind=Int64Range&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);

                    if ((int) response.StatusCode == 404)
                    {
                        this.RenewProxy();

                        url = this.proxy + $"Exists/?playerid={playerid}&roomid={playerPackage.RoomId}&PartitionKind=Int64Range&PartitionKey={key}";
                        response = await this.httpClient.GetAsync(url);
                    }

                    string responseMessage = await response.Content.ReadAsStringAsync();
                    if ((int) response.StatusCode == 200)
                    {
                        //Player is logged in, so we must deny this request
                        if (responseMessage == "true")
                            return new ContentResult {StatusCode = 400, Content = "This player is already logged in"};

                        //Player is not logged in, so we can log into whichever room we want
                        if (responseMessage == "false")
                        {
                            using (ITransaction tx1 = this.stateManager.CreateTransaction())
                            {
                                playerPackage.RoomId = roomid;
                                playerPackage.NumLogins++;
                                await playdict.SetAsync(tx1, playerid, playerPackage);

                                await tx1.CommitAsync();
                            }

                            return await this.NewGameRequestHelper(roomid, playerid, roomtype, playerPackage.Player);
                        }

                        Environment.FailFast("If returning a success code, the message must be either true or false.");
                    }
                    else
                    {
                        return new ContentResult {StatusCode = 500, Content = "Something went wrong, please retry"};
                    }
                }

                Environment.FailFast("Players must exist with a valid state attached to them");
                return new ContentResult {StatusCode = 500};
            }
            catch (Exception e)
            {
                return exceptionHandler(e);
            }
        }

        /// <summary>
        /// Called by the EndGame RoomController function to coordinate the end of a game. Responsible for ensuring that the most recent
        /// player data is stored in the player dictionary and that the player is known logged out.
        /// </summary>
        /// <param name="playerid"></param>
        /// <param name="playerdata"></param>
        /// <returns>200 if successful, 503 if requesting a retry, and 500 if failure</returns>
        [Route("api/[controller]/EndGame")]
        [HttpGet]
        public async Task<IActionResult> EndGame(string playerid, string playerdata)
        {
            try
            {
                if (!PlayerManager.IsActive)
                    return new ContentResult {StatusCode = 500, Content = "Service is still starting up. Please retry."};

                // Get newer game data from the active room
                Player player = JsonConvert.DeserializeObject<Player>(playerdata);

                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<PlayerPackage> playerOption = await playdict.TryGetValueAsync(tx, playerid, LockMode.Update);

                    if (!playerOption.HasValue)
                    {
                        // Tried to end game for a player that isn't here. This is a fail state.
                        await tx.CommitAsync();
                        return new ContentResult {StatusCode = 500, Content = "Cannot log out a player not in this system. Check partition."};
                    }

                    // Player says already logged out, this means the last log in attempt was successful, but the return message never got back to
                    // room manager or it failed to remove the player.
                    if (playerOption.Value.State == LogState.LoggedOut)
                    {
                        await tx.CommitAsync();
                        return new ContentResult {StatusCode = 200};
                    }

                    //The normal functionality, update the player and return a success
                    if (playerOption.Value.State == LogState.LoggedIn)
                    {
                        PlayerPackage newPlayerPackage = playerOption.Value;
                        newPlayerPackage.Player = player;
                        newPlayerPackage.State = LogState.LoggedOut;
                        await playdict.SetAsync(tx, playerid, newPlayerPackage);
                        await tx.CommitAsync();

                        return new ContentResult {StatusCode = 200};
                    }

                    await tx.CommitAsync();
                    Environment.FailFast("Player must have one of the above states: doesn't exist, loggedin, or loggedout.");
                    return new ContentResult {StatusCode = 500};
                }
            }
            catch (Exception e)
            {
                return exceptionHandler(e);
            }
        }

        /// <summary>
        /// This function iterates through this partitions player dictionary gathering player statistics and rolling them into averages and
        /// totals. It then sends its partial statistics which will be combined with those of other partitions.
        /// </summary>
        /// <returns>a JSON serialized PlayerStats object if successful, 503 if requesting a retry, and 500 if failure</returns>
        [Route("api/[controller]/GetPlayerStats")]
        [HttpGet]
        public async Task<IActionResult> GetPlayerStats()
        {
            try
            {
                if (!PlayerManager.IsActive)
                    return new ContentResult {StatusCode = 500, Content = "Service is still starting up. Please retry."};

                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                PlayerStats stats = new PlayerStats(0, 0, 0, 0);

                //Go through the player dictionary gathering data and averaging or summing as you go.
                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    stats.NumAccounts = await playdict.GetCountAsync(tx);

                    IAsyncEnumerable<KeyValuePair<string, PlayerPackage>> enumerable = await playdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, PlayerPackage>> enumerator = enumerable.GetAsyncEnumerator();

                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                    {
                        //Averaging
                        if (enumerator.Current.Value.State == LogState.LoggedIn)
                            stats.NumLoggedIn++;

                        stats.AvgNumLogins += (float) enumerator.Current.Value.NumLogins / stats.NumAccounts;
                        stats.AvgAccountAge += (double) DateTime.Now.ToUniversalTime().Subtract(enumerator.Current.Value.FirstLogin).Seconds /
                                               stats.NumAccounts;
                    }
                    await tx.CommitAsync();
                }

                return this.Json(stats);
            }
            catch (Exception e)
            {
                return exceptionHandler(e);
            }
        }


        private void RenewProxy()
        {
            // Proxy to coordinate with RoomManager
            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{this.configSettings.ReverseProxyPort}/" +
                         $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{this.configSettings.RoomManagerName}/api/RoomStore/";
        }

        private async Task<ContentResult> NewGameRequestHelper(string roomid, string playerid, string roomtype, Player player)
        {
            // Request a newgame in the room we want to join
            int key = Partitioners.GetRoomPartition(roomid);
            string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}" +
                         $"&playerdata={JsonConvert.SerializeObject(player)}" +
                         $"&roomtype={roomtype}" +
                         $"&PartitionKind=Int64Range&PartitionKey={key}";
            HttpResponseMessage response = await this.httpClient.GetAsync(url);

            if ((int) response.StatusCode == 404)
            {
                this.RenewProxy();

                url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}" +
                      $"&playerdata={JsonConvert.SerializeObject(player)}" +
                      $"&roomtype={roomtype}" +
                      $"&PartitionKind=Int64Range&PartitionKey={key}";
                response = await this.httpClient.GetAsync(url);
            }

            // If this was successful we are now in agreement state, otherwise we may be in a failure state, handled below
            string responseMessage = await response.Content.ReadAsStringAsync();
            return (int) response.StatusCode == 200
                ? new ContentResult {StatusCode = 200, Content = roomtype}
                : new ContentResult {StatusCode = 500, Content = responseMessage};
        }
    }
}