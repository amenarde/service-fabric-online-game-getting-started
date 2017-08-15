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
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Common;
    using Microsoft.AspNetCore.Mvc;
    using Newtonsoft.Json;

    /// <summary>
    /// This stateless service has two roles. The primary role is to route calls to the correct partitions in the stateful service, and also
    /// to generate information that requires knowledge of all the stateful partitions. This is also the place to do verification that you
    /// would not want to do client side to prevent malicious actions or catch things that accidentally got through.
    /// </summary>
    public class PlayerController : Controller
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
        public PlayerController(StatelessServiceContext serviceContext, HttpClient httpClient, FabricClient fabricClient, ConfigSettings settings)
        {
            this.serviceContext = serviceContext;
            this.httpClient = httpClient;
            this.configSettings = settings;
            this.fabricClient = fabricClient;

            this.RenewProxy();
        }

        /// <summary>
        /// Handles the login process by verifying arguments.
        /// </summary>
        /// <param name="playerid">The username of the player trying to log in</param>
        /// <param name="roomid">The room the player is trying to join</param>
        /// <param name="roomtype">Holds the room type, which is only used if making a new room, making it optional for existing rooms</param>
        /// <returns></returns>
        [Route("api/[controller]/NewGame")]
        [HttpGet]
        public async Task<IActionResult> NewGameAsync(string playerid, string roomid, string roomtype)
        {
            // Only accept nonempty alphanumeric usernames under 20 characters long
            // this should have been handled in javascript, may point to aggressive behavior
            // also verify no one tries to make a room other than those provided
            Regex names = new Regex(@"(?i)([a-z?0-9?\-?]\s?){0,10}");
            if (!names.IsMatch(playerid))
                return this.StatusCode(400, this.Json("Invalid username."));
            if (!names.IsMatch(roomid))
                return this.StatusCode(400, this.Json("Invalid room name."));
            try
            {
                Enum.Parse(typeof(RoomTypes), roomtype);
            }
            catch
            {
                return this.StatusCode(400, this.Json("This is not a valid room type."));
            }

            int key = Partitioners.GetPlayerPartition(playerid); //Direct query to correct partition
            string url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&roomtype={roomtype}&PartitionKind=Int64Range&PartitionKey={key}";
            HttpResponseMessage response = await this.httpClient.GetAsync(url); //Send the request and wait for the response

            // A 404 tells us that our proxy is old, so we renew it
            if ((int) response.StatusCode == 404)
            {
                this.RenewProxy();

                url = this.proxy + $"NewGame/?playerid={playerid}&roomid={roomid}&roomtype={roomtype}&PartitionKind=Int64Range&PartitionKey={key}";
                response = await this.httpClient.GetAsync(url); //Send the request and wait for the response
            }

            // Forward the response we get to the client
            return this.StatusCode((int) response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));
        }

        /// <summary>
        /// Handles gathering account statistics from the backend. This demonstrates the special purpose in having a dedicated
        /// RoomManager. Gathers and concatenates these statistics from all the partitions. Do this infrequently.
        /// </summary>
        /// <returns>PlayerStats object and success code or a failure code</returns>
        [Route("api/[controller]/GetStats")]
        [HttpGet]
        public async Task<IActionResult> GetStatsAsync()
        {
            //TODO: Transition out of static cache

            // Only update data every 20 seconds, this is to allow most requests to this slow function to get it quickly
            if (Startup.PlayerStatsLastUpdated == DateTime.MinValue || DateTime.UtcNow.Subtract(Startup.PlayerStatsLastUpdated) > new TimeSpan(0, 0, 20))
            {
                this.RenewProxy(); // Since this function runs infrequently we update our proxy

                ServicePartitionList partitions = await this.fabricClient.QueryManager.GetPartitionListAsync(
                    new Uri($"{this.serviceContext.CodePackageActivationContext.ApplicationName}/{this.configSettings.PlayerManagerName}"));

                //holds our final results
                PlayerStats result = new PlayerStats(0, 0, 0, 0);


                //Ask each partition for their own statistics, and then merge the statistics
                foreach (Partition partition in partitions)
                {
                    long key = ((Int64RangePartitionInformation) partition.PartitionInformation).LowKey;
                    string url = this.proxy + $"GetPlayerStats/?PartitionKind={partition.PartitionInformation.Kind}&PartitionKey={key}";
                    HttpResponseMessage response = await this.httpClient.GetAsync(url);

                    if (response.StatusCode != HttpStatusCode.OK) //TODO: May not need to end the whole thing
                        return this.StatusCode((int) response.StatusCode, this.Json(await response.Content.ReadAsStringAsync()));

                    PlayerStats thisStat = JsonConvert.DeserializeObject<PlayerStats>(await response.Content.ReadAsStringAsync());

                    //Merge statistics
                    long newTotal = result.NumAccounts + thisStat.NumAccounts;
                    result.AvgAccountAge = result.AvgAccountAge * (result.NumAccounts / (float) newTotal) +
                                           thisStat.AvgAccountAge * (thisStat.NumAccounts / (float) newTotal);
                    result.AvgNumLogins = result.AvgNumLogins * (result.NumAccounts / (float) newTotal) +
                                          thisStat.AvgNumLogins * (thisStat.NumAccounts / (float) newTotal);
                    result.NumLoggedIn += thisStat.NumLoggedIn;
                    result.NumAccounts = newTotal;

                    //Divide by 0 errors if there are no accounts yet
                    if (double.IsNaN(result.AvgAccountAge))
                        result.AvgAccountAge = 0;
                    if (float.IsNaN(result.AvgNumLogins))
                        result.AvgNumLogins = 0;
                }

                //Return our stats and mark them as freshly updated
                Startup.PlayerStats = result;
                Startup.PlayerStatsLastUpdated = DateTime.UtcNow;
                return this.StatusCode(200, JsonConvert.SerializeObject(result));
            }
            return this.StatusCode(200, JsonConvert.SerializeObject(Startup.PlayerStats));
        }

        // Should be run to update the address to the stateful service, which may change when the service moves to a different node
        // during failover or regular load balancing.
        private void RenewProxy()
        {
            // gets the IP address of the application, and the port of the proxy, adds fabric to it, and address to controller
            this.proxy = $"http://{FabricRuntime.GetNodeContext().IPAddressOrFQDN}:" +
                         $"{this.configSettings.ReverseProxyPort}/" +
                         $"{this.serviceContext.CodePackageActivationContext.ApplicationName.Replace("fabric:/", "")}/" +
                         $"{this.configSettings.PlayerManagerName}/api/PlayerStore/";
        }
    }
}