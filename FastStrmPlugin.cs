using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace FastSTRM
{
    public class FastStrmPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "FastSTRM";

        public override Guid Id => Guid.Parse("7b2049e3-8551-409d-8c11-92b1552b7156");

        public override string Description => "Bypasses PlaybackInfo probe for .strm files to eliminate starting delay.";

        public FastStrmPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static FastStrmPlugin? Instance { get; private set; }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            };
        }
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Comma separated language codes used to pick the default subtitle track,
        /// in order of preference, when the client does not request one.
        /// </summary>
        public string PreferredSubtitleLanguages { get; set; } = "en";
    }
}
