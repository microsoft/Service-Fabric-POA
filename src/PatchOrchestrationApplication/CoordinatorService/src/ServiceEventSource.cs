// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using System.Threading.Tasks;

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using TelemetryLib;

    [EventSource(Name = "POA-CoordinatorService", Guid = "24afa313-0d3b-4c7c-b485-1047fd964b60")]
    internal sealed class ServiceEventSource : EventSource, ITelemetryEventSource
    {
        public static readonly ServiceEventSource Current = new ServiceEventSource();

        static ServiceEventSource()
        {
            // A workaround for the problem where ETW activities do not get tracked until Tasks infrastructure is initialized.
            // This problem will be fixed in .NET Framework 4.6.2.
            Task.Run(() => { });
        }

        // Instance constructor is private to enforce singleton semantics
        private ServiceEventSource() : base() { }

        #region Events
        // Define an instance method for each event you want to record and apply an [Event] attribute to it.
        // The method name is the name of the event.
        // Pass any parameters you want to record with the event (only primitive integer types, DateTime, Guid & string are allowed).
        // Each event method implementation should check whether the event source is enabled, and if it is, call WriteEvent() method to raise the event.
        // The number and types of arguments passed to every event method must exactly match what is passed to WriteEvent().
        // Put [NonEvent] attribute on all methods that do not define an event.
        // For more information see https://msdn.microsoft.com/en-us/library/system.diagnostics.tracing.eventsource.aspx

        [NonEvent]
        public void VerboseMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.VerboseMessage(finalMessage);
            }
        }

        [NonEvent]
        public void InfoMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.InfoMessage(finalMessage);
            }
        }

        [NonEvent]
        public void ErrorMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.ErrorMessage(finalMessage);
            }
        }

        private const int ErrorMessageEventId = 7;
        [Event(ErrorMessageEventId, Level = EventLevel.Error, Message = "{0}")]
        public void ErrorMessage(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(ErrorMessageEventId, message);
            }
        }

        private const int InfoMessageEventId = 8;
        [Event(InfoMessageEventId, Level = EventLevel.Informational, Message = "{0}")]
        public void InfoMessage(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(InfoMessageEventId, message);
            }
        }

        private const int VerboseMessageEventId = 9;
        [Event(VerboseMessageEventId, Level = EventLevel.Verbose, Message = "{0}")]
        public void VerboseMessage(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(VerboseMessageEventId, message);
            }
        }

        private const int PrintRepairTaskEventId = 10;
        [Event(PrintRepairTaskEventId, Level = EventLevel.Verbose, Message = "TasksID = {0}, State = {1}, Action = {2}, Executor = {3}, Description = {4}, ExecutorData = {5}, Target = {6}")]
        public void PrintRepairTasks(string taskId, string state, string action, string executor, string description, string executordata, string target)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(PrintRepairTaskEventId, taskId, state, action, executor, description, executordata, target);
            }
        }

        private const int PatchInstallationTelemetryEventId = 11;
        [Event(PatchInstallationTelemetryEventId, Level = EventLevel.Verbose, 
            Message = "TelemetryEvent : PatchInstallationEvent clusterId = {0}, tentantId = {1}, clusterType = {2}" +
            "nodeName = {3}, updateFrequency = {4}, updateQuery = {5}, approvalPolicy = {6}, applicationVersion = {7}, totalInstallations = {8}" +
            "successfulInstallations = {9}, operationResult = {10}")]
        public void PatchInstallationTelemetryEvent(string clusterId,
            string tenantId,
            string clusterType,
            string nodeName,
            string updateFrequency,
            string updateQuery,
            string approvalPolicy,
            string applicationVersion,
            double totalInstallations,
            double successfulInstallations,
            string operationResult)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(PatchInstallationTelemetryEventId, clusterId, tenantId, clusterType, nodeName, updateFrequency, updateQuery, approvalPolicy, applicationVersion, totalInstallations,
                    successfulInstallations, operationResult);
            }
        }
        #endregion
    }
}
