// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility.Helpers
{
    using System;
    using System.Fabric;
    using System.Fabric.Health;
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
        private const string ServicePackageName = "NodeAgentServicePkg";

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
            HealthState healthState, long timeToLiveInMinutes = -1)
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
                fabricClient.HealthManager.ReportHealth(healthReport);
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
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
        }


        /// <summary>
        /// Posts a health report against Patch Orchestration Application's NodeAgentService
        /// </summary>
        /// <param name="fabricClient">Fabric client object to carry out HM operations</param>
        /// <param name="applicationName">Name of the application to construct servicename</param>
        /// <param name="healthReportProperty">Property of the health report</param>
        /// <param name="description">Description of the health report</param>
        /// <param name="healthState">HealthState for the health report</param>
        /// <param name="timeToLiveInMinutes">Time to live in minutes for health report</param>
        internal static NodeAgentSfUtilityExitCodes PostServiceHealthReportOnDeployedServicePackage(FabricClient fabricClient,Uri applicationName, string nodeName, string healthReportProperty, string description,
            HealthState healthState, long timeToLiveInMinutes = -1)
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
                DeployedServicePackageHealthReport healthReport = new DeployedServicePackageHealthReport(new Uri(applicationName + ServiceNameSuffix), ServicePackageName, nodeName, healthInformation);
                fabricClient.HealthManager.ReportHealth(healthReport);
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
                else
                {
                    return NodeAgentSfUtilityExitCodes.Failure;
                }
            }
        }
    }
}
