// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility.Helpers
{
    using System;
    using System.Diagnostics;
    using System.Fabric;
    using System.Fabric.Health;
    using System.Fabric.Query;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.PatchOrchestration.Common;
    using HealthState = System.Fabric.Health.HealthState;

    /// <summary>
    /// Provides utilities for ServiceFabric HealthManager
    /// </summary>
    class HealthManagerHelper
    {
        /// <summary>
        /// Source ID for the health report generated from Patch orchestration Agent
        /// </summary>
        private const string SourceId = "Patch Orchestration Node Agent Service";

        /// <summary>
        /// Suffix name to be appended with ApplicationName
        /// </summary>
        private const string ServiceNameSuffix = "/NodeAgentService";
        private const string CoordinatorServiceSuffix = "/CoordinatorService";

        /// <summary>
        /// Posts a health report against Patch Orchestration Application's NodeAgentService
        /// </summary>
        /// <param name="fabricClient">Fabric client object to carry out HM operations</param>
        /// <param name="applicationName">Name of the application to construct servicename</param>
        /// <param name="healthReportProperty">Property of the health report</param>
        /// <param name="description">Description of the health report</param>
        /// <param name="healthState">HealthState for the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live in minutes for health report</param>
        internal static NodeAgentSfUtilityExitCodes PostServiceHealthReport(FabricClient fabricClient, Uri applicationName, string healthReportProperty, string description,
            HealthState healthState, TimeSpan timeout,long timeToLiveInMinutes = -1)
        {
            HealthInformation healthInformation = new HealthInformation(SourceId, healthReportProperty,
                healthState)
            {
                RemoveWhenExpired = true,
                Description = description
            };
            if (timeToLiveInMinutes >= 0)
            {
                healthInformation.TimeToLive = TimeSpan.FromMinutes(timeToLiveInMinutes);
            }

            try
            {
                ServiceHealthReport healthReport = new ServiceHealthReport(new Uri(applicationName + ServiceNameSuffix), healthInformation);
                Func<string, bool> condition = (s) => {
                    bool serviceExists = CheckIfServiceIsUp(fabricClient, applicationName, s).GetAwaiter().GetResult();
                    if (serviceExists)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                };
                HealthReportSendOptions sendOptions = new HealthReportSendOptions();
                sendOptions.Immediate = true;
                TimeoutProcessWithRetry(() =>
                {
                    fabricClient.HealthManager.ReportHealth(healthReport, sendOptions);
                }, condition,
                ServiceNameSuffix, timeout
                );

                Task.Delay(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                return NodeAgentSfUtilityExitCodes.Success;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("HealthManagerHelper.PostNodeHealthReport failed. Exception details {0}", e));
                if (e is FabricTransientException)
                {
                    return NodeAgentSfUtilityExitCodes.RetryableException;
                }
                else if(e is TimeoutException)
                {
                    return NodeAgentSfUtilityExitCodes.TimeoutException;
                }
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
        }

        internal static void TimeoutProcessWithRetry(Action process, Func<string, bool> condition, string s, TimeSpan timeout)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            int count = 0;
            while(stopwatch.Elapsed < timeout)
            {
                if(condition(s))
                {
                    process();
                    return;
                }
                count++;
                Thread.Sleep(TimeSpan.FromSeconds(1 * count));
            }
            stopwatch.Reset();
            throw new TimeoutException(string.Format("Timeout occurred while trying to run the process {0}", process.GetType().Name.ToString()));
        }


        internal static async Task<bool> CheckIfServiceIsUp(FabricClient fabricClient, Uri applicationName, string serviceNameSuffix)
        {
            ServiceList list = await fabricClient.QueryManager.GetServiceListAsync(applicationName);
            Uri serviceUri = new Uri(applicationName + serviceNameSuffix);
            foreach (var s in list)
            {
                if (s.ServiceName == serviceUri)
                {
                    return true;
                }
            }
            return false;
        }



        /// <summary>
        /// Utility to Report information logs of windows update on Coordinator Service.
        /// </summary>
        /// <param name="fabricClient">Fabric client object to carry out HM operations</param>
        /// <param name="applicationName">Name of the application to construct servicename</param>
        /// <param name="healthReportProperty">Property of the health report</param>
        /// <param name="description">Description of the health report</param>
        /// <param name="healthState">HealthState for the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live in minutes for health report</param>
        internal static NodeAgentSfUtilityExitCodes PostServiceHealthReportOnCoordinatorService(FabricClient fabricClient, Uri applicationName, string healthReportProperty, string description,
            HealthState healthState, TimeSpan timeout,long timeToLiveInMinutes = -1)
        {
            HealthInformation healthInformation = new HealthInformation(SourceId, healthReportProperty,
                healthState)
            {
                RemoveWhenExpired = true,
                Description = description
            };
            if (timeToLiveInMinutes >= 0)
            {
                healthInformation.TimeToLive = TimeSpan.FromMinutes(timeToLiveInMinutes);
            }

            try
            {
                ServiceHealthReport healthReport = new ServiceHealthReport(new Uri(applicationName + CoordinatorServiceSuffix), healthInformation);
                Func<string, bool> condition = (s) => {
                    bool serviceExists = CheckIfServiceIsUp(fabricClient, applicationName, s).GetAwaiter().GetResult();
                    if (serviceExists)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                };

                HealthReportSendOptions sendOptions = new HealthReportSendOptions();
                sendOptions.Immediate = true;
                TimeoutProcessWithRetry(() =>
                {
                    fabricClient.HealthManager.ReportHealth(healthReport, sendOptions);
                }, condition,
                CoordinatorServiceSuffix, timeout
                );

                return NodeAgentSfUtilityExitCodes.Success;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("HealthManagerHelper.PostServiceHealthReportOnCoordinatorService failed. Exception details {0}", e));
                if (e is FabricTransientException)
                {
                    return NodeAgentSfUtilityExitCodes.RetryableException;
                }
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
        }
    }
}
