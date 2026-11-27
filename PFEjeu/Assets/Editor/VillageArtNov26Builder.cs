using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Core.Village;
using UnityEngine.AI;

public static class VillageArtNov26Builder
{
    public const string Root="Assets/Arts/VillagePack_Nov26";
    public const string SceneRoot="Assets/Scenes/SceneJeux/VillageArt";
    public const string Work="C:/Users/cleme/Documents/Codex/2026-10-14/unity-plugin-unity-openai-curated-remote/work/village-art-nov26";
    static string[] paths;
    static readonly string[] paletteNames={"Timber","TimberDark","Canvas","RoofOchre","RoofSage","RoofClay","Stone","Iron","Bone","Leaves","LeavesLight","Leather","Terracotta","Water","AccentRed","RoofBlue"};
    static readonly Color[] paletteColors={new Color(.37f,.24f,.16f),new Color(.23f,.17f,.13f),new Color(.78f,.70f,.53f),new Color(.65f,.46f,.26f),new Color(.40f,.53f,.43f),new Color(.62f,.34f,.27f),new Color(.45f,.47f,.44f),new Color(.25f,.29f,.30f),new Color(.77f,.74f,.61f),new Color(.40f,.53f,.34f),new Color(.55f,.63f,.43f),new Color(.43f,.28f,.20f),new Color(.56f,.31f,.21f),new Color(.31f,.49f,.53f),new Color(.52f,.24f,.26f),new Color(.36f,.46f,.53f)};
    static Material[] palette;
    [Serializable] public class Entry { public int index; public string source,prefab;public float width,height,depth;public long triangles; }
    [Serializable] public class Registry { public List<Entry> entries=new List<Entry>(); }
    static Registry registry;
    public static void Folder(string folder){if(AssetDatabase.IsValidFolder(folder))return;string parent=folder.Substring(0,folder.LastIndexOf('/'));Folder(parent);AssetDatabase.CreateFolder(parent,folder.Substring(folder.LastIndexOf('/')+1));}
    static void Materials()
    {
        Folder(Root+"/Materials");palette=new Material[paletteNames.Length];
        for(int i=0;i<palette.Length;i++){string path=Root+"/Materials/"+paletteNames[i]+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material==null){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}material.SetColor("_BaseColor",paletteColors[i]);material.SetFloat("_Smoothness",i==13?.55f:.16f);material.SetFloat("_Metallic",i==7?.35f:0);material.enableInstancing=true;EditorUtility.SetDirty(material);palette[i]=material;}
    }
    public static void Generate()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play first");
        Directory.CreateDirectory(Work);Folder(Root+"/Meshes");Folder(Root+"/Prefabs");Materials();
        paths=AssetDatabase.FindAssets("t:Model",new[]{Root+"/Models"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();registry=new Registry();int next=0;
        EditorApplication.CallbackFunction tick=null;
        tick=()=>{try{if(next<paths.Length){Make(next,paths[next]);next++;File.WriteAllText(Work+"/generation-progress.txt",next+" / "+paths.Length);return;}EditorApplication.update-=tick;AssetDatabase.SaveAssets();File.WriteAllText(Work+"/generated.json",JsonUtility.ToJson(registry,true));File.WriteAllText(Work+"/generation-complete.txt","complete");}catch(Exception e){EditorApplication.update-=tick;File.WriteAllText(Work+"/generation-error.txt",e.ToString());Debug.LogException(e);}};
        EditorApplication.update+=tick;
    }
    static float Target(int i,Bounds b)
    {
        float[] widths={12,14,16,9,10.5f,12,14,15.5f,17,8,9,10,6,7,7.8f,8,8.8f,9.5f,7,8.2f,9};
        if(i<widths.Length)return widths[i]/Mathf.Max(b.size.x,b.size.z);
        if(i<24)return new[]{4.2f,5.1f,7f}[i-21]/b.size.y;
        if(i<27)return new[]{6.2f,7.2f,8f}[i-24]/b.size.y;
        if(i<30)return new[]{6.3f,7.2f,8.5f}[i-27]/b.size.y;
        return 1;
    }
    struct Accum { public Vector3 position,normal; public int count; }
    static int Region(int index,string path,Material material,Vector3 point,Vector3 normal,Vector3 size)
    {
        float h=point.y/Mathf.Max(.01f,size.y);
        bool building=index<30;
        if(building){
            int roof=index<3?4:index<6?15:index<9?3:index<12?5:index<18?4:5;
            if(index>=21&&index<27){if(h<.12f)return 6;if(Mathf.Abs(point.x)>size.x*.22f&&h>.3f)return 8;return normal.y>.35f?4:0;}
            if(index>=27){if(h<.58f)return 0;if(normal.y>.2f&&h>.67f)return 5;return h>.88f?8:1;}
            if(h<.055f)return 6;
            if(h>.83f&&Mathf.Abs(normal.y)<.35f)return 8;
            if(normal.y>.22f&&h>.25f)return roof;
            if(normal.y<-.25f)return 1;
            return h>.45f?roof:2;
        }
        bool tree=path.Contains("/Defense/Arbre")||path.Contains("/arbres(")||path.EndsWith("/sapin.fbx",StringComparison.OrdinalIgnoreCase);
        Color color=Color.gray;if(material!=null){if(material.HasProperty("_BaseColor"))color=material.GetColor("_BaseColor");else if(material.HasProperty("_Color"))color=material.color;}
        Color.RGBToHSV(color,out float hue,out float saturation,out float value);
        if(tree)return saturation>.18f&&h<.8f?0:9;
        if(saturation>.15f){if(value>.85f&&color.r>.7f&&color.g>.55f)return 2;return 0;}
        if(path.Contains("Barrere")||path.Contains("Panneau")||path.Contains("Caisse")||path.Contains("caisse")||path.Contains("Planches")||path.Contains("tronc")||path.Contains("souche")||path.Contains("Remorque")||path.Contains("Etabli"))return h<.1f?1:0;
        if(path.Contains("Puit"))return h<.45f?6:h>.7f?3:0;
        if(path.Contains("Pot_terre"))return 12;
        if(path.Contains("Sac_")||path.EndsWith("/sac.fbx"))return 2;
        if(path.Contains("Leather"))return h>.25f&&h<.8f?11:0;
        if(path.Contains("enclume"))return h>.58f?7:0;
        if(path.Contains("meule"))return h>.45f?6:0;
        if(path.Contains("Bassin"))return h>.8f?7:0;
        if(path.Contains("Feux_camp"))return h<.22f?6:h>.68f?7:0;
        if(path.Contains("Cuisiniere"))return 6;
        if(path.Contains("Toneau"))return 7;
        if(path.Contains("Bouclier"))return 7;
        if(path.Contains("cible"))return normal.y>.4f?0:2;
        if(path.Contains("torche"))return h>.7f?7:0;
        return 7;
    }
    static void Make(int index,string path)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);var filters=source.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();var rs=source.GetComponentsInChildren<Renderer>(true);if(rs.Length==0)return;
        var bounds=rs[0].bounds;foreach(var r in rs.Skip(1))bounds.Encapsulate(r.bounds);float factor=Target(index,bounds);var size=bounds.size*factor;
        var lists=new Dictionary<int,List<int>>();var acc=new List<Accum>();var unique=new Dictionary<(int,int,int,int,int,int,int),int>();
        float cell=index<30?.025f: .012f;
        if(index==68)cell=.035f;
        foreach(var filter in filters){var original=filter.sharedMesh;var vertices=original.vertices;var normals=original.normals;var matrix=filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;var renderer=filter.GetComponent<Renderer>();bool reverse=matrix.determinant<0;
            var baked=vertices.Select(v=>(matrix.MultiplyPoint3x4(v)-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z))*factor).ToArray();
            var ns=normals.Length==vertices.Length?normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray():Enumerable.Repeat(Vector3.up,vertices.Length).ToArray();
            for(int sub=0;sub<original.subMeshCount;sub++){var tri=original.GetTriangles(sub);var material=renderer!=null&&sub<renderer.sharedMaterials.Length?renderer.sharedMaterials[sub]:null;
                for(int t=0;t<tri.Length;t+=3){int a=tri[t],b=tri[t+1],c=tri[t+2];if(reverse){int swap=b;b=c;c=swap;}var normal=Vector3.Cross(baked[b]-baked[a],baked[c]-baked[a]).normalized;var center=(baked[a]+baked[b]+baked[c])/3;int region=Region(index,path,material,center,normal,size);if(!lists.TryGetValue(region,out var destination))lists[region]=destination=new List<int>();
                    int[] indices={a,b,c};int[] output=new int[3];
                    for(int k=0;k<3;k++){int old=indices[k];var v=baked[old];var n=ns[old];var key=(Mathf.RoundToInt(v.x/cell),Mathf.RoundToInt(v.y/cell),Mathf.RoundToInt(v.z/cell),Mathf.RoundToInt(n.x*5),Mathf.RoundToInt(n.y*5),Mathf.RoundToInt(n.z*5),region);if(!unique.TryGetValue(key,out int id)){id=acc.Count;unique.Add(key,id);acc.Add(new Accum());}var value=acc[id];value.position+=v;value.normal+=n;value.count++;acc[id]=value;output[k]=id;}
                    if(output[0]==output[1]||output[1]==output[2]||output[0]==output[2])continue;destination.AddRange(output);
                }
            }
        }
        var mesh=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32,name="Village_"+index};mesh.SetVertices(acc.Select(v=>v.position/v.count).ToList());mesh.SetNormals(acc.Select(v=>v.normal.normalized).ToList());var regions=lists.Where(p=>p.Value.Count>0).OrderBy(p=>p.Key).ToArray();mesh.subMeshCount=regions.Length;for(int s=0;s<regions.Length;s++)mesh.SetTriangles(regions[s].Value,s);mesh.RecalculateBounds();MeshUtility.Optimize(mesh);
        string meshPath=Root+"/Meshes/"+index.ToString("D2")+".asset";var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(saved!=null){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,meshPath);
        var root=new GameObject(index.ToString("D2")+"_"+(index<30?new DirectoryInfo(Path.GetDirectoryName(path)).Name+"_"+Path.GetFileNameWithoutExtension(path):Path.GetFileNameWithoutExtension(path)));root.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=root.AddComponent<MeshRenderer>();mr.sharedMaterials=regions.Select(p=>palette[p.Key]).ToArray();root.isStatic=index>=30;
        if(index>=24&&index<30){var fire=new GameObject("FirePoint").transform;fire.SetParent(root.transform,false);fire.localPosition=new Vector3(0,size.y*.78f,.35f);}
        string prefabPath=Root+"/Prefabs/"+index.ToString("D2")+".prefab";PrefabUtility.SaveAsPrefabAsset(root,prefabPath);UnityEngine.Object.DestroyImmediate(root);
        registry.entries.Add(new Entry{index=index,source=path,prefab=prefabPath,width=size.x,height=size.y,depth=size.z,triangles=regions.Sum(p=>(long)p.Value.Count/3)});
    }

    static Registry Data()=>registry??(registry=JsonUtility.FromJson<Registry>(File.ReadAllText(Work+"/generated.json")));
    public static GameObject Prefab(int index)=>AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+index.ToString("D2")+".prefab");
    public static float Ground(float x,float z)=>Terrain.activeTerrain.SampleHeight(new Vector3(x,0,z))+Terrain.activeTerrain.transform.position.y;
    static GameObject Group(string name,Transform parent){var go=new GameObject(name);go.transform.SetParent(parent,false);return go;}
    static Bounds LocalBounds(GameObject root){var rs=root.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;}
    public static GameObject Place(int index,Transform parent,float x,float z,float yaw=0,float scale=1,string name=null,bool collider=false)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(Prefab(index),parent);if(name!=null)go.name=name;go.transform.SetPositionAndRotation(new Vector3(x,Ground(x,z)+.025f,z),Quaternion.Euler(0,yaw,0));go.transform.localScale=Vector3.one*scale;
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
        if(collider){var e=Data().entries.Single(v=>v.index==index);var box=go.AddComponent<BoxCollider>();box.size=new Vector3(e.width,e.height,e.depth);box.center=Vector3.up*(e.height*.5f);}
        return go;
    }
    public static string ApplyBuildings()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException();Folder(SceneRoot+"/Definitions");
        var specs=new[]{("HotelDeVille",6,-30f,-132f,45f),("Forge",3,-12.68f,-81.48f,120f),("Ferme",0,27.38f,-59.22f,95f),("Taverne",9,20f,-114f,100f),("Scierie",9,-32.7f,-67.34f,150f),("Mine",12,-53.62f,-55.97f,145f),("Poste_Expedition",18,50.63f,-76.72f,135f),("Camp_Recrutement",18,66f,-116f,210f)};
        foreach(var spec in specs){var b=UnityEngine.Object.FindObjectsByType<Building>().Single(v=>v.name==spec.Item1);var so=new SerializedObject(b);string defPath=SceneRoot+"/Definitions/"+b.name+".asset";if(AssetDatabase.LoadAssetAtPath<BuildingDefinition>(defPath)==null)AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(b.Definition),defPath);var definition=AssetDatabase.LoadAssetAtPath<BuildingDefinition>(defPath);var serialized=new SerializedObject(definition);var levels=serialized.FindProperty("levels");for(int i=0;i<levels.arraySize;i++)levels.GetArrayElementAtIndex(i).FindPropertyRelative("visualPrefab").objectReferenceValue=Prefab(spec.Item2+Mathf.Min(i,2));serialized.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(definition);
            Undo.RecordObject(b.transform,"Aménager les bâtiments du village");b.transform.position=new Vector3(spec.Item3,Ground(spec.Item3,spec.Item4)+.02f,spec.Item4);b.transform.rotation=Quaternion.Euler(0,spec.Item5,0);var parentScale=b.transform.parent!=null?b.transform.parent.lossyScale:Vector3.one;b.transform.localScale=new Vector3(1/parentScale.x,1/parentScale.y,1/parentScale.z);
            foreach(var renderer in b.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            var pivot=Group("Architecture",b.transform);var visual=(GameObject)PrefabUtility.InstantiatePrefab(Prefab(spec.Item2+Mathf.Min(b.CurrentLevel-1,2)),pivot.transform);
            so.FindProperty("definition").objectReferenceValue=definition;so.FindProperty("visualRoot").objectReferenceValue=pivot.transform;so.FindProperty("_currentVisual").objectReferenceValue=visual;so.ApplyModifiedPropertiesWithoutUndo();
            var e=Data().entries.Single(v=>v.index==spec.Item2);var box=b.GetComponent<BoxCollider>();if(box==null)box=b.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(e.width*.78f,Mathf.Min(4,e.height*.7f),e.depth*.78f);box.center=Vector3.up*(box.size.y*.5f);box.enabled=true;box.isTrigger=false;
            var highlight=b.GetComponent<BuildingHighlight>();if(highlight!=null){var hs=new SerializedObject(highlight);hs.FindProperty("renderers").arraySize=0;hs.ApplyModifiedPropertiesWithoutUndo();}
        }
        var forge=GameObject.Find("Forgeron");if(forge!=null){forge.transform.position=new Vector3(-5.8f,Ground(-5.8f,-82.8f),-82.8f);forge.transform.rotation=Quaternion.Euler(0,110,0);}
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());AssetDatabase.SaveAssets();return "Eight interactive buildings equipped with level art; roots and gameplay references preserved.";
    }
}

