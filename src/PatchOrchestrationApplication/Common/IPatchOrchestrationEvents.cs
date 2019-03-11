// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.Common
{
    /// <summary>
    /// Interface for common events which should be implemented by POA modules
    /// </summary>
    public interface IPatchOrchestrationEvents
    {
        void InfoMessage(string message, params object[] args);
        void ErrorMessage(string message, params object[] args);
        void VerboseMessage(string message, params object[] args);
        void InfoMessage(string message);
        void ErrorMessage(string message);
        void VerboseMessage(string message);
    }
}