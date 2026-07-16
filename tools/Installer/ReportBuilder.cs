using System;
using System.Collections.Generic;

namespace Mnemotron.Data.ClickHouse.Installer;

// Accumulates (item, status) rows and prints them in the same
// "=== <title> ===" / "  <item>" / "      -> <status>" shape both ps1
// scripts use for their final status report.
internal sealed class ReportBuilder
{
    private readonly List<(string Item, string Status)> _rows = new();

    public void Add(string item, string status) => _rows.Add((item, status));

    public void Print(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");
        foreach ((string Item, string Status) row in _rows)
        {
            Console.WriteLine($"  {row.Item}");
            Console.WriteLine($"      -> {row.Status}");
        }
    }
}
