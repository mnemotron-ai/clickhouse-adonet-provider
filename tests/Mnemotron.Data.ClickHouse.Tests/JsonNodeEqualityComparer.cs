using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using NUnit.Framework.Constraints;

namespace Mnemotron.Data.ClickHouse.Tests;

internal class JsonNodeEqualityComparer : IComparer<JsonObject>
{
    public int Compare(JsonObject x, JsonObject y) => JsonNode.DeepEquals(x, y) ? 0 : 1;
}
