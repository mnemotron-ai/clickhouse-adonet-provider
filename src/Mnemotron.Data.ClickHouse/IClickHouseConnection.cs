using System.Data;
using Mnemotron.Data.ClickHouse.ADO;

namespace Mnemotron.Data.ClickHouse;

public interface IClickHouseConnection : IDbConnection
{
    new ClickHouseCommand CreateCommand();
}
