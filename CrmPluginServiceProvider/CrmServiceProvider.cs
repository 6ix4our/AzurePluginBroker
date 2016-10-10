using System;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Sdk;

namespace CrmPluginServiceProvider
{
    public class CrmServiceProvider : IServiceProvider
    {
        private readonly CrmConnection _crmConnection = null;
        private readonly IPluginExecutionContext _context = null;

        private ITracingService _tracing = null;

        public CrmServiceProvider( CrmConnection crmConnection, IPluginExecutionContext context )
        {
            if ( crmConnection == null )
                throw new ArgumentNullException( "crmConnection" );

            if ( context == null )
                throw new ArgumentNullException( "context" );

            _crmConnection = crmConnection;
            _context = context;
        }

        public object GetService( Type serviceType )
        {
            if ( serviceType == typeof( IPluginExecutionContext ) )
            {
                return _context;
            }
            if ( serviceType == typeof( ITracingService ) )
            {
                if ( _tracing == null )
                    _tracing = new AzureTracingService();

                return _tracing;
            }
            else if ( serviceType == typeof( IOrganizationServiceFactory ) )
            {
                return new CrmOrganizationServiceFactory( _crmConnection );
            }
            else
                throw new ArgumentException( String.Format(
                    "The requested type, {0} is not available with this service provider.",
                    serviceType.FullName ) );
        }
    }
}
