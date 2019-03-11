// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.CoordinatorService.WebService
{
    using System.Net.Http.Formatting;
    using System.Web.Http;
    using global::Owin;
    using Microsoft.ServiceFabric.Data;
    using Newtonsoft.Json;

    public class Startup : IWebServiceAppBuilder
    {
        private readonly IReliableStateManager stateManager;

        public Startup(IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        /// <summary>
        /// Configures the output of the webservice
        /// </summary>
        /// <param name="formatters">reference to the <see cref="MediaTypeFormatterCollection"/></param>
        private static void ConfigureFormatters(MediaTypeFormatterCollection formatters)
        {
            formatters.JsonFormatter.SerializerSettings.Formatting = Formatting.Indented;
        }

        /// <summary>
        /// Configure the properties of WebService App here
        /// </summary>
        /// <param name="appBuilder">reference to <see cref="IAppBuilder"/></param>
        public void ConfigureApp(IAppBuilder appBuilder)
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 256;
            HttpConfiguration config = new HttpConfiguration();
            ConfigureFormatters(config.Formatters);
            UnityConfig.RegisterComponents(config, this.stateManager);
            config.MapHttpAttributeRoutes();
            appBuilder.UseWebApi(config);
        }
    }
}
