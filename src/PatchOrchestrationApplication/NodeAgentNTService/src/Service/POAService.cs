// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Service
{
    using System;
    using System.ServiceProcess;
    using Manager;
    using System.Threading;
    using Utility;

    /// <summary>
    /// This is a Windows Service for managing search, download and installation of updates.
    /// This service will be running with super user permissions.
    /// </summary>
    public class POAService : ServiceBase
    {
        private readonly ServiceEventSource _eventSource = ServiceEventSource.Current;
        private readonly TimerManager _timerManager;
        private readonly CancellationTokenSource _tokenSource;

        /// <summary>
        /// Initializes Windows Service.
        /// </summary>
        public POAService(string nodeName, string applicationName)
        {
            _eventSource.InfoMessage("POSNodeSvc initalizing for Node {0}, Application {1}", nodeName, applicationName);
            this.ServiceName = "POSNodeSvc";
            Uri applicationUri = new Uri(applicationName);

            this._tokenSource = new CancellationTokenSource();
            SettingsManager settingsManager = new SettingsManager();
            var nodeAgentSfUtility = new NodeAgentSfUtility(nodeName, applicationUri, settingsManager,
                this._tokenSource.Token);
            OperationResultFormatter operationResultFormatter =
                new OperationResultFormatter(nodeName, settingsManager.GetSettings());

            WindowsUpdateManager windowsUpdateManager = new WindowsUpdateManager(operationResultFormatter,
                nodeAgentSfUtility, settingsManager, this._tokenSource.Token);

            // timer manager will take care of stopping and disposing windows update manager also.
            this._timerManager = new TimerManager(nodeAgentSfUtility, settingsManager, windowsUpdateManager,
                this._tokenSource.Token);
        }

        /// <summary>
        /// This function is called on start of windows service.
        /// </summary>
        /// <param name="args"></param>
        protected override void OnStart(string[] args)
        {
            //System.Diagnostics.Debugger.Launch();
            try
            {
                _eventSource.InfoMessage("OnStart called for {0}", this.ServiceName);

                var worker = new Thread(() =>
                {
                    StartLogman();
                    this._timerManager.StartTimer();
                });
                worker.IsBackground = false;
                worker.Start();
                _eventSource.InfoMessage("OnStart finished for {0}", this.ServiceName);
            }
            catch (Exception e)
            {
                _eventSource.InfoMessage("OnStart for {0} failed with exception : {1}", this.ServiceName, e);
            }
        }

        private void StartLogman()
        {
            ProcessExecutor processExecutor = new ProcessExecutor("logman", "start PatchOrchestrationServiceTraces");
            int exitCode = processExecutor.Execute();
            if (exitCode != 0)
            {
                _eventSource.InfoMessage(
                    "Not able to start logman session - 'PatchOrchestrationServiceTraces'. Exit code: {0}", exitCode);
            }
            else
            {
                _eventSource.InfoMessage("Logman session 'PatchOrchestrationServiceTraces' started.");
            }
        }

        /// <summary>
        /// This is called when Windows Service stops.
        /// </summary>
        protected override void OnStop()
        {
            try
            {
                _eventSource.InfoMessage("OnStop called for {0}", this.ServiceName);
                this._tokenSource.Cancel();
                this._timerManager.StopTimer();
                _eventSource.InfoMessage("OnStop finished for {0}", this.ServiceName);
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("OnStop for {0} failed with exception : {1}", this.ServiceName, e);
            }
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                _eventSource.InfoMessage("Dispose called for {0}", this.ServiceName);
                this._tokenSource.Dispose();
                this._timerManager.DisposeTimer();
                _eventSource.InfoMessage("Dispose finished for {0}", this.ServiceName);
            }
            catch (Exception e)
            {
                _eventSource.ErrorMessage("Dispose for {0} failed with exception : {1}", this.ServiceName, e);
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}