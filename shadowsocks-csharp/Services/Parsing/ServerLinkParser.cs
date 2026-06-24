using Shadowsocks.Model;
using System;
using System.Collections.Generic;

namespace Shadowsocks.Services.Parsing
{
    /// <summary>
    /// Selects the appropriate <see cref="IServerLinkParser"/> for a share-link and parses it.
    /// Extend protocol support by adding parsers to the set passed to the constructor (or to
    /// <see cref="Default"/>).
    /// </summary>
    public sealed class ServerLinkParser
    {
        private readonly IReadOnlyList<IServerLinkParser> _parsers;

        public ServerLinkParser(IEnumerable<IServerLinkParser> parsers)
        {
            _parsers = new List<IServerLinkParser>(parsers);
        }

        /// <summary>Default parser set: SSR and SS.</summary>
        public static ServerLinkParser Default { get; } = new(new IServerLinkParser[]
        {
            new SsrLinkParser(),
            new SsLinkParser(),
        });

        /// <summary>
        /// Parse <paramref name="url"/> into <paramref name="target"/>.
        /// Throws <see cref="FormatException"/> if no registered parser recognizes the link.
        /// </summary>
        public void Parse(string url, Server target, string forceGroup)
        {
            foreach (var parser in _parsers)
            {
                if (parser.CanParse(url))
                {
                    parser.Parse(url, target, forceGroup);
                    return;
                }
            }

            throw new FormatException();
        }
    }
}
