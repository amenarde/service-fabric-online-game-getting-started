// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RoomManager.Controllers
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
    /// The role of this controller is to manage active, in-game state. The service knows which rooms it is hosting, from store:/rooms,
    /// and coordinates requests to get the entire room state or to update state to those rooms. This
    /// </summary>
    public class RoomStoreController : Controller
    {
        private static readonly Uri RoomDictionaryName = new Uri("store:/rooms");

        private readonly HttpClient httpClient;
        private readonly IReliableStateManager stateManager;
        private readonly ConfigSettings configSettings;
        private readonly StatefulServiceContext serviceContext;

        private RoomManager cache;
        private string proxy; //Proxy for PlayerManager

        /// <summary>
        ///     This constructor will execute the first time a function in the controller is called.
        /// </summary>
        public RoomStoreController(
            StatefulServiceContext serviceContext, HttpClient httpClient, ConfigSettings configSettings,
            IReliableStateManager stateManager, RoomManager cache)
        {
            this.stateManager = stateManager;
            this.httpClient = httpClient;
            this.configSettings = configSettings;
            this.serviceContext = serviceContext;
            this.cache = cache;

            this.RenewProxy();
        }

        // Should be run to update the address to the stateful service, which may change when the service moves to a different node
        // during failover or regular load balancing.
        private void RenewProxy()
        {
            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{this.configSettings.ReverseProxyPort}/" +
                         $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{this.configSettings.PlayerManagerName}/api/PlayerStore/";
        }


        /// <summary>
        /// Requests here are routed from the PlayerStoreController. At this point that controller is waiting for this function to add the
        /// data and return a success. Failure cases are handled by the NewGame in PlayerManager.
        /// </summary>
        /// <param name="roomid">room to find or create to store this player</param>
        /// <param name="playerid">the playerid to put in room</param>
        /// <param name="playerdata">the player data to link to the player</param>
        /// <param name="roomtype">which room to make if making a new room</param>
        /// <returns>200 success code if successfully logged in, appropriate failure code otherwise.</returns>
        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string roomid, string playerid, string playerdata, string roomtype)
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult { StatusCode = 500, Content = "Service is still starting up. Please retry." };

                Player player = JsonConvert.DeserializeObject<Player>(playerdata);

                //get the general room dictionary
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                //get or add the active room we will put this player in
                IReliableDictionary<string, ActivePlayer> activeroom =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {

                    //Find the room in the room dictionary
                    ConditionalValue<Room> roomOption = await roomdict.TryGetValueAsync(tx, roomid);
                    if (!roomOption.HasValue)
                    {
                        //Scenario: Room does not exist yet
                        await roomdict.AddAsync(tx, roomid, new Room(1, roomtype));
                    }
                    else
                    {
                        //Scenario: Room does exist
                        Room r = roomOption.Value;
                        r.NumPlayers++;
                        await roomdict.SetAsync(tx, roomid, r);
                    }

                    //Add the data to that room
                    await activeroom.SetAsync(tx, playerid, new ActivePlayer(player, DateTime.UtcNow));
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = "Successfully Logged In"};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else
                {
                    return new ContentResult { StatusCode = 500, Content = e.Message };
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }

        /// <summary>
        /// In states where something went wrong and PlayerManager or RoomManager disagree, PlayerManager will resolve these by checking
        /// with the room it thinks the player should be in. This is a read-only look into existence of a player in a room.
        /// </summary>
        /// <param name="roomid">the room to check in for the player</param>
        /// <param name="playerid">the player to look for</param>
        /// <returns>If successful: a 200 with either true or false. If not, the appropriate failure code. </returns>
        [Route("api/[controller]/Exists")]
        [HttpGet]
        public async Task<IActionResult> Exists(string roomid, string playerid)
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult { StatusCode = 500, Content = "Service is still starting up. Please retry." };

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    //Check that the room exists
                    if (!await roomdict
                        .ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 200, Content = "false"};

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    //check int hat room for the player
                    if (!await activeroom.ContainsKeyAsync(tx, playerid))
                        return new ContentResult {StatusCode = 200, Content = "false"};

                    return new ContentResult {StatusCode = 200, Content = "true"};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else
                {
                    return new ContentResult { StatusCode = 500, Content = e.Message };
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }

        /// <summary>
        /// Simple iterator returns all the rooms current on this partition. Returns as a List that will be appended to the lists of
        /// other partitions by the WebService.
        /// </summary>
        /// <returns> List of KeyValuePair of (string, Room)</returns>
        [Route("api/[controller]/GetRooms")]
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult { StatusCode = 500, Content = "Service is still starting up. Please retry." };

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {

                    // Make an enumerable over the room to gather all the data in this partitions room dictionary.
                    // Letting this partition own a room dictionary makes this operation more quick.

                    List<KeyValuePair<string, Room>> result = new List<KeyValuePair<string, Room>>();
                    IAsyncEnumerable<KeyValuePair<string, Room>> enumerable = await roomdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, Room>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                        result.Add(enumerator.Current);
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = JsonConvert.SerializeObject(result)};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else
                {
                    return new ContentResult { StatusCode = 500, Content = e.Message };
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }

        /// <summary>
        /// This is the game request that will most likely see the most throughput. Each client of each room will call this function on
        /// the order of 20 times a second. Grabs the entire state of the requested room and returns it. Future optimizations may be
        /// a memory update system: returning current memory cached data and updating periodically, or enforcing state and never needing
        /// to update memory besides during failover.
        /// </summary>
        /// <param name="roomid"></param>
        /// <returns>List of KeyValuePair of (playerid, Player)</returns>
        [Route("api/[controller]/GetGame")]
        [HttpGet]
        public async Task<IActionResult> GetGame(string roomid)
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult { StatusCode = 500, Content = "Service is still starting up. Please retry." };

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    //Make sure the requested room exists
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    //Return an eumerator of the rooms in this dictionary to be parsed together with others
                    List<KeyValuePair<string, Player>> result = new List<KeyValuePair<string, Player>>();
                    IAsyncEnumerable<KeyValuePair<string, ActivePlayer>> enumerable = await activeroom.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, ActivePlayer>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                        result.Add(new KeyValuePair<string, Player>(enumerator.Current.Key, enumerator.Current.Value.Player));
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = JsonConvert.SerializeObject(result)};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else
                {
                    return new ContentResult { StatusCode = 500, Content = e.Message };
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }

        /// <summary>
        /// Used by the client to update their game state, which currently includes position and color. Because of little game data,
        /// a player's game state is entirely replaced by the client's game state.
        /// </summary>
        /// <param name="roomid">the room that you expect the player to be in</param>
        /// <param name="playerid">the player you want to update</param>
        /// <param name="playerdata">the serialized playerdata to update</param>
        /// <returns>Success code if successful, or appropriate failure code</returns>
        [Route("api/[controller]/UpdateGame")]
        [HttpGet]
        public async Task<IActionResult> UpdateGame(string roomid, string playerid, string playerdata)
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult {StatusCode = 500, Content = "Service is still starting up. Please retry."};

                Player newPlayerData = JsonConvert.DeserializeObject<Player>(playerdata);

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    //Make sure the room exists
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    //Check that the player exists, take update lcok because we will write to this key
                    ConditionalValue<ActivePlayer> playerOption = await activeroomdict.TryGetValueAsync(tx, playerid, LockMode.Update);
                    if (!playerOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This player is not in this room"};


                    //Generate the new player package and update it
                    ActivePlayer newPlayerPackage = playerOption.Value;
                    newPlayerPackage.Player = newPlayerData;
                    newPlayerPackage.LastUpdated = DateTime.UtcNow;

                    await activeroomdict.SetAsync(tx, playerid, newPlayerPackage);

                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = "Game successfully updated"};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult {StatusCode = 503, Content = e.Message};
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult {StatusCode = 503, Content = e.Message};
                }
                else
                {
                    return new ContentResult {StatusCode = 500, Content = e.Message};
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }

        /// <summary>
        /// Handles the coordination of an end game. This includes handing off the most recent player data to the PlayerManager, and if
        /// successful, removing that player from the room. It is important that we do not remove data until the player manager has that data.
        /// </summary>
        /// <param name="roomid"></param>
        /// <param name="playerid"></param>
        /// <returns></returns>
        [Route("api/[controller]/EndGame")]
        [HttpGet]
        public async Task<IActionResult> EndGame(string roomid, string playerid)
        {
            try
            {
                if (!RoomManager.IsActive)
                    return new ContentResult { StatusCode = 500, Content = "Service is still starting up. Please retry." };

                //Make sure we have the most updated address
                this.RenewProxy();

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                Room room; //want to maintain this information between our transactions

                //Transaction one: Send the PlayerManager the updated player data
                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<Room> roomOption = await roomdict.TryGetValueAsync(tx, roomid);

                    //make sure room exists
                    if (!roomOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};

                    //Hand the room data up so that transaction two does not have to gather it again
                    room = roomOption.Value;

                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    //try to get the player  data
                    ConditionalValue<ActivePlayer> playerOption = await activeroomdict.TryGetValueAsync(tx, playerid);
                    if (!playerOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This player is not in this room"};

                    //now that we have it, we are finished with the transaction.
                    await tx.CommitAsync();

                    // We send the data to the PlayerManager to update data and mark that player as offline. We do not want to
                    // remove data until we are sure it is stored in the PlayerManager.

                    int key = Partitioners.GetPlayerPartition(playerid);
                    string url = this.proxy + $"EndGame/?playerid={playerid}" +
                                 $"&playerdata={JsonConvert.SerializeObject(playerOption.Value.Player)}" +
                                 $"&PartitionKind=Int64Range&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);
                    string responseMessage = await response.Content.ReadAsStringAsync();
                    if ((int) response.StatusCode != 200)
                        return new ContentResult {StatusCode = 500, Content = responseMessage};
                }

                //Transaction two: remove the player from the room
                using (ITransaction tx1 = this.stateManager.CreateTransaction())
                {
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    //Remove the player from the active room
                    await activeroomdict.TryRemoveAsync(tx1, playerid);

                    //Deincrement the number of players in the room
                    room.NumPlayers--;

                    // If that number is now 0, remove that room from the room dictionary
                    if (room.NumPlayers == 0)
                    {
                        await roomdict.TryRemoveAsync(tx1, roomid);
                        await tx1.CommitAsync();
                    }
                    else
                    {
                        // Otherwise update the entry to reflect less players
                        await roomdict.SetAsync(tx1, roomid, room);
                        await tx1.CommitAsync();
                    }

                    //TODO: Could transition away from numPlayers as a field and asking the active room dict for its count

                    return new ContentResult {StatusCode = 200, Content = "Successfully logged out"};
                }
            }
            catch (FabricException e)
            {
                if (e is FabricObjectClosedException || e is FabricNotPrimaryException)
                {
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else if (e is FabricTransientException)
                {
                    //TODO: retry the transaction
                    return new ContentResult { StatusCode = 503, Content = e.Message };
                }
                else
                {
                    return new ContentResult { StatusCode = 500, Content = e.Message };
                }
            }
            catch (TimeoutException e)
            {
                //TODO: retry the transaction
                return new ContentResult { StatusCode = 503, Content = e.Message };
            }
            catch (Exception e)
            {
                return new ContentResult { StatusCode = 500, Content = e.GetBaseException().Message };
            }
        }
    }
}