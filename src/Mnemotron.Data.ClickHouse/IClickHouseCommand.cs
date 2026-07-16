using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Parameters;

namespace Mnemotron.Data.ClickHouse;

public interface IClickHouseCommand : IDbCommand
{
    new ClickHouseDbParameter CreateParameter();

    Task<ClickHouseRawResult> ExecuteRawResultAsync(CancellationToken cancellationToken);

    IDictionary<string, object> CustomSettings { get; }
}
