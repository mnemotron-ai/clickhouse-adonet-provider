using Mnemotron.Data.ClickHouse.ADO;
using Mnemotron.Data.ClickHouse.ADO.Parameters;

namespace Mnemotron.Data.ClickHouse.Utility;

public static class CommandExtensions
{
    public static ClickHouseDbParameter AddParameter(this ClickHouseCommand command, string parameterName, object parameterValue)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = parameterValue;
        command.Parameters.Add(parameter);
        return parameter;
    }

    public static ClickHouseDbParameter AddParameter(this ClickHouseCommand command, string parameterName, string clickHouseType, object parameterValue)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.ClickHouseType = clickHouseType;
        parameter.Value = parameterValue;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
