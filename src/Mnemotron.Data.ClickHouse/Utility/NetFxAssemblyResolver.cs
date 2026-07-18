#if NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
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
///
/// Inert for the additive ILRepack-merged single-assembly payload (issue #5,
/// `make merge-net48`): a merged deployment never requests a separate
/// dependency name, so <see cref="Resolve"/> never matches anything.
/// </summary>
internal static class NetFxAssemblyResolver
{
    private static int installed;

    // Re-entrancy guard: a probe issued from inside the handler (e.g.
    // Assembly.LoadWithPartialName missing entirely) raises AssemblyResolve
    // again for the same name; without the guard that recurses until the
    // stack is exhausted and the process dies with an access violation.
    [ThreadStatic]
    private static HashSet<string> resolving;

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

        // Satellite resource probes (Name ends with ".resources") legitimately
        // miss for the neutral culture — never ours to answer.
        if (!KnownDependencies.Contains(requested.Name)
            || requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        resolving ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!resolving.Add(requested.Name))
            return null; // already resolving this name further up the stack

        try
        {
            // Prefer an already-loaded assembly of the same simple name.
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase)
                    && TokensMatch(requested, loaded.GetName()))
                {
                    return loaded;
                }
            }

            // The GAC'd version of the dependency (the one the provider ships),
            // then — for xcopy deployments — a copy sitting next to the provider.
            // (Assembly.LoadWithPartialName is NOT reliable here: partial-name
            // GAC binding is a CLR 1.x leftover; proven to miss on CLR 4.)
            return LoadFromGac(requested) ?? LoadFromProviderDirectory(requested);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            resolving.Remove(requested.Name);
        }
    }

    // v4 GAC layout: %WINDIR%\Microsoft.NET\assembly\GAC_MSIL\<name>\v4.0_<version>__<token>\<name>.dll
    // (the same layout the installer relies on). Pick the highest installed
    // version whose public key token matches the request.
    private static Assembly LoadFromGac(AssemblyName requested)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Microsoft.NET", "assembly", "GAC_MSIL", requested.Name);
        if (!Directory.Exists(root))
            return null;

        var wantToken = TokenToString(requested.GetPublicKeyToken());
        Version bestVersion = null;
        string bestToken = null;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var leaf = Path.GetFileName(dir);
            if (leaf == null || !leaf.StartsWith("v4.0_", StringComparison.OrdinalIgnoreCase))
                continue;
            var sep = leaf.IndexOf("__", StringComparison.Ordinal);
            if (sep < 0)
                continue;
            var token = leaf.Substring(sep + 2);
            if (wantToken != null && !string.Equals(token, wantToken, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Version.TryParse(leaf.Substring(5, sep - 5), out var version))
                continue;
            if (!File.Exists(Path.Combine(dir, requested.Name + ".dll")))
                continue;
            if (bestVersion == null || version > bestVersion)
            {
                bestVersion = version;
                bestToken = token;
            }
        }

        if (bestVersion == null)
            return null;
        return Assembly.Load(string.Format(
            CultureInfo.InvariantCulture,
            "{0}, Version={1}, Culture=neutral, PublicKeyToken={2}",
            requested.Name, bestVersion, bestToken));
    }

    private static Assembly LoadFromProviderDirectory(AssemblyName requested)
    {
        var baseDir = Path.GetDirectoryName(typeof(NetFxAssemblyResolver).Assembly.Location);
        if (string.IsNullOrEmpty(baseDir))
            return null;
        var file = Path.Combine(baseDir, requested.Name + ".dll");
        if (!File.Exists(file))
            return null;
        var candidate = Assembly.LoadFrom(file);
        return TokensMatch(requested, candidate.GetName()) ? candidate : null;
    }

    private static string TokenToString(byte[] token)
    {
        if (token == null || token.Length == 0)
            return null;
        var sb = new StringBuilder(token.Length * 2);
        foreach (var b in token)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
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
