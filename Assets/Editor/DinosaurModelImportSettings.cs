using UnityEditor;

namespace JurassicWorld.Editor
{
    public sealed class DinosaurModelImportSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Dinosaurs/") ||
                assetPath.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) < 0)
                return;

            ((TextureImporter)assetImporter).textureType = TextureImporterType.NormalMap;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Resources/Dinosaurs/") || !assetPath.EndsWith(".fbx"))
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
