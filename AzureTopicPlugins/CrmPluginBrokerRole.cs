using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Sdk;
using AzureServiceConfigurationManager;

namespace AzureTopicPlugins
{
    public class CrmPluginBrokerRole : RoleEntryPoint
    {
        internal static readonly DataContractJsonSerializer ExecutionContextSerializer = new DataContractJsonSerializer( typeof( RemoteExecutionContext ) );

        internal static CrmConnection Crm
        {
            get { return CrmConnection.Parse( ServiceConfigurationManager.GetSetting( Constants.AppCrmConnectionStringSecret ) ); }
        }

        internal static String ServiceBusConnectionString
        {
            get { return ServiceConfigurationManager.GetSetting( Constants.AppServiceBusConnectionStringSecret ); }
        }

        private const int DefaultSecretTimeoutInMinutes = 20;

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent _runCompleteEvent = new ManualResetEvent( false );

        /// <summary>
        /// Each registration uses the same Azure Service Bus connection string and SAS key to read 
        /// from the configured TopicPath and SubscriptionName.  The plugins defined within the Plugins
        /// collection are each called with every received message, in the order listed.
        /// </summary>
        private readonly TopicRegistration[] _topicRegistrations = {
            new TopicRegistration()
            {
                TopicPath = "",
                SubscriptionName = "",
                Plugins = {

                }
            }
        };

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            Trace.TraceInformation( "CrmPluginBrokerRole is starting." );

            /// Initialize the ServiceConfigurationManager to retrieve secrets held by an Azure Key Vault
            ServiceConfigurationManager.InitializeSecrets( Constants.AppAuthClientIdSetting, Constants.AppAuthCertThumbprintSetting, TimeSpan.FromMinutes( DefaultSecretTimeoutInMinutes ) );

            foreach ( var topicRegistration in _topicRegistrations )
            {
                topicRegistration.Initialize();
            }

            var started = base.OnStart();

            Trace.TraceInformation( "CrmPluginBrokerRole has been started." );

            return started;
        }

        public override void Run()
        {
            Trace.TraceInformation( "CrmPluginBrokerRole is entering main process." );

            try
            {
                RunAsync( _cancellationTokenSource.Token ).Wait();
            }
            finally
            {
                _runCompleteEvent.Set();
            }
        }

        public async Task RunAsync( CancellationToken cancellationToken )
        {
            Trace.TraceInformation( "Starting processing of messages." );

            // Initiates the message pump and callback is invoked for each message that is received, calling close on the client will stop the pump.
            var topicOptions = new OnMessageOptions()
            {
                AutoComplete = false,
                MaxConcurrentCalls = 1,
                AutoRenewTimeout = TimeSpan.FromHours( 2 )
            };

            topicOptions.ExceptionReceived += MessageExceptionReceived;

            try
            {
                Task.WaitAll( ( from topicRegistration in _topicRegistrations
                                select topicRegistration.Run( topicOptions ) ).ToArray(), cancellationToken );
            }
            catch ( AggregateException ex )
            {
                throw ex.Flatten().InnerException;
            }
            catch ( Exception )
            {
                throw;
            }

            while ( !cancellationToken.IsCancellationRequested )
            {
                Trace.TraceInformation( "CrmPluginBrokerRole is running." );
                await Task.Delay( 6000, cancellationToken );
            }
        }

        public override void OnStop()
        {
            Trace.TraceInformation( "CrmPluginBrokerRole is stopping." );

            // Close the connections to Service Bus
            foreach ( var topicRegistration in _topicRegistrations )
                topicRegistration.Close();

            _cancellationTokenSource.Cancel();

            base.OnStop();

            Trace.TraceInformation( "CrmPluginBrokerRole has stopped." );
        }

        private void MessageExceptionReceived( object sender, ExceptionReceivedEventArgs e )
        {
            Trace.TraceError( e.Exception.Message );
        }
    }
}
