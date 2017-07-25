// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RoomManager

{
    using System.Fabric;
    using System.Fabric.Description;

    /// <summary>
    /// Runs to establish configuration for this service. Does this vis-a-vis ApplicationPackageRoot/ApplicationManifest.xml
    /// </summary>
    public class ConfigSettings
    {
        /// <summary>
        /// Configures to the current context.
        /// </summary>
        /// <param name="context"></param>
        public ConfigSettings(StatefulServiceContext context)
        {
            context.CodePackageActivationContext.ConfigurationPackageModifiedEvent += this.CodePackageActivationContext_ConfigurationPackageModifiedEvent;
            this.UpdateConfigSettings(context.CodePackageActivationContext.GetConfigurationPackageObject("Config").Settings);
        }

        /// <summary>
        /// This reference is used by the stateful service controller in order to correctly route requests to the service.
        /// </summary>
        public string PlayerManagerName { get; private set; }
        /// <summary>
        /// Called to dynamically get the correct port that related services will have open.
        /// </summary>
        public int ReverseProxyPort { get; private set; }


        private void CodePackageActivationContext_ConfigurationPackageModifiedEvent(object sender, PackageModifiedEventArgs<ConfigurationPackage> e)
        {
            this.UpdateConfigSettings(e.NewPackage.Settings);
        }

        private void UpdateConfigSettings(ConfigurationSettings settings)
        {
            ConfigurationSection section = settings.Sections["MyConfigSection"];
            this.PlayerManagerName = section.Parameters["PlayerManagerName"].Value;
            this.ReverseProxyPort = int.Parse(section.Parameters["ReverseProxyPort"].Value);
        }
    }
}