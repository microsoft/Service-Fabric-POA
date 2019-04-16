// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Fabric.Query;
using System.Fabric.Repair;
using System.Linq;
using System.Threading.Tasks;
using System.Fabric.Health;
    
namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using System.Diagnostics;
    using System.Threading;

    using Microsoft.ServiceFabric.PatchOrchestration.Common;

    using HealthState = System.Fabric.Health.HealthState;

    /// <summary>
    /// Helper class for RepairManager functionalities
    /// </summary>
    internal class RepairManagerHelper
    {
        private const string TaskIdPrefix = "POS";
        private const string ExecutorName = "POS";
        private readonly FabricClient fabricClient;
        private readonly ServiceContext context;
        private static readonly Uri SystemUri = new Uri("fabric:/System");
        private static readonly Uri RepairManagerUri = new Uri("fabric:/System/RepairManagerService");
        private const string RepairManagerStatus = "RepairManager status";
        private const string NodeTimeoutStatusFormat = "Node : {0} exceeding installation timeout";
        internal TaskApprovalPolicy RmPolicy = TaskApprovalPolicy.NodeWise;
        private const string RMTaskUpdateProperty = "RMTaskUpdate";
        private int postUpdateCount = 0;
        private const string WUOperationStatusUpdate = "WUOperationStatusUpdate";

        /// <summary>
        /// Default timeout for async operations
        /// Default timeout for async operations
        /// </summary>
        internal TimeSpan DefaultTimeoutForOperation = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Flag which governs if repair tasks should be monitored for installation timeouts,
        /// this configuration is internal and should be only overriden during troubleshooting
        /// </summary>
        internal bool ManageRepairTasksOnTimeout = true;

        /// <summary>
        /// Grace time which would be added to repair task timeout
        /// </summary>
        internal TimeSpan GraceTimeForNtService = TimeSpan.FromMinutes(45);

        /// <summary>
        /// Install only on nodes which are in Up state, this configuration is internal and should be only overriden during troubleshooting
        /// </summary>
        internal bool InstallOnUpNodesOnly = true;

        /// <summary>
        /// Policy to approve repair tasks
        /// </summary>
        internal enum TaskApprovalPolicy
        {
            /// <summary>
            /// Repair tasks can be approved only one at a time
            /// </summary>
            NodeWise,
            /// <summary>
            /// Repair task can be approved for an entire UpgradeDomain
            /// </summary>
            UpgradeDomainWise
        }

        /// <summary>
        /// Constructor for RepairManager helper
        /// </summary>
        /// <param name="fabricClient"></param>
        /// <param name="context"></param>
        internal RepairManagerHelper(FabricClient fabricClient, ServiceContext context)
        {
            this.fabricClient = fabricClient;
            this.context = context;
        }

        /// <summary>
        /// Checks if repair manager is enalbed on the cluster or not
        /// </summary>
        /// <param name="cancellationToken">cancellation token to stop the asyn operation</param>
        /// <returns>true if repair manager application is present in cluster, otherwise false</returns>
        internal async Task<bool> CheckRepairManagerStatus(CancellationToken cancellationToken)
        {
            ServiceList serviceList = await this.fabricClient.QueryManager.GetServiceListAsync(SystemUri, RepairManagerUri, this.DefaultTimeoutForOperation, cancellationToken);

            if (serviceList.Count == 0)
            {
                string warningDescription =
                    string.Format("{0} could not be found, Patch Orchestration Service requires RepairManager system service to be enable on the cluster. Consider adding RepairManager section in cluster manifest.",
                        RepairManagerUri);
                HealthManagerHelper.PostNodeHealthReport(this.fabricClient, this.context.ServiceName,
                    RepairManagerStatus, warningDescription, HealthState.Warning);
                return false;
            }

            string description = string.Format("{0} is available", RepairManagerUri);
            HealthManagerHelper.PostNodeHealthReport(this.fabricClient, this.context.ServiceName,
                RepairManagerStatus, description, HealthState.Ok, 1);
            return true;
        }

        /// <summary>
        /// Gets the list of POS Repair Tasks which are claimed
        /// These tasks would be put in preparing state UD-wise by POS.
        /// </summary>
        /// <returns>List of repair tasks in claimed state</returns>
        private async Task<IList<RepairTask>> GetClaimedRepairTasks(NodeList nodeList, CancellationToken cancellationToken)
        {
            IList<RepairTask> repairTasks = await this.fabricClient.RepairManager.GetRepairTaskListAsync(TaskIdPrefix,
                RepairTaskStateFilter.Claimed,
                ExecutorName, this.DefaultTimeoutForOperation, cancellationToken);

            int claimedRepairTaskCount = repairTasks.Count;

            IList<RepairTask> selectedRepairTasks = new List<RepairTask>();

            // Select repair tasks belonging to Up nodes (if flag is set) and
            // Prune out orphan repair tasks
            foreach (var repairTask in repairTasks)
            {
                string targetName = this.GetNodeNameFromRepairTask(repairTask);
                if (string.IsNullOrEmpty(targetName))
                {
                    continue;
                }

                bool repairTaskIsOprhan = true;
                foreach (var node in nodeList)
                {
                    if (node.NodeName.Equals(targetName))
                    {
                        repairTaskIsOprhan = false;
                        if (this.InstallOnUpNodesOnly && node.NodeStatus != NodeStatus.Up)
                            break;

                        selectedRepairTasks.Add(repairTask);
                        break;
                    }
                }

                // prune out the orphan repair tasks
                if (repairTaskIsOprhan)
                {
                    ServiceEventSource.Current.VerboseMessage("Cancelling Orphan repair task {0} which is in {1} state", repairTask.TaskId, repairTask.State);
                    await this.CancelRepairTask(repairTask);
                    claimedRepairTaskCount--;
                }

                Node repairNode = nodeList.SingleOrDefault(node => node.NodeName.Equals(this.GetNodeNameFromRepairTask(repairTask)));
            }

            if (claimedRepairTaskCount != selectedRepairTasks.Count)
            {
                ServiceEventSource.Current.VerboseMessage(
                    "{0} out of {1} claimed repair tasks would be considered for approval. Remaining {2} tasks belonged to nodes which are not in 'Up' state",
                    selectedRepairTasks.Count, claimedRepairTaskCount, claimedRepairTaskCount - selectedRepairTasks.Count);
            }

            return selectedRepairTasks;
        }

        /// <summary>
        /// This function returns the list of repair tasks which are undergoing work.
        /// At any point of time there will be only one UD which will have POS repair tasks in these states.
        /// </summary>
        /// <returns>List of repair tasks in Preparing, Approved, Executing or Restoring state</returns>
        internal async Task<RepairTaskList> GetRepairTasksUnderProcessing(CancellationToken cancellationToken)
        {
            RepairTaskList repairTasks = await this.fabricClient.RepairManager.GetRepairTaskListAsync(TaskIdPrefix,
                RepairTaskStateFilter.Preparing | RepairTaskStateFilter.Approved | RepairTaskStateFilter.Executing |
                RepairTaskStateFilter.Restoring,
                ExecutorName, this.DefaultTimeoutForOperation, cancellationToken);

            return repairTasks;
        }

        /// <summary>
        /// Gets the node name from targetdescription of a repair task.
        /// </summary>
        /// <param name="task">repair task</param>
        /// <returns>target node name of the repair task if only one node was found in target,  null in case multiple target nodes were found or target description is null</returns>
        internal string GetNodeNameFromRepairTask(RepairTask task)
        {
            var targetDescription = task.Target as NodeRepairTargetDescription;

            if (targetDescription == null)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("RepairTask with ID = {0} found with null target description", task.TaskId));
                return null;
            }

            // Check if someone has set more than one target for a node, we're expecting only one target
            if (targetDescription.Nodes.Count != 1)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("RepairTask with ID = {0}. Target description node count:{1}", task.TaskId,
                        targetDescription.Nodes.Count));
                return null;
            }

            return targetDescription.Nodes.First();
        }

        /// <summary>
        /// Returns the upgrade domain of a repair task
        /// </summary>
        /// <param name="task">repair task</param>
        /// <param name="nodeList">List of nodes in cluster</param>
        /// <returns>Upgrade domain of repair task, or null in case of errors</returns>
        internal string GetUpgradeDomainOfRepairTask(RepairTask task, NodeList nodeList)
        {
            string nodeName = this.GetNodeNameFromRepairTask(task);
            if (string.IsNullOrEmpty(nodeName))
            {
                return null;
            }

            IEnumerable<Node> node =
                nodeList.Where(
                    tempNode =>
                        tempNode.NodeName.Equals(nodeName,
                            StringComparison.CurrentCultureIgnoreCase));

            string udName = node.Any() ? node.First().UpgradeDomain : null;

            return udName;
        }

        /// <summary>
        /// Puts a repair task in Preparing state.
        /// By doing this RM will do health check (if applicable) and start disabling the node. Post disable task would be moved to Approved state by RM
        /// </summary>
        /// <param name="task">Repair task to be put to preparing state</param>
        internal void StartPreparingRepairTask(RepairTask task)
        {
            // Update the health policy for the repair task
            Task<long> resultTask = this.fabricClient.RepairManager.UpdateRepairTaskHealthPolicyAsync(task.TaskId, task.Version, true, null);
            resultTask.Wait();
            ServiceEventSource.Current.VerboseMessage("Updated health policy for repair task {0} successfully, moving to preparing state", task.TaskId);

            // Put the repair task in preparing state
            string nodeName = this.GetNodeNameFromRepairTask(task);
            task.State = RepairTaskState.Preparing;
            NodeRepairImpactDescription impact = new NodeRepairImpactDescription();
            impact.ImpactedNodes.Add(new NodeImpact(nodeName, NodeImpactLevel.Restart));
            task.Impact = impact;
            task.PerformPreparingHealthCheck = true;
            // Use the latest version or else next Update call would fail
            task.Version = resultTask.Result;
            this.fabricClient.RepairManager.UpdateRepairExecutionStateAsync(task);
        }

        /// <summary>
        /// Prints the repair task for ease of debugging
        /// </summary>
        internal async Task PrintRepairTasks(CancellationToken cancellationToken)
        {
            RepairTaskList repairTasks = await this.fabricClient.RepairManager.GetRepairTaskListAsync(TaskIdPrefix,
                RepairTaskStateFilter.All,
                ExecutorName, this.DefaultTimeoutForOperation, cancellationToken);

            ServiceEventSource.Current.VerboseMessage("Total {0} repair tasks were found for POS", repairTasks.Count);
            foreach (var task in repairTasks)
            {
                ServiceEventSource.Current.PrintRepairTasks(task.TaskId, task.State.ToString(), task.Action, task.Executor,
                    task.Description, task.ExecutorData, task.Target.ToString());
            }
        }

        public async Task PostRMTaskUpdates(CancellationToken cancellationToken)
        {
            try
            {
                NodeList nodeList = await this.fabricClient.QueryManager.GetNodeListAsync(null, null, this.DefaultTimeoutForOperation, cancellationToken);
                IList<RepairTask> claimedTaskList = await this.GetClaimedRepairTasks(nodeList, cancellationToken);
                RepairTaskList processingTaskList = await this.GetRepairTasksUnderProcessing(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (claimedTaskList.Any())
                {
                    if (!processingTaskList.Any())
                    {
                        // This means that repair tasks are not getting approved.
                        ClusterHealth clusterHealth = await this.fabricClient.HealthManager.GetClusterHealthAsync();
                        if (clusterHealth.AggregatedHealthState != HealthState.Ok)
                        {
                            // Reset Count
                            postUpdateCount = 0;
                            string warningDescription = "Cluster is unhealthy. Repair task created for OS update will not be approved. Please take cluster to healthy state for POA to start working.";
                            HealthManagerHelper.PostNodeHealthReport(this.fabricClient, this.context.ServiceName, RMTaskUpdateProperty, warningDescription, HealthState.Warning, -1);
                        }
                        else
                        {
                            postUpdateCount++;
                            if (postUpdateCount > 60)
                            {
                                // Reset Count and throw a warning on the service saying we dont know the reason. But POA not is not approving tasks.
                                postUpdateCount = 0;
                                string warningDescription = "POA repair tasks are not getting approved, So, update installation is halted. Please try to find out why is this blocked.";
                                HealthManagerHelper.PostNodeHealthReport(this.fabricClient, this.context.ServiceName, RMTaskUpdateProperty, warningDescription, HealthState.Warning, -1);
                            }
                        }
                    }
                    else
                    {
                        // Reset Count
                        postUpdateCount = 0;
                        await PostRMTaskNodeUpdate(cancellationToken);
                    }
                }
                else
                {
                    // Reset Count
                    postUpdateCount = 0;
                    if (processingTaskList.Any())
                    {
                        await PostRMTaskNodeUpdate(cancellationToken);
                    }
                    else
                    {
                        // Post the health event saying that there is no repair task and things are working fine.
                        string description = "No claimed tasks and no processing tasks are found.";
                        HealthManagerHelper.PostNodeHealthReport(this.fabricClient, this.context.ServiceName, RMTaskUpdateProperty, description, HealthState.Ok, -1);
                    }
                }
            }
            catch(Exception ex)
            {
                ServiceEventSource.Current.ErrorMessage("PostRMTaskUpdates failed with exception {0}", ex.ToString());
            }

        }
        
        public async Task ClearOrphanEvents(CancellationToken cancellationToken)
        {
            try
            {
                ServiceHealth health = await this.fabricClient.HealthManager.GetServiceHealthAsync(this.context.ServiceName);
                List<HealthEvent> healthEventsToCheck = new List<HealthEvent>();
                foreach (var e in health.HealthEvents)
                {
                    if (e.HealthInformation.Property.Contains(WUOperationStatusUpdate))
                    {
                        healthEventsToCheck.Add(e);
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();

                NodeList nodeList = await this.fabricClient.QueryManager.GetNodeListAsync(null, null, this.DefaultTimeoutForOperation, cancellationToken);
                List<string> orphanProperties = new List<string>();
                Dictionary<string, bool> propertyDict = new Dictionary<string, bool>();
                if (healthEventsToCheck.Count == nodeList.Count)
                {
                    return;
                }
                else
                {
                    foreach (var node in nodeList)
                    {
                        propertyDict.Add(WUOperationStatusUpdate + "-" + node.NodeName, true);
                    }
                    foreach (var e in healthEventsToCheck)
                    {
                        if (!propertyDict.ContainsKey(e.HealthInformation.Property))
                        {
                            orphanProperties.Add(e.HealthInformation.Property);
                        }
                    }

                    foreach (var property in orphanProperties)
                    {
                        ServiceEventSource.Current.VerboseMessage("Property {0}'s event is removed from CoordinatorService", property);

                        // I think we would need to change the expiry time to ~0
                        string description = "This health event will be expired in 1 seconds as node corresponding to this event is deleted.";
                        HealthManagerHelper.PostNodeHealthReport(fabricClient, this.context.ServiceName, property, description, HealthState.Ok, 1);
                    }
                }
            }
            catch(Exception ex)
            {
                ServiceEventSource.Current.ErrorMessage("ClearOrphanEvents failed with exception {0}", ex.ToString());
            }
        }

        private async Task PostRMTaskNodeUpdate(CancellationToken cancellationToken)
        {
            NodeList nodeList = await this.fabricClient.QueryManager.GetNodeListAsync(null, null, this.DefaultTimeoutForOperation, cancellationToken);
            HashSet<string> processingNodes = new HashSet<string>();
            HashSet<string> pendingNodes = new HashSet<string>();
            IList<RepairTask> claimedTaskList = await this.GetClaimedRepairTasks(nodeList, cancellationToken);
            foreach (var task in claimedTaskList)
            {
                pendingNodes.Add(task.Target.ToString());
            }
            cancellationToken.ThrowIfCancellationRequested();
            RepairTaskList processingTaskListFinal = await this.GetRepairTasksUnderProcessing(cancellationToken);
            foreach (var task in processingTaskListFinal)
            {
                processingNodes.Add(task.Target.ToString());
            }

            string pendingNodesString = string.Join(",", pendingNodes);
            string processingNodesString = string.Join(",", processingNodes);
            
            if(String.IsNullOrEmpty(pendingNodesString))
            {
                pendingNodesString = "None";
            }

            if (String.IsNullOrEmpty(processingNodesString))
            {
                processingNodesString = "None";
            }

            string description = string.Format("ProcessingNodes :{0}, PendingNodes: {1}", processingNodesString, pendingNodesString);
            HealthManagerHelper.PostNodeHealthReport(fabricClient, this.context.ServiceName, RMTaskUpdateProperty, description, HealthState.Ok);
        }

        /// <summary>
        /// Gets the upgrade domain from current repair tasks under processing, ideally all the repair tasks under processing should've the same upgrade domain.
        /// However if repair tasks belonging to multiple UpgradeDomains are found, then we consider the UD of first repair task in the list of repair tasks.
        /// </summary>
        /// <param name="nodeList">List of Nodes currently in cluster, used to get nodename and upgradedomain mapping</param>
        /// <returns>Upgrade domain of the first repair task among the list of repair tasks under processing</returns>
        private async Task<string> GetCurrentUpgradeDomainUnderProcessing(NodeList nodeList, CancellationToken cancellationToken)
        {
            string currentUpgradeDomain = null;
            RepairTaskList processingTaskList = await this.GetRepairTasksUnderProcessing(cancellationToken);
            ServiceEventSource.Current.VerboseMessage(String.Format("{0} repair tasks were found under processing",
                processingTaskList.Count));

            // All the tasks under processing should ideally be from the same UpgradeDomain.
            // However in case the cluster topology has changed or repair tasks were manually created from some other entity resulting in multiple repair tasks with target nodes belonging to different UD's.
            // In that case we'll consider the upgrade domain of the first repair task among the list of repair tasks which are already under processing.
            foreach (var task in processingTaskList)
            {
                string udName = this.GetUpgradeDomainOfRepairTask(task, nodeList);

                if (string.IsNullOrEmpty(currentUpgradeDomain))
                {
                    currentUpgradeDomain = udName;
                }
                else if (currentUpgradeDomain != udName)
                {
                    ServiceEventSource.Current.ErrorMessage(
                        String.Format(
                            "Found repair task {0} under processing belonging to UpgradeDomains {1}, Expected only repair tasks from {2}. cluster topology might've changed",
                            task.TaskId, currentUpgradeDomain, udName));
                }
            }

            return currentUpgradeDomain;
        }

        /// <summary>
        /// Prepares the claimed repair tasks belonging to POS as per the TaskApprovalPolicy
        /// </summary>
        /// <returns>Task for the asynchronous operation</returns>
        internal async Task PrepareRepairTasks(CancellationToken cancellationToken)
        {
            NodeList nodeList = await this.fabricClient.QueryManager.GetNodeListAsync(null, null, this.DefaultTimeoutForOperation, cancellationToken);
            IList<RepairTask> claimedTaskList = await this.GetClaimedRepairTasks(nodeList, cancellationToken);

            switch (RmPolicy)
            {
                case TaskApprovalPolicy.NodeWise:
                {
                    RepairTaskList processingTaskList = await this.GetRepairTasksUnderProcessing(cancellationToken);
                    if (!processingTaskList.Any())
                    {
                        if (claimedTaskList.Any())
                        {
                            RepairTask oldestClaimedTask = claimedTaskList.Aggregate(
                                (curMin, task) => (task.CreatedTimestamp < curMin.CreatedTimestamp ? task : curMin));
                            ServiceEventSource.Current.VerboseMessage(
                                "Out of {0} claimed tasks, Oldest repair task = {0} with node = {1} will be prepared",
                                    claimedTaskList.Count, oldestClaimedTask.TaskId, oldestClaimedTask.Target);
                            this.StartPreparingRepairTask(oldestClaimedTask);
                        }
                    }
                    break;
                }

                case TaskApprovalPolicy.UpgradeDomainWise:
                {
                    string currentUpgradeDomain = await this.GetCurrentUpgradeDomainUnderProcessing(nodeList, cancellationToken);

                    ServiceEventSource.Current.VerboseMessage(String.Format("{0} repair tasks were found in claimed state", claimedTaskList.Count));
                    // Below line can be enabled for debugging
                    // rmHelper.PrintRepairTasks(claimedTaskList);

                    foreach (var claimedTask in claimedTaskList)
                    {
                        string udName = this.GetUpgradeDomainOfRepairTask(claimedTask, nodeList);

                        if (string.IsNullOrEmpty(currentUpgradeDomain))
                        {
                            currentUpgradeDomain = udName;
                        }

                        if (udName == currentUpgradeDomain)
                        {
                            this.StartPreparingRepairTask(claimedTask);
                        }
                    }
                    break;
                }

                default:
                {
                    string errorMessage = String.Format("Illegal RmPolicy found: {0}", RmPolicy);
                    ServiceEventSource.Current.ErrorMessage(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }
            }

        }

        

        /// <summary>
        /// Fetches all the repair tasks which are under execution and checks
        /// if any of them has exceeded the pre-specified execution timeout limit
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        internal async Task TimeoutRepairTasks(CancellationToken cancellationToken)
        {
            if (!this.ManageRepairTasksOnTimeout)
            {
                return;
            }

            // Get repair tasks which have been approved and are still under execution by POA
            RepairTaskList repairTasks = await this.fabricClient.RepairManager.GetRepairTaskListAsync(TaskIdPrefix,
                RepairTaskStateFilter.Approved | RepairTaskStateFilter.Executing, ExecutorName, this.DefaultTimeoutForOperation, cancellationToken);
            foreach (var task in repairTasks)
            {
                ExecutorDataForRmTask executorData =
                    SerializationUtility.Deserialize<ExecutorDataForRmTask>(task.ExecutorData);
                Debug.Assert(task.ApprovedTimestamp != null, "ApprovedTimestamp of an approved repair task can never be null");
                TimeSpan elapsedTime = DateTime.UtcNow.Subtract(task.ApprovedTimestamp.Value);
                if (elapsedTime > (TimeSpan.FromMinutes(executorData.ExecutorTimeoutInMinutes) + GraceTimeForNtService))
                {
                    switch (executorData.ExecutorSubState)
                    {
                        // These are special states where its best if NodeAgentNtService should move the repair task, just post warning in this case
                        case NodeAgentSfUtilityExitCodes.RestartRequested:
                        case NodeAgentSfUtilityExitCodes.RestartCompleted:
                        case NodeAgentSfUtilityExitCodes.InstallationCompleted:
                        {
                            string nodeName = this.GetNodeNameFromRepairTask(task);
                            string healthproperty = string.Format(
                                NodeTimeoutStatusFormat,
                                nodeName);
                            string healthDescription =
                                string.Format(
                                    "Installation timeout {0} minutes alloted to repair task {1}, node {2} is over, however since node is in post-installation phase, wait for few more minutes for operation to complete"
                                    + "In case problem persists, please check if recent installations of updates has caused any problem on the node",
                                    executorData.ExecutorTimeoutInMinutes,
                                    task.TaskId,
                                    nodeName);
                                ServiceEventSource.Current.ErrorMessage("Title = {0}, Description = {1}",healthproperty, healthDescription);
                            HealthManagerHelper.PostNodeHealthReport(this.fabricClient,
                                this.context.ServiceName,
                                healthproperty,
                                healthDescription,
                                HealthState.Warning,
                                60);
                            break;
                        }

                        default:
                        {
                            string nodeName = this.GetNodeNameFromRepairTask(task);
                            task.State = RepairTaskState.Restoring;
                            task.ResultStatus = RepairTaskResult.Cancelled;
                            ServiceEventSource.Current.ErrorMessage(
                                "Installation timeout {0} minutes alloted to task {1}, node {2} is over. Moving the repair task to restoring state to unblock installation on other nodes",
                                executorData.ExecutorTimeoutInMinutes,
                                task.TaskId,
                                nodeName);
                            await this.fabricClient.RepairManager.UpdateRepairExecutionStateAsync(task, this.DefaultTimeoutForOperation, cancellationToken);
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cancels a repair task based on its current state
        /// </summary>
        /// <param name="repairTask"><see cref="RepairTask"/> to be cancelled</param>
        /// <returns></returns>
        private async Task CancelRepairTask(RepairTask task)
        {
            switch (task.State)
            {
                case RepairTaskState.Restoring:
                case RepairTaskState.Completed:
                {
                    break;
                }

                case RepairTaskState.Created:
                case RepairTaskState.Claimed:
                case RepairTaskState.Preparing:
                {
                    await this.fabricClient.RepairManager.CancelRepairTaskAsync(task.TaskId, 0, true);
                    break;
                }

                case RepairTaskState.Approved:
                case RepairTaskState.Executing:
                {
                    task.State = RepairTaskState.Restoring;
                    task.ResultStatus = RepairTaskResult.Cancelled;
                    await this.fabricClient.RepairManager.UpdateRepairExecutionStateAsync(task);
                    break;
                }

                default:
                {
                    throw new Exception(string.Format("Repair task {0} in invalid state {1}", task.TaskId, task.State));
                }
            }
        }
    }
}