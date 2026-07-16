using System.Text.Json;
using Mnemotron.Data.ClickHouse.Utility;

namespace Mnemotron.Data.ClickHouse.Json;

internal class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static SnakeCaseNamingPolicy Instance { get; } = new SnakeCaseNamingPolicy();

    public override string ConvertName(string name)
    {
        return name.ToSnakeCase();
    }
}
