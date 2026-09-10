using UnityEngine;

namespace JurassicWorld
{
    public sealed class DinosaurAgent : MonoBehaviour
    {
        public enum DinosaurState
        {
            Idle,
            RandomWalk,
            Approaching
        }

        public Camera ViewCamera { get; set; }
        public string DisplayName { get; set; } = "恐龙";
        public float MovementHeight { get; set; }
        public float ViewFocusHeight { get; set; } = 2.5f;
        public float VisualYaw { get; set; }
        public float WalkSpeed { get; set; } = 1.15f;
        public float ApproachSpeed { get; set; } = 2.8f;
        public float MoveAnimationGroundSpeed { get; set; } = 1.15f;
        public DinosaurState CurrentState { get; private set; } = DinosaurState.Idle;
        public bool HasBeenSummoned { get; private set; }

        private const float WanderRadius = 45f;
        private const float WanderTurnAngle = 70f;
        private const float WanderStepDistance = 18f;
        private const float MinimumTurnRate = 22f;
        private const float MaximumTurnRate = 55f;
        private const float PassDistance = 9f;
        private const float PassSideOffset = 6f;
        private readonly System.Random random = new System.Random(314159);
        private Vector3 spawnPoint;
        private Vector3 target;
        private Vector3 approachDestination;
        private float stateTimer;
        private Animation legacyAnimation;
        private string activeClip;
        private string idleClip;
        private string moveClip;
        private static int lastSummonInputFrame = -1;

        private void Awake()
        {
            spawnPoint = transform.position;
            BeginIdle();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
                TrySummonVisibleDinosaur();

            switch (CurrentState)
            {
                case DinosaurState.Idle:
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f)
                        BeginRandomWalk();
                    break;
                case DinosaurState.RandomWalk:
                    MoveTowards(target, WalkSpeed);
                    stateTimer -= Time.deltaTime;
                    if (stateTimer <= 0f || FlatDistance(transform.position, target) < 0.5f)
                        BeginIdle();
                    break;
                case DinosaurState.Approaching:
                    UpdateApproach();
                    break;
            }
        }

        public void BindAnimation(Animation animation, string idleHint = "idle", string moveHint = "run")
        {
            legacyAnimation = animation;
            idleClip = FindClip(idleHint, "idle", "stand", "wait");
            moveClip = FindClip(moveHint, "run", "walk", "fly", "animation");
            if (string.IsNullOrEmpty(idleClip))
                idleClip = moveClip;
            if (string.IsNullOrEmpty(moveClip))
                moveClip = idleClip;
            if (CurrentState == DinosaurState.Idle)
                PlayClip(idleClip);
            else
                PlayMovementClip(CurrentState == DinosaurState.Approaching ? ApproachSpeed : WalkSpeed);
        }

        public bool Summon()
        {
            if (CurrentState == DinosaurState.Approaching || ViewCamera == null)
                return false;

            Vector3 cameraPosition = ViewCamera.transform.position;
            cameraPosition.y = transform.position.y;
            Vector3 runDirection = cameraPosition - transform.position;
            runDirection.y = 0f;
            if (runDirection.sqrMagnitude < 0.01f)
                return false;

            Vector3 cameraRight = Vector3.ProjectOnPlane(ViewCamera.transform.right, Vector3.up).normalized;
            float side = Vector3.Dot(transform.position - cameraPosition, cameraRight) < 0f ? -1f : 1f;
            approachDestination = cameraPosition + runDirection.normalized * PassDistance
                + cameraRight * side * PassSideOffset;
            HasBeenSummoned = true;
            CurrentState = DinosaurState.Approaching;
            stateTimer = 30f;
            PlayMovementClip(ApproachSpeed);
            HudController.ShowStatus(DisplayName + "正在穿越视角…");
            return true;
        }

