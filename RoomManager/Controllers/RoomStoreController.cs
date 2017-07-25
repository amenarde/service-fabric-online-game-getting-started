using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Fabric;
using System.Net.Http;
using Newtonsoft.Json;
using Microsoft.ServiceFabric.Data;
using Common;
using Microsoft.ServiceFabric.Data.Collections;
using System.Threading;

namespace RoomManager.Controllers
{
    public class RoomStoreController : Controller
    {

        private readonly HttpClient httpClient;
        private readonly StatefulServiceContext serviceContext;
        private readonly ConfigSettings configSettings;
        private readonly FabricClient fabricClient;

        private static readonly Uri RoomDictionaryName = new Uri("store:/rooms");
        private readonly IReliableStateManager stateManager;

        public RoomStoreController(StatefulServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings, IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;
        }


        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string roomid, string playerid, string playerdata)
        {
            try
            {
                Player player = JsonConvert.DeserializeObject<Player>(playerdata);

                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    ConditionalValue<Room> roomOption = await roomdict.TryGetValueAsync(tx, roomid);
                    if (!roomOption.HasValue)
                    {
                        //Scenario: Room does not exist yet
                        await roomdict.AddAsync(tx, roomid, new Room(1, "default"));
                    }

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    if (await activeroom.ContainsKeyAsync(tx, playerid, LockMode.Update))
                    {
                        //Player is already in here
                        return new ContentResult { StatusCode = 400, Content = $"This player is already logged in" };
                    }

                    Room r = roomOption.Value; r.numplayers++;
                    await roomdict.SetAsync(tx, roomid, r);
                    await activeroom.AddAsync(tx, playerid, new ActivePlayer(player, DateTime.UtcNow, LogState.LoggedIn));
                    await tx.CommitAsync();
                    return new ContentResult { StatusCode = 200, Content = $"Successfully Logged In" };
                }
            }
            catch (Exception e)
            {
                throw new NotImplementedException();
            }
        }

        [Route("api/[controller]/Exists")]
        [HttpGet]
        public async Task<bool> Exists(string roomid, string playerid)
        {
            try
            {
                IReliableDictionary<string, Room> roomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    if (!await roomdict.ContainsKeyAsync(tx, roomid))
                    {
                        return false;
                    }

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    if (!await activeroom.ContainsKeyAsync(tx, playerid))
                    {
                        return false;
                    }

                    return true;
                }
            }
            catch
            {
                throw new NotImplementedException();
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
                    {
                        result.Add(enumerator.Current);
                    }
                    await tx.CommitAsync();
                    return new ContentResult { StatusCode = 200, Content = JsonConvert.SerializeObject(result) };
                }
            }
            catch
            {
                return new ContentResult { StatusCode = 500, Content = "Something went wrong, please try again." };
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
                    {
                        //Scenario: Room does not exist
                        return new ContentResult { StatusCode = 400, Content = $"This room does not exist" };
                    }

                    IReliableDictionary<string, ActivePlayer> activeroom =
                        await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);

                    List<KeyValuePair<string, Player>> result = new List<KeyValuePair<string, Player>>();
                    IAsyncEnumerable<KeyValuePair<string, ActivePlayer>> enumerable = await activeroom.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, ActivePlayer>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(CancellationToken.None))
                    {
                        if(enumerator.Current.Value.state == LogState.LoggedIn)
                        {
                            result.Add(new KeyValuePair<string, Player>(enumerator.Current.Key, enumerator.Current.Value.player));
                        }
                    }
                    await tx.CommitAsync();
                    return new ContentResult { StatusCode = 200, Content = JsonConvert.SerializeObject(result) };
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
                    {
                        //Requested room state for a room that isn't active
                        return new ContentResult { StatusCode = 400, Content = $"This room does not exist" };
                    }
                    IReliableDictionary<string, ActivePlayer> activeroomdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomid);


                    ConditionalValue<ActivePlayer> player_option = await activeroomdict.TryGetValueAsync(tx, playerid, LockMode.Update);
                    if (!player_option.HasValue)
                    {
                        //Requested to update state of a player that is not in this room
                        return new ContentResult { StatusCode = 400, Content = "This player is not in this room" };
                    }

                    ActivePlayer pp = player_option.Value;
                    pp.player = p;
                    await activeroomdict.SetAsync(tx, playerid, pp);

                    await tx.CommitAsync();
                    return new ContentResult { StatusCode = 200, Content = $"Game successfully updated" };
                }
            }
            catch (ArgumentException)
            { return new ContentResult { StatusCode = 400, Content = $"A value with name already exists." }; }
            catch (FabricNotPrimaryException)
            { return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." }; }
            catch (FabricException)
            { return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." }; }
        }

        [Route("api/[controller]/EndGame")]
        [HttpGet]
        public async Task<string> EndGame(string roomid, string playerid)
        {
            throw new NotImplementedException();
        }

        [Route("api/[controller]/GetRoomStats")]
        [HttpGet]
        public async Task<string> GetRoomStats()
        {
            throw new NotImplementedException();
        }
    }
}
