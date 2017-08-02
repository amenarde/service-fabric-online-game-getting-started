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
    ///     on average hold much more data than a room dictionary, but handle a lot less throughput (Cold Storage). It handles starting
    ///     and ending games by transfering data to and from cold storage, and maintaining longer term account information like account age,
    ///     settings, and how often an account is used.
    /// </summary>
    public class PlayerStoreController : Controller
    {
        private static readonly Uri PlayersDictionaryName = new Uri("store:/players");
        private readonly HttpClient httpClient;
        private readonly FabricClient fabricClient;
        private readonly IReliableStateManager stateManager;

        private readonly string proxy;

        /// <summary>
        ///     This constructor will execute the first time a function in the controller is called.
        /// </summary>
        /// <param name="serviceContext"></param>
        /// <param name="httpClient"></param>
        /// <param name="fabricClient"></param>
        /// <param name="configSettings"></param>
        /// <param name="stateManager"></param>
        public PlayerStoreController(
            StatefulServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings configSettings,
            IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
            this.httpClient = httpClient;
            this.fabricClient = fabricClient;

            // Proxy to coordinate with the RoomManager
            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{configSettings.ReverseProxyPort}/" +
                         $"{serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{configSettings.RoomManagerName}/api/RoomStore/";
        }

        /// <summary>
        /// Coordinates the new game process. This entails either gathering the player data or creating a new player, and handing that
        /// data off to an active room. It coordinates the current state of the player by using the LogState of the player here and the
        /// existence of the player in the room.
        /// </summary>
        /// <param name="playerid"></param>
        /// <param name="roomid"></param>
        /// <param name="roomtype"></param>
        /// <returns></returns>
        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string playerid, string roomid, string roomtype)
        {
            try
            {
                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<PlayerPackage> playerOption = await playdict.TryGetValueAsync(tx, playerid, LockMode.Update);

                    if (!playerOption.HasValue)
                    {
                        //Scenario: Player does not exist (N-N)
                        Random rand = new Random();
                        Player p = new Player(rand.Next() % 100 - 6, rand.Next() % 96 - 6, "ADD8E6");
                        PlayerPackage pp = new PlayerPackage(p, LogState.LoggedIn, 1, DateTime.UtcNow, roomid);
                        await playdict.AddAsync(tx, playerid, pp);
                        await tx.CommitAsync();

                        //Scenario: Player says logged in, but is not logged in to room (LI-N)
                        int key = Partitioners.GetRoomPartition(roomid);
                        string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}" +
                                     $"&playerdata={JsonConvert.SerializeObject(p)}" +
                                     $"&roomtype={roomtype}" +
                                     $"&PartitionKind=Int64Range&PartitionKey={key}";
                        HttpResponseMessage response = await this.httpClient.GetAsync(url);
                        string responseMessage = await response.Content.ReadAsStringAsync();
                        return (int) response.StatusCode == 200 ? new ContentResult {StatusCode = 200, Content = roomtype} : 
                            new ContentResult {StatusCode = 500, Content = responseMessage};
                    }
                    switch (playerOption.Value.State)
                    {
                        case LogState.LoggedOut:
                        {
                            // Scenario: We think player is logged out (LO-N), 
                            // could also be (LO-LI). If this is the case, there are two options. The first is that the room we are about to log
                            // into has data, in which case we will override that data. The second case is that a different room has the player
                            // logged in. In this case we trust that the protocol has removed that clients access to the player, so that player
                            // will be cleaned up by the timeout.

                            PlayerPackage pp = playerOption.Value;
                            pp.State = LogState.LoggedIn;
                            pp.RoomId = roomid;
                            pp.NumLogins++;
                            await playdict.SetAsync(tx, playerid, pp);
                            await tx.CommitAsync();

                            // Scenario: We are in (LI-N) or (LI-LI)
                            int key = Partitioners.GetRoomPartition(roomid);
                            string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}" +
                                         $"&playerdata={JsonConvert.SerializeObject(playerOption.Value.Player)}" +
                                         $"&roomtype={roomtype}" +
                                         $"&PartitionKind=Int64Range&PartitionKey={key}";
                            HttpResponseMessage response = await this.httpClient.GetAsync(url);
                            string responseMessage = await response.Content.ReadAsStringAsync();
                            if ((int) response.StatusCode == 200)
                                return new ContentResult {StatusCode = 200, Content = roomtype};
                            return new ContentResult {StatusCode = 500, Content = responseMessage};
                        }
                        case LogState.LoggedIn:
                        {
                            // Scenario: Assuming the last login was successful, we think the state will be (LI-LI). However in case of failure,
                            // we could be in (LI-N)

                            // So first we ask if the room has the data, if it does, the player is logged in
                            await tx.CommitAsync();
                            int key = Partitioners.GetRoomPartition(roomid);
                            string url = this.proxy + $"Exists/?playerid={playerid}&roomid={roomid}&PartitionKind=Int64Range&PartitionKey={key}";
                            HttpResponseMessage response = await this.httpClient.GetAsync(url);
                            string responseMessage = await response.Content.ReadAsStringAsync();
                            if ((int) response.StatusCode == 200)
                            {
                                if (responseMessage == "true")
                                    return new ContentResult {StatusCode = 400, Content = "This player is already logged in"};
                                if (responseMessage == "false")
                                    using (ITransaction tx1 = this.stateManager.CreateTransaction())
                                    {
                                        PlayerPackage pp = playerOption.Value;
                                        pp.RoomId = roomid;
                                        pp.NumLogins++;
                                        await playdict.SetAsync(tx, playerid, pp);
                                        await tx1.CommitAsync();

                                        key = Partitioners.GetRoomPartition(roomid);
                                        url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}" +
                                              $"&playerdata={JsonConvert.SerializeObject(pp.Player)}" +
                                              $"&roomtype={roomtype}" +
                                              $"&PartitionKind=Int64Range&PartitionKey={key}";
                                        response = await this.httpClient.GetAsync(url);
                                        responseMessage = await response.Content.ReadAsStringAsync();
                                        return (int) response.StatusCode == 200 ? new ContentResult {StatusCode = 200, Content = roomtype} : 
                                            new ContentResult {StatusCode = 500, Content = responseMessage};
                                    }
                            }
                            else
                            {
                                return new ContentResult {StatusCode = 500, Content = "Something went wrong, please retry"};
                            }
                        }
                            break;
                    }
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 500, Content = "Something went wrong, please retry"};
                }
            }
            catch (FabricNotPrimaryException)
            {
                return new ContentResult {StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service."};
            }
            catch (FabricException)
            {
                return new ContentResult {StatusCode = 503, Content = "The service was unable to process the request. Please try again."};
            }
        }

        [Route("api/[controller]/EndGame")]
        [HttpGet]
        public async Task<IActionResult> EndGame(string playerid, string playerdata)
        {
            try
            {
                Player player = JsonConvert.DeserializeObject<Player>(playerdata);
                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<PlayerPackage> playerOption = await playdict.TryGetValueAsync(tx, playerid, LockMode.Update);

                    if (!playerOption.HasValue)
                    {
                        await tx.CommitAsync();
                        return new ContentResult {StatusCode = 500};
                    }

                    if (playerOption.Value.State == LogState.LoggedOut)
                    {
                        await tx.CommitAsync();
                        return new ContentResult {StatusCode = 200};
                    }
                    if (playerOption.Value.State == LogState.LoggedIn)
                    {
                        PlayerPackage pp = playerOption.Value;
                        pp.Player = player;
                        pp.State = LogState.LoggedOut;
                        await playdict.SetAsync(tx, playerid, pp);
                        await tx.CommitAsync();
                        return new ContentResult {StatusCode = 200};
                    }

                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 500};
                }
            }
            catch
            {
                return new ContentResult {StatusCode = 500};
            }
        }


        [Route("api/[controller]/GetPlayerStats")]
        [HttpGet]
        public async Task<IActionResult> GetPlayerStats()
        {
            try
            {
                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                PlayerStats stats = new PlayerStats(0, 0, 0, 0);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    stats.NumAccounts = await playdict.GetCountAsync(tx);

                    IAsyncEnumerable<KeyValuePair<string, PlayerPackage>> enumerable = await playdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, PlayerPackage>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                    {
                        if (enumerator.Current.Value.State == LogState.LoggedIn)
                            stats.NumLoggedIn++;

                        stats.AvgNumLogins += (float) enumerator.Current.Value.NumLogins / stats.NumAccounts;
                        stats.AvgAccountAge += (double) DateTime.Now.ToUniversalTime().Subtract(enumerator.Current.Value.FirstLogin).Seconds /
                                               stats.NumAccounts;
                    }
                    await tx.CommitAsync();

                    return this.Json(stats);
                }
            }
            catch
            {
                //TODO
                return null;
            }
        }
    }
}