    // Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentService
{
    using System.Diagnostics.Tracing;
    using System.Threading.Tasks;

    [EventSource(Name = "POA-NodeAgentService", Guid = "e39b723c-590c-4090-abb0-11e3e6616346")]
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
        public void InfoMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                InfoMessage(finalMessage);
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

        [NonEvent]
        public void ErrorMessage(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.ErrorMessage(finalMessage);
            }
        }

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

        private const int VerboseMessageEventId = 2;
        [Event(VerboseMessageEventId, Level = EventLevel.Verbose, Message = "{0}")]
        public void VerboseMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(VerboseMessageEventId, message);
            }
        }

        private const int ErrorMessageEventId = 3;
        [Event(ErrorMessageEventId, Level = EventLevel.Error, Message = "{0}")]
        public void ErrorMessage(string message)
        {
            if (this.IsEnabled())
            {
                WriteEvent(ErrorMessageEventId, message);
            }
        }
        #endregion
    }
}
