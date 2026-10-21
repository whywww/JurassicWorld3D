namespace JurassicWorld
{
    public sealed partial class DinosaurAgent
    {
        // Special performances may clip through trees and static scenery, but never
        // through another dinosaur's body; foot corrections follow the same gate.
        internal bool ForestPerformanceSupport => frontActive && frontAction != null && !frontWaiting && !frontPeerClearing;
    }
}
