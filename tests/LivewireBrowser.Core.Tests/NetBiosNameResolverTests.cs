using System.Text;
using LivewireBrowser.Core.Network;
using Xunit;

namespace LivewireBrowser.Core.Tests;

public class NetBiosNameResolverTests
{
    private static byte[] BuildNodeStatusResponse(params (string Name, byte Suffix)[] entries)
    {
        var header = new byte[12];
        var rrName = new byte[1 + 32 + 1]; // length + encoded "*" + terminator, content irrelevant for parsing
        var typeClassTtlRdLength = new byte[2 + 2 + 4 + 2];

        var body = new List<byte> { (byte)entries.Length };
        foreach (var (name, suffix) in entries)
        {
            var nameBytes = Encoding.ASCII.GetBytes(name.PadRight(15));
            body.AddRange(nameBytes);
            body.Add(suffix);
            body.Add(0x04); // flags, irrelevant
            body.Add(0x00);
        }

        return header.Concat(rrName).Concat(typeClassTtlRdLength).Concat(body).ToArray();
    }

    [Fact]
    public void ParseNodeStatusResponse_ReturnsWorkstationServiceName()
    {
        var response = BuildNodeStatusResponse(("MYPC-01", 0x20), ("MYPC-01", 0x00), ("WORKGROUP", 0x00));

        var name = NetBiosNameResolver.ParseNodeStatusResponse(response);

        Assert.Equal("MYPC-01", name);
    }

    [Fact]
    public void ParseNodeStatusResponse_NoWorkstationEntry_ReturnsNull()
    {
        var response = BuildNodeStatusResponse(("MYPC-01", 0x20), ("WORKGROUP", 0x1e));

        Assert.Null(NetBiosNameResolver.ParseNodeStatusResponse(response));
    }

    [Fact]
    public void ParseNodeStatusResponse_TruncatedData_ReturnsNullWithoutThrowing()
    {
        Assert.Null(NetBiosNameResolver.ParseNodeStatusResponse(new byte[10]));
    }
}
