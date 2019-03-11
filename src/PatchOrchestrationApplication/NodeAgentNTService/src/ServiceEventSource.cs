// Copyright (c) Microsoft Corporation. All rights reserved. 
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService
{
    using System;
    using System.Diagnostics.Tracing;
    using System.Threading.Tasks;

    [EventSource(Name = "POA-NodeAgentNTService", Guid = "fc0028ff-bfdc-499f-80dc-ed922c52c5e9")]
    internal sealed class ServiceEventSource : EventSource
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

        [NonEvent]
        public void Message(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                Message(finalMessage);
            }
        }

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

        private const int MessageEventId = 1;
        [Event(MessageEventId, Level = EventLevel.Informational, Message = "{0}")]
        public void Message(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(MessageEventId, message);
            }
        }

        private const int ErrorMessageEventId = 7;
        [Event(ErrorMessageEventId, Level = EventLevel.Error, Message = "{0}")]
        public void ErrorMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(ErrorMessageEventId, message);
            }
        }

        private const int InfoMessageEventId = 8;
        [Event(InfoMessageEventId, Level = EventLevel.Informational, Message = "{0}")]
        public void InfoMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(InfoMessageEventId, message);
            }
        }

        private const int WarningMessageEventId = 9;
        [Event(WarningMessageEventId, Level = EventLevel.Warning, Message = "{0}")]
        public void WarningMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(WarningMessageEventId, message);
            }
        }

        private const int VerboseMessageEventId = 10;
        [Event(VerboseMessageEventId, Level = EventLevel.Verbose, Message = "{0}")]
        public void VerboseMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(VerboseMessageEventId, message);
            }
        }
    }
}
