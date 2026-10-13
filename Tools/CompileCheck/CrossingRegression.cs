// Compiled with the production MovementCorridor.cs, outside the Unity editor.
using System;
using System.Reflection;
using UnityEngine;
using JurassicWorld;

namespace JurassicWorld
{
    public sealed class DinosaurAgent
    {
        public bool IsOffRouteInteraction;
        public enum DinosaurState { Idle, RandomWalk, Approaching }
        public sealed class Pose { public Vector3 position; }
        public readonly Pose transform = new Pose();
        public int RouteIndex, LaneIndex;
        public float CollisionRadius;
        public bool isActiveAndEnabled = true;
        public DinosaurState CurrentState = DinosaurState.RandomWalk;
    }
}

public static class CrossingRegression
{
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    static void Reset() => typeof(MovementCorridorCrossings).GetMethod("ResetOwners",
        BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    static Vector2 Position(DinosaurAgent a) => new Vector2(a.transform.position.x, a.transform.position.z);
    static void Place(DinosaurAgent a, Vector2 p) => a.transform.position = new Vector3(p.x, 0, p.y);
    static DinosaurAgent Agent(int route, float radius, int index) => new DinosaurAgent
    { RouteIndex = route, CollisionRadius = radius, LaneIndex = index };
    static bool Protected(int route, int other, Vector2 p)
        => MovementCorridor.MinDistanceToRoute(p, other) < 7f;
    static float SafeArc(int route, int other, float fraction)
    {
        int n = MovementCorridor.SampleCount(route);
        for (int j = 0; j < n; j++)
        {
            var sample = MovementCorridor.GetSample(route, ((int)(fraction * n) + j) % n);
            if (!Protected(route, other, sample.Position)) return sample.Distance;
        }
        throw new Exception("No waiting area for routes " + route + "," + other);
    }
    static void OccupancyAndReservation()
    {
        // Both names/orderings must yield to the dinosaur already in the conflict region.
        for (int ownerRoute = 3; ownerRoute <= 5; ownerRoute += 2)
        for (int ownerIndex = 0; ownerIndex <= 1; ownerIndex++)
        {
            int other = ownerRoute == 3 ? 5 : 3;
            var owner = Agent(ownerRoute, ownerRoute == 3 ? 5f : 1.5f, ownerIndex);
            var waiting = Agent(other, other == 3 ? 5f : 1.5f, 1 - ownerIndex);
            Reset();
            int n = MovementCorridor.SampleCount(other);
            bool checkedEntry = false;
            for (int i = 0; i < n; i++)
            {
                var before = MovementCorridor.GetSample(other, i);
                var after = MovementCorridor.GetSample(other, (i + 1) % n);
                if (Protected(other, ownerRoute, before.Position) || !Protected(other, ownerRoute, after.Position)) continue;
                MovementCorridor.ProjectToCorridor(ownerRoute, after.Position, out float arc, out _);
                Place(owner, MovementCorridor.PointAt(ownerRoute, arc));
                Place(waiting, before.Position);
                Require(!MovementCorridorCrossings.CanAdvance(waiting, new[] { owner }, after.Position), "Entrant stole occupied gate");
                Require(MovementCorridorCrossings.CanAdvance(owner, new[] { waiting }, Position(owner)), "Occupant denied passage");
                Require(MovementCorridor.MinDistanceToRoute(Position(waiting), ownerRoute) >= 7f, "Waiting root blocks owner's route");
                // An idle owner still occupies the gate; it must not be preempted.
                owner.CurrentState = DinosaurAgent.DinosaurState.Idle;
                Require(!MovementCorridorCrossings.CanAdvance(waiting, new[] { owner }, after.Position), "Idle owner lost reservation");
                owner.IsOffRouteInteraction = true;
                MovementCorridorCrossings.Release(owner);
                Require(MovementCorridorCrossings.CanAdvance(waiting, new[] { owner }, after.Position), "Off-route interaction retained a gate");
                owner.IsOffRouteInteraction = false;
                Reset();
                Require(!MovementCorridorCrossings.CanAdvance(waiting, new[] { owner }, after.Position), "Returning route occupant was ignored");
                Place(owner, MovementCorridor.PointAt(ownerRoute, SafeArc(ownerRoute, other, 0f)));
                Require(MovementCorridorCrossings.CanAdvance(waiting, new[] { owner }, after.Position), "Owner exit did not release gate");
                checkedEntry = true;
                break;
            }
            Require(checkedEntry, "No entry tested");
        }
        Console.WriteLine("PASS occupied-first, reserved entry, idle owner, safe waiting, release (both directions and indices)");
    }
    static void Simulate(int routeA, int routeB, bool reverseOrder, int recordedSpawn = 0, int spawnSeed = -1)
    {
        Reset();
        var agents = new[] { Agent(routeA, 5f, 1), Agent(routeB, 1.5f, 0) };
        float[] arcs = { SafeArc(routeA, routeB, .15f), SafeArc(routeB, routeA, .65f) };
        if (recordedSpawn != 0)
        {
            Vector2 rex = recordedSpawn == 1 ? new Vector2(-11.5f, 39.3f) : new Vector2(-44f, 48f);
            Vector2 raptor = recordedSpawn == 1 ? new Vector2(-22.2f, 51.4f) : new Vector2(-23.7f, 60.1f);
            MovementCorridor.ProjectToCorridor(routeA, rex, out arcs[0], out _);
            MovementCorridor.ProjectToCorridor(routeB, raptor, out arcs[1], out _);
        }
        if (spawnSeed >= 0)
        {
            var random = new System.Random(spawnSeed);
            arcs[0] = (float)random.NextDouble() * MovementCorridor.Length(routeA);
            bool accepted = false;
            for (int attempt = 0; attempt < 200; attempt++)
            {
                arcs[1] = (float)random.NextDouble() * MovementCorridor.Length(routeB);
                Vector2 p = MovementCorridor.PointAt(routeA, arcs[0]), q = MovementCorridor.PointAt(routeB, arcs[1]);
                if (!MovementCorridorCrossings.CanSpawnTogether(routeA, p, 5f, routeB, q, 1.5f)
                    || Vector2.Distance(p, q) < 12.5f) continue;
                accepted = true; break;
            }
            Require(accepted, "No compatible random spawn");
        }
        if (recordedSpawn == 2)
        {
            if (!MovementCorridorCrossings.CanSpawnTogether(routeA, MovementCorridor.PointAt(routeA, arcs[0]), 5f,
                routeB, MovementCorridor.PointAt(routeB, arcs[1]), 1.5f))
            {
                arcs[1] = SafeArc(routeB, routeA, arcs[1] / MovementCorridor.Length(routeB));
                Console.WriteLine("PASS rejected physically conflicting recorded spawn; replaying safe replacement");
            }
        }
        float[] travelled = new float[2], stopped = new float[2];
        float[] timers = { 5f, 7f };
        bool[] idle = new bool[2];
        for (int i = 0; i < 2; i++) Place(agents[i], MovementCorridor.PointAt(agents[i].RouteIndex, arcs[i]));
        for (int frame = 0; frame < 8000; frame++)
        for (int order = 0; order < 2; order++)
        {
            int i = reverseOrder ? 1 - order : order, j = 1 - i;
            var a = agents[i]; var b = agents[j];
            timers[i] -= .1f;
            if (timers[i] <= 0f)
            {
                idle[i] = !idle[i];
                timers[i] = idle[i] ? 4f + i : 5f + i;
                a.CurrentState = idle[i] ? DinosaurAgent.DinosaurState.Idle : DinosaurAgent.DinosaurState.RandomWalk;
            }
            if (idle[i]) continue;
            float step = (i == 0 ? 2.3486404f : 3f) * .1f;
            float nextArc = MovementCorridor.TravelDistance(a.RouteIndex, arcs[i] + step);
            Vector2 next = MovementCorridor.PointAt(a.RouteIndex, nextArc);
            if (MovementCorridorCrossings.CanAdvance(a, new[] { b }, next)
                && Vector2.Distance(next, Position(b)) >= 6.5f)
            {
                Place(a, next); arcs[i] = nextArc; travelled[i] += step; stopped[i] = 0;
            }
            else stopped[i] += .1f;
            Require(Vector2.Distance(Position(a), Position(b)) >= 6.49f, "Overlap");
            Require(Math.Min(stopped[0], stopped[1]) < 120f,
                $"Deadlock routes={routeA},{routeB} recorded={recordedSpawn} frame={frame} positions={Position(agents[0])},{Position(agents[1])}");
        }
        for (int i = 0; i < 2; i++) Require(travelled[i] > MovementCorridor.Length(agents[i].RouteIndex) * 2f, "Did not complete two loops");
        Console.WriteLine($"PASS routes={routeA},{routeB} reverse={reverseOrder} recorded={recordedSpawn} seed={spawnSeed} travelled={travelled[0]:F1},{travelled[1]:F1}");
    }
    static void SimulateThree(bool reverse, int routeA = 0, int routeB = 2, int routeC = 4)
    {
        Reset();
        var agents = new[] { Agent(routeA, 5f, 0), Agent(routeB, 3f, 1), Agent(routeC, 1.5f, 2) };
        float[] arcs = new float[3], travelled = new float[3], stopped = new float[3];
        float[] speeds = { 2.3486404f, 2.339f, 3f };
        for (int i = 0; i < agents.Length; i++)
        {
            bool placed = false;
            for (int k = 0; k < MovementCorridor.SampleCount(agents[i].RouteIndex); k++)
            {
                var sample = MovementCorridor.GetSample(agents[i].RouteIndex, k);
                bool safe = true;
                for (int j = 0; j < i; j++)
                    if (!MovementCorridorCrossings.CanSpawnTogether(agents[i].RouteIndex, sample.Position, agents[i].CollisionRadius,
                        agents[j].RouteIndex, Position(agents[j]), agents[j].CollisionRadius)) safe = false;
                if (!safe) continue;
                arcs[i] = sample.Distance; Place(agents[i], sample.Position); placed = true; break;
            }
            Require(placed, "No safe triple placement");
        }
        for (int frame = 0; frame < 6000; frame++)
            for (int order = 0; order < 3; order++)
            {
                int i = reverse ? 2 - order : order;
                float step = speeds[i] * .1f;
                float nextArc = MovementCorridor.TravelDistance(agents[i].RouteIndex, arcs[i] + step);
                Vector2 next = MovementCorridor.PointAt(agents[i].RouteIndex, nextArc);
                bool safe = MovementCorridorCrossings.CanAdvance(agents[i], agents, next);
                if (safe)
                    for (int j = 0; j < 3; j++)
                        if (j != i && Vector2.Distance(next, Position(agents[j])) < agents[i].CollisionRadius + agents[j].CollisionRadius)
                        {
                            safe = false;
                            break;
                        }
                if (safe) { Place(agents[i], next); arcs[i] = nextArc; travelled[i] += step; stopped[i] = 0f; }
                else stopped[i] += .1f;
                if (stopped[i] >= 120f)
                {
                    for (int j = 0; j < 3; j++) Console.WriteLine($"WAIT {j} route={agents[j].RouteIndex} arc={arcs[j]:F1} travelled={travelled[j]:F1} stopped={stopped[j]:F1} position={Position(agents[j])}");
                    var gates = (Array)typeof(MovementCorridorCrossings).GetField("Gates", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                    foreach (var gate in gates)
                    {
                        var type = gate.GetType(); var owner = type.GetField("Owner").GetValue(gate) as DinosaurAgent;
                        if (owner != null) Console.WriteLine($"GATE {type.GetField("RouteA").GetValue(gate)}-{type.GetField("RouteB").GetValue(gate)} owner={Array.IndexOf(agents, owner)}");
                    }
                }
                Require(stopped[i] < 120f, "Triple starvation, agent " + i);
                for (int j = 0; j < i; j++) Require(Vector2.Distance(Position(agents[i]), Position(agents[j])) >= agents[i].CollisionRadius + agents[j].CollisionRadius - .01f, "Triple overlap");
            }
        for (int i = 0; i < 3; i++) Require(travelled[i] > MovementCorridor.Length(agents[i].RouteIndex) * 2f, "Triple did not complete two loops");
        Console.WriteLine($"PASS triple routes={routeA},{routeB},{routeC} reverse={reverse} travelled={travelled[0]:F1},{travelled[1]:F1},{travelled[2]:F1}");
    }

    public static int Main()
    {
        try
        {
            OccupancyAndReservation();
            SimulateThree(false); SimulateThree(true);
            SimulateThree(false, 1, 5, 2); SimulateThree(true, 1, 5, 2);
            SimulateThree(false, 3, 0, 5); SimulateThree(true, 3, 0, 5);
            Simulate(5, 3, false, 2);
            Simulate(5, 3, true, 2);
            Simulate(3, 5, false, 1);
            for (int a = 0; a < 6; a++) for (int b = a + 1; b < 6; b++)
            {
                Simulate(a, b, false); Simulate(a, b, true);
                Simulate(a, b, false, 0, 1000 + a * 6 + b);
                Simulate(b, a, true, 0, 2000 + a * 6 + b);
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
