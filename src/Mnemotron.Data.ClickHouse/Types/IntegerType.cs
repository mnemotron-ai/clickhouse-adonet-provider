namespace Mnemotron.Data.ClickHouse.Types;

internal abstract class IntegerType : ClickHouseType
{
    public virtual bool Signed => true;
}
