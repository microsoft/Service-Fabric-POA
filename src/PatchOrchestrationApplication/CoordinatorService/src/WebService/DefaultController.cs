// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService.WebService
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;

    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.PatchOrchestration.Common;

    /// <summary>
    /// Default controller for the Coordinator Service's Owin webservice
    /// </summary>
    [RoutePrefix("v1")]
    public class DefaultController : ApiController
    {
        private readonly IReliableStateManager stateManager;

        public DefaultController(IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        /// <summary>
        /// Gets the operation results from CoordinatorService's resultstore
        /// </summary>
        /// <returns>List of <see cref="WindowsUpdateOperationResult"/></returns>
        [HttpGet]
        [Route("GetWindowsUpdateResults/{operationType?}")]
        public async Task<IList<WindowsUpdateNodeResults>> GetWuResults(WindowsUpdateOperationType operationType = WindowsUpdateOperationType.Installation)
        {
            var resultsStore = await this.stateManager.GetOrAddAsync<IReliableQueue<WindowsUpdateOperationResult>>(CoordinatorService.ResultsStoreName);
            using (var tx = this.stateManager.CreateTransaction())
            {
                var resultsEnumerator = (await resultsStore.CreateEnumerableAsync(tx)).GetAsyncEnumerator();

                IDictionary<string, WindowsUpdateNodeResults> bucketizedResults = new Dictionary<string, WindowsUpdateNodeResults>();
                while (await resultsEnumerator.MoveNextAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (resultsEnumerator.Current.OperationType == operationType)
                    {
                        if (!bucketizedResults.ContainsKey(resultsEnumerator.Current.NodeName))
                        {
                            bucketizedResults[resultsEnumerator.Current.NodeName] = new WindowsUpdateNodeResults(resultsEnumerator.Current.NodeName);
                        }

                        bucketizedResults[resultsEnumerator.Current.NodeName].WindowsUpdateOperationResults.Add(resultsEnumerator.Current);
                    }
                }

                IList<WindowsUpdateNodeResults> nodeResults = bucketizedResults.Values.ToList();
                ServiceEventSource.Current.InfoMessage("Returned {0} node results from queue {1}", nodeResults.Count, CoordinatorService.ResultsStoreName);
                return nodeResults;
            }
        }
    }
}
