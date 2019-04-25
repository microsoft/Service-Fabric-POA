// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.ServiceFabric.PatchOrchestration.Common;
using System;
using System.Threading.Tasks;

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService
{
    using System.Threading;

    /// <summary>
    /// This interface contains RPC methods which are to be used by NodeAgentSFUtility
    /// Its implemented by CoordinatorService
    /// </summary>
    public interface IDataInterface : IService
    {
        /// <summary>
        /// Updates the result of Search and download or installation operation for a node
        /// </summary>
        /// <param name="operationResult"><see cref="WindowsUpdateOperationResult"/> of the operation</param>
        /// <param name="timeout">timeout for this async operation</param>
        /// <param name="cancellationToken">Cancellation token to cancel the async operation</param>
        Task UpdateWuOperationResult(WindowsUpdateOperationResult operationResult, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
