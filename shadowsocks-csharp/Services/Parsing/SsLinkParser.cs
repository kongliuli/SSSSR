using Shadowsocks.Model;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Shadowsocks.Services.Parsing
{
    /// <summary>Parser for Shadowsocks <c>ss://</c> share-links (legacy base64 form).</summary>
    public sealed class SsLinkParser : IServerLinkParser
    {
        public bool CanParse(string url) =>
            url is not null && url.StartsWith(@"ss://", StringComparison.OrdinalIgnoreCase);

        public void Parse(string ssUrl, Server target, string forceGroup)
        {
            Regex urlFinder = new("^(?i)ss://([A-Za-z0-9+-/=_]+)(#(.+))?", RegexOptions.IgnoreCase),
                detailsParser = new("^((?<method>.+):(?<password>.*)@(?<hostname>.+?)" +
                                    ":(?<port>\\d+?))$", RegexOptions.IgnoreCase);

            var match = urlFinder.Match(ssUrl);
            if (!match.Success)
            {
                throw new FormatException();
            }

            var base64 = match.Groups[1].Value;
            match = detailsParser.Match(Encoding.UTF8.GetString(Convert.FromBase64String(
                base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='))));
            target.Protocol = "origin";
            target.Method = match.Groups["method"].Value;
            target.Password = match.Groups["password"].Value;
            target.server = match.Groups["hostname"].Value;
            target.Server_Port = ushort.Parse(match.Groups["port"].Value);
            target.SubTag = !string.IsNullOrEmpty(forceGroup) ? forceGroup : string.Empty;
        }
    }
}
