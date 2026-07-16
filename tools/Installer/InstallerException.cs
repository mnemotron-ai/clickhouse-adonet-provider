using System;

namespace Mnemotron.Data.ClickHouse.Installer;

// Raised for any expected failure. The message is always the whole story:
// what went wrong AND what to do about it, so Program.cs can print it
// verbatim as the final line before exiting 1.
internal sealed class InstallerException : Exception
{
    public InstallerException(string message)
        : base(message)
    {
    }
}
