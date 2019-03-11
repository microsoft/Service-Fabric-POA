// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.ServiceFabric.Services.Runtime;

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    internal static class Program
    {
        /// <summary>
        /// This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            try
            {
                // The ServiceManifest.XML file defines one or more service type names.
                // Registering a service maps a service type name to a .NET type.
                // When Service Fabric creates an instance of this service type,
                // an instance of the class is created in this host process.

                ServiceRuntime.RegisterServiceAsync("CoordinatorServiceType",
                    context => new CoordinatorService(context)).GetAwaiter().GetResult();

                ServiceEventSource.Current.InfoMessage("Service registered successfully Process ID = {0} type = {1}", Process.GetCurrentProcess().Id, typeof(CoordinatorService).Name);

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.InfoMessage("Service host initialization failed {0}", e.ToString());
                throw;
            }
        }
    }
}
