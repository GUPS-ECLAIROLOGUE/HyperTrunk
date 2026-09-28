using HyperTrunk.Services;

namespace HyperTrunk.Tests
{
    public class VSwitchNamingTests
    {
        [Theory]
        [InlineData("Ethernet 2", "HyperTrunk_Ethernet_2")]
        [InlineData("Realtek PCIe GbE", "HyperTrunk_Realtek_PCIe_GbE")]
        [InlineData("NoSpaces", "HyperTrunk_NoSpaces")]
        public void ForAdapter_ReplacesSpacesAndAddsPrefix(string adapterName, string expected)
        {
            Assert.Equal(expected, VSwitchNaming.ForAdapter(adapterName));
        }
    }
}
