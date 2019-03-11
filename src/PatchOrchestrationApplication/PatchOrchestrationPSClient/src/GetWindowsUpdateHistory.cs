// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.PowershellClient
{
    using System;
    using System.Management.Automation;
    using System.Net.Http;
    using System.Net.Http.Headers;

    [Cmdlet(VerbsCommon.Get, "ServiceFabricWindowsUpdateHistory")]
    public class GetWindowsUpdateHistory : PSCmdlet
    {
        [Parameter(Mandatory = false, HelpMessage = "Provide the Uri of Patch Orchestation Application. Default is PatchOrchestrationApplication")]
        public Uri ApplicationUri { get; set; } = new Uri("PatchOrchestrationApplication");

        [Parameter(Mandatory = false, HelpMessage = "Provide the server url for the Service Fabric cluster. Default is http://localhost. Eg : http://goldy3.eastus.cloudapp.azure.com")]
        public string ClusterUrl { get; set; } = "http://localhost";


        [Parameter(Mandatory = false, HelpMessage = 
            "Provide the port number for RESTEndpoint of CoordinatorService. Default is 12345. Refer to ServiceManifest of CoordinatorService for checking the port ")]
        public string ApplicationPort { get; set; } = "12345";

        [Parameter(Mandatory = false, HelpMessage =
            "Provide the port number for ReverseProxy of the cluster. Default is 19008. Refer to ClusterManifest to check this port")]
        public string ReverseProxyPort { get; set; } = "19008";

        protected async override void ProcessRecord()
        {
            string restUrl = string.Format("{0}:{1}/{2}/{3}", ClusterUrl, ApplicationPort, "PatchOrchestrationService", "GetWindowsUpdateResults");
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri(restUrl);

            Console.WriteLine("Getting result from {0}", restUrl);

            // Add an Accept header for JSON format.
            client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

            // List data response.
            HttpResponseMessage response = client.GetAsync("").Result;
            if (response.IsSuccessStatusCode)
            {
                String data = await response.Content.ReadAsStringAsync();
                this.WriteObject(data);
            }
            else
            {
                Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
            }
        }
    }

}