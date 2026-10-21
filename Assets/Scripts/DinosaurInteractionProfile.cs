using System;
using UnityEngine;

namespace JurassicWorld
{
    public enum InteractionMotionRole { LookForward, LookLeft, LookRight, StartRun, Run, StopRun, TurnLeft, TurnRight, Walk,
        Investigate, InvestigateOut, BigRoarStep, ChaseRoar, Roar, CaveBite, CaveBiteOut, Turn180Look,
        FollowWalk, BiteCloseLeft, BiteCloseRight, IdleAttack, LookForwardTwo,
        SideRoarLeft, SideRoarRight, SideStartRunLeft, SideStartRunRight, SideRunLow }

    [CreateAssetMenu(menuName = "Jurassic World/Interaction Animation Profile")]
    public sealed class DinosaurInteractionProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Motion
        {
            public InteractionMotionRole role;
            public string sourceName;
            public AnimationClip clip;
            public AnimationCurve speed = AnimationCurve.Constant(0f, 1f, 0f);
            public float groundSpeed;
            public float turnDegrees;
            public FootFrame[] footFrames = Array.Empty<FootFrame>();
            public float blendDuration = .15f;
            public float turnPlaybackSpeed = 1f;
            public DinosaurCoreBody.Band[] bodyBands = Array.Empty<DinosaurCoreBody.Band>();
            public PlayerPose[] playerPoses = Array.Empty<PlayerPose>();
            // Body/neck/head only, sampled independently of foot/root calibration.
            public PlayerPose[] obstaclePoses = Array.Empty<PlayerPose>();
            [NonSerialized] private string stateName;
            public string StateName => stateName ?? (stateName = "Curiosity_" + role);
        }
        [Serializable]
        public struct FootFrame
        {
            public float time;
            public Vector3 leftSole, rightSole;
            public Vector3 leftHeading, rightHeading, leftKnee, rightKnee;
            public float leftGroundHeight, rightGroundHeight;
            public bool leftContact, rightContact;
            public Vector2 rootOffset;
            public float yaw;
            public float authoredYaw;
            [NonSerialized] public DinosaurCoreBody.Band[] playerBodyBands;
        }
        [Serializable]
        public struct PlayerPose { public float time; public DinosaurCoreBody.Band[] bands; }
        public Motion[] motions = Array.Empty<Motion>();
        public Motion Find(InteractionMotionRole role)
        {
            foreach (var motion in motions) if (motion.role == role && motion.clip != null) return motion;
            return null;
        }
    }
}
