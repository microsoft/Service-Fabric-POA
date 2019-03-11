// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService.WebService
{
    using System;
    using System.Fabric;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Owin.Hosting;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;

    /// <summary>
    /// Listener for Owin Communications
    /// </summary>
    internal class OwinCommunicationListener : ICommunicationListener
    {
        private const string EndpointSuffix = "PatchOrchestrationApplication";
        private readonly ServiceEventSource eventSource;
        private readonly IWebServiceAppBuilder startup;
        private readonly ServiceContext serviceContext;
        private readonly string endpointName;

        private IDisposable webApp;
        private string publishAddress;
        private string listeningAddress;

        public OwinCommunicationListener(IWebServiceAppBuilder startup, ServiceContext serviceContext,
            ServiceEventSource eventSource, string endpointName)
            : this(startup, serviceContext, eventSource, endpointName, null)
        {
        }

        public OwinCommunicationListener(IWebServiceAppBuilder startup, ServiceContext serviceContext,
            ServiceEventSource eventSource, string endpointName, string appRoot)
        {
            if (startup == null)
            {
                throw new ArgumentNullException(nameof(startup));
            }

            if (serviceContext == null)
            {
                throw new ArgumentNullException(nameof(serviceContext));
            }

            if (endpointName == null)
            {
                throw new ArgumentNullException(nameof(endpointName));
            }

            if (eventSource == null)
            {
                throw new ArgumentNullException(nameof(eventSource));
            }

            this.startup = startup;
            this.serviceContext = serviceContext;
            this.endpointName = endpointName;
            this.eventSource = eventSource;
        }

        /// <summary>
        /// Called by ServiceFabric framework when listener is getting started <see cref="ICommunicationListener"/>
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the async task</param>
        /// <returns>Task for the async operation, result of the task is an Endpoint url</returns>
        public Task<string> OpenAsync(CancellationToken cancellationToken)
        {
            var serviceEndpoint = this.serviceContext.CodePackageActivationContext.GetEndpoint(this.endpointName);
            var protocol = serviceEndpoint.Protocol;
            int port = serviceEndpoint.Port;

            if (this.serviceContext is StatefulServiceContext)
            {
                StatefulServiceContext statefulServiceContext = this.serviceContext as StatefulServiceContext;
                this.listeningAddress = String.Format(
                    CultureInfo.InvariantCulture,
                    "http://+:{0}/{1}",
                    port,
                    EndpointSuffix);
            }
            else
            {
                throw new InvalidOperationException();
            }

            this.publishAddress = this.listeningAddress.Replace("+", FabricRuntime.GetNodeContext().IPAddressOrFQDN);

            try
            {
                this.eventSource.InfoMessage("Starting web server on " + this.listeningAddress);

                this.webApp = WebApp.Start(this.listeningAddress, appBuilder => this.startup.ConfigureApp(appBuilder));

                this.eventSource.InfoMessage("Listening on " + this.publishAddress);

                return Task.FromResult(this.publishAddress);
            }
            catch (Exception ex)
            {
                this.eventSource.InfoMessage("Web server failed to open endpoint {0}. {1}", this.endpointName, ex.ToString());

                this.StopWebServer();

                throw;
            }
        }

        /// <summary>
        /// Called by ServiceFabric framework when listener is getting closed <see cref="ICommunicationListener"/>
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the async task</param>
        /// <returns>Task for the async operation</returns>
        public Task CloseAsync(CancellationToken cancellationToken)
        {
            this.eventSource.InfoMessage("Closing web server on endpoint {0}", this.endpointName);

            this.StopWebServer();

            return Task.FromResult(true);
        }

        /// <summary>
        /// Called by ServiceFabric framework when listener is getting aborted abruptly <see cref="ICommunicationListener"/>
        /// </summary>
        public void Abort()
        {
            this.eventSource.InfoMessage("Aborting web server on endpoint {0}", this.endpointName);

            this.StopWebServer();
        }

        private void StopWebServer()
        {
            if (this.webApp != null)
            {
                try
                {
                    this.webApp.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // no-op
                }
            }
        }
    }
}