        private static void TrySummonVisibleDinosaur()
        {
            if (lastSummonInputFrame == Time.frameCount)
                return;

            lastSummonInputFrame = Time.frameCount;
            DinosaurAgent selected = null;
            float bestScreenDistance = float.MaxValue;
            foreach (DinosaurAgent candidate in FindObjectsOfType<DinosaurAgent>())
            {
                if (candidate.ViewCamera == null || candidate.CurrentState == DinosaurState.Approaching)
                    continue;

                Vector3 viewport = candidate.ViewCamera.WorldToViewportPoint(
                    candidate.transform.position + Vector3.up * candidate.ViewFocusHeight);
                bool visible = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f
                    && viewport.y >= 0f && viewport.y <= 1f;
                if (!visible)
                    continue;

                float screenDistance = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f).sqrMagnitude;
                if (screenDistance < bestScreenDistance)
                {
                    selected = candidate;
                    bestScreenDistance = screenDistance;
                }
            }

            if (selected != null)
                selected.Summon();
        }

        private void UpdateApproach()
        {
            if (ViewCamera == null)
            {
                BeginIdle();
                return;
            }

            stateTimer -= Time.deltaTime;
            if (FlatDistance(transform.position, approachDestination) <= 0.75f || stateTimer <= 0f)
            {
                CompleteSummon();
                return;
            }

            MoveTowards(approachDestination, ApproachSpeed);
        }

        private void CompleteSummon()
        {
            HasBeenSummoned = false;
            HudController.ShowStatus(DisplayName + "已穿过视角，恢复自由活动");
            BeginRandomWalk();
        }

        private void BeginIdle()
        {
            CurrentState = DinosaurState.Idle;
            stateTimer = Range(3f, 7f);
            PlayClip(idleClip);
        }

        private void BeginRandomWalk()
        {
            CurrentState = DinosaurState.RandomWalk;
            stateTimer = Range(4f, 8f);
            float angle = Range(-WanderTurnAngle, WanderTurnAngle);
            float distance = Range(8f, WanderStepDistance);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * GetVisualForward();
            target = transform.position + direction * distance;
            Vector3 spawnOffset = target - spawnPoint;
            spawnOffset.y = 0f;
            if (spawnOffset.magnitude > WanderRadius)
                target = spawnPoint + spawnOffset.normalized * WanderRadius;
            target.y = MovementHeight;
            PlayMovementClip(WalkSpeed);
        }

        private void MoveTowards(Vector3 destination, float speed)
        {
            Vector3 direction = destination - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return;

            Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, -VisualYaw, 0f);
            float turnRate = Mathf.Lerp(MinimumTurnRate, MaximumTurnRate,
                Mathf.InverseLerp(0.7f, 5.5f, speed));
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desiredRotation, turnRate * Time.deltaTime);
            Vector3 visualForward = GetVisualForward();
            transform.position += visualForward * speed * Time.deltaTime;
            transform.position = new Vector3(transform.position.x, MovementHeight, transform.position.z);
        }

        private Vector3 GetVisualForward()
        {
            return transform.rotation * Quaternion.Euler(0f, VisualYaw, 0f) * Vector3.forward;
        }

        private void PlayMovementClip(float groundSpeed)
        {
            float referenceSpeed = Mathf.Max(0.01f, MoveAnimationGroundSpeed);
            PlayClip(moveClip, groundSpeed / referenceSpeed);
        }

        private void PlayClip(string clipName, float playbackSpeed = 1f)
        {
            if (legacyAnimation == null || string.IsNullOrEmpty(clipName))
                return;

            AnimationState state = legacyAnimation[clipName];
            if (state == null)
            {
                Debug.LogWarning("动画片段不存在：" + clipName, this);
                return;
            }

            state.wrapMode = WrapMode.Loop;
            state.speed = Mathf.Max(0.01f, playbackSpeed);
            if (activeClip == clipName)
                return;

            legacyAnimation.CrossFade(clipName, 0.35f);
            activeClip = clipName;
        }

        private string FindClip(params string[] hints)
        {
            if (legacyAnimation == null)
                return null;

            string first = null;
            string rootMotionMatch = null;
            foreach (AnimationState state in legacyAnimation)
            {
                if (first == null)
                    first = state.name;
                foreach (string hint in hints)
                {
                    if (!string.IsNullOrEmpty(hint) && state.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool isRootMotion = state.name.IndexOf("_RM", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isRootMotion)
                            return state.name;
                        if (rootMotionMatch == null)
                            rootMotionMatch = state.name;
                    }
                }
            }
            return rootMotionMatch ?? first;
        }

        private float Range(float min, float max)
        {
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
