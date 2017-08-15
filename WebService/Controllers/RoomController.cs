// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;

    /// <summary>
    /// This stateless service has two roles. The primary role is to route calls to the correct partitions in the stateful service, and also
    /// to generate information that requires knowledge of all the stateful partitions. This is also the place to do verification that you
    /// would not want to do client side to prevent malicious actions or catch things that accidentally got through.
    /// </summary>
    public class RoomController : Controller
    {
        private readonly HttpClient httpClient; //Be able to send requests to our stateful service
        private readonly StatelessServiceContext serviceContext; //Know things about the service it is running on
        private readonly ConfigSettings configSettings; //Get information from our manifests
        private readonly FabricClient fabricClient; //Get information from the fabric it is running on

        private string proxy;

        /// <summary>
        /// Allows this controller, which is recreated and destroyed for each call, 
        /// to access the contexts it needs to control the behavior wanted.
        /// </summary>
        public RoomController(StatelessServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings)
        {
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.RenewProxy();
        }

        /// <summary>
        /// Called once a second by clients that are not currently logged in
        /// </summary>
        /// <returns></returns>
        [Route("api/[controller]/GetRooms")]
        [HttpGet]
        public async Task<IActionResult> GetRoomsAsync()
        {
            try
            {
                List<KeyValuePair<string, Room>> rooms = new List<KeyValuePair<string, Room>>();

                ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(
                    new Uri($"{this.serviceContext.CodePackageActivationContext.ApplicationName}/{this.configSettings.RoomManagerName}"));

                foreach (Partition partition in partitions)
                {
                    long key = ((Int64RangePartitionInformation) partition.PartitionInformation).LowKey;
                    string url = this.proxy + $"GetRooms/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);

                    //Renew your proxy if it moved
                    if ((int) response.StatusCode == 404)
                    {
                        this.RenewProxy();

                        url = this.proxy + $"GetRooms/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                        response = await this.httpClient.GetAsync(url);
                    }

                    // If one partition fails, fail the whole request. Can also choose to just retry that parititon or retry the whole
                    // function, or even return everything else that came back.
                    if (response.StatusCode != HttpStatusCode.OK)
                        return this.StatusCode((int) response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));


                    //Add on the new rooms that came back
                    List<KeyValuePair<string, Room>> theserooms =
                        JsonConvert.DeserializeObject<List<KeyValuePair<string, Room>>>(await response.Content.ReadAsStringAsync());

                    rooms.AddRange(theserooms);
                }

                return this.StatusCode(200, JsonConvert.SerializeObject(rooms));
            }
            catch
            {
                //TODO
                return this.StatusCode(500, "Something went wrong, please retry");
            }
        }

        /// <summary>
        /// Handles requests to get a current game state. This will likely be the most common request in this application. Push it
        /// through the backend.
        /// </summary>
        /// <param name="roomid">Current room the player is in</param>
        /// <returns></returns>
        [Route("api/[controller]/GetGame")]
        [HttpGet]
        public async Task<IActionResult> GetGameAsync(string roomid)
        {
            int key = Partitioners.GetRoomPartition(roomid);
            string url = this.proxy + $"GetGame/?roomid={roomid}&PartitionKind=Int64Range&PartitionKey={key}";
            HttpResponseMessage response = await this.httpClient.GetAsync(url);

            //Renew the proxy if the stateful service has moved
            if ((int) response.StatusCode == 404)
            {
                this.RenewProxy();

                url = this.proxy + $"GetGame/?roomid={roomid}&PartitionKind=Int64Range&PartitionKey={key}";
                response = await this.httpClient.GetAsync(url);
            }

            return this.StatusCode((int) response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Called to update features of the player like position and color. Likely the second most common request in this application.
        /// If player information grows, this should be moved to a differencing model rather than a full update every time.
        /// </summary>
        /// <param name="playerid">The player they want to update</param>
        /// <param name="roomid">The room they think they are in. This is provided by the client to make routing more quick.</param>
        /// <param name="player">The new package of player data</param>
        /// <returns></returns>
        [Route("api/[controller]/UpdateGame")]
        [HttpGet]
        public async Task<IActionResult> UpdateGameAsync(string playerid, string roomid, string player)
        {
            //Unpackage the player data from its JSON representation
            Player p = JsonConvert.DeserializeObject<Player>(player);

            int key = Partitioners.GetRoomPartition(roomid);
            string url = this.proxy +
                         $"UpdateGame/?roomid={roomid}&playerid={playerid}" +
                         $"&playerdata={JsonConvert.SerializeObject(p)}&PartitionKind=Int64Range&PartitionKey={key}";

            HttpResponseMessage response = await this.httpClient.GetAsync(url);

            if ((int) response.StatusCode == 404)
            {
                this.RenewProxy();

                url = this.proxy +
                      $"UpdateGame/?roomid={roomid}&playerid={playerid}" +
                      $"&playerdata={JsonConvert.SerializeObject(p)}&PartitionKind=Int64Range&PartitionKey={key}";

                response = await this.httpClient.GetAsync(url);
            }

            return this.StatusCode((int) response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Forwards the request to end the game. Since this function is often called if a user closes the window without logging out,
        /// it is likely they will not see the return message.
        /// </summary>
        /// <param name="playerid">The player you want to logout</param>
        /// <param name="roomid">The room they are in. Used to speed up the routing process.</param>
        /// <returns></returns>
        [Route("api/[controller]/EndGame")]
        [HttpGet]
        public async Task<IActionResult> EndGameAsync(string playerid, string roomid)
        {
            int key = Partitioners.GetRoomPartition(roomid);
            string url = this.proxy + $"EndGame/?roomid={roomid}&playerid={playerid}&PartitionKind=Int64Range&PartitionKey={key}";
            HttpResponseMessage response = await this.httpClient.GetAsync(url);

            if ((int) response.StatusCode == 404)
            {
                this.RenewProxy();

                url = this.proxy + $"EndGame/?roomid={roomid}&playerid={playerid}&PartitionKind=Int64Range&PartitionKey={key}";
                response = await this.httpClient.GetAsync(url);
            }

            return this.StatusCode((int) response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // Should be run to update the address to the stateful service, which may change when the service moves to a different node
        // during failover or regular load balancing.
        private void RenewProxy()
        {
            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{this.configSettings.ReverseProxyPort}/" +
                         $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{this.configSettings.RoomManagerName}/api/RoomStore/";
        }
    }
}