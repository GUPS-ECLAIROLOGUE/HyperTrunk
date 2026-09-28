using HyperTrunk.Services;

namespace HyperTrunk.Tests
{
    public class IpUtilsTests
    {
        [Theory]
        [InlineData("255.255.255.0", 24)]
        [InlineData("255.255.0.0", 16)]
        [InlineData("255.0.0.0", 8)]
        [InlineData("255.255.255.128", 25)]
        [InlineData("255.255.255.192", 26)]
        public void MaskToPrefixLength_ComputesCorrectPrefix(string mask, int expectedPrefix)
        {
            Assert.Equal(expectedPrefix, IpUtils.MaskToPrefixLength(mask));
        }

        [Theory]
        [InlineData(24, "255.255.255.0")]
        [InlineData(16, "255.255.0.0")]
        [InlineData(8, "255.0.0.0")]
        [InlineData(25, "255.255.255.128")]
        [InlineData(26, "255.255.255.192")]
        public void PrefixLengthToMask_IsInverseOfMaskToPrefixLength(int prefix, string expectedMask)
        {
            Assert.Equal(expectedMask, IpUtils.PrefixLengthToMask(prefix));
        }

        [Fact]
        public void NormalizeIp_StripsLeadingZeros()
        {
            Assert.Equal("192.168.1.1", IpUtils.NormalizeIp("192.168.001.001"));
        }
    }
}
