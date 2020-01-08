// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using System;
    using WebService;
    using System.Linq;
    using TelemetryLib;
    using System.Fabric;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Fabric.Description;
    using System.Collections.Generic;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Microsoft.ServiceFabric.PatchOrchestration.Common;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;

    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class CoordinatorService : StatefulService, IDataInterface
    {
        /// <summary>
        /// Polling interval for POS main loop
        /// </summary>
        private int pollingFrequencyInSec = 60;

        /// <summary>
        /// Number of Windows Update operation results to cache
        /// </summary>
        private long maxResultsToCache = 3000;
        private string SettingsSectionName = "CoordinatorService";
        internal static string ResultsStoreName = "WindowsUpdateResults";
        private RepairManagerHelper rmHelper;
        private TelemetryEvents telemetryEvents;

        public CoordinatorService(StatefulServiceContext context)
            : base(context)
        {
        }

        /// <summary>
        /// Creates listeners for this service replica to handle client or user requests.
        /// For Coordinator Service we're having ServiceRemoting and Owin listeners
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see https://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            ServiceEventSource.Current.InfoMessage("CreateServiceReplicaListeners of CoordinatorService called");
            
            try
            {
                return new[]
                        {
                            new ServiceReplicaListener(context => 
                            this.CreateServiceRemotingListener(context)),

                            new ServiceReplicaListener(context =>
                                new OwinCommunicationListener(new Startup(this.StateManager), context,
                                    ServiceEventSource.Current, "RESTEndpoint"), "RESTEndpoint")
                        };
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.InfoMessage("CreateServiceReplicaListeners of CoordinatorService failed with {0}", e);
                throw;
            }
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            FabricClient fc = new FabricClient();
            this.telemetryEvents = new TelemetryEvents(fc, ServiceEventSource.Current);
            this.rmHelper = new RepairManagerHelper(fc, this.Context);
            ICodePackageActivationContext activationContext = this.Context.CodePackageActivationContext;
            this.InitializeConfiguration(activationContext.GetConfigurationPackageObject("Config"));
            activationContext.ConfigurationPackageModifiedEvent += this.OnConfigurationPackageModified;
            await this.RunLoopAsync(cancellationToken);
        }

        /// <summary>
        /// Core logic of execution of POS service, comprises of a loop which polls for Repair tasks and puts them in preparing state for further execution UD-wise
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>Task for the async operation</returns>
        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {   
            while (true)
            {
                if (await this.rmHelper.CheckRepairManagerStatus(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Print the current repair tasks for verbosity
                    await this.rmHelper.PrintRepairTasks(cancellationToken);
                    // Prepare the repair tasks
                    await this.rmHelper.PrepareRepairTasks(cancellationToken);
                    // Repair tasks which are under processing for too long would be moved to restoring state.
                    await this.rmHelper.TimeoutRepairTasks(cancellationToken);
                    // Cleanup the ResultStore in case it exceeds the quota
                    await this.CleanupWuOperationResult(cancellationToken);
                    // This task will post updates of Repair tasks on the Coordinator Service.
                    await this.rmHelper.PostClusterPatchingStatus(cancellationToken);
                    // Clears the orphan event posted on coordinator service.
                    await this.rmHelper.ClearOrphanEvents(cancellationToken);
                }

                await Task.Delay(TimeSpan.FromSeconds(this.pollingFrequencyInSec), cancellationToken);
            }
        }

        /// <summary>
        /// Updates the ResultStore of Coordinator Service with operation result
        /// </summary>
        /// <param name="operationResult">Result for the Windows Update operation <see cref="WindowsUpdateOperationResult"/></param>
        /// <param name="timeout">timeout for the operation</param>
        /// <param name="cancellationToken">cancellation token for the async task</param>
        /// <returns>Task for the async operation</returns>
        public async Task UpdateWuOperationResult(WindowsUpdateOperationResult operationResult, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.InfoMessage(
                "UpdateWuOperationResult recieved {0} {1} results from node = {2}, Operation time = {3}",
                operationResult.UpdateDetails.Count,
                operationResult.OperationType,
                operationResult.NodeName,
                operationResult.OperationTime);

            // Update the result in ResultStore
            try
            {
                var resultsStore = await this.StateManager.GetOrAddAsync<IReliableQueue<WindowsUpdateOperationResult>>(ResultsStoreName);

                using (var tx = this.StateManager.CreateTransaction())
                {
                    await resultsStore.EnqueueAsync(tx, operationResult, timeout, cancellationToken);
                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ErrorMessage("Storing of result failed with exception = {0}", ex);
                throw;
            }

            // Send the telemetry event
            try
            {
                if (operationResult.OperationType == WindowsUpdateOperationType.Installation)
                {
                    this.telemetryEvents.PatchInstallationEvent(
                    operationResult.NodeName,
                    operationResult.WindowsUpdateFrequency,
                    operationResult.WindowsUpdateQuery,
                    this.rmHelper.RmPolicy.ToString(),
                    operationResult.UpdateDetails.Count,
                    operationResult.UpdateDetails.Count(update => update.ResultCode == WuOperationResult.Succeeded),
                    operationResult.OperationResult.ToString());
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ErrorMessage("Telemetry failure ex = {0}", ex);
                // Do not re-throw, telemetry failure is not a critical error
            }
        }

        /// <summary>
        /// Cleans up the result store in case it exceeds the max allowed quota
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task for the asynchronous operation</returns>
        internal async Task CleanupWuOperationResult(CancellationToken cancellationToken)
        {
            try
            {
                var resultsStore = await this.StateManager.GetOrAddAsync<IReliableQueue<WindowsUpdateOperationResult>>(ResultsStoreName);
                using (var tx = this.StateManager.CreateTransaction())
                {
                    long resultCount = await resultsStore.GetCountAsync(tx);
                    ServiceEventSource.Current.VerboseMessage("{0} results were found in {1}", resultCount, ResultsStoreName);
                    while (await resultsStore.GetCountAsync(tx) > this.maxResultsToCache)
                    {
                        var removedEntry = await resultsStore.TryDequeueAsync(tx);
                        if (removedEntry.HasValue)
                        {
                            ServiceEventSource.Current.VerboseMessage(
                                "Dequeued result details {0} {1} results from node = {2}, Operation time = {3}",
                                removedEntry.Value.UpdateDetails.Count,
                                removedEntry.Value.OperationType,
                                removedEntry.Value.NodeName,
                                removedEntry.Value.OperationTime);
                        }
                    }

                    await tx.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ErrorMessage("Cleaning of result failed with exception = {0}", ex);
                throw;
            }
        }

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
            if (package.Settings != null && package.Settings.Sections.Contains(this.SettingsSectionName))
            {
                this.ModifySettings(package.Settings.Sections[this.SettingsSectionName]);
            }
        }

        private void ModifySettings(ConfigurationSection configurationSection)
        {
            if (configurationSection != null)
            {
                string paramName = "PollingFrequencyInSec";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.pollingFrequencyInSec = int.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.pollingFrequencyInSec);
                }

                paramName = "MaxResultsToCache";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.maxResultsToCache = long.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.maxResultsToCache);
                }

                paramName = "TaskApprovalPolicy";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.RmPolicy = (RepairManagerHelper.TaskApprovalPolicy) Enum.Parse(typeof(RepairManagerHelper.TaskApprovalPolicy), configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.RmPolicy.ToString());
                }

                paramName = "InstallOnUpNodesOnly";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.InstallOnUpNodesOnly = bool.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.InstallOnUpNodesOnly);
                }

                paramName = "ManageRepairTasksOnTimeout";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.ManageRepairTasksOnTimeout = bool.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.ManageRepairTasksOnTimeout);
                }

                paramName = "DefaultTimeoutForOperation";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.DefaultTimeoutForOperation = TimeSpan.FromMinutes(int.Parse(configurationSection.Parameters[paramName].Value));
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.DefaultTimeoutForOperation);
                }

                paramName = "GraceTimeForNtService";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.GraceTimeForNtService = TimeSpan.FromMinutes(int.Parse(configurationSection.Parameters[paramName].Value));
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.GraceTimeForNtService);
                }

                paramName = "MinWaitTimeBetweenNodes";
                if (configurationSection.Parameters.Contains(paramName))
                {
                    this.rmHelper.MinWaitTimeBetweenNodes = TimeSpan.Parse(configurationSection.Parameters[paramName].Value);
                    ServiceEventSource.Current.VerboseMessage("Parameter : {0}, value : {1}", paramName, this.rmHelper.MinWaitTimeBetweenNodes);
                }
            }
        }
    }
}
