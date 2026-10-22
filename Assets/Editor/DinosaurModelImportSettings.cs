using UnityEditor;

namespace JurassicWorld.Editor
{
    public sealed class DinosaurModelImportSettings : AssetPostprocessor
    {
        // These sources used the legacy Resources importer. Vendor packages and
        // converted models have their own settings; never override their rigs.
        private bool IsLegacySource =>
            assetPath.StartsWith("Assets/Dinosaurs/TRex/Source/") ||
            assetPath.StartsWith("Assets/Dinosaurs/Velociraptor/Source/Sketchfab/") ||
            assetPath.StartsWith("Assets/Dinosaurs/Rajasaurus/Source/") ||
            assetPath.StartsWith("Assets/Dinosaurs/Argentinosaurus/Source/") ||
            assetPath.StartsWith("Assets/Dinosaurs/Baurusuchus/Source/");

        private void OnPreprocessTexture()
        {
            if (!IsLegacySource || !assetImporter.importSettingsMissing ||
                assetPath.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
        }

        private void OnPreprocessModel()
        {
            if (!IsLegacySource || !assetImporter.importSettingsMissing || !assetPath.EndsWith(".fbx"))
                return;

            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = true;
            importer.animationType = ModelImporterAnimationType.Legacy;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialSearch = ModelImporterMaterialSearch.Everywhere;
        }
    }
}
