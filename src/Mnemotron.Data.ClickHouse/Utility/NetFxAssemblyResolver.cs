#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Mnemotron.Data.ClickHouse.Utility;

/// <summary>
/// .NET Framework only: a process-scoped fallback for missing bindingRedirects.
///
/// The provider is deployed to the GAC together with its dependency closure,
/// but some closure assemblies internally reference OLDER versions of their
/// own dependencies (e.g. System.Memory wants System.Runtime.CompilerServices
/// .Unsafe 4.0.4.1 while the closure ships 6.0.0.0). Fusion resolves such
/// references only via bindingRedirect entries in the HOST's config — which
/// third-party hosts (SSAS msmdsrv, SSIS DtsDebugHost, SSDT) do not have.
/// Instead of editing host configs, resolve the failed load to the version
/// that ships with the provider. The handler fires only after a load has
/// already FAILED, and only for the provider's own dependency names.
/// </summary>
internal static class NetFxAssemblyResolver
{
    private static int installed;

    // The net48 publish closure (see THIRD-PARTY-NOTICES.md). Keep in sync
    // when dependencies change; the RANGE/SSAS smoke run catches drift.
    private static readonly HashSet<string> KnownDependencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Bcl.AsyncInterfaces",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        "Microsoft.IO.RecyclableMemoryStream",
        "NodaTime",
        "System.Buffers",
        "System.Diagnostics.DiagnosticSource",
        "System.IO.Pipelines",
        "System.Memory",
        "System.Numerics.Vectors",
        "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encodings.Web",
        "System.Text.Json",
        "System.Threading.Tasks.Extensions",
        "System.ValueTuple",
    };

    internal static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) == 1)
            return;
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        AssemblyName requested;
        try
        {
            requested = new AssemblyName(args.Name);
        }
        catch (Exception)
        {
            return null;
        }

        if (!KnownDependencies.Contains(requested.Name))
            return null;

        // Prefer an already-loaded assembly of the same simple name.
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                && TokensMatch(requested, loaded.GetName()))
            {
                return loaded;
            }
        }

        try
        {
            // Newest GAC version of the dependency (the one the provider ships).
#pragma warning disable CS0618 // partial-name GAC binding is exactly the intent here
            var candidate = Assembly.LoadWithPartialName(requested.Name);
#pragma warning restore CS0618
            return candidate != null && TokensMatch(requested, candidate.GetName()) ? candidate : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TokensMatch(AssemblyName requested, AssemblyName candidate)
    {
        var want = requested.GetPublicKeyToken();
        if (want == null || want.Length == 0)
            return true; // no token requested — nothing to protect against
        var have = candidate.GetPublicKeyToken();
        if (have == null || have.Length != want.Length)
            return false;
        for (var i = 0; i < want.Length; i++)
        {
            if (want[i] != have[i])
                return false;
        }

        return true;
    }
}
#endif
