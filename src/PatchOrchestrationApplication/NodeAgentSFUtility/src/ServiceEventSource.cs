// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentSFUtility
{
    using System.Diagnostics.Tracing;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.PatchOrchestration.Common;

    /// <summary>
    /// EventSource class used for logging messages from this executable
    /// </summary>
    [EventSource(Name = "POA-NodeAgentSFUtility", Guid = "05dc046c-60e9-4ef7-965e-91660adffa68")]
    internal sealed class ServiceEventSource : EventSource, IPatchOrchestrationEvents
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

        #region NonEvents

        [NonEvent]
        public void InfoMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                InfoMessage(finalMessage);
            }
        }

        [NonEvent]
        public void ErrorMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                ErrorMessage(finalMessage);
            }
        }

        [NonEvent]
        public void VerboseMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                VerboseMessage(finalMessage);
            }
        }

        #endregion

        #region Events
        private const int InfoMessageEventId = 1;
        [Event(InfoMessageEventId, Level = EventLevel.Informational, Message = "{0}")]
        public void InfoMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(InfoMessageEventId, message);
            }
        }

        private const int ErrorMessageEventId = 2;
        [Event(ErrorMessageEventId, Level = EventLevel.Error, Message = "{0}")]
        public void ErrorMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(ErrorMessageEventId, message);
            }
        }

        private const int VerboseMessageEventId = 3;
        [Event(VerboseMessageEventId, Level = EventLevel.Verbose, Message = "{0}")]
        public void VerboseMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(VerboseMessageEventId, message);
            }
        }
        #endregion
    }
}