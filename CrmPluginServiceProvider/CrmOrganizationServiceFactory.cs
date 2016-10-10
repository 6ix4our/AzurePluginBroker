using System;
using Microsoft.Xrm.Client;
using Microsoft.Xrm.Client.Services;
using Microsoft.Xrm.Sdk;

namespace CrmPluginServiceProvider
{
    public class CrmOrganizationServiceFactory : IOrganizationServiceFactory
    {
        private readonly CrmConnection _crmConnection = null;

        public IOrganizationService CreateOrganizationService( Guid? userId )
        {
            var service = new OrganizationService( _crmConnection );

            return service;
        }

        public CrmOrganizationServiceFactory( CrmConnection crmConnection )
        {
            _crmConnection = crmConnection;
        }
    }
}
