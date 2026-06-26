using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace UnitTest
{
    public interface ITestDependency
    {
        int GetValue();
    }

    [TestClass]
    public class NSubstituteSetupTest
    {
        [TestMethod]
        public void SubstituteMock_ReturnsConfiguredValue()
        {
            var mock = Substitute.For<ITestDependency>();
            mock.GetValue().Returns(42);
            Assert.AreEqual(42, mock.GetValue());
        }
    }
}
