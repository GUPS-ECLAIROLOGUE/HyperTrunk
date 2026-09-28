using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HyperTrunk.Models;
using HyperTrunk.Services;
using HyperTrunk.Tests.Fakes;

namespace HyperTrunk.Tests
{
    public class LuminexGroupsProviderTests : IDisposable
    {
        private readonly string _configPath;

        public LuminexGroupsProviderTests()
        {
            _configPath = Path.Combine(AppContext.BaseDirectory, "luminex-groups.json");
            if (File.Exists(_configPath)) File.Delete(_configPath);
        }

        public void Dispose()
        {
            if (File.Exists(_configPath)) File.Delete(_configPath);
        }

        [Fact]
        public void GetGroups_NoFile_ReturnsEmbeddedDefaults()
        {
            var provider = new LuminexGroupsProvider(new FakeLogger());

            var groups = provider.GetGroups();

            Assert.Equal(LuminexGroups.All.Count, groups.Count);
            Assert.Contains(groups, g => g.Name == "Group02" && g.VlanId == 200 && g.ColorHex == "#E80000");
        }

        [Fact]
        public void GetGroups_ValidFile_OverridesDefaults()
        {
            var custom = new List<LuminexGroup>
            {
                new() { Name = "Custom", VlanId = 42, ColorHex = "#123456" }
            };
            File.WriteAllText(_configPath, JsonSerializer.Serialize(custom));

            var provider = new LuminexGroupsProvider(new FakeLogger());
            var groups = provider.GetGroups();

            Assert.Single(groups);
            Assert.Equal("Custom", groups[0].Name);
            Assert.Equal(42, groups[0].VlanId);
        }

        [Fact]
        public void GetGroups_MalformedFile_FallsBackToDefaultsWithoutThrowing()
        {
            File.WriteAllText(_configPath, "{ ceci n'est pas du json valide");

            var logger = new FakeLogger();
            var provider = new LuminexGroupsProvider(logger);
            var groups = provider.GetGroups();

            Assert.Equal(LuminexGroups.All.Count, groups.Count);
            Assert.Contains(logger.Entries, e => e.Level == HyperTrunk.Logging.LogLevel.Warn);
        }

        [Fact]
        public void GetGroups_EmptyArrayFile_FallsBackToDefaults()
        {
            File.WriteAllText(_configPath, "[]");

            var logger = new FakeLogger();
            var provider = new LuminexGroupsProvider(logger);
            var groups = provider.GetGroups();

            Assert.Equal(LuminexGroups.All.Count, groups.Count);
        }
    }
}
