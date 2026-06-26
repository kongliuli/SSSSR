namespace Shadowsocks.Proxy.Core.Tests;

[TestClass]
public class SmokeTest
{
    [TestMethod]
    public void BufferSize_ShouldEqual_18497()
    {
        Assert.AreEqual(18497, Constants.BufferSize);
    }
}
