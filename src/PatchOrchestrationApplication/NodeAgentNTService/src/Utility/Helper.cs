// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    class Helper
    {
        public bool WaitOnTask(Task task, CancellationToken cancellationToken)
        {
            try
            {
                task.Wait(cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public bool WaitOnTask(Task task, int millisecondsTimeout, CancellationToken cancellationToken)
        {
            try
            {
                return task.Wait(millisecondsTimeout, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
