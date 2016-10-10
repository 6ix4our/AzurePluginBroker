namespace AzureTopicPlugins
{
    internal static class Constants
    {
        public const string AppAuthClientIdSetting = "App.Auth.ServicePrincipalId";
        public const string AppAuthCertThumbprintSetting = "App.Auth.CertificateThumbprint";
        public const string AppSecretCacheDefaultTimeSpanSetting = "App.Cache.DefaultTimeSpan";

        /// <summary>
        /// Settings from Azure Key Vault secrets.
        /// </summary>
        public const string AppServiceBusConnectionStringSecret = "Microsoft.ServiceBus.ConnectionString";
        public const string AppCrmConnectionStringSecret = "Microsoft.DynamicsCrm.ConnectionString";
    }
}
