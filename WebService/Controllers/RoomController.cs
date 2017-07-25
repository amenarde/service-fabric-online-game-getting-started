// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Authored by Antonio Menarde.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;
    using Common;

    /// <summary>
    /// This controller is responsible for routing requests sent from clients to the correct stateful service replica, and then
    /// deserialize and package the response data.
    /// </summary>
    public class RoomController : Controller
    {
        private readonly HttpClient httpClient;
        private readonly StatelessServiceContext serviceContext;
        private readonly ConfigSettings configSettings;
        private readonly FabricClient fabricClient;

        private readonly string uri;
        private readonly string proxy;

        /// <summary>
        /// Receives the context of the webservice the controller is operating in, the context of the Fabric client it is talking to,
        /// and then its configuration and parameters for sending http messages.
        /// </summary>
        /// <param name="serviceContext"></param>
        /// <param name="httpClient"></param>
        /// <param name="fabricClient"></param>
        /// <param name="settings"></param>
        public RoomController(StatelessServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings)
        {
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.uri = $"{ this.configSettings.ReverseProxyPort}/" +
               $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}";
            this.proxy = $"http://localhost:{this.uri}/" +
                $"{this.configSettings.RoomManagerName}/api/RoomStore/";
        }

        [Route("api/[controller]/GetRooms")]
        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            try
            {
                List<KeyValuePair<string, Room>> rooms = new List<KeyValuePair<string, Room>>();

                ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(
                    new Uri($"{this.serviceContext.CodePackageActivationContext.ApplicationName}/{this.configSettings.RoomManagerName}"));

                foreach (Partition partition in partitions)
                {
                    long key = ((Int64RangePartitionInformation)partition.PartitionInformation).LowKey;
                    string url = this.proxy + $"GetRooms/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);

                    if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        return this.StatusCode((int)response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));
                    }

                    List<KeyValuePair<string, Room>> theserooms =
                        JsonConvert.DeserializeObject<List<KeyValuePair<string, Room>>>(await response.Content.ReadAsStringAsync());

                    rooms.AddRange(theserooms);
                }

                return this.StatusCode(200, JsonConvert.SerializeObject(rooms));
            }
            catch (Exception e)
            {
                //TODO
                return this.StatusCode(500, "Something went wrong, please retry");
            }
        }

        [Route("api/[controller]/GetGame")]
        [HttpGet]
        public async Task<IActionResult> GetGameAsync(string roomid)
        {
            int key = Partitioners.GetRoomPartition(roomid);
            string url = this.proxy + $"GetGame/?roomid={roomid}&PartitionKind=Int64Range&PartitionKey={key}";

            HttpResponseMessage response = await this.httpClient.GetAsync(url);
            return this.StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [Route("api/[controller]/UpdateGame")]
        [HttpGet]
        public async Task<IActionResult> UpdateGameAsync(string playerid, string roomid, string xpos, string ypos, string color)
        {

            if (!int.TryParse(xpos, out int xpos_int))
            {
                return new ContentResult { StatusCode = 400, Content = "This is not a valid update request" };
            }
            if (!int.TryParse(ypos, out int ypos_int)) {
                return new ContentResult { StatusCode = 400, Content = "This is not a valid update request" };
            }

            int key = Partitioners.GetRoomPartition(roomid);
            Player p = new Player(xpos_int, ypos_int, color);
            string url = this.proxy + $"UpdateGame/?roomid={roomid}&playerid={playerid}&playerdata={JsonConvert.SerializeObject(p)}&PartitionKind=Int64Range&PartitionKey={key}";

            HttpResponseMessage response = await this.httpClient.GetAsync(url);
            return this.StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}