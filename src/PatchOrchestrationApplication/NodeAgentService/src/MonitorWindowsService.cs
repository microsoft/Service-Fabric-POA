// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentService
{
    using System;
    using System.Diagnostics;
    using System.Fabric;
    using System.Fabric.Health;
    using System.ServiceProcess;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides functionality to monitor windows service
    /// Also monitors logs folder for trimming local logs
    /// </summary>
    internal class MonitorWindowsService
    {
        private ServiceController serviceController;
        private const string ServiceName = "POSNodeSvc";

        /// <summary>
        /// Time interval in milliseconds before a unhealthy event should trigger health alert for current service instance.
        /// </summary>
        internal long NtServiceWatchdogInMilliseconds { get; set; }= 240000;

        /// <summary>
        /// Polling frequency in seconds between each run
        /// </summary>
        internal int PollingFrequencyInSeconds { get; set; } = 120;

        /// <summary>
        /// Disk quota in bytes for local logs folder
        /// </summary>
        internal long LogsDiskQuotaInBytes { get; set;} = 1073741824;

        /// <summary>
        /// Health Property for the health report, current class has only one property for the status of the service.
        /// </summary>
        private readonly string healthProperty;
        private readonly FabricClient fabricClient;
        private readonly ServiceContext context;
        private readonly string logsFolder;

        internal MonitorWindowsService(FabricClient fabricClient, StatelessServiceContext context, string logsFolder)
        {
            this.fabricClient = fabricClient;
            this.context = context;
            this.logsFolder = logsFolder;
            this.healthProperty = string.Format("Status of NT service : {0}", ServiceName);
        }

        /// <summary>
        /// Monitor the windows NT Service and raise health alerts if service is down after the configured timeperiod.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        internal async Task RunMonitoringAsync(CancellationToken cancellationToken)
        {
            Stopwatch serviceStopwatch = new Stopwatch();
            serviceStopwatch.Start();
            this.serviceController = new ServiceController(ServiceName);
            DateTime? lastSuccessfullRunningDataDateTime = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.serviceController.Refresh();
                ServiceControllerStatus status;

                try
                {
                    status = this.serviceController.Status;
                }
                catch (Exception ex)
                {
                    string alertMessage = string.Format("Couldn't get the status of service, Exception = {0}", ex);
                    ServiceEventSource.Current.InfoMessage(alertMessage);
                    this.SetHealthAlert(alertMessage);
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
                    continue;
                }

                switch (status)
                {
                    case ServiceControllerStatus.Running:
                    {
                        lastSuccessfullRunningDataDateTime = DateTime.UtcNow;
                        serviceStopwatch.Restart();
                        this.ResetHealthAlert(String.Format("Service : {0} is running successfully", ServiceName));
                        break;
                    }
                    default:
                    {
                        long elapsedTime = serviceStopwatch.ElapsedMilliseconds;
                        if (elapsedTime > NtServiceWatchdogInMilliseconds)
                        {
                            string alertMessage =
                                String.Format(
                                    "Service {0} is not running since {1} milliseconds. Last successfull running time : {2}",
                                    ServiceName, elapsedTime,
                                    null == lastSuccessfullRunningDataDateTime
                                        ? "unknown"
                                        : lastSuccessfullRunningDataDateTime.ToString());
                            this.SetHealthAlert(alertMessage);
                        }

                        Console.WriteLine(
                            "Service {0} not running. Found in {1} state. Time elapsed since last running = {2}",
                            ServiceName, status, elapsedTime);
                        break;
                    }
                }

                DirectoryUtility.TrimDirectory(logsFolder, LogsDiskQuotaInBytes);

                await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationToken);
            }
        }

        /// <summary>
        /// Reset the Health Alert for current stateless service instance
        /// Typically called when condition causing health alert is remedied
        /// </summary>
        /// <param name="description">description of the health report</param>
        private void ResetHealthAlert(string description)
        {
            HealthManagerHelper.PostServiceInstanceHealthReport(this.fabricClient, this.context, HealthManagerHelper.SourceId, this.healthProperty, description, HealthState.Ok,
                HealthManagerHelper.HealthyEventTtlInMinutes);
        }

        /// <summary>
        /// Sets health alert for current stateless service instance
        /// Typically called when there is a problem in service instance or its sub-components(NT service).
        /// </summary>
        /// <param name="description">description of the health report</param>
        private void SetHealthAlert(string description)
        {
            HealthManagerHelper.PostServiceInstanceHealthReport(this.fabricClient, this.context, HealthManagerHelper.SourceId, this.healthProperty, description, HealthState.Warning);
        }
    }
}