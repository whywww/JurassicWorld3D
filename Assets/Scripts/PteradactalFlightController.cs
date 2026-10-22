using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicWorld
{
    public sealed class PteradactalFlightController : MonoBehaviour
    {
        public enum FlightMode { Roaming, FrontPass }
        [SerializeField] private Transform visualPivot;
        [SerializeField] private Animation flightAnimation;
        [SerializeField] private PteradactalAirspace airspace;
        [SerializeField] private float minimumHeight = 6f;
        [SerializeField] private float maximumHeight = 10f;
        [SerializeField, Range(1f, 5f)] private float speed = 5f;
        [SerializeField] private Vector2 passInterval = new Vector2(25f, 40f);
        [SerializeField] private Vector2 passDistance = new Vector2(30f, 45f);
        [SerializeField] private float retryInterval = 5f;
        [SerializeField] private int randomSeed = 7319;
        [SerializeField] private int initialOrbit;
        [SerializeField] private float initialAngle;
        [SerializeField] private bool initialPass;
        private System.Random random;
        private Camera observer;
        private PteradactalFlightPath connection;
        private int orbitIndex, destinationOrbit, direction = 1, destinationDirection;
        private float angle, destinationAngle, pathDistance, elapsed, bank, nextPass, nextRoam, passRemaining;
        private bool initialized;
        public string DisplayName => "翼龙";
        public string Description => "生活年代：晚白垩世\n体型：翼展约 6 米\n食性：肉食（食鱼）\n特点：第四指撑起翼膜，可振翅滑翔";
        public float MinimumHeight => minimumHeight;
        public float MaximumHeight => maximumHeight;
        public float HeightAboveGround => transform.position.y - airspace.Ground(transform.position.x, transform.position.z);
        public PteradactalAirspace Airspace => airspace;
        public FlightMode Mode { get; private set; }
        public int PassCount { get; private set; }
        public float LastPassTime { get; private set; }
        public const float HorizontalLimit = 75f;
        private const float TurnRate = 45f;

        private struct Candidate { public int orbit; public float angle, score; }

        private void Start()
        {
            if (flightAnimation == null || flightAnimation.clip == null || visualPivot == null || airspace == null || airspace.orbits.Length == 0)
            {
                Debug.LogError("翼龙缺少低空航线或飞行动画。请重新运行翼龙接入。", this);
                enabled = false; return;
            }
            // The scene already contains a safe spawn. Re-evaluate only the next flyby for the live player.
            if (!initialized) InitializeState(Camera.main);
            flightAnimation.wrapMode = WrapMode.Loop;
            flightAnimation.cullingType = AnimationCullingType.AlwaysAnimate;
            flightAnimation.Play();
        }

        // Called by scene integration and acceptance, before play; never teleports an active flyer.
        public void PrepareFlight(PteradactalAirspace data, Camera camera)
        {
            airspace = data;
            observer = camera;
            random = new System.Random(randomSeed);
            var candidates = PassCandidates();
            initialPass = candidates.Count > 0;
            if (candidates.Count > 0)
            {
                initialOrbit = candidates[0].orbit;
                initialAngle = candidates[0].angle - speed * 6f / airspace.orbits[initialOrbit].radius;
            }
            else
            {
                initialOrbit = 0;
                float best = float.MaxValue;
                Vector2 p = camera != null ? new Vector2(camera.transform.position.x, camera.transform.position.z) : Vector2.zero;
                for (int i = 0; i < airspace.orbits.Length; i++)
                {
                    float distance = Vector2.Distance(airspace.orbits[i].center, p);
                    if (distance < best) { best = distance; initialOrbit = i; }
                }
                initialAngle = 0f;
            }
            InitializeState(camera);
            Mode = candidates.Count > 0 ? FlightMode.FrontPass : FlightMode.Roaming;
            passRemaining = 10f;
            nextPass = candidates.Count > 0 ? 35f : retryInterval;
            ApplyPose(0f);
        }

        private void InitializeState(Camera camera)
        {
            observer = camera;
            random = new System.Random(randomSeed);
            orbitIndex = initialOrbit; angle = initialAngle; direction = 1;
            connection = null; elapsed = bank = pathDistance = 0f;
            PassCount = 0; LastPassTime = -1f;
            Mode = initialPass ? FlightMode.FrontPass : FlightMode.Roaming; passRemaining = 10f;
            nextPass = initialPass ? 35f : retryInterval; nextRoam = 14f;
            initialized = true;
        }

        private void Update() => AdvanceFlight(Time.deltaTime);

        private void AdvanceFlight(float deltaTime)
        {
            if (!initialized) InitializeState(Camera.main);
            while (deltaTime > .000001f)
            {
                float dt = Mathf.Min(deltaTime, 1f / 30f); deltaTime -= dt; elapsed += dt;
                if (connection == null && Mode == FlightMode.Roaming)
                {
                    if (elapsed >= nextPass)
                    {
                        if (!TryFrontPass()) nextPass = elapsed + retryInterval;
                    }
                    else if (elapsed >= nextRoam)
                    {
                        TryRoam(); nextRoam = elapsed + 12f;
                    }
                }
                float remaining = speed * dt;
                if (connection != null)
                {
                    float step = Mathf.Min(remaining, connection.Length - pathDistance);
                    pathDistance += step; remaining -= step;
                    if (pathDistance >= connection.Length - .0001f)
                    {
                        orbitIndex = destinationOrbit; angle = destinationAngle; direction = destinationDirection;
                        connection = null;
                    }
                }
                if (connection == null)
                {
                    angle += direction * remaining / airspace.orbits[orbitIndex].radius;
                    if (Mode == FlightMode.FrontPass)
                    {
                        float before = passRemaining; passRemaining -= remaining / speed;
                        if (before > 4f && passRemaining <= 4f) { PassCount++; LastPassTime = elapsed; }
                        if (passRemaining <= 0f)
                        {
                            Mode = FlightMode.Roaming;
                            nextPass = elapsed + Mathf.Lerp(passInterval.x, passInterval.y, (float)random.NextDouble());
                            nextRoam = elapsed + 10f;
                        }
                    }
                }
                ApplyPose(dt);
            }
        }

        private void Pose(out Vector2 xz, out float heading, out float height, out float curvature)
        {
            if (connection != null)
            {
                connection.Sample(pathDistance, out xz, out heading, out height);
                float s = pathDistance; curvature = 0f;
                foreach (var segment in connection.segments)
                { if (s <= segment.length) { curvature = segment.curvature; break; } s -= segment.length; }
            }
            else
            {
                var orbit = airspace.orbits[orbitIndex];
                xz = orbit.Point(angle); heading = angle + direction * Mathf.PI * .5f;
                height = orbit.height; curvature = direction / orbit.radius;
            }
        }

        private void ApplyPose(float dt)
        {
            Pose(out var xz, out float heading, out float height, out float curvature);
            Vector3 p = airspace.Position(xz, height + Mathf.Sin(elapsed * .3f) * PteradactalAirspace.BobAmplitude);
            float verticalSpeed = dt > 0f ? (p.y - transform.position.y) / dt : 0f;
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading))));
            bank = Mathf.MoveTowards(bank, curvature * speed * Mathf.Rad2Deg / TurnRate * 15f, 20f * dt);
            if (visualPivot != null) visualPivot.localRotation = Quaternion.Euler(-Mathf.Atan2(verticalSpeed, speed) * Mathf.Rad2Deg, 0f, bank);
        }

        private List<Candidate> PassCandidates()
        {
            var result = new List<Candidate>();
            if (observer == null) return result;
            Vector3 f3 = Vector3.ProjectOnPlane(observer.transform.forward, Vector3.up).normalized;
            Vector2 forward = new Vector2(f3.x, f3.z), right = new Vector2(forward.y, -forward.x);
            Vector2 eye = new Vector2(observer.transform.position.x, observer.transform.position.z);
            for (int i = 0; i < airspace.orbits.Length; i++)
            {
                var orbit = airspace.orbits[i];
                for (int step = 0; step < 36; step++)
                {
                    float a = step * Mathf.PI / 18f;
                    Vector2 offset = orbit.Point(a) - eye;
                    float front = Vector2.Dot(offset, forward), side = Mathf.Abs(Vector2.Dot(offset, right));
                    Vector2 tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                    if (front < passDistance.x || front > passDistance.y || side > 10f || Mathf.Abs(Vector2.Dot(tangent, right)) < .85f) continue;
                    float score = side + Mathf.Abs(front - 35f) * .35f + Mathf.Abs(orbit.height - 7f);
                    result.Add(new Candidate { orbit = i, angle = a, score = score });
                }
            }
            result.Sort((a, b) => a.score.CompareTo(b.score));
            // The mesh field also checks tree occlusion; no collider is required on leaves.
            var visible = new List<Candidate>();
            foreach (var candidate in result)
            {
                var orbit = airspace.orbits[candidate.orbit];
                if (airspace.Sightline(observer.transform.position, airspace.Position(orbit.Point(candidate.angle), orbit.height))) visible.Add(candidate);
                if (visible.Count >= 32) break;
            }
            return visible;
        }

        private bool TryFrontPass()
        {
            foreach (var candidate in PassCandidates())
                foreach (int sign in new[] { 1, -1 })
                {
                    var orbit = airspace.orbits[candidate.orbit];
                    float entry = candidate.angle - sign * 12f / orbit.radius;
                    if (!Connect(candidate.orbit, entry, sign)) continue;
                    Mode = FlightMode.FrontPass; passRemaining = 12f / speed + 4f;
                    return true;
                }
            return false;
        }

        private void TryRoam()
        {
            for (int i = 0; i < 12; i++)
            {
                int destination = random.Next(airspace.orbits.Length);
                if (destination == orbitIndex || Vector2.Distance(airspace.orbits[destination].center, airspace.orbits[orbitIndex].center) > 40f) continue;
                if (Connect(destination, (float)random.NextDouble() * Mathf.PI * 2f, random.Next(2) == 0 ? -1 : 1)) return;
            }
        }

        private bool Connect(int destination, float entry, int sign)
        {
            Pose(out var p, out float heading, out float height, out _);
            var orbit = airspace.orbits[destination];
            foreach (var path in PteradactalFlightPath.Connections(p, heading, height, orbit.Point(entry),
                entry + sign * Mathf.PI * .5f, orbit.height, speed / (TurnRate * Mathf.Deg2Rad)))
            {
                if (path.Length < .5f || path.Length > 130f || !path.IsSafe(airspace)) continue;
                connection = path; pathDistance = 0f;
                destinationOrbit = destination; destinationAngle = entry; destinationDirection = sign;
                return true;
            }
            return false;
        }
    }
}
