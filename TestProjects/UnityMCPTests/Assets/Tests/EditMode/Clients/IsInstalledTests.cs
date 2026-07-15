using MCPForUnity.Editor.Clients;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Clients
{
    [TestFixture]
    public class IsInstalledTests
    {
        [Test]
        public void IMcpClientConfigurator_ExposesIsInstalled()
        {
            var prop = typeof(IMcpClientConfigurator).GetProperty("IsInstalled");
            Assert.IsNotNull(prop, "IMcpClientConfigurator must expose an IsInstalled property");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
        }
    }
}
