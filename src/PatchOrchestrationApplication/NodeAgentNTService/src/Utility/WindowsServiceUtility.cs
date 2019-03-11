// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    /// <summary>
    /// Utility class to manage Windows NT services
    /// </summary>
    public class WindowsServiceUtility
    {
        public const string FabricInstallerSvcName = "FabricInstallerSvc";
        public const string FabricHostSvcName = "FabricHostSvc";

        /// <summary>
        /// Issues command to Stop the NT service and wait infinitely for it to stop
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <param name="eventSource">eventsource to do logging</param>
        internal static void StopNtService(string serviceName, ServiceEventSource eventSource)
        {
            ServiceController serviceController = new ServiceController(serviceName);
            serviceController.Refresh();
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (serviceController.Status != ServiceControllerStatus.Stopped)
            {
                eventSource.InfoMessage("Stopping {0}", serviceName);
                serviceController.Stop();
            }
            else
            {
                eventSource.InfoMessage("{0} is already in {1} state", serviceName, serviceController.Status);
            }

            while (serviceController.Status != ServiceControllerStatus.Stopped)
            {
                eventSource.InfoMessage("Waiting for {0} to stop since {1}, current status = {2}", serviceName, stopwatch.Elapsed, serviceController.Status);
                serviceController.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(5));
                serviceController.Refresh();
            }

            eventSource.InfoMessage("Successfully stopped {0} in {1}", serviceName, stopwatch.Elapsed);
        }

        /// <summary>
        /// Checks the presence of an NT service
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>true if service was found, false if it wasn't found</returns>
        public static bool CheckNtServiceExists(string serviceName)
        {
            ServiceController ctl = ServiceController.GetServices()
                .FirstOrDefault(s => s.ServiceName == serviceName);
            if (ctl == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Utility to check if FabricHostSvc is running
        /// </summary>
        /// <param name="eventSource">eventsource to log events</param>
        /// <param name="waitForStart">flag to indicate indefinite wait in case service is not running</param>
        /// <returns>true if service was running at the time of query, else false</returns>
        internal static bool CheckFabricHostSvcRunning(ServiceEventSource eventSource, bool waitForStart = false)
        {
            return CheckServiceRunning(FabricHostSvcName, eventSource, waitForStart);
        }

        /// <summary>
        /// Utility to check if a service is running
        /// </summary>
        /// <param name="serviceName">Service name which needs to be checked</param>
        /// <param name="eventSource">eventsource to log events</param>
        /// <param name="waitForStart">flag to indicate indefinite wait in case service is not running</param>
        /// <returns>true if service was running at the time of query, else false</returns>
        internal static bool CheckServiceRunning(string serviceName, ServiceEventSource eventSource, bool waitForStart = false)
        {
            ServiceController serviceController = new ServiceController(serviceName);
            serviceController.Refresh();

            if (serviceController.Status != ServiceControllerStatus.Running)
            {
                return true;
            }

            if (waitForStart)
            {
                while (serviceController.Status != ServiceControllerStatus.Running)
                {
                    eventSource.InfoMessage("Waiting for {0} to start, current status = {1}", FabricHostSvcName,
                        serviceController.Status);
                    serviceController.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(5));
                    serviceController.Refresh();
                }
            }

            return false;
        }
    }
}
