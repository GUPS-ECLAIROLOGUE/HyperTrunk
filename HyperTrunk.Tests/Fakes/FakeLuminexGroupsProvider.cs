using System.Collections.Generic;
using HyperTrunk.Models;
using HyperTrunk.Services;

namespace HyperTrunk.Tests.Fakes
{
    public class FakeLuminexGroupsProvider : ILuminexGroupsProvider
    {
        private readonly IReadOnlyList<LuminexGroup> _groups;

        public FakeLuminexGroupsProvider(IReadOnlyList<LuminexGroup>? groups = null)
        {
            _groups = groups ?? LuminexGroups.All;
        }

        public IReadOnlyList<LuminexGroup> GetGroups() => _groups;
    }
}
