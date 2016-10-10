using System;
using Microsoft.Xrm.Sdk;

namespace CrmPluginServiceProvider
{
    public class AzureTracingService : ITracingService
    {
        public void Trace( string format, params object[] args )
        {
            System.Diagnostics.Trace.TraceInformation( String.Format( format, args ) );
        }
    }
}
