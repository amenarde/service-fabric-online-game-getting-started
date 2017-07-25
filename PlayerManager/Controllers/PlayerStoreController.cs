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

namespace PlayerManager.Controllers
{
    public class PlayerStoreController : Controller
    {
        private readonly HttpClient httpClient;
        private readonly StatefulServiceContext serviceContext;
        private readonly ConfigSettings configSettings;
        private readonly FabricClient fabricClient;

        private static readonly Uri PlayersDictionaryName = new Uri("store:/players");
        private readonly IReliableStateManager stateManager;

        private readonly string uri;
        private readonly string proxy;

        public PlayerStoreController(StatefulServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings, IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.uri = $"{ this.configSettings.ReverseProxyPort}/" +
                $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}";
            this.proxy = $"http://localhost:{this.uri}/" +
                $"{this.configSettings.RoomManagerName}/api/RoomStore/";
        }


        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string playerid, string roomid)
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
                        //Scenario: Player does not exist, make a new account
                        Player p = new Player(0, 0, "ADD8E6");
                        PlayerPackage pp = new PlayerPackage(p, LogState.InTransit, 1, DateTime.Now.ToUniversalTime(), roomid);
                        await playdict.AddAsync(tx, playerid, pp);
                        await tx.CommitAsync();


                        int key = Partitioners.GetRoomPartition(roomid);
                        string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&playerdata={JsonConvert.SerializeObject(p)}&PartitionKind=Int64Range&PartitionKey={key}";
                        HttpResponseMessage response = await this.httpClient.GetAsync(url);
                        string response_message = await response.Content.ReadAsStringAsync();
                        if ((int)response.StatusCode == 200)
                        {
                            using (ITransaction tx1 = this.stateManager.CreateTransaction())
                            {
                                pp.state = LogState.LoggedIn;
                                await playdict.SetAsync(tx1, playerid, pp);
                                await tx1.CommitAsync();
                                return new ContentResult { StatusCode = 200, Content = $"Successfully logged in" };
                            }
                        }
                        else
                        {
                            return new ContentResult { StatusCode = 500, Content = $"Something went wrong, please retry" };
                        }

                    }
                    else if (playerOption.Value.state == LogState.LoggedIn)
                    {
                        //Scenario: Player is already logged in, deny request
                        await tx.CommitAsync();
                        return new ContentResult { StatusCode = 400, Content = $"This player is already logged in" };
                    }
                    else if (playerOption.Value.state == LogState.InTransit)
                    {
                        //Scenario: There was a failure and player state must be investigated
                        //Ping the room to see if the player exists
                        await tx.CommitAsync();
                        int key = Partitioners.GetRoomPartition(playerOption.Value.roomid);
                        string url = this.proxy + $"Exists/?playerid={playerid}&roomid={playerOption.Value.roomid}&PartitionKind=Int64Range&PartitionKey={key}";
                        HttpResponseMessage response = await this.httpClient.GetAsync(url);

                        string message = await response.Content.ReadAsStringAsync();

                        if (message == "true")
                        {
                            //Case 1: Player is logged in, in same room
                            if (playerOption.Value.roomid == roomid)
                            {
                                using (ITransaction tx1 = this.stateManager.CreateTransaction())
                                {
                                    PlayerPackage pp = playerOption.Value;
                                    pp.state = LogState.LoggedIn;
                                    pp.numLogins++;
                                    await playdict.SetAsync(tx1, playerid, pp);
                                    await tx1.CommitAsync();
                                    return new ContentResult { StatusCode = 200, Content = $"Successfully logged in" };
                                }
                            }
                            else
                            {
                                //Case 1.1 Player is logged in, but to a different room, do nothing
                                return new ContentResult { StatusCode = 400, Content = $"This player is already logged in" };
                            }
                        }
                        else if (message == "InTransit")
                        {
                            //Case 2: This should not be possible in the matrix of state
                            throw new NotSupportedException("Double InTransit unsupported scenario");
                        }
                        else if (message == "LoggedOut")
                        {
                            //Case 3: someone initiated a newgame but it did not complete
                            int key2 = Partitioners.GetRoomPartition(roomid);
                            string url2 = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&playerdata={playerOption.Value.player}&PartitionKind=Int64Range&PartitionKey={key}";
                            HttpResponseMessage response2 = await this.httpClient.GetAsync(url);
                            if ((int)response2.StatusCode == 200)
                            {
                                using (ITransaction tx1 = this.stateManager.CreateTransaction())
                                {
                                    PlayerPackage pp = playerOption.Value;
                                    pp.state = LogState.LoggedIn;
                                    pp.numLogins++;
                                    await playdict.SetAsync(tx1, playerid, pp);
                                    await tx1.CommitAsync();
                                    return new ContentResult { StatusCode = 200, Content = $"Successfully logged in" };
                                }
                            }
                            else
                            {
                                return new ContentResult { StatusCode = 500, Content = $"Something went wrong, please retry" };
                            }
                        }
                    }
                    else if (playerOption.Value.state == LogState.LoggedOut)
                    {
                        int key = Partitioners.GetRoomPartition(roomid);
                        string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&playerdata={playerOption.Value.player}&PartitionKind=Int64Range&PartitionKey={key}";
                        HttpResponseMessage response = await this.httpClient.GetAsync(url);

                        if ((int)response.StatusCode == 200)
                        {
                            using (ITransaction tx1 = this.stateManager.CreateTransaction())
                            {
                                PlayerPackage pp = playerOption.Value;
                                pp.state = LogState.LoggedIn;
                                pp.numLogins++;
                                await playdict.SetAsync(tx1, playerid, pp);
                                await tx1.CommitAsync();
                                return new ContentResult { StatusCode = 200, Content = $"Successfully logged in" };
                            }
                        }
                        else
                        {
                            return new ContentResult { StatusCode = 500, Content = $"Something went wrong, please retry" };
                        }

                    }

                }

                return new ContentResult { StatusCode = 500, Content = $"Something went wrong, please retry" };
            }
            catch (FabricNotPrimaryException)
            {
                return new ContentResult { StatusCode = 410, Content = "The primary replica has moved. Please re-resolve the service." };
            }
            catch (FabricException)
            {
                return new ContentResult { StatusCode = 503, Content = "The service was unable to process the request. Please try again." };
            }
        }

        [Route("api/[controller]/GetPlayerStats")]
        [HttpGet]
        public async Task<IActionResult> GetPlayerStats(string playerid, string roomid)
        {
            try
            {
                IReliableDictionary<string, PlayerPackage> playdict =
                    await this.stateManager.GetOrAddAsync<IReliableDictionary<string, PlayerPackage>>(PlayersDictionaryName);

                PlayerStats stats = new PlayerStats(0,0,0,0);

                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    stats.numAccounts = await playdict.GetCountAsync(tx);

                    IAsyncEnumerable<KeyValuePair<string, PlayerPackage>> enumerable = await playdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string,PlayerPackage>> enumerator = enumerable.GetAsyncEnumerator();
                    while (await enumerator.MoveNextAsync(System.Threading.CancellationToken.None))
                    {
                        if(enumerator.Current.Value.state == LogState.LoggedIn)
                        {
                            stats.numLoggedIn++;
                        }

                        stats.avgNumLogins += (float)enumerator.Current.Value.numLogins / stats.numAccounts;
                        stats.avgAccountAge += (double)(DateTime.Now.ToUniversalTime().Subtract(enumerator.Current.Value.firstLogin)).Seconds / stats.numAccounts;
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
