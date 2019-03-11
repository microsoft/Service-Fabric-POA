// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
 
namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility.Helpers
{
    using System;
    using System.Fabric;
    using System.Fabric.Repair;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.PatchOrchestration.Common;

    /// <summary>
    /// Helper class used for creating, monitoring and updating <see cref="System.Fabric.Repair.RepairTask"/>
    /// </summary>
    internal class RepairManagerHelper
    {
        /// <summary>
        /// Prefix for all the repair tasks created for Patch Orchestration Service
        /// </summary>
        private const string TaskIdPrefix = "POS";

        /// <summary>
        /// Name of executor for repair tasks
        /// </summary>
        private const string ExecutorName = "POS";

        /// <summary>
        /// Type of repair action to be performed
        /// </summary>
        private const string RepairAction = "POS.OSUpgrade";

        /// <summary>
        /// Updates the repair task for current node
        /// </summary>
        /// <param name="fc">Fabric client object used for carrying out service fabric client requests</param>
        /// <param name="nodeName">Nodename against which repair task needs to be updated</param>
        /// <param name="taskState">State of the repair task <see cref="RepairTaskState"/></param>
        /// <param name="taskResultStatus">Result status for last completed operation by RE</param>
        /// <param name="resultDetails">Result details for last completed operation by RE</param>
        /// <param name="executorState">Substate of repair executor</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation</returns>
        internal static async Task<NodeAgentSfUtilityExitCodes> UpdateRepairTask(FabricClient fc, string nodeName,
            RepairTaskState taskState,
            RepairTaskResult taskResultStatus, string resultDetails, NodeAgentSfUtilityExitCodes executorState,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            return await
                UpdateRepairTask(fc, nodeName, taskState, taskResultStatus, resultDetails, executorState,
                    timeout,
                    cancellationToken, null);
        }

        /// <summary>
        /// Updates the repair task for current node
        /// </summary>
        /// <param name="fc">Fabric client object used for carrying out service fabric client requests</param>
        /// <param name="nodeName">Nodename against which repair task needs to be updated</param>
        /// <param name="taskState">State of the repair task <see cref="RepairTaskState"/></param>
        /// <param name="taskResultStatus">Result status for last completed operation by RE</param>
        /// <param name="resultDetails">Result details for last completed operation by RE</param>
        /// <param name="executorState">Substate of repair executor</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <param name="restartRequesteDateTime">Timestamp at which restart was requested</param>
        /// <returns>A Task representing the asnyc operation</returns>
        internal static async Task<NodeAgentSfUtilityExitCodes> UpdateRepairTask(FabricClient fc, string nodeName,
            RepairTaskState taskState,
            RepairTaskResult taskResultStatus, string resultDetails, NodeAgentSfUtilityExitCodes executorState,
            TimeSpan timeout,
            CancellationToken cancellationToken, DateTime? restartRequesteDateTime)
        {
            try
            {
                var repairTask = await GetRepairTaskForNode(fc, nodeName, timeout, cancellationToken);
                if (null != repairTask)
                {
                    await
                        UpdateRepairTask(fc, repairTask, taskState, taskResultStatus, resultDetails, executorState,
                            timeout,
                            cancellationToken, restartRequesteDateTime);
                    return NodeAgentSfUtilityExitCodes.Success;
                }
                else
                {
                    // If repair task does not exist we're in a unknown state.
                    ServiceEventSource.Current.ErrorMessage(
                        String.Format("RepairManagerHelper.UpdateRepairTask failed. No repair task found for this node"));
                    return NodeAgentSfUtilityExitCodes.RepairTaskInvalidState;
                }
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("RepairManagerHelper.UpdateRepairTask failed. Exception details {0}", e));
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
        /// Updates the repair task for current node
        /// </summary>
        /// <param name="fc">Fabric client object used for carrying out service fabric client requests</param>
        /// <param name="task">Repair task which needs to be updated</param>
        /// <param name="taskState">State of the repair task <see cref="RepairTaskState"/></param>
        /// <param name="taskResultStatus">Result status for last completed operation by RE</param>
        /// <param name="resultDetails">Result details for last completed operation by RE</param>
        /// <param name="executorState">Substate of repair executor</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation</returns>
        private static async Task UpdateRepairTask(FabricClient fc, RepairTask task, RepairTaskState taskState,
            RepairTaskResult taskResultStatus, string resultDetails, NodeAgentSfUtilityExitCodes executorState, TimeSpan timeout,
            CancellationToken cancellationToken, DateTime? restartRequesteDateTime)
        {
            // Do the actual work before mark the task as Executing.
            task.State = taskState;
            task.ResultStatus = taskResultStatus;
            task.ResultDetails = resultDetails;
            ExecutorDataForRmTask executorData =
                SerializationUtility.Deserialize<ExecutorDataForRmTask>(task.ExecutorData);
            executorData.ExecutorSubState = executorState;
            if (restartRequesteDateTime.HasValue)
            {
                executorData.RestartRequestedTime = restartRequesteDateTime.Value;
            }

            task.ExecutorData = SerializationUtility.Serialize(executorData);
            await fc.RepairManager.UpdateRepairExecutionStateAsync(task, timeout, cancellationToken);
        }

        /// <summary>
        /// Creates Repair task for a node with executor set as Patch Orchestration Service
        /// </summary>
        /// <param name="fc">Fabric client object used for carrying out service fabric client requests</param>
        /// <param name="nodeName">Node name for which repair task needs to be created</param>
        /// <param name="taskDescription">Description of repair task which needs to be created</param>
        /// <param name="resultDetails">Result details for the completed operation to make the repair task verbose</param>
        /// <param name="executorData">Executor data associated with the repair task</param>
        /// <param name="timeout">Timeout for the async operation</param>
        /// <param name="cancellationToken">The cancellation token to cancel the async operation</param>
        /// <returns>A Task representing the asnyc operation, result of task would be <see cref="NodeAgentSfUtilityExitCodes"/></returns>
        internal static async Task<NodeAgentSfUtilityExitCodes> CreateRepairTaskForNode(FabricClient fc, string nodeName,
            string taskDescription, string resultDetails, ExecutorDataForRmTask executorData, TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            string taskIdPrefix = string.Format("{0}_{1}", TaskIdPrefix, nodeName);
            string taskId = string.Format("{0}_{1}", taskIdPrefix, Guid.NewGuid());
            ClusterRepairTask repairTask = new ClusterRepairTask(taskId, RepairAction);
            repairTask.Description = taskDescription;
            repairTask.State = RepairTaskState.Claimed;
            repairTask.Executor = ExecutorName;
            repairTask.ExecutorData = SerializationUtility.Serialize(executorData);
            repairTask.Target = new NodeRepairTargetDescription(nodeName);
            repairTask.ResultDetails = resultDetails;

            try
            {
                await fc.RepairManager.CreateRepairTaskAsync(repairTask, timeout, cancellationToken);
                return NodeAgentSfUtilityExitCodes.Success;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ErrorMessage(
                    String.Format("RepairManagerHelper.CreateRepairTaskForNode failed. Exception details {0}", e));
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
        /// Gets repair task for the node specified
        /// </summary>
        /// <param name="fc">Fabric client to carry out ServiceFabric operations</param>
        /// <param name="nodeName">Node for which repair task is queried</param>
        /// <param name="timeout">Timeout for operation</param>
        /// <param name="cancellationToken">Cancellation token to cancel this async operation</param>
        /// <param name="taskFilter">Optional parameter to specify filter when searching repair tasks for current node</param>
        /// <returns>A Task representing the asnyc operation, result of task would be <see cref="RepairTask"/></returns>
        internal static async Task<RepairTask> GetRepairTaskForNode(FabricClient fc, string nodeName, TimeSpan timeout,
            CancellationToken cancellationToken, RepairTaskStateFilter taskFilter = RepairTaskStateFilter.Active)
        {
            var taskIdPrefix = String.Format("{0}_{1}_", TaskIdPrefix, nodeName);
            var repairTasks = await fc.RepairManager.GetRepairTaskListAsync(taskIdPrefix,
                taskFilter,
                ExecutorName, timeout, cancellationToken);

            ServiceEventSource.Current.InfoMessage("{0} repair tasks found for node {1}", repairTasks.Count, nodeName);
            if (repairTasks.Count > 0)
            {
                RepairTask oldestActiveTask = repairTasks.Aggregate(
                    (curMin, task) => (task.CreatedTimestamp < curMin.CreatedTimestamp ? task : curMin));
                ServiceEventSource.Current.VerboseMessage(String.Format("Oldest active repair task = {0} found in {1} state",
                    oldestActiveTask.TaskId, oldestActiveTask.State));
                return oldestActiveTask;
            }

            return null;
        }
    }
}
