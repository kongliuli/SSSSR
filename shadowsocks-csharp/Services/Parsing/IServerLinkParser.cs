using Shadowsocks.Model;

namespace Shadowsocks.Services.Parsing
{
    /// <summary>
    /// Parses a single share-link (e.g. <c>ssr://…</c>, <c>ss://…</c>) into a <see cref="Server"/>.
    /// Additional protocols (vmess/trojan/…) plug in by implementing this interface and
    /// registering with <see cref="ServerLinkParser"/>.
    /// </summary>
    public interface IServerLinkParser
    {
        /// <summary>Whether this parser recognizes the given link.</summary>
        bool CanParse(string url);

        /// <summary>
        /// Populate <paramref name="target"/> from <paramref name="url"/>.
        /// Throws <see cref="System.FormatException"/> if the link is malformed.
        /// </summary>
        void Parse(string url, Server target, string forceGroup);
    }
}
