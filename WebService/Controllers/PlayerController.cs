// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Controllers
{
    using System;
    using System.Fabric;
    using System.Fabric.Query;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;

    public class PlayerController : Controller
    {
        private readonly HttpClient httpClient;
        private readonly StatelessServiceContext serviceContext;
        private readonly ConfigSettings configSettings;
        private readonly FabricClient fabricClient;

        private readonly string proxy;

        /// <summary>
        ///     Receives the context of the webservice the controller is operating in, the context of the Fabric client it is
        ///     talking to,
        ///     and then its configuration and parameters for sending http messages.
        /// </summary>
        /// <param name="serviceContext"></param>
        /// <param name="httpClient"></param>
        /// <param name="fabricClient"></param>
        /// <param name="settings"></param>
        public PlayerController(StatelessServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings)
        {
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{this.configSettings.ReverseProxyPort}/" +
                         $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{this.configSettings.PlayerManagerName}/api/PlayerStore/";
        }

        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string playerid, string roomid, string roomtype)
        {
            int key = Partitioners.GetPlayerPartition(playerid);
            string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&roomtype={roomtype}&PartitionKind=Int64Range&PartitionKey={key}";

            HttpResponseMessage response = await this.httpClient.GetAsync(url);
            return this.StatusCode((int) response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));
        }

        [Route("api/[controller]/GetStats")]
        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            //Only update your data every 20 seconds
            if (Startup.PlayerStatsLastUpdated == DateTime.MinValue || DateTime.UtcNow.Subtract(Startup.PlayerStatsLastUpdated) > new TimeSpan(0, 0, 20))
            {
                ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(
                    new Uri($"{this.serviceContext.CodePackageActivationContext.ApplicationName}/{this.configSettings.PlayerManagerName}"));
                PlayerStats result = new PlayerStats(0, 0, 0, 0);

                foreach (Partition partition in partitions)
                {
                    long key = ((Int64RangePartitionInformation) partition.PartitionInformation).LowKey;
                    string url = this.proxy + $"GetPlayerStats/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);

                    if (response.StatusCode != HttpStatusCode.OK)
                        return this.StatusCode((int) response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));

                    PlayerStats thisStat = JsonConvert.DeserializeObject<PlayerStats>(await response.Content.ReadAsStringAsync());

                    long newTotal = result.NumAccounts + thisStat.NumAccounts;
                    result.AvgAccountAge = result.AvgAccountAge * (result.NumAccounts / (float) newTotal) +
                                           thisStat.AvgAccountAge * (thisStat.NumAccounts / (float) newTotal);
                    result.AvgNumLogins = result.AvgNumLogins * (result.NumAccounts / (float) newTotal) +
                                          thisStat.AvgNumLogins * (thisStat.NumAccounts / (float) newTotal);
                    result.NumLoggedIn += thisStat.NumLoggedIn;
                    result.NumAccounts = newTotal;

                    if (double.IsNaN(result.AvgAccountAge))
                        result.AvgAccountAge = 0;
                    if (float.IsNaN(result.AvgNumLogins))
                        result.AvgNumLogins = 0;
                }   
                Startup.PlayerStats = result;
                Startup.PlayerStatsLastUpdated = DateTime.UtcNow;
                return this.StatusCode(200, JsonConvert.SerializeObject(result));
            }
            else
            {
                return this.StatusCode(200, JsonConvert.SerializeObject(Startup.PlayerStats));
            }
        }
    }
}