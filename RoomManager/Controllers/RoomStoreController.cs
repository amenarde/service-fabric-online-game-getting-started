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

    public class RoomStoreController : Controller
    {
        private static readonly Uri RoomDictionaryName = new Uri("store:/rooms");

        private readonly HttpClient httpClient;
        private readonly FabricClient fabricClient;
        private readonly IReliableStateManager stateManager;

        private readonly string proxy;

        public RoomStoreController(
            StatefulServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings configSettings,
            IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
            this.httpClient = httpClient;
            this.fabricClient = fabricClient;

            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{configSettings.ReverseProxyPort}/" +
                         $"{serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{configSettings.PlayerManagerName}/api/PlayerStore/";
        }


        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string roomid, string playerid, string playerdata, string roomtype)
        {
            try
            {
                Player player = JsonConvert.DeserializeObject<Player>(playerdata);

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    ConditionalValue<Room> roomOption = await roomdict.TryGetValueAsync(tx, roomid);
                    if (!roomOption.HasValue)
                    {
                        //Scenario: Room does not exist yet
                        await roomdict.AddAsync(tx, roomid, new Room(1, roomtype));
                    }
                    else
                    {
                        Room r = roomOption.Value;
                        r.NumPlayers++;
                        await roomdict.SetAsync(tx, roomid, r);
                    }

                    await activeroom.SetAsync(tx, playerid, new ActivePlayer(player, DateTime.UtcNow));
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = "Successfully Logged In"};
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

        [Route("api/[controller]/Exists")]
        [HttpGet]
        public async Task<IActionResult> Exists(string roomid, string playerid)
        {
            try
            {
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 200, Content = "false"};

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    if (!await activeroom.ContainsKeyAsync(tx, playerid))
                        return new ContentResult {StatusCode = 200, Content = "false"};

                    return new ContentResult {StatusCode = 200, Content = "true"};
                }
            }
            catch
            {
                return new ContentResult {StatusCode = 500};
            }
        }

        [Route("api/[controller]/GetRooms")]
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    List<KeyValuePair<string, Room>> result = new List<KeyValuePair<string, Room>>();
                    IAsyncEnumerable<KeyValuePair<string, Room>> enumerable = await roomdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, Room>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                        result.Add(enumerator.Current);
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = JsonConvert.SerializeObject(result)};
                }
            }
            catch
            {
                return new ContentResult {StatusCode = 500, Content = "Something went wrong, please try again."};
            }
        }

        [Route("api/[controller]/GetGame")]
        [HttpGet]
        public async Task<IActionResult> GetGame(string roomid)
        {
            try
            {
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    List<KeyValuePair<string, Player>> result = new List<KeyValuePair<string, Player>>();
                    IAsyncEnumerable<KeyValuePair<string, ActivePlayer>> enumerable = await activeroom.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, ActivePlayer>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                        result.Add(new KeyValuePair<string, Player>(enumerator.Current.Key, enumerator.Current.Value.Player));
                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = JsonConvert.SerializeObject(result)};
                }
            }
            catch
            {
                throw new NotImplementedException();
            }
        }

        [Route("api/[controller]/UpdateGame")]
        [HttpGet]
        public async Task<IActionResult> UpdateGame(string roomid, string playerid, string playerdata)
        {
            try
            {
                Player p = JsonConvert.DeserializeObject<Player>(playerdata);

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);


                    ConditionalValue<ActivePlayer> playerOption = await activeroomdict.TryGetValueAsync(tx, playerid, LockMode.Update);
                    if (!playerOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This player is not in this room"};

                    ActivePlayer pp = playerOption.Value;
                    pp.Player = p;
                    pp.LastUpdated = DateTime.UtcNow;

                    await activeroomdict.SetAsync(tx, playerid, pp);

                    await tx.CommitAsync();
                    return new ContentResult {StatusCode = 200, Content = "Game successfully updated"};
                }
            }
            catch (ArgumentException)
            {
                return new ContentResult {StatusCode = 400, Content = "A value with name already exists."};
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
        public async Task<IActionResult> EndGame(string roomid, string playerid)
        {
            try
            {
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                Room room;


                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<Room> roomOption = await roomdict.TryGetValueAsync(tx, roomid);
                    if (!roomOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This room does not exist"};

                    room = roomOption.Value;
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    ConditionalValue<ActivePlayer> playerOption = await activeroomdict.TryGetValueAsync(tx, playerid);
                    if (!playerOption.HasValue)
                        return new ContentResult {StatusCode = 400, Content = "This player is not in this room"};

                    await tx.CommitAsync();

                    int key = Partitioners.GetPlayerPartition(playerid);
                    string url = this.proxy + $"EndGame/?playerid={playerid}" +
                                 $"&playerdata={JsonConvert.SerializeObject(playerOption.Value.Player)}" +
                                 $"&PartitionKind=Int64Range&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);
                    string responseMessage = await response.Content.ReadAsStringAsync();
                    if ((int) response.StatusCode != 200)
                        return new ContentResult {StatusCode = 500, Content = responseMessage};
                }
                using (ITransaction tx1 = this.stateManager.CreateTransaction())
                {
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    await activeroomdict.TryRemoveAsync(tx1, playerid);

                    room.NumPlayers--;
                    if (room.NumPlayers == 0)
                    {
                        await roomdict.TryRemoveAsync(tx1, roomid);
                        await tx1.CommitAsync();
                    }
                    else
                    {
                        await roomdict.SetAsync(tx1, roomid, room);
                        await tx1.CommitAsync();
                    }


                    return new ContentResult {StatusCode = 200, Content = "Successfully logged out"};
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
            catch
            {
                return new ContentResult {StatusCode = 500, Content = "Bad state"};
            }
        }
    }
}