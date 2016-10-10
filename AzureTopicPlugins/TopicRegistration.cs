using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.ServiceBus.Messaging;
using Microsoft.Xrm.Sdk;
using CrmPluginServiceProvider;

namespace AzureTopicPlugins
{
    class TopicRegistration
    {
        public string TopicPath { get; set; } = null;
        public string SubscriptionName { get; set; } = null;
        public List<IPlugin> Plugins { get; set; } = new List<IPlugin>();

        private SubscriptionClient _topic;
        private SubscriptionClient _deadLetter;

        public void Initialize()
        {
            _topic = SubscriptionClient.CreateFromConnectionString( CrmPluginBrokerRole.ServiceBusConnectionString, TopicPath, SubscriptionName, ReceiveMode.PeekLock );
            _deadLetter = SubscriptionClient.CreateFromConnectionString( CrmPluginBrokerRole.ServiceBusConnectionString, TopicPath, SubscriptionName + "/$DeadLetterQueue", ReceiveMode.ReceiveAndDelete );
        }

        public async Task Run( OnMessageOptions topicOptions )
        {
            while ( true )
            {
                var message = _deadLetter.Receive();

                if ( message == null )
                    break;

                var context = message.GetBody<RemoteExecutionContext>( CrmPluginBrokerRole.ExecutionContextSerializer );

                try
                {
                    await PassContextToPlugins( context );
                }
                catch ( Exception )
                {
                    Trace.TraceError( "Failing dead-letter message: {0}({1}) {2}",
                        context.PrimaryEntityName,
                        context.PrimaryEntityId,
                        context.MessageName );

                    throw;
                }
            }

            _deadLetter.Close();
            _topic.OnMessageAsync( OnTopicMessageAsync, topicOptions );
        }

        public void Close()
        {
            _topic.Close();
        }

        private async Task OnTopicMessageAsync( BrokeredMessage message )
        {
            try
            {
                // Process the message
                Trace.TraceInformation( String.Format( "Processing Service Bus message: {0}", message.SequenceNumber ) );
                var executionContext = message.GetBody<RemoteExecutionContext>( CrmPluginBrokerRole.ExecutionContextSerializer );

                await PassContextToPlugins( executionContext );

                Trace.TraceInformation( String.Format( "Successfully processed Service Bus message: {0}", message.SequenceNumber ) );
                message.Complete();
            }
            catch ( Exception ex )
            {
                Trace.TraceError( String.Format( "Failed processing Service Bus message: {0}", ex.Message ) );
                message.Abandon();

                throw;
            }
        }

        private async Task PassContextToPlugins( RemoteExecutionContext executionContext )
        {
            foreach ( var plugin in Plugins )
            {
                var serviceProvider = new CrmServiceProvider( CrmPluginBrokerRole.Crm, executionContext );

                try
                {
                    await Task.Run( () => plugin.Execute( serviceProvider ) );
                }
                catch ( Exception )
                {
                    Trace.TraceError( String.Format( "Failed processing plugin: {0}", plugin.GetType() ) );

                    throw;
                }
            }
        }
    }
}
