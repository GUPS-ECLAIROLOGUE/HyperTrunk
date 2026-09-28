using System.Threading.Tasks;
using HyperTrunk.Services;
using HyperTrunk.Tests.Fakes;

namespace HyperTrunk.Tests
{
    // Ces tests appellent le vrai moteur Hyper-V et nécessitent une machine avec
    // Hyper-V installé + les droits administrateur. Ils ne peuvent pas s'exécuter
    // dans un environnement de build sans ce matériel : à activer manuellement
    // (retirer le Skip) sur la machine cible pour une vérification de bout en bout.
    public class HyperVServiceHardwareTests
    {
        [Fact(Skip = "Nécessite une vraie machine Hyper-V + droits administrateur - à exécuter manuellement.")]
        public async Task InitializeAsync_OnRealMachine_LoadsHyperVModulesWithoutError()
        {
            var service = new HyperVService(new FakeLogger());
            await service.InitializeAsync();

            bool enabled = await service.IsHyperVEnabledAsync();

            Assert.True(enabled);
        }
    }
}
