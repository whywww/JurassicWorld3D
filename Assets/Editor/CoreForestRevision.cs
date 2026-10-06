using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JurassicWorld.Editor
{
    public static class CoreForestRevision
    {
        public static void Verify()
        {
            CentralForestDensification.VerifyOwnershipAndBakeRestore();
            CentralForestDensification.VerifyIdempotence();
        }
    }
}
