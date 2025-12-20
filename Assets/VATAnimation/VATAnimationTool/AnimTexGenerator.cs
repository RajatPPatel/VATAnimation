using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class AnimTexGenerator : MonoBehaviour
{
    private SkinnedMeshRenderer _meshRenderer;
    private Mesh _tempMesh;
    private Animator _animator;
    [SerializeField] private List<AnimationClip> clips;

    private Mesh _fileMesh;
    private String _pathToMesh;
    private String _selectedFolderPath;
    
#if UNITY_EDITOR
    private bool SelectSaveFolder()
    {
        // Show native folder picker dialog
        string absolutePath = EditorUtility.SaveFolderPanel(
            "Select folder to save baked animation assets",
            Application.dataPath,
            "Anim Baker"
        );
        
        if (string.IsNullOrEmpty(absolutePath))
        {
            Debug.LogWarning("No folder selected. Operation cancelled.");
            return false;
        }
        
        // Convert absolute path to relative Assets path
        if (!absolutePath.StartsWith(Application.dataPath))
        {
            EditorUtility.DisplayDialog(
                "Invalid Folder",
                "Please select a folder inside the Assets directory.",
                "OK"
            );
            return false;
        }
        
        // Convert to relative path (Assets/...)
        _selectedFolderPath = "Assets" + absolutePath.Substring(Application.dataPath.Length);
        
        // Ensure the path ends without a slash for consistency
        _selectedFolderPath = _selectedFolderPath.TrimEnd('/');
        
        return true;
    }
    
    private void Init()
    {
        _animator = GetComponent<Animator>();
        _meshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        _tempMesh = new Mesh();
        
        transform.position = Vector3.zero; 
        _animator.enabled = false;

        // Create a subfolder for this object inside the selected folder
        var objectFolderPath = _selectedFolderPath + "/" + gameObject.name;
        if (AssetDatabase.IsValidFolder(objectFolderPath) == false)
        {
            print("Creating folder: " + objectFolderPath);
            AssetDatabase.CreateFolder(_selectedFolderPath, gameObject.name);
        }
        var pathToObjectFolder = objectFolderPath + "/";
        
        //get the mesh from the files so changes are saved when scene/editor restarts
        var meshPath = pathToObjectFolder + gameObject.name + "_mesh" + ".mesh";
        Mesh m = new Mesh();
        _meshRenderer.BakeMesh(m);
        print(meshPath);
        AssetDatabase.CreateAsset(m, meshPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        _fileMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
    }

    [ContextMenu("Generate All")]
    public void GenerateAll()
    {
        // Ask user to select save folder
        if (!SelectSaveFolder())
        {
            return;
        }
        
        Init();
        
        int verticesCount = _meshRenderer.sharedMesh.vertices.Length;
        int totalLengthInFrames = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            totalLengthInFrames += GetLengthInFrames(clips[i]);
        }
        
        float maxDist = -1;
        foreach (var clip in clips)
        {
            maxDist = GetMaxVertDist(totalLengthInFrames, maxDist, clip);
        }
        
        var pathToFolder = _selectedFolderPath + "/" + gameObject.name + "/";
        var pathToFolderTextures = _selectedFolderPath + "/" + gameObject.name + "/";

        var posTexture = new Texture2D(verticesCount, totalLengthInFrames + clips.Count, TextureFormat.RGBAFloat, false);
        var normTexture = new Texture2D(verticesCount, totalLengthInFrames + clips.Count, TextureFormat.RGBAFloat, false);
        GenerateTextures(maxDist, posTexture, normTexture, pathToFolderTextures);

        GenerateUvs(verticesCount, totalLengthInFrames);

        var mat = GenerateMaterial(maxDist, pathToFolder);
        
        GenerateObj(mat);
    }

    [ContextMenu("Generate Mesh & Material")]
    public void GenerateMesh()
    {
        // Ask user to select save folder
        if (!SelectSaveFolder())
        {
            return;
        }
        
        Init();
        
        int verticesCount = _meshRenderer.sharedMesh.vertices.Length;
        int totalLengthInFrames = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            totalLengthInFrames += GetLengthInFrames(clips[i]);
        }
        
        float maxDist = -1;
        foreach (var clip in clips)
        {
            maxDist = GetMaxVertDist(totalLengthInFrames, maxDist, clip);
        }
        
        var pathToFolder = _selectedFolderPath + "/" + gameObject.name + "/";

        GenerateUvs(verticesCount, totalLengthInFrames);

        var mat = GenerateMaterial(maxDist, pathToFolder);
        
        GenerateObj(mat);
    }

    [ContextMenu("Generate Only Textures")]
    public void GenerateOnlyTextures()
    {
        // Ask user to select save folder
        if (!SelectSaveFolder())
        {
            return;
        }
        if(_meshRenderer == null)
        {
            Debug.LogError("MeshRenderer not found");
            return;
        }
        
        int verticesCount = _meshRenderer.sharedMesh.vertices.Length;
        int totalLengthInFrames = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            totalLengthInFrames += GetLengthInFrames(clips[i]);
        }
        
        float maxDist = -1;
        foreach (var clip in clips)
        {
            maxDist = GetMaxVertDist(totalLengthInFrames, maxDist, clip);
        }
        
        var pathToFolder = _selectedFolderPath + "/" + gameObject.name + "/";

        var posTexture = new Texture2D(verticesCount, totalLengthInFrames + clips.Count, TextureFormat.RGBAFloat, false);
        var normTexture = new Texture2D(verticesCount, totalLengthInFrames + clips.Count, TextureFormat.RGBAFloat, false);
        GenerateTextures(maxDist, posTexture, normTexture, pathToFolder);
    }

    private static int GetLengthInFrames(AnimationClip clip)
    {
        return (int)(clip.length * clip.frameRate);
    }

    private void GenerateObj(Material mat)
    {
        var obj = new GameObject();
        Undo.RegisterCreatedObjectUndo(obj, "Created obj");
        obj.transform.position = transform.position;
        var objFilter = obj.AddComponent<MeshFilter>();
        var objMat = obj.AddComponent<MeshRenderer>();

        objFilter.mesh = _fileMesh;
        objMat.material = mat;
        obj.name = transform.name + "_baked";
    }

    private Material GenerateMaterial(float maxDist, String pathToFolder)
    {
        var mat = new Material(Shader.Find("Shader Graphs/VATShaderGraph"));
        mat.SetFloat("_MaxDist", maxDist);
        mat.SetFloat("_SpeedController", 1f/ clips.Count);

        var savedPosTexture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(pathToFolder + gameObject.name + "_PositionTex.exr");
        var savedNormTexture =
            AssetDatabase.LoadAssetAtPath<Texture2D>(pathToFolder + gameObject.name + "_NormalTex.exr");

        mat.SetTexture("_MainTex", _meshRenderer.sharedMaterial.mainTexture);
        mat.SetTexture("_PosTex1", savedPosTexture);
        mat.SetTexture("_NormTex1", savedNormTexture);
        mat.enableInstancing = true;

        AssetDatabase.CreateAsset(mat, pathToFolder + gameObject.name + ".mat");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return mat;
    }

    private void GenerateUvs(int verticesCount, int lengthInFrames)
    {
        Vector2[] uvs = new Vector2[verticesCount];
        float pixelWidth = 1f / verticesCount;
        for (int j = 0; j < verticesCount; j++)
        {
            uvs[j] = new Vector2(pixelWidth * j + pixelWidth / 2, 1f / (lengthInFrames));
        }

        _fileMesh.uv2 = uvs;
        var guid = AssetDatabase.GUIDFromAssetPath(_pathToMesh);
        AssetDatabase.SaveAssetIfDirty(guid); //so it works when the scene restarts
    }

    private void GenerateTextures(float maxDist, Texture2D posTexture, Texture2D normTexture, String pathToFolder)
    {
        int startRange = 0;
        int endRange = GetLengthInFrames(clips[0]);

        int n = 0;
        //create texture
        while (n < clips.Count)
        {
            for (int i = startRange; i <= endRange; i++)
            {
                int lengthInFrames = endRange - startRange;
                
                clips[n].SampleAnimation(gameObject, (clips[n].length / lengthInFrames) * (i-startRange));

                _tempMesh.Clear();
                _meshRenderer.BakeMesh(_tempMesh);
                var verts = _tempMesh.vertices;

                for (int j = 0; j < verts.Length; j++)
                {
                    //Vertex pos ranges from 0 to 1
                    var compressedVertPos = verts[j] / maxDist;
                    var remappedVertPos = (compressedVertPos + Vector3.one) * 0.5f;

                    if (j == 0 && i == 0) print(remappedVertPos);
                    //Color texture
                    posTexture.SetPixel(j, i,
                        new Color(remappedVertPos.x, remappedVertPos.y, remappedVertPos.z));

                    //Same thing for normals 
                    var remappedNormals = (_tempMesh.normals[j] + Vector3.one) * 0.5f;

                    normTexture.SetPixel(j, i,
                        new Color(remappedNormals.x, remappedNormals.y, remappedNormals.z));
                }
            }
            startRange = endRange + 1;
            n++;
            if (n >= clips.Count) break;
            endRange += GetLengthInFrames(clips[n]) + 1;
        }


        //save the textures to file
        SaveTexture(pathToFolder, posTexture, "PositionTex");
        SaveTexture(pathToFolder, normTexture, "NormalTex");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private float GetMaxVertDist(int lengthInFrames, float maxDist, AnimationClip clip)
    {
        for (int i = 0; i <= lengthInFrames; i++)
        {
            clip.SampleAnimation(gameObject, (clip.length / lengthInFrames) * i);

            _meshRenderer.BakeMesh(_tempMesh);
            var verts = _tempMesh.vertices;

            foreach (var vertPos in verts)
            {
                var dist = Vector3.Distance(vertPos, (transform.position));
                if (dist > maxDist)
                {
                    maxDist = dist;
                }
            }
        }

        return maxDist;
    }

    private void SaveTexture(String path, Texture2D texture, String name)
    {
        texture.Apply();
        var texBytes = texture.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
        var texturePath = path + gameObject.name + "_" + name + ".exr";
        File.WriteAllBytes(texturePath, texBytes);
        
        // Import and configure the texture settings
        AssetDatabase.ImportAsset(texturePath);
        ConfigureTextureImportSettings(texturePath);
    }
    
    private void ConfigureTextureImportSettings(string texturePath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer != null)
        {
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
    }
#endif
}
