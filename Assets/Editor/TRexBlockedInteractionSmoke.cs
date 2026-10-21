namespace JurassicWorld.Editor
{
    public static class TRexBlockedInteractionSmoke
    {
        // Retain the existing batch entry point. The replacement suite checks front/left/right,
        // blocked performances, turning, returns and peer yielding at 30/60/144 FPS. The former
        // assertions requiring static-obstacle bypass conflict with natural forest movement.
        public static void Run() => RainforestSampleAcceptance.Run();
    }
}
