// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RoomManager
{
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using global::RoomManager.Controllers;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public sealed class RoomManager : StatefulService
    {
        /// <summary>
        /// The IsActive token is used to ensure our controllers do not serve requests until the application is initialized. Controller
        /// functions check this token before they serve requests. When RunAsync is called on the Service is when it is ready to
        /// serve requests.
        /// </summary>
        public static bool IsActive;

        private static readonly TimeSpan InactivityLogoutSeconds = new TimeSpan(0, 0, 300);
        private static readonly Uri RoomDictionaryName = new Uri("store:/rooms");

        /// <summary>
        /// This class is the basis of the stateful service. Functions can be defined here and called by other service if using reverse proxy,
        /// or controllers can call functions here. In this application we can store cached information that we would like controllers to have
        /// access to, since they cannot maintain their own state.
        /// </summary>
        /// <param name="context"></param>
        public RoomManager(StatefulServiceContext context)
            : base(context)
        {
        }

        /// <summary>
        /// Called when the Service is started up. It is here that you would initialize things in your service or begin long-running tasks.
        /// </summary>
        /// <param name="cancellationToken"></param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            IsActive = true;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RoomStoreController roomController = new RoomStoreController(
                    new HttpClient(),
                    new ConfigSettings(this.Context),
                    this);

                //Here is the autologoff protocol, used for logging out inactive players.
                IReliableDictionary<string, Room> roomdict =
                    await this.StateManager.GetOrAddAsync<IReliableDictionary<string, Room>>(RoomDictionaryName);

                List<string> roomList = new List<string>();
                using (ITransaction tx = this.StateManager.CreateTransaction())
                {
                    //Gather the names of all the rooms, of which you will check all of them
                    IAsyncEnumerable<KeyValuePair<string, Room>> enumerable = await roomdict.CreateEnumerableAsync(tx);
                    IAsyncEnumerator<KeyValuePair<string, Room>> roomEnumerator = enumerable.GetAsyncEnumerator();
                    while (await roomEnumerator.MoveNextAsync(CancellationToken.None))
                        roomList.Add(roomEnumerator.Current.Key);

                    await tx.CommitAsync();
                }

                // For each room, get an enumerator, find players that are inactive, and ask for them to be logged off.
                foreach (string roomName in roomList)
                    using (ITransaction tx = this.StateManager.CreateTransaction())
                    {
                        IReliableDictionary<string, ActivePlayer> activeroom =
                            await this.StateManager.GetOrAddAsync<IReliableDictionary<string, ActivePlayer>>(roomName);

                        // Get an enumerator over this room
                        IAsyncEnumerable<KeyValuePair<string, ActivePlayer>> enumerable = await activeroom.CreateEnumerableAsync(tx);
                        IAsyncEnumerator<KeyValuePair<string, ActivePlayer>> activeRoomEnumerator = enumerable.GetAsyncEnumerator();
                        List<KeyValuePair<string, ActivePlayer>> activePlayerList = new List<KeyValuePair<string, ActivePlayer>>();

                        while (await activeRoomEnumerator.MoveNextAsync(CancellationToken.None))
                            activePlayerList.Add(activeRoomEnumerator.Current);

                        await tx.CommitAsync();

                        foreach (KeyValuePair<string, ActivePlayer> player in activePlayerList)
                            // For each player, check if the time since they were last updated is greater than the inactivity timeout
                            if (DateTime.UtcNow.Subtract(player.Value.LastUpdated) > InactivityLogoutSeconds)
                                await roomController.EndGame(roomName, player.Key);
                    }

                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        /// <summary>
        /// Optional override to create listeners (like tcp, http) for this service instance.
        /// </summary>
        /// <returns>The collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                new ServiceReplicaListener(
                    serviceContext =>
                        new KestrelCommunicationListener(
                            serviceContext,
                            (url, listener) =>
                            {
                                ServiceEventSource.Current.ServiceMessage(serviceContext, $"Starting Kestrel on {url}");

                                return new WebHostBuilder()
                                    .UseKestrel()
                                    .ConfigureServices(
                                        services => services
                                            .AddSingleton(new ConfigSettings(serviceContext))
                                            .AddSingleton(new HttpClient())
                                            .AddSingleton(this))
                                    .UseContentRoot(Directory.GetCurrentDirectory())
                                    .UseStartup<Startup>()
                                    .UseApplicationInsights()
                                    .UseServiceFabricIntegration(listener, ServiceFabricIntegrationOptions.UseUniqueServiceUrl)
                                    .UseUrls(url)
                                    .Build();
                            }))
            };
        }
    }
}