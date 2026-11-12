using Unity.Profiling;
namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        private static readonly ProfilerMarker EmergencyCost = new ProfilerMarker("JurassicWorld.AI.Emergency");
        private static readonly ProfilerMarker TrafficCost = new ProfilerMarker("JurassicWorld.AI.Traffic");
        private static readonly ProfilerMarker InteractionCost = new ProfilerMarker("JurassicWorld.AI.Interaction");
        private static readonly ProfilerMarker RoamingCost = new ProfilerMarker("JurassicWorld.AI.Roaming");
        private bool ProfiledEmergency(float delta) { using (EmergencyCost.Auto()) return UpdateEmergencyEscape(delta); }
        private bool ProfiledTraffic() { using (TrafficCost.Auto()) return UpdateGroundTraffic(); }
        private bool ProfiledInteraction() { using (InteractionCost.Auto()) return UpdatePlayerInteraction(); }
        private bool ProfiledRoaming() { using (RoamingCost.Auto()) return UpdateFreeRoaming(); }
    }
}
