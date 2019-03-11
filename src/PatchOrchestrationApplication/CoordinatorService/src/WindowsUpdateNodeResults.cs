// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using Common;

    /// <summary>
    /// Class for storing windows update operation results for a node
    /// </summary>
    [DataContract]
    public class WindowsUpdateNodeResults
    {
        /// <summary>
        /// Name of the node
        /// </summary>
        [DataMember]
        public string NodeName { get; private set; }

        /// <summary>
        /// List of Windows Update operations which were done on the node
        /// </summary>
        [DataMember]
        public IList<WindowsUpdateOperationResult> WindowsUpdateOperationResults { get; private set; }

        public WindowsUpdateNodeResults(string nodeName)
        {
            this.NodeName = nodeName;
            this.WindowsUpdateOperationResults = new List<WindowsUpdateOperationResult>();
        }
    }
}
