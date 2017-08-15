// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PlayerManager
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.ServiceFabric.Services.Communication.AspNetCore;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// The FabricRuntime creates an instance of this class for each service type instance. 
    /// </summary>
    public sealed class PlayerManager : StatefulService
    {
        /// <summary>
        /// The IsActive token is used to ensure our controllers do not serve requests until the application is initialized. Controller
        /// functions check this token before they serve requests. When RunAsync is called on the Service is when it is ready to
        /// serve requests.
        /// </summary>
        public static bool IsActive;

        /// <summary>
        /// This class is the basis of the stateful service. Functions can be defined here and called by other service if using reverse proxy,
        /// or controllers can call functions here. In this application we can store cached information that we would like controllers to have
        /// access to, since they cannot maintain their own state.
        /// </summary>
        public PlayerManager(StatefulServiceContext context)
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