// LOCAL DEV BUILD SHIM — NOT part of the normal build.
//
// The project depends on HMBSbige's private fork "ARSoft.Tools.Net 2.3.0", which adds the
// DNS-over-TLS types below on top of the public ARSoft.Tools.Net. That fork is only available
// from a private GitHub Packages feed (see .github/workflows/CI.yml). On machines without that
// feed credential, NuGet falls back to the public ARSoft.Tools.Net 3.0.0, which lacks these
// two types, so the project cannot compile for local verification.
//
// Building with `-p:LocalDnsShim=true` swaps the package to the public 3.0.0 and compiles this
// minimal shim so the rest of the codebase builds and can be type-checked locally. The shim is
// compile-only scaffolding (its DNS-over-TLS methods do nothing); never ship a build produced
// this way. The verified public 3.0.0 base API (ResolveAsync, DnsQueryOptions, DomainName, etc.)
// is identical to the fork, so build feedback for all other code is accurate.
//
// Excluded from the default build via <Compile Remove> in shadowsocksr.csproj.

using System.Net;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace ARSoft.Tools.Net.Dns
{
    /// <summary>Local build shim — mirrors the fork's TLS upstream server descriptor.</summary>
    internal sealed class TlsUpstreamServer
    {
        public IPAddress IPAddress { get; set; }
        public string AuthName { get; set; }
        public SslProtocols SslProtocols { get; set; }
    }

    /// <summary>Local build shim — mirrors the fork's DNS-over-TLS client surface used by DnsClient.</summary>
    internal sealed class DnsOverTlsClient
    {
        public DnsOverTlsClient(TlsUpstreamServer server, int queryTimeout, ushort port)
        {
        }

        public Task<DnsMessage> ResolveAsync(
            ARSoft.Tools.Net.DomainName name,
            RecordType recordType,
            RecordClass recordClass,
            DnsQueryOptions options,
            CancellationToken token = default)
        {
            return Task.FromResult<DnsMessage>(null);
        }
    }
}
