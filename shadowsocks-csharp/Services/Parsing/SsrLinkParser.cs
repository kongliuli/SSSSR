using Shadowsocks.Model;
using Shadowsocks.Util;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Shadowsocks.Services.Parsing
{
    /// <summary>Parser for ShadowsocksR <c>ssr://</c> share-links.</summary>
    public sealed class SsrLinkParser : IServerLinkParser
    {
        public bool CanParse(string url) =>
            url is not null && url.StartsWith(@"ssr://", StringComparison.OrdinalIgnoreCase);

        public void Parse(string ssrUrl, Server target, string forceGroup)
        {
            // ssr://host:port:protocol:method:obfs:base64pass/?obfsparam=base64&remarks=base64&group=base64&udpport=0&uot=1
            var ssr = Regex.Match(ssrUrl, "ssr://([A-Za-z0-9+/=_-]+)", RegexOptions.IgnoreCase);
            if (!ssr.Success)
            {
                throw new FormatException();
            }

            var data = Base64.DecodeUrlSafeBase64(ssr.Groups[1].Value);
            var paramsDict = new Dictionary<string, string>();

            var paramStartPos = data.IndexOf("?", StringComparison.Ordinal);
            if (paramStartPos > 0)
            {
                paramsDict = ParseParam(data.Substring(paramStartPos + 1));
                data = data.Substring(0, paramStartPos);
            }
            if (data.IndexOf("/", StringComparison.Ordinal) >= 0)
            {
                data = data.Substring(0, data.LastIndexOf("/", StringComparison.Ordinal));
            }

            var urlFinder = new Regex("^(.+):([^:]+):([^:]*):([^:]+):([^:]*):([^:]+)");
            var match = urlFinder.Match(data);

            if (!match.Success)
            {
                throw new FormatException();
            }

            target.server = match.Groups[1].Value;
            target.Server_Port = ushort.Parse(match.Groups[2].Value);
            target.Protocol = match.Groups[3].Value.Length == 0 ? "origin" : match.Groups[3].Value;
            target.Protocol = target.Protocol.Replace("_compatible", "");
            target.Method = match.Groups[4].Value;
            target.obfs = match.Groups[5].Value.Length == 0 ? "plain" : match.Groups[5].Value;
            target.obfs = target.obfs.Replace("_compatible", "");
            target.Password = Base64.DecodeUrlSafeBase64(match.Groups[6].Value);

            if (paramsDict.ContainsKey("protoparam"))
            {
                target.ProtocolParam = Base64.DecodeUrlSafeBase64(paramsDict["protoparam"]);
            }
            if (paramsDict.ContainsKey("obfsparam"))
            {
                target.ObfsParam = Base64.DecodeUrlSafeBase64(paramsDict["obfsparam"]);
            }
            if (paramsDict.ContainsKey("remarks"))
            {
                target.Remarks = Base64.DecodeUrlSafeBase64(paramsDict["remarks"]);
            }
            target.Group = paramsDict.ContainsKey("group") ? Base64.DecodeUrlSafeBase64(paramsDict["group"]) : string.Empty;

            if (paramsDict.ContainsKey("uot"))
            {
                target.UdpOverTcp = int.Parse(paramsDict["uot"]) != 0;
            }
            if (paramsDict.ContainsKey("udpport"))
            {
                target.Server_Udp_Port = ushort.Parse(paramsDict["udpport"]);
            }
            if (!string.IsNullOrEmpty(forceGroup))
            {
                target.SubTag = forceGroup;
            }
        }

        internal static Dictionary<string, string> ParseParam(string paramStr)
        {
            var paramsDict = new Dictionary<string, string>();
            var obfsParams = paramStr.Split('&');
            foreach (var p in obfsParams)
            {
                var index = p.IndexOf('=');
                if (index > 0)
                {
                    var key = p.Substring(0, index);
                    var val = p.Substring(index + 1);
                    paramsDict[key] = val;
                }
            }
            return paramsDict;
        }
    }
}
