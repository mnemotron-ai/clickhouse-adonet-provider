namespace Mnemotron.Data.ClickHouse.Types;

internal class RingType : ArrayType
{
    public RingType()
    {
        UnderlyingType = new PointType();
    }

    public override string ToString() => "Ring";
}
