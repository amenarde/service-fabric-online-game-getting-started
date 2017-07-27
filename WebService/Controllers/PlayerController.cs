using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using System.Fabric;
using System.Net.Http;
using Newtonsoft.Json;
using Common;
using System.Fabric.Query;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace WebService.Controllers
{
    public class PlayerController : Controller
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
        public PlayerController(StatelessServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings)
        {
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.uri = $"{ this.configSettings.ReverseProxyPort}/" +
                $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}";
            this.proxy = $"http://localhost:{this.uri}/" +
                $"{this.configSettings.PlayerManagerName}/api/PlayerStore/";

        }

        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGame(string playerid, string roomid)
        {
            int key = Partitioners.GetPlayerPartition(playerid);
            string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&PartitionKind=Int64Range&PartitionKey={key}";

            HttpResponseMessage response = await this.httpClient.GetAsync(url);
            return this.StatusCode((int)response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));
        }

        [Route("api/[controller]/GetStats")]
        [HttpGet]
        public async Task<IActionResult> GetStats()
        {

            ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(
                    new Uri($"{this.serviceContext.CodePackageActivationContext.ApplicationName}/{this.configSettings.PlayerManagerName}"));
            PlayerStats result = new PlayerStats(0, 0, 0, 0);
            
            foreach (Partition partition in partitions)
            {
                long key = ((Int64RangePartitionInformation)partition.PartitionInformation).LowKey;
                string url = this.proxy + $"GetPlayerStats/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                HttpResponseMessage response = await this.httpClient.GetAsync(url);

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return this.StatusCode((int)response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));
                }

                PlayerStats thisStat = JsonConvert.DeserializeObject<PlayerStats>(await response.Content.ReadAsStringAsync());

                long newTotal = result.numAccounts + thisStat.numAccounts;
                result.avgAccountAge = result.avgAccountAge * (result.numAccounts / (float)newTotal) + thisStat.avgAccountAge * (thisStat.numAccounts / (float)newTotal);
                result.avgNumLogins = result.avgNumLogins * (result.numAccounts / (float)newTotal) + thisStat.avgNumLogins * (thisStat.numAccounts / (float)newTotal);
                result.numLoggedIn += thisStat.numLoggedIn;
                result.numAccounts = newTotal;
            }

            return this.StatusCode(200, JsonConvert.SerializeObject(result));
        }
    }
}
