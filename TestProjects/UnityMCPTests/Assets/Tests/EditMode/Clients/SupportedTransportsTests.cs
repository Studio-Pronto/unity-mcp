using MCPForUnity.Editor.Clients;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class SupportedTransportsTests
    {
        [Test]
        public void IMcpClientConfigurator_ExposesSupportedTransports()
        {
            var prop = typeof(IMcpClientConfigurator).GetProperty("SupportedTransports");
            Assert.IsNotNull(prop, "Must expose SupportedTransports");
        }
    }
}
