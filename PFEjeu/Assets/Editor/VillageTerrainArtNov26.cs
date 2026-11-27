using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Splines;

public static class VillageTerrainArtNov26
{
    static bool Inside(Vector2 p,Vector2[] polygon){bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)if((polygon[i].y>p.y)!=(polygon[j].y>p.y)&&p.x<(polygon[j].x-polygon[i].x)*(p.y-polygon[i].y)/(polygon[j].y-polygon[i].y)+polygon[i].x)inside=!inside;return inside;}
    public static string Paint()
    {
        var terrain=Terrain.activeTerrain;var original=terrain.terrainData;string path=VillageArtNov26Builder.SceneRoot+"/VillageTerrain.asset";
        if(AssetDatabase.LoadAssetAtPath<TerrainData>(path)==null)AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original),path);
        var data=AssetDatabase.LoadAssetAtPath<TerrainData>(path);terrain.terrainData=data;terrain.GetComponent<TerrainCollider>().terrainData=data;
        var routeJson=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(VillageArtNov26Builder.Work+"/routes.json"));var routes=routeJson.Select(v=>new Vector2((float)v["x"],(float)v["z"])).ToArray();
        var pads=new List<Bounds>();
        foreach(var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())if(filter.sharedMesh!=null&&filter.sharedMesh.name.StartsWith("Village_")&&int.TryParse(filter.sharedMesh.name.Substring(8),out int id)&&id<30){var renderer=filter.GetComponent<Renderer>();if(renderer!=null){var b=renderer.bounds;b.Expand(new Vector3(2,0,2));pads.Add(b);}}
        var zone=UnityEngine.Object.FindObjectsByType<SplineContainer>().Single(s=>s.name=="zone village");var poly=Enumerable.Range(0,160).Select(i=>{var p=(Vector3)zone.EvaluatePosition(i/159f);return new Vector2(p.x,p.z);}).ToArray();
        int grass=Array.FindIndex(data.terrainLayers,l=>l!=null&&l.name.IndexOf("grass",StringComparison.OrdinalIgnoreCase)>=0);if(grass<0)grass=0;
        int dirt=Array.FindIndex(data.terrainLayers,l=>l!=null&&(l.name.IndexOf("dirt",StringComparison.OrdinalIgnoreCase)>=0||l.name.IndexOf("ground",StringComparison.OrdinalIgnoreCase)>=0));if(dirt<0)dirt=Mathf.Min(9,data.alphamapLayers-1);
        int stone=Array.FindIndex(data.terrainLayers,l=>l!=null&&(l.name.IndexOf("cobble",StringComparison.OrdinalIgnoreCase)>=0||l.name.IndexOf("pav",StringComparison.OrdinalIgnoreCase)>=0||l.name.IndexOf("gravel",StringComparison.OrdinalIgnoreCase)>=0));if(stone<0)stone=dirt;
        var pos=terrain.transform.position;var size=data.size;int x0=Mathf.Clamp(Mathf.FloorToInt((-87-pos.x)/size.x*data.alphamapWidth),0,data.alphamapWidth-1),z0=Mathf.Clamp(Mathf.FloorToInt((-176-pos.z)/size.z*data.alphamapHeight),0,data.alphamapHeight-1);int x1=Mathf.Clamp(Mathf.CeilToInt((105-pos.x)/size.x*data.alphamapWidth),0,data.alphamapWidth),z1=Mathf.Clamp(Mathf.CeilToInt((-32-pos.z)/size.z*data.alphamapHeight),0,data.alphamapHeight);var alpha=data.GetAlphamaps(x0,z0,x1-x0,z1-z0);
        var cleared=new List<Vector2>();int painted=0;
        for(int z=0;z<z1-z0;z++)for(int x=0;x<x1-x0;x++){
            float nx=(x+x0)/(float)data.alphamapWidth,nz=(z+z0)/(float)data.alphamapHeight;var p=new Vector2(pos.x+nx*size.x,pos.z+nz*size.z);if(!Inside(p,poly)||data.GetSteepness(nx,nz)>18)continue;
            float routeDistance=routes.Length>0?Mathf.Sqrt(routes.Min(v=>(v-p).sqrMagnitude)):1000;
            float onRoute=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.65f,3.1f,routeDistance));float onPad=0;
            foreach(var pad in pads){float dx=Mathf.Max(pad.min.x-p.x,0,p.x-pad.max.x),dz=Mathf.Max(pad.min.z-p.y,0,p.y-pad.max.z);onPad=Mathf.Max(onPad,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,1.8f,Mathf.Sqrt(dx*dx+dz*dz))));}
            float plaza=Mathf.Max(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,7,Vector2.Distance(p,new Vector2(-42,-137)))),1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(4,7,Vector2.Distance(p,new Vector2(59,-129)))));
            float gravel=Mathf.Max(onRoute*.30f,plaza*.75f);float soil=Mathf.Max(onRoute*.55f,onPad*.85f)*(1-gravel);float green=1-gravel-soil;float strength=.92f;
            for(int l=0;l<data.alphamapLayers;l++){float target=(l==grass?green:0)+(l==dirt?soil:0)+(l==stone?gravel:0);alpha[z,x,l]=Mathf.Lerp(alpha[z,x,l],target,strength);}if(onRoute>.65f||onPad>.7f)cleared.Add(p);painted++;
        }
        data.SetAlphamaps(x0,z0,alpha);
        bool Obstructs(Vector2 p){if(pads.Any(b=>p.x>=b.min.x&&p.x<=b.max.x&&p.y>=b.min.z&&p.y<=b.max.z))return true;return routes.Any(v=>(v-p).sqrMagnitude<4);}
        var trees=data.treeInstances.Where(tree=>{var p=new Vector2(pos.x+tree.position.x*size.x,pos.z+tree.position.z*size.z);return !Obstructs(p);}).ToArray();data.SetTreeInstances(trees,true);
        int detailX0=Mathf.Clamp(Mathf.FloorToInt((-87-pos.x)/size.x*data.detailWidth),0,data.detailWidth-1),detailZ0=Mathf.Clamp(Mathf.FloorToInt((-176-pos.z)/size.z*data.detailHeight),0,data.detailHeight-1),detailX1=Mathf.Clamp(Mathf.CeilToInt((105-pos.x)/size.x*data.detailWidth),0,data.detailWidth),detailZ1=Mathf.Clamp(Mathf.CeilToInt((-32-pos.z)/size.z*data.detailHeight),0,data.detailHeight);
        int dw=detailX1-detailX0,dh=detailZ1-detailZ0;var clearMask=new bool[dh,dw];for(int z=0;z<dh;z++)for(int x=0;x<dw;x++)clearMask[z,x]=Obstructs(new Vector2(pos.x+(detailX0+x)/(float)data.detailWidth*size.x,pos.z+(detailZ0+z)/(float)data.detailHeight*size.z));
        for(int layer=0;layer<data.detailPrototypes.Length;layer++){var detail=data.GetDetailLayer(detailX0,detailZ0,dw,dh,layer);for(int z=0;z<dh;z++)for(int x=0;x<dw;x++)if(clearMask[z,x])detail[z,x]=0;data.SetDetailLayer(detailX0,detailZ0,layer,detail);}
        data.SetBaseMapDirty();terrain.Flush();EditorUtility.SetDirty(data);AssetDatabase.SaveAssetIfDirty(data);EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
        return "Village ground dressed: "+painted+" texture cells; grass between streets; building/path vegetation cleared. Layers: "+grass+", "+dirt+", "+stone;
    }
}
