// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentService
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.Fabric.Description;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;

    /// <summary>
    /// Stateless agent service responsible for carrying out the actual patch work on each node.
    /// It comprises of following subcomponents.
    /// 1) Stateless service - The current class belongs to the stateless service (also called Patch orchestration Agent).
    /// 2) Windows NT service (Packaged as Data for current stateless service)- This windows NT service is installed and invoked at the setup entry point of current stateless service,
    ///      its responsbile for carrying out windows updates. This service is devoid of any functionality of Service Fabric.
    /// 3) ServiceFabric executable utility (Packaged as Data for current stateless service) - Is a helper executable for Windows NT service and provides Service Fabric functionality to Windows NT service 
    ///     (Eg: Functionalities related to Repair Manager, Health Manager, invoking client calls to Patch Orchestration Service's Coordinator)
    /// </summary>
    internal sealed class NodeAgentService : StatelessService
    {
        private FabricClient fabricClient;
        private const string settingsSectionName = "NodeAgentService";
        private const string NtServicePath = @"\NodeAgentNTService\";
        private const string HealthProperty = "Copy Settings.xml to NodeAgentNTService";
        private MonitorWindowsService monitorWindowsService = null;

        public NodeAgentService(StatelessServiceContext context)
            : base(context)
        {
        }

        /// <summary>
        /// Optional override to create listeners (e.g., TCP, HTTP) for this service replica to handle client or user requests.
        /// </summary>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return new ServiceInstanceListener[0];
        }

        /// <summary>
        /// This is the main entry point for NodeAgentService
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service instance.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            this.fabricClient = new FabricClient();
            ICodePackageActivationContext activationContext = this.Context.CodePackageActivationContext;
            ConfigurationPackage configurationPackage = activationContext.GetConfigurationPackageObject("Config");

            string logsFolderPath = this.GetLocalPathForApplication(configurationPackage.Path) + @"\logs";
            this.monitorWindowsService = new MonitorWindowsService(this.fabricClient, this.Context, logsFolderPath);

            this.InitializeConfiguration(configurationPackage);
            activationContext.ConfigurationPackageModifiedEvent += this.OnConfigurationPackageModified;
            await this.monitorWindowsService.RunMonitoringAsync(cancellationToken);
        }

        /// <summary>
        /// Callback event for Configuration upgrade, this function is added to the 
        /// list of <see cref="ICodePackageActivationContext.ConfigurationPackageModifiedEvent"/>
        /// </summary>
        /// <param name="sender">Sender of config upgrade</param>
        /// <param name="e">Contains new and old package along with other details related to configuration</param>
        internal void OnConfigurationPackageModified(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            if (e.NewPackage.Description.Name.Equals("Config"))
            {
                ServiceEventSource.Current.VerboseMessage("Configuration upgrade triggered from {0} to {1}",
                    e.OldPackage.Description.Version, e.NewPackage.Description.Version);
                this.InitializeConfiguration(e.NewPackage);
            }
        }

        private void InitializeConfiguration(ConfigurationPackage package)
        {
            string settingsDestinationPath = this.GetLocalPathForApplication(package.Path) + NtServicePath + "Settings.xml";
            NtServiceConfigurationUtility.CreateConfigurationForNtService(package, settingsDestinationPath);
            if (package.Settings != null && package.Settings.Sections.Contains(settingsSectionName))
            {
                this.ModifySettings(package.Settings.Sections[settingsSectionName]);
            }
        }

        private void ModifySettings(ConfigurationSection configurationSection)
        {
            if (configurationSection != null)
            {
                string paramName = "LogsDiskQuotaInMB";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.monitorWindowsService.LogsDiskQuotaInBytes = long.Parse(configurationSection.Parameters[paramName].Value) * 1024 * 1024;
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.monitorWindowsService.LogsDiskQuotaInBytes);
                }

                paramName = "NtServiceWatchdogInMilliseconds";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.monitorWindowsService.NtServiceWatchdogInMilliseconds = long.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.monitorWindowsService.NtServiceWatchdogInMilliseconds);
                }

                paramName = "PollingFrequencyInSeconds";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.monitorWindowsService.PollingFrequencyInSeconds = int.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.monitorWindowsService.PollingFrequencyInSeconds);
                }
            }
        }

        private string GetLocalPathForApplication(string configPath)
        {
            FileInfo f = new FileInfo(configPath);
            return Path.GetPathRoot(f.FullName) + @"\PatchOrchestrationApplication";
        }
    }
}