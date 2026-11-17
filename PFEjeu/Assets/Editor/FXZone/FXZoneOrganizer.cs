#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core.FXZone.Editor
{
    /// <summary>
    /// Range les FX de ZONE_07_FX dans des sous-zones par type (Zone_Feu, Zone_Glace, ...) avec une
    /// délimitation au sol (cadre coloré) et un titre. Idempotent : les Zone_* existantes sont recréées.
    /// </summary>
    public static class FXZoneOrganizer
    {
        [MenuItem("Tools/FX Zone/Ranger les FX par zones")]
        public static void Organize()
        {
            string result = Run();
            Debug.Log("[FXZone] " + result);
        }

        private static string Run()
        {
            var root = GameObject.Find("ZONE_07_FX").transform;
            // (nom, titre, cx, cz, sx, sz, couleur, enfants)
            var defs = new (string n,string title,float cx,float cz,float sx,float sz,Color col,string[] kids)[]{
             ("Zone_Torche","TORCHE",60.5f,79.75f,12,20,new Color(1f,.75f,.2f),new[]{"Torch_Realistic_Flame_V2"}),
             ("Zone_Brouillard","BROUILLARD",73.5f,79.75f,12,20,new Color(.7f,.8f,.9f),new[]{"GroundFogZone"}),
             ("Zone_Feu","FEU",86.5f,79.75f,12,20,new Color(1f,.4f,.1f),new[]{"FireballVFX","FireLoop_Shooter"}),
             ("Zone_Glace","GLACE",99.5f,79.75f,12,20,new Color(.4f,.8f,1f),new[]{"IceVFX_Loop"}),
             ("Zone_Construction","CONSTRUCTION",112.5f,79.75f,12,20,new Color(.9f,.8f,.3f),new[]{"Niv_BuildUpgrade"}),
             ("Zone_Etats","ETATS",125.5f,79.75f,12,20,new Color(.4f,1f,.5f),new[]{"Etats_Loop"}),
             ("Zone_Portail","PORTAIL / ESPRIT",138.5f,79.75f,12,20,new Color(.7f,.4f,1f),new[]{"Portal_VFX"}),
             ("Zone_Desintegration","DESINTEGRATION",151.5f,79.75f,12,20,new Color(1f,.3f,.5f),new[]{"DisintegrationVFX"}),
            };
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var sb = new System.Text.StringBuilder();
            string folder="Assets/Materials";
            foreach(var d in defs){
              var old = root.Find(d.n); if(old!=null){ for(int i=old.childCount-1;i>=0;i--){ var ch=old.GetChild(i); if(ch.name!="Delimitation"&&ch.name!="Titre"&&ch.name!="Murs") Undo.SetTransformParent(ch,root,"Sortir FX"); } Undo.DestroyObjectImmediate(old.gameObject);}
              var g = new GameObject(d.n); Undo.RegisterCreatedObjectUndo(g,"FX zone");
              g.transform.SetParent(root,false); g.transform.position=new Vector3(d.cx,0,d.cz);
              string matPath=folder+"/FXZone_"+d.n+".mat";
              var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
              if(mat==null){ mat=new Material(shader); AssetDatabase.CreateAsset(mat,matPath);} 
              mat.SetColor("_BaseColor",d.col); EditorUtility.SetDirty(mat);
              var border=new GameObject("Delimitation"); border.transform.SetParent(g.transform,false);
              void Strip(string nm,Vector3 pos,Vector3 sc){ var s=GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(s.GetComponent<BoxCollider>()); s.name=nm; s.transform.SetParent(border.transform,false); s.transform.position=pos; s.transform.localScale=sc; var r=s.GetComponent<MeshRenderer>(); r.sharedMaterial=mat; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false; }
              float t=0.2f, y=0.03f;
              Strip("Nord",new Vector3(d.cx,y,d.cz+d.sz/2),new Vector3(d.sx+t,0.06f,t));
              Strip("Sud",new Vector3(d.cx,y,d.cz-d.sz/2),new Vector3(d.sx+t,0.06f,t));
              Strip("Est",new Vector3(d.cx+d.sx/2,y,d.cz),new Vector3(t,0.06f,d.sz));
              Strip("Ouest",new Vector3(d.cx-d.sx/2,y,d.cz),new Vector3(t,0.06f,d.sz));
              // Murs : posés sur le cadre, avec un passage de 3 m au milieu du mur Sud.
              string wallPath=folder+"/FXZone_Mur.mat";
              var wallMat=AssetDatabase.LoadAssetAtPath<Material>(wallPath);
              if(wallMat==null){ wallMat=new Material(Shader.Find("Universal Render Pipeline/Lit")); wallMat.SetColor("_BaseColor",new Color(.16f,.17f,.2f)); AssetDatabase.CreateAsset(wallMat,wallPath);}
              var walls=new GameObject("Murs"); walls.transform.SetParent(g.transform,false);
              float wt=0.3f, wh=10f, gap=3f;
              void Wall(string nm,Vector3 pos,Vector3 sc){ var w=GameObject.CreatePrimitive(PrimitiveType.Cube); w.name=nm; w.transform.SetParent(walls.transform,false); w.transform.position=pos; w.transform.localScale=sc; w.GetComponent<MeshRenderer>().sharedMaterial=wallMat; }
              float wy=wh/2f;
              Wall("Mur_Nord",new Vector3(d.cx,wy,d.cz+d.sz/2),new Vector3(d.sx+wt,wh,wt));
              Wall("Mur_Est",new Vector3(d.cx+d.sx/2,wy,d.cz),new Vector3(wt,wh,d.sz));
              Wall("Mur_Ouest",new Vector3(d.cx-d.sx/2,wy,d.cz),new Vector3(wt,wh,d.sz));
              float half=(d.sx+wt-gap)/2f;
              Wall("Mur_Sud_Gauche",new Vector3(d.cx-d.sx/2-wt/2+half/2,wy,d.cz-d.sz/2),new Vector3(half,wh,wt));
              Wall("Mur_Sud_Droit",new Vector3(d.cx+d.sx/2+wt/2-half/2,wy,d.cz-d.sz/2),new Vector3(half,wh,wt));
              var lab=new GameObject("Titre"); lab.transform.SetParent(g.transform,false); lab.transform.position=new Vector3(d.cx,0.1f,d.cz-d.sz/2+0.8f); lab.transform.rotation=Quaternion.Euler(90,0,0);
              var tm=lab.AddComponent<TextMesh>(); tm.text=d.title; tm.anchor=TextAnchor.MiddleCenter; tm.fontSize=64; tm.characterSize=0.08f; tm.color=d.col;
              foreach(var k in d.kids){ var c=root.Find(k); if(c==null){sb.AppendLine("introuvable: "+k);continue;} Undo.SetTransformParent(c,g.transform,"Ranger FX"); }
              sb.AppendLine(d.n+" ok");
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            return sb.ToString();
        }
    }
}
#endif
